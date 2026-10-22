using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Lawgivers;
using Lawgivers.Interface;
using ModLibrary;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace McpServer
{
    /// <summary>
    /// Everything on screen a player can use, as one kind of <see cref="Element"/>,
    /// so one set of tools covers the game's UI, Unity's UI and every mod's UI.
    /// </summary>
    static class UiElements
    {
        public sealed class Element
        {
            public string Id;
            public string Kind;
            public string Label;
            public GameObject Target;
            /// <summary>The component that holds the value, matching <see cref="Kind"/>.</summary>
            public Component Control;
            public Automation.Element Registered;
            /// <summary>In Unity's screen coordinates: pixels from the bottom left.</summary>
            public Rect Bounds;
            public Vector2 Center => Bounds.center;
            /// <summary>The tooltip, when it says more than the label. Often why something is disabled.</summary>
            public string Hint;
            public bool Enabled;
            public bool Covered;
            /// <summary>In a scrolling list but outside its visible part. Acting on it scrolls it into view first.</summary>
            public bool ScrolledOut;
            internal int[] Order;

            public bool Matches(string filter, string under) =>
                (under == null || Id.StartsWith(under, StringComparison.OrdinalIgnoreCase)) &&
                (filter == null || Id.Contains(filter, StringComparison.OrdinalIgnoreCase) || Label.Contains(filter, StringComparison.OrdinalIgnoreCase));

            public string Describe()
            {
                var value = Read(this);
                return $"{Id} [{Kind}] \"{Label}\"" + (value == null ? "" : $" = {value}") +
                    (Enabled ? "" : " (disabled)") + (Covered ? " (covered)" : "") + (ScrolledOut ? " (scrolled out)" : "") + (Hint == null ? "" : $" tooltip: {Hint}");
            }
        }

        /// <summary>The visible elements in scene order, so each window's elements stay together.</summary>
        public static List<Element> Visible(bool withTexts)
        {
            var found = new List<Element>();
            var taken = new HashSet<IntPtr>();
            // Parts of a switch or slider aren't listed on their own, and texts inside buttons are their labels.
            var owners = new HashSet<IntPtr>();
            var buttons = new HashSet<IntPtr>();

            void Add(GameObject target, string kind, Component control, Automation.Element registered = null)
            {
                if (taken.Contains(target.Pointer) || IsInside(target.transform, owners)) return;
                if (kind == "text" && IsInside(target.transform, buttons)) return;
                var element = Describe(target, kind, control, registered);
                if (element == null) return;
                found.Add(element);
                taken.Add(target.Pointer);
                (kind is "button" or "text" ? buttons : owners).Add(target.Pointer);
            }

            foreach (var registered in Automation.All.Where(registered => registered.Target.activeInHierarchy))
                Add(registered.Target, "custom", null, registered);
            foreach (var toggle in Live<UIToggle>()) Add(toggle.gameObject, "switch", toggle);
            foreach (var slider in Live<UISlider>()) Add(slider.gameObject, "slider", slider);
            foreach (var slider in Live<Slider>()) Add(slider.gameObject, "slider", slider);
            foreach (var toggle in Live<Toggle>()) Add(toggle.gameObject, "toggle", toggle);
            foreach (var field in Live<TMP_InputField>()) Add(field.gameObject, "input", field);
            foreach (var dropdown in Live<TMP_Dropdown>()) Add(dropdown.gameObject, "dropdown", dropdown);
            foreach (var choice in Live<UISelection>()) Add(choice.gameObject, "choice", choice);
            foreach (var level in Live<UILevel>()) Add(level.gameObject, "level", level);
            foreach (var button in Live<Button>()) Add(button.gameObject, "button", button);
            foreach (var trigger in Live<EventTrigger>().Where(HasClick)) Add(trigger.gameObject, "button", trigger);
            if (withTexts)
            {
                foreach (var text in Live<TextMeshProUGUI>().Where(text => text.enabled && Clean(text.text).Length > 0))
                    Add(text.gameObject, "text", text);
            }

            found.Sort((a, b) => CompareOrder(a.Order, b.Order));
            return found;
        }

        /// <summary>Where an element or any other UI object (a window, a panel) is on screen, from its target or full path.</summary>
        public static Rect BoundsOf(string target)
        {
            var (element, problem) = Find(target);
            if (element != null) return element.Bounds;
            var rect = GameObject.Find(target)?.transform.TryCast<RectTransform>();
            if (rect != null && IsShown(rect, out _) && IsOnScreen(rect, out var bounds)) return bounds;
            throw new InvalidOperationException(problem + " A window or panel needs its full path, like UI/Game/Windows/Party.");
        }

        /// <summary>
        /// The element a target names: its id, the end of its id, or its label, tried in that order.
        /// Returns why not when nothing or more than one element matches.
        /// </summary>
        public static (Element element, string problem) Find(string target)
        {
            var all = Visible(withTexts: false);
            var ways = new Func<Element, bool>[]
            {
                element => element.Id == target,
                element => element.Id.EndsWith("/" + target),
                element => string.Equals(element.Label, target, StringComparison.OrdinalIgnoreCase),
            };
            foreach (var matchesTarget in ways)
            {
                var matches = all.Where(matchesTarget).ToList();
                if (matches.Count == 0) continue;
                if (matches.Count == 1) return (matches[0], null);
                var reachable = matches.Where(element => !element.Covered).ToList();
                if (reachable.Count == 1) return (reachable[0], null);
                return (null, $"\"{target}\" matches {matches.Count} elements. Use one of these ids:\n" +
                    string.Join("\n", matches.Select(element => element.Describe())));
            }
            return (null, $"Nothing visible matches \"{target}\". ui_list shows what's there.");
        }

        public static string Read(Element element)
        {
            var control = element.Control;
            switch (element.Kind)
            {
                case "switch": return OnOff(control.Cast<UIToggle>().Active);
                case "toggle": return OnOff(control.Cast<Toggle>().isOn);
                case "slider":
                    var value = control.TryCast<UISlider>()?.Value ?? control.Cast<Slider>().value;
                    return value.ToString("0.###", CultureInfo.InvariantCulture);
                case "input": return control.Cast<TMP_InputField>().text;
                case "dropdown": return control.Cast<TMP_Dropdown>().captionText?.text;
                case "choice":
                    var options = Options(control.Cast<UISelection>());
                    var shown = control.Cast<UISelection>().Text?.text;
                    return options.Count is > 1 and <= 12 ? $"{shown} (of {string.Join(", ", options)})" : shown;
                case "level": return $"{control.Cast<UILevel>().Level} (of 1-{control.Cast<UILevel>().Buttons.Length})";
                case "custom": return element.Registered.Read?.Invoke();
                case "button":
                    // A button with more than its label, like "Difficulty  3,0 - Leader", shows the rest as its value.
                    var rest = TextIn(element.Target);
                    rest = rest.StartsWith(element.Label) ? rest[element.Label.Length..].Trim() : "";
                    return rest.Length > 0 ? rest : null;
                default: return null;
            }
        }

        public static string Set(Element element, string value)
        {
            if (!element.Enabled) throw new InvalidOperationException($"{element.Id} is disabled.");
            var control = element.Control;
            switch (element.Kind)
            {
                case "switch":
                    var toggle = control.Cast<UIToggle>();
                    if (toggle.Active != ParseOnOff(value)) toggle.Button();
                    break;
                case "toggle":
                    control.Cast<Toggle>().isOn = ParseOnOff(value);
                    break;
                case "slider":
                    var gameSlider = control.TryCast<UISlider>();
                    var unitySlider = gameSlider?.Slider ?? control.Cast<Slider>();
                    var number = IsRandom(value)
                        ? Mathf.Lerp(unitySlider.minValue, unitySlider.maxValue, (float)random.NextDouble())
                        : float.Parse(value, CultureInfo.InvariantCulture);
                    if (unitySlider.wholeNumbers) number = Mathf.Round(number);
                    if (gameSlider == null) unitySlider.value = number;
                    else
                    {
                        // As if the player dragged the handle there and let go.
                        gameSlider.Slider.value = number;
                        gameSlider.SliderEnd();
                    }
                    break;
                case "input":
                    var field = control.Cast<TMP_InputField>();
                    field.text = value;
                    field.onEndEdit.Invoke(value);
                    break;
                case "dropdown":
                    var dropdown = control.Cast<TMP_Dropdown>();
                    var dropdownOptions = new List<string>();
                    for (var i = 0; i < dropdown.options.Count; i++) dropdownOptions.Add(dropdown.options[i].text);
                    dropdown.value = Pick(dropdownOptions, value, firstNumber: 0);
                    break;
                case "choice":
                    // Steps with the arrows like the player, so the game reacts as usual.
                    var choice = control.Cast<UISelection>();
                    var choices = Options(choice);
                    var wanted = Pick(choices, value, firstNumber: 0);
                    for (var step = 0; step < choices.Count && choice.Index != wanted; step++) choice.Right();
                    break;
                case "level":
                    var level = control.Cast<UILevel>();
                    var levels = Enumerable.Range(1, level.Buttons.Length).Select(n => n.ToString()).ToList();
                    level.Buttons[Pick(levels, value, firstNumber: 1)].onClick.Invoke();
                    break;
                case "custom" when element.Registered.Write != null:
                    element.Registered.Write(value);
                    break;
                default:
                    throw new InvalidOperationException($"{element.Id} is a {element.Kind} without a value. Use ui_click.");
            }
            return $"{element.Id} = {Read(element)}";
        }

        static readonly System.Random random = new();

        static bool IsRandom(string value) => value.Trim().Equals("random", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// The index of an option, picked by its text, by its number (counting from <paramref name="firstNumber"/>) or at random.
        /// One rule for every control with options.
        /// </summary>
        static int Pick(IReadOnlyList<string> options, string value, int firstNumber)
        {
            if (options.Count == 0) throw new InvalidOperationException("There's nothing to choose from.");
            if (IsRandom(value)) return random.Next(options.Count);
            var index = options.ToList().FindIndex(option => string.Equals(option, value, StringComparison.OrdinalIgnoreCase));
            if (index < 0 && int.TryParse(value, out var number) && number - firstNumber >= 0 && number - firstNumber < options.Count)
                index = number - firstNumber;
            if (index < 0) throw new ArgumentException($"No option \"{value}\". The options are: {string.Join(", ", options)}, or random.");
            return index;
        }

        static List<string> Options(UISelection choice) =>
            choice.Strings != null ? choice.Strings.ToList() : Enumerable.Range(0, choice.Images?.Count ?? 0).Select(n => n.ToString()).ToList();

        static Element Describe(GameObject target, string kind, Component control, Automation.Element registered)
        {
            var rect = target.transform.TryCast<RectTransform>();
            if (rect == null || !IsShown(target.transform, out var interactable)) return null;
            var onScreen = IsOnScreen(rect, out var bounds);
            var scrolledOut = IsScrolledOut(rect);
            if (!onScreen && !scrolledOut) return null;

            var element = new Element
            {
                Id = PathOf(target.transform),
                Kind = kind,
                Target = target,
                Control = control,
                Registered = registered,
                Bounds = bounds,
                Enabled = interactable && (control?.TryCast<Selectable>()?.IsInteractable() ?? true),
                Order = OrderOf(target.transform),
                ScrolledOut = scrolledOut,
            };
            element.Label = LabelOf(element);
            if (kind != "text")
            {
                var tooltip = Tooltip(target, maxLength: 200);
                if (tooltip != null && tooltip != element.Label) element.Hint = tooltip;
            }
            if (kind != "text" && element.Enabled && !scrolledOut)
            {
                var hit = Pointer.TopHit(element.Center, out _);
                element.Covered = hit == null || !IsInside(hit.transform, target.transform);
            }
            return element;
        }

        static string LabelOf(Element element)
        {
            if (element.Registered?.Label != null) return element.Registered.Label;
            var self = element.Target.transform;
            if (element.Kind == "text") return Clean(element.Control.Cast<TextMeshProUGUI>().text);
            // These controls show their value, so their name sits next to them, like in a settings row.
            if (element.Kind is "input" or "dropdown" or "slider" or "choice" or "level")
                return NearbyText(self) ?? Tooltip(element.Target) ?? element.Target.name;
            var label = FirstText(self, skip: null) ?? Tooltip(element.Target);
            // A switch has no text of its own: its name is in the row around it.
            if (element.Kind is "switch" or "toggle") label ??= NearbyText(self);
            return label ?? element.Target.name;
        }

        /// <summary>
        /// The first text next to a control: in its parent, or a text right beside its parent,
        /// like the "Year" next to the slider's frame. Deeper texts up there belong to other rows.
        /// </summary>
        static string NearbyText(Transform self)
        {
            var near = FirstText(self.parent, skip: self);
            var row = self.parent?.parent;
            if (near != null || row == null) return near;
            for (var i = 0; i < row.childCount; i++)
            {
                var text = row.GetChild(i).GetComponent<TextMeshProUGUI>();
                if (text != null && text.isActiveAndEnabled && Clean(text.text).Length > 0) return Clean(text.text);
            }
            return null;
        }

        static string FirstText(Transform root, Transform skip)
        {
            if (root == null) return null;
            foreach (var text in root.GetComponentsInChildren<TextMeshProUGUI>())
            {
                if (skip != null && IsInside(text.transform, skip)) continue;
                var clean = Clean(text.text);
                if (clean.Length > 0) return clean;
            }
            return null;
        }

        /// <summary>All texts inside an element, for finding it by what it shows.</summary>
        public static string TextIn(GameObject target) =>
            string.Join(" ", target.GetComponentsInChildren<TextMeshProUGUI>().Select(text => Clean(text.text)).Where(text => text.Length > 0));

        static string Tooltip(GameObject target, int maxLength = 80)
        {
            try
            {
                var text = Clean(target.GetComponent<UITooltip>()?.tooltip?.funcText?.Invoke(), maxLength);
                return text.Length > 0 ? text : null;
            }
            catch
            {
                return null; // Some tooltips need a loaded game.
            }
        }

        /// <summary>The path from the scene root, like "UI/Menu/Main/Logo/Buttons/Mods". "[n]" tells apart siblings with the same name.</summary>
        public static string PathOf(Transform transform)
        {
            var names = new List<string>();
            for (; transform != null; transform = transform.parent) names.Add(NameAmongSiblings(transform));
            names.Reverse();
            return string.Join("/", names);
        }

        static string NameAmongSiblings(Transform transform)
        {
            var parent = transform.parent;
            if (parent == null) return transform.name;
            int sameName = 0, index = 0;
            for (var i = 0; i < parent.childCount; i++)
            {
                var sibling = parent.GetChild(i);
                if (sibling.name != transform.name) continue;
                if (sibling.Pointer == transform.Pointer) index = sameName;
                sameName++;
            }
            return sameName > 1 ? $"{transform.name}[{index}]" : transform.name;
        }

        public static bool IsShown(Transform transform, out bool interactable)
        {
            interactable = true;
            var alpha = 1f;
            for (; transform != null; transform = transform.parent)
            {
                var canvas = transform.GetComponent<Canvas>();
                if (canvas != null && !canvas.enabled) return false;
                var group = transform.GetComponent<CanvasGroup>();
                if (group == null || !group.enabled) continue;
                alpha *= group.alpha;
                interactable &= group.interactable && group.blocksRaycasts;
                if (group.ignoreParentGroups) break;
            }
            return alpha > 0.01f;
        }

        static bool IsScrolledOut(RectTransform rect)
        {
            var scroll = rect.GetComponentInParent<ScrollRect>();
            if (scroll == null) return false;
            var viewport = scroll.viewport ?? scroll.transform.Cast<RectTransform>();
            var center = viewport.InverseTransformPoint(rect.TransformPoint(rect.rect.center));
            return !viewport.rect.Contains(center);
        }

        /// <summary>Scrolls the list around an element so that the element sits in the middle of the visible part.</summary>
        public static void ScrollIntoView(Element element)
        {
            var rect = element.Target.transform.Cast<RectTransform>();
            var scroll = rect.GetComponentInParent<ScrollRect>();
            var viewport = scroll.viewport ?? scroll.transform.Cast<RectTransform>();
            var offset = viewport.TransformPoint(viewport.rect.center) - rect.TransformPoint(rect.rect.center);
            scroll.StopMovement();
            scroll.content.position += new Vector3(scroll.horizontal ? offset.x : 0, scroll.vertical ? offset.y : 0, 0);
        }

        static int[] OrderOf(Transform transform)
        {
            var indices = new List<int>();
            for (; transform != null; transform = transform.parent) indices.Add(transform.GetSiblingIndex());
            indices.Reverse();
            return indices.ToArray();
        }

        static int CompareOrder(int[] a, int[] b)
        {
            for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
                if (a[i] != b[i]) return a[i].CompareTo(b[i]);
            return a.Length.CompareTo(b.Length);
        }

        static bool IsOnScreen(RectTransform rect, out Rect bounds)
        {
            var canvas = rect.GetComponentInParent<Canvas>()?.rootCanvas;
            var camera = canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            var (min, max) = ScreenBounds(rect, camera);
            // Some of the game's controls, like UIToggle, have no size of their own: the parts below them do.
            if (max.x - min.x < 1 || max.y - min.y < 1)
            {
                foreach (var part in rect.GetComponentsInChildren<RectTransform>())
                {
                    var (partMin, partMax) = ScreenBounds(part, camera);
                    min = Vector2.Min(min, partMin);
                    max = Vector2.Max(max, partMax);
                }
            }
            bounds = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            return max.x - min.x >= 1 && max.y - min.y >= 1 &&
                max.x > 0 && min.x < Screen.width && max.y > 0 && min.y < Screen.height;
        }

        static (Vector2 min, Vector2 max) ScreenBounds(RectTransform rect, Camera camera)
        {
            var corners = new Il2CppStructArray<Vector3>(4);
            rect.GetWorldCorners(corners);
            return (RectTransformUtility.WorldToScreenPoint(camera, corners[0]), RectTransformUtility.WorldToScreenPoint(camera, corners[2]));
        }

        // Not isActiveAndEnabled: the game keeps some of its controls, like UIToggle, switched off and drives them from a Button.
        static IEnumerable<T> Live<T>() where T : Component =>
            Resources.FindObjectsOfTypeAll<T>().Where(component => component.gameObject.activeInHierarchy && component.gameObject.scene.IsValid());

        static bool HasClick(EventTrigger trigger)
        {
            foreach (var entry in trigger.triggers)
                if (entry.eventID == EventTriggerType.PointerClick) return true;
            return false;
        }

        public static bool IsInside(Transform transform, Transform root)
        {
            for (; transform != null; transform = transform.parent)
                if (transform.Pointer == root.Pointer) return true;
            return false;
        }

        static bool IsInside(Transform transform, HashSet<IntPtr> roots)
        {
            if (roots.Count == 0) return false;
            for (; transform != null; transform = transform.parent)
                if (roots.Contains(transform.gameObject.Pointer)) return true;
            return false;
        }

        static string Clean(string text, int maxLength = 80)
        {
            var plain = Regex.Replace(text ?? "", "<[^>]*>", "").Replace('\n', ' ').Trim();
            return plain.Length > maxLength ? plain[..(maxLength - 3)] + "..." : plain;
        }

        static string OnOff(bool on) => on ? "on" : "off";

        static bool ParseOnOff(string value) => value.Trim().ToLowerInvariant() switch
        {
            "random" => random.Next(2) == 1,
            "on" or "true" or "1" or "yes" => true,
            "off" or "false" or "0" or "no" => false,
            _ => throw new ArgumentException($"\"{value}\" isn't on or off."),
        };
    }
}
