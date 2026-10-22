using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using Lawgivers;
using ModLibrary;
using UnityEngine;

namespace McpServer
{
    static class GameInfo
    {
        public static string State()
        {
            var lines = new List<string> { $"Screen: {Where()}" };
            var open = OpenWindows().ToList();
            if (open.Count > 0) lines.Add($"Open: {string.Join(", ", open)}");

            var player = GameEvents.Player;
            if (player != null)
            {
                lines.Add($"Turn: {Instance.Turn}");
                lines.Add($"Action points: {player.actions?.points}");
                lines.Add($"Party money: {player.Party?.money?.value}, black money: {player.Party?.black?.value}");
            }

            lines.Add($"Size: {Screen.width}x{Screen.height}");
            lines.Add("Plugins:");
            lines.AddRange(IL2CPPChainloader.Instance.Plugins.Values.Select(plugin => $"- {plugin} ({plugin.Metadata.GUID})"));
            return string.Join("\n", lines);
        }

        static string Where()
        {
            if (GameEvents.Player != null) return "in game";
            var ui = GameEvents.MainMenu?.transform.root;
            if (ui?.Find("Info")?.gameObject.activeInHierarchy == true) return "title screen";
            return GameEvents.MainMenu?.gameObject.activeInHierarchy == true ? "main menu" : "loading";
        }

        /// <summary>The game's open menus and windows, by id. Mod windows show up in ui_list.</summary>
        static IEnumerable<string> OpenWindows()
        {
            var ui = GameEvents.MainMenu?.transform.root;
            if (ui == null) yield break;
            foreach (var path in new[] { "Menu", "Game/Windows" })
            {
                var parent = ui.Find(path);
                if (parent == null) continue;
                for (var i = 0; i < parent.childCount; i++)
                {
                    var window = parent.GetChild(i);
                    if (window.gameObject.activeInHierarchy && UiElements.IsShown(window, out _)) yield return UiElements.PathOf(window);
                }
            }
        }

        /// <summary>The last <paramref name="count"/> log entries. An entry is a "[Level :Source]" line and the lines below it, such as a stack trace.</summary>
        public static string Log(int count, bool errorsOnly)
        {
            var path = Path.Combine(Paths.BepInExRootPath, "LogOutput.log");
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(file);

            var entries = new List<string>();
            while (reader.ReadLine() is { } line)
            {
                if (line.StartsWith("[") || entries.Count == 0) entries.Add(line);
                else entries[^1] += "\n" + line;
            }
            var picked = entries.Where(entry => !errorsOnly || entry.StartsWith("[Error") || entry.StartsWith("[Fatal") || entry.StartsWith("[Warning"));
            var text = string.Join("\n", picked.TakeLast(count));
            return text.Length == 0 ? "Nothing logged." : text;
        }
    }
}
