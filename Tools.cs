using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Lawgivers;
using ModLibrary;
using UnityEngine;

namespace McpServer
{
    /// <summary>
    /// The tools the server offers. Every action (click, hover, set, press, wait) is one function that both its own tool
    /// and ui_do use, and every tool that acts on a UI element finds it the same way, see <see cref="OnTarget"/>.
    /// </summary>
    static class Tools
    {
        public const string Instructions =
            "Drives a running Lawgivers II. load_save gets from the title screen into a game. ui_list shows what's on screen; every element has an id " +
            "(its path in the scene) and a label. Tools that take a target accept the id, the end of the id (\"Buttons/Mods\") or the label. " +
            "Actions report what appeared afterwards, so you rarely need ui_list after them. Chain several actions with ui_do. " +
            "press runs the game's own shortcuts (Turn ends the turn, Close closes the top window). Use wait_seconds instead of sleeping. " +
            "After building a mod, check game_log with errors_only.";

        // Classes, not records: records need compiler attributes this build setup lacks (CS0656).
        public sealed class Arg
        {
            public readonly string Name, Type, Description;
            public readonly bool Required;

            public Arg(string name, string type, string description, bool required = false) =>
                (Name, Type, Description, Required) = (name, type, description, required);

            public JsonObject Describe()
            {
                var schema = new JsonObject { ["type"] = Type, ["description"] = Description };
                if (Type == "array") schema["items"] = new JsonObject { ["type"] = "object" };
                return schema;
            }
        }

        public sealed class Tool
        {
            public readonly string Name, Description;
            public readonly Arg[] Args;
            public readonly Func<JsonObject, Task<JsonArray>> Run;

            public Tool(string name, string description, Arg[] args, Func<JsonObject, Task<JsonArray>> run) =>
                (Name, Description, Args, Run) = (name, description, args, run);

            public JsonObject Describe() => new()
            {
                ["name"] = Name,
                ["description"] = Description,
                ["inputSchema"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject(Args.Select(arg => new KeyValuePair<string, JsonNode>(arg.Name, arg.Describe()))),
                    ["required"] = new JsonArray(Args.Where(arg => arg.Required).Select(arg => (JsonNode)arg.Name).ToArray()),
                },
            };
        }

        const string TargetDescription = "The element: its id from ui_list, the end of its id (\"Buttons/Mods\"), or its label.";
        static readonly Arg Target = new("target", "string", TargetDescription);
        static readonly Arg X = new("x", "number", "Instead of a target: screen x in pixels from the left, as in the full-size screenshot. Where there's no UI, this clicks the map.");
        static readonly Arg Y = new("y", "number", "Instead of a target: screen y in pixels from the top.");
        static readonly Arg Wait = new("wait_seconds", "number", "Keep trying this long while the element is missing, covered or disabled, for example while a window opens. Default 0.");
        static readonly string KeyDescription =
            $"One of the game's actions ({string.Join(", ", Enum.GetNames(typeof(Controls.EID)))}) " +
            "or a key of Unity's input system (Escape, Space, A, Digit1, F5...).";

        /// <summary>The actions ui_do can chain, by the name its steps use.</summary>
        static readonly Dictionary<string, Func<JsonObject, Task<string>>> Actions = new()
        {
            ["click"] = args => PointAt(args, Pointer.Click, Pointer.ClickAt),
            ["hover"] = args => PointAt(args, Pointer.Hover, Pointer.HoverAt),
            ["set"] = args => OnTarget(args, element => UiElements.Set(element, Text(args, "value") ?? throw new ArgumentException("Give a value."))),
            ["drag"] = args => PointAt(args, element => Pointer.Drag(element, DragEnd(args)), point => Pointer.DragAt(point, DragEnd(args))),
            ["press"] = Press,
            ["wait"] = async args =>
            {
                await Task.Delay(TimeSpan.FromSeconds(Number(args, "seconds") ?? 1));
                return "Waited";
            },
        };

        public static readonly Tool[] All =
        {
            new("ui_list", "Lists the visible buttons, switches, sliders, text fields and dropdowns, window by window: id [kind] \"label\" = value.",
                new[]
                {
                    new Arg("filter", "string", "Only elements whose id or label contains this text."),
                    new Arg("under", "string", "Only elements whose id starts with this, for example one window."),
                    new Arg("include_text", "boolean", "Also list plain texts, to read what the screen says."),
                    Wait,
                },
                ListUi),
            new("ui_click", "Clicks an element like the player would: at its center, so it fails when something covers it. With x and y, clicks there, on the map if there's no UI.",
                new[] { Target, X, Y, Wait }, args => ReportChanges(() => Actions["click"](args))),
            new("ui_hover", "Moves the pointer onto an element or a point of the UI, for hover states.",
                new[] { Target, X, Y, Wait }, args => ReportChanges(() => Actions["hover"](args))),
            new("ui_set", "Sets the value of a switch or toggle (on/off), slider (number), text field (text), dropdown or arrow choice (option text or index) or level (1-5). \"random\" picks any value.",
                new[] { new Arg("target", "string", TargetDescription, required: true), new Arg("value", "string", "The new value.", required: true), Wait },
                args => ReportChanges(() => Actions["set"](args))),
            new("ui_drag", "Drags like the player would: presses on an element or at x and y, moves to to_x and to_y and lets go. For sliders, scroll lists and anything else that takes drags.",
                new[]
                {
                    Target, X, Y,
                    new Arg("to_x", "number", "Where to let go: screen x in pixels from the left, as in the full-size screenshot.", required: true),
                    new Arg("to_y", "number", "Where to let go: screen y in pixels from the top.", required: true),
                    Wait,
                },
                args => ReportChanges(() => Actions["drag"](args))),
            new("ui_inspect", "Shows the objects under a window, panel or element, hidden ones included, with their size, layout, images, fonts and the game's own components. For fitting new UI into the game's windows.",
                new[]
                {
                    new Arg("target", "string", "An element's target, or the full path of any UI object, like UI/Game/Windows/Party. Hidden objects need the full path.", required: true),
                    new Arg("depth", "number", "How many levels below the target. Default 2."),
                },
                async args => Reply(await MainThread.Run(() =>
                    Inspector.Describe(Text(args, "target") ?? throw new ArgumentException("Give a target."), (int)(Number(args, "depth") ?? 2))))),
            new("press", "Presses one of the game's actions or a key, as if on the keyboard. Turn ends the turn, Close closes the top window, Party/Person/Nation open those windows.",
                new[] { new Arg("key", "string", KeyDescription, required: true) }, args => ReportChanges(() => Press(args))),
            new("ui_do", "Runs several actions in a row and stops at the first that fails. Each step is an object like " +
                "{\"do\":\"click\",\"target\":\"Singleplayer\"}, {\"do\":\"set\",\"target\":\"...\",\"value\":\"on\"}, {\"do\":\"press\",\"key\":\"Close\"}, " +
                "{\"do\":\"hover\",\"x\":100,\"y\":200}, {\"do\":\"drag\",\"target\":\"...\",\"to_x\":300,\"to_y\":200} or {\"do\":\"wait\",\"seconds\":2}. Steps take the same arguments as their tools, including wait_seconds.",
                new[] { new Arg("steps", "array", "The actions, in order.", required: true) }, RunSteps),
            new("load_save", "Loads a saved world, from the title screen or the main menu, and waits until the game is ready.",
                new[] { new Arg("name", "string", "Part of the save's name as the list shows it, like \"Welt #1\". Default: the first in the list.") }, LoadSave),
            new("screenshot", "Captures the game's own picture, nothing else on the desktop. Saves it at full size and returns a smaller copy.",
                new[]
                {
                    new Arg("width", "number", "Width of the returned image. Default 1280."),
                    new Arg("target", "string", "Only this part of the screen: an element's target, or the full path of a window or panel, like UI/Game/Windows/Party."),
                },
                TakeScreenshot),
            new("game_state", "Where the game is (title screen, main menu, in game, loading), open windows, turn, action points, money and the loaded plugins.",
                Array.Empty<Arg>(), async _ => Reply(await MainThread.Run(GameInfo.State))),
            new("game_log", "The end of BepInEx's log, where mods' errors go.",
                new[]
                {
                    new Arg("lines", "number", "How many entries. Default 50."),
                    new Arg("errors_only", "boolean", "Only errors and warnings."),
                },
                args => Task.FromResult(Reply(GameInfo.Log((int)(Number(args, "lines") ?? 50), Flag(args, "errors_only"))))),
        };

        public static async Task<JsonObject> Call(string name, JsonObject args)
        {
            var tool = All.FirstOrDefault(tool => tool.Name == name);
            try
            {
                if (tool == null) throw new ArgumentException($"There's no tool called {name}.");
                return new JsonObject { ["content"] = await tool.Run(args) };
            }
            catch (Exception e)
            {
                return new JsonObject { ["content"] = Reply(e.Message), ["isError"] = true };
            }
        }

        static async Task<JsonArray> ListUi(JsonObject args)
        {
            var filter = Text(args, "filter");
            var under = Text(args, "under");
            var withTexts = Flag(args, "include_text");
            var lines = await Retry(args, () => Lines(UiElements.Visible(withTexts)
                .Where(element => element.Matches(filter, under))).ToList(), lines => lines.Count > 0);
            return Reply(lines.Count == 0 ? "Nothing found." : string.Join("\n", lines));
        }

        /// <summary>
        /// Runs an action and adds what appeared or changed and how much went away, which usually saves a ui_list.
        /// Elements count as changed when they show something else, like a list window refilled with other entries.
        /// </summary>
        static async Task<JsonArray> ReportChanges(Func<Task<string>> act)
        {
            var before = new Dictionary<string, string>();
            await MainThread.Run(() =>
            {
                foreach (var element in UiElements.Visible(withTexts: false)) before[element.Id] = Shows(element);
                return before.Count;
            });
            var result = await act();
            await Settle();
            var after = await MainThread.Run(() => UiElements.Visible(withTexts: false)
                .Select(element => (element, changed: !before.TryGetValue(element.Id, out var shown) || shown != Shows(element)))
                .ToList());

            var changed = Lines(after.Where(entry => entry.changed).Select(entry => entry.element)).ToList();
            var gone = before.Keys.Except(after.Select(entry => entry.element.Id)).Count();
            const int shownLines = 40;
            var report = new List<string> { result };
            if (changed.Count > 0) report.Add($"Appeared or changed ({changed.Count}):");
            report.AddRange(changed.Take(shownLines));
            if (changed.Count > shownLines) report.Add($"... and {changed.Count - shownLines} more, see ui_list.");
            if (gone > 0) report.Add($"{gone} elements went away.");
            if (changed.Count == 0 && gone == 0) report.Add("Nothing appeared, changed or went away.");
            return Reply(string.Join("\n", report));
        }

        static string Shows(UiElements.Element element) => $"{element.Label}\n{UiElements.Read(element)}";

        /// <summary>Waits until the UI stops moving (windows sliding in, lists filling), at most two seconds. Clicks during a slide get lost.</summary>
        static async Task Settle()
        {
            string last = null;
            for (var check = 0; check < 20; check++)
            {
                var now = await MainThread.Run(() => string.Join("|", UiElements.Visible(withTexts: false)
                    .Select(element => $"{element.Id}@{element.Center.x:0},{element.Center.y:0}")));
                if (now == last) return;
                last = now;
                await Task.Delay(100);
            }
        }

        /// <summary>
        /// One line per element, except that long rows of look-alikes, like the calendar's weeks, become one line.
        /// Look-alikes have the same id apart from numbers and all the same label, or only numbers as labels.
        /// </summary>
        static IEnumerable<string> Lines(IEnumerable<UiElements.Element> elements)
        {
            foreach (var group in elements.GroupBy(element => (pattern: Regex.Replace(element.Id, @"\d+", "#"), element.Kind)))
            {
                var members = group.ToList();
                var alike = members.Count >= 5 &&
                    (members.All(member => member.Label == members[0].Label) || members.All(member => member.Label.All(char.IsDigit)));
                if (!alike)
                {
                    foreach (var member in members) yield return member.Describe();
                    continue;
                }
                var labels = members[0].Label == members[^1].Label ? $"\"{members[0].Label}\"" : $"\"{members[0].Label}\" to \"{members[^1].Label}\"";
                yield return $"{group.Key.pattern} [{group.Key.Kind}] {labels}: {members.Count} alike, # stands for a number";
            }
        }

        static Task<JsonArray> RunSteps(JsonObject args) => ReportChanges(async () =>
        {
            var steps = args["steps"] as JsonArray ?? throw new ArgumentException("steps must be a list of actions.");
            var done = new List<string>();
            for (var i = 0; i < steps.Count; i++)
            {
                var step = steps[i] as JsonObject;
                var name = step == null ? null : Text(step, "do");
                if (name == null || !Actions.TryGetValue(name, out var action))
                    throw new ArgumentException($"Step {i + 1} needs \"do\": one of {string.Join(", ", Actions.Keys)}.");
                try
                {
                    done.Add($"{i + 1}. {await action(step)}");
                    await Settle();
                }
                catch (Exception e)
                {
                    done.Add($"Step {i + 1} failed: {e.Message}");
                    throw new InvalidOperationException(string.Join("\n", done));
                }
            }
            return string.Join("\n", done);
        });

        static async Task<string> Press(JsonObject args)
        {
            var result = await MainThread.Run(() => VirtualInput.Press(Text(args, "key") ?? throw new ArgumentException("Give a key.")));
            await WaitForVirtualInput();
            return result;
        }

        static async Task<string> PointAt(JsonObject args, Func<UiElements.Element, string> onElement, Func<Vector2, string> onPoint)
        {
            if (args["x"] == null || args["y"] == null) return await OnTarget(args, onElement);
            var point = new Vector2((float)Number(args, "x"), (float)Number(args, "y"));
            var result = await MainThread.Run(() => onPoint(point));
            await WaitForVirtualInput();
            return result;
        }

        static Vector2 DragEnd(JsonObject args) =>
            new((float)(Number(args, "to_x") ?? throw new ArgumentException("Give to_x and to_y.")),
                (float)(Number(args, "to_y") ?? throw new ArgumentException("Give to_x and to_y.")));

        static async Task WaitForVirtualInput()
        {
            while (await MainThread.Run(() => VirtualInput.IsBusy)) await Task.Delay(20);
        }

        /// <summary>
        /// Finds the target and acts on it in the same frame, so it can't vanish in between.
        /// While waiting, a missing, covered or disabled element is tried again.
        /// </summary>
        static async Task<string> OnTarget(JsonObject args, Func<UiElements.Element, string> act)
        {
            var target = Text(args, "target") ?? throw new ArgumentException("Give a target, or x and y.");
            var (result, problem) = await Retry<(string result, string problem)>(args, () =>
            {
                var (element, notFound) = UiElements.Find(target);
                if (element == null) return (null, notFound);
                if (element.ScrolledOut)
                {
                    UiElements.ScrollIntoView(element);
                    (element, notFound) = UiElements.Find(target);
                    if (element == null) return (null, notFound);
                }
                try
                {
                    return (act(element), null);
                }
                catch (InvalidOperationException notReady)
                {
                    return (null, notReady.Message);
                }
            }, found => found.problem == null);
            return result ?? throw new InvalidOperationException(problem);
        }

        static Task<T> Retry<T>(JsonObject args, Func<T> query, Func<T, bool> found) =>
            Retry(Number(args, "wait_seconds") ?? 0, query, found);

        static async Task<T> Retry<T>(double seconds, Func<T> query, Func<T, bool> found)
        {
            var deadline = DateTime.UtcNow.AddSeconds(seconds);
            while (true)
            {
                var result = await MainThread.Run(query);
                if (found(result) || DateTime.UtcNow >= deadline) return result;
                await Task.Delay(100);
            }
        }

        static async Task<JsonArray> LoadSave(JsonObject args)
        {
            var name = Text(args, "name");
            if (await MainThread.Run(() => GameEvents.Player != null))
                throw new InvalidOperationException("A game is already loaded. Go back to the main menu first.");

            if (await MainThread.Run(() => UiElements.Find("UI/Info/Button").element != null))
                await Actions["click"](new JsonObject { ["target"] = "UI/Info/Button", ["wait_seconds"] = 10 });
            if (!await MainThread.Run(() => UiElements.Visible(withTexts: false).Any(element => element.Id.StartsWith("UI/Menu/Singleplayer/"))))
                await Actions["click"](new JsonObject { ["target"] = "Buttons/Singleplayer", ["wait_seconds"] = 30 });

            var (save, problem) = await Retry<(UiElements.Element save, string problem)>(10, () =>
            {
                var saves = UiElements.Visible(withTexts: false)
                    .Where(element => element.Id.StartsWith("UI/Menu/Singleplayer/Content/Scroll/") && !element.Id.EndsWith("/Delete"))
                    .Select(element => (element, texts: UiElements.TextIn(element.Target)))
                    .ToList();
                var match = saves.FirstOrDefault(entry => name == null || entry.texts.Contains(name, StringComparison.OrdinalIgnoreCase));
                if (match.element != null && match.element.Enabled && !match.element.Covered) return (match.element, null);
                return (null, saves.Count == 0 ? "The save list is empty."
                    : $"No save matches \"{name}\". The saves are:\n{string.Join("\n", saves.Select(entry => entry.texts))}");
            }, found => found.problem == null);
            if (save == null) throw new InvalidOperationException(problem);
            await MainThread.Run(() => Pointer.Click(save));

            var ready = await Retry(120, () => GameEvents.Player != null && UiElements.Find("TopRight/Pause").element is { Covered: false },
                isReady => isReady);
            if (!ready) throw new TimeoutException("The game didn't finish loading within two minutes.");

            // The camera flies in after loading. Until it stops, map clicks land somewhere else.
            Vector3? last = null;
            await Retry(10, () =>
            {
                var now = VirtualInput.Map?.transform.position;
                var stopped = now == last;
                last = now;
                return stopped;
            }, stopped => stopped);
            return Reply(await MainThread.Run(GameInfo.State));
        }

        static async Task<JsonArray> TakeScreenshot(JsonObject args)
        {
            var target = Text(args, "target");
            RectInt? area = null;
            if (target != null)
            {
                area = await MainThread.Run(() =>
                {
                    var bounds = UiElements.BoundsOf(target);
                    var minX = Math.Max(0, Mathf.FloorToInt(bounds.xMin));
                    var minY = Math.Max(0, Mathf.FloorToInt(bounds.yMin));
                    var maxX = Math.Min(Screen.width, Mathf.CeilToInt(bounds.xMax));
                    var maxY = Math.Min(Screen.height, Mathf.CeilToInt(bounds.yMax));
                    return new RectInt(minX, minY, maxX - minX, maxY - minY);
                });
            }

            var (path, screen) = await MainThread.Run(Screenshot.Start);
            var bytes = await Screenshot.WaitForFile(path);
            if (area is { } crop)
            {
                bytes = await MainThread.Run(() => Screenshot.Crop(bytes, crop));
                await System.IO.File.WriteAllBytesAsync(path, bytes);
                screen = new Vector2Int(crop.width, crop.height);
            }
            var (png, size) = await MainThread.Run(() => Screenshot.Shrink(bytes, (int)(Number(args, "width") ?? 1280)));

            var scale = ((float)screen.x / size.x).ToString("0.###", CultureInfo.InvariantCulture);
            var reply = Reply($"Saved {path} ({screen.x}x{screen.y}). This copy is {size.x}x{size.y}" +
                (area == null ? $": multiply its coordinates by {scale} for ui_click x and y." : "."));
            reply.Add(new JsonObject { ["type"] = "image", ["data"] = Convert.ToBase64String(png), ["mimeType"] = "image/png" });
            return reply;
        }

        static JsonArray Reply(string text) => new(new JsonObject { ["type"] = "text", ["text"] = text });

        static string Text(JsonObject args, string name) => args[name]?.ToString();

        static double? Number(JsonObject args, string name) =>
            args[name] == null ? null : double.Parse(args[name].ToString(), CultureInfo.InvariantCulture);

        static bool Flag(JsonObject args, string name) => bool.TryParse(args[name]?.ToString(), out var flag) && flag;
    }
}
