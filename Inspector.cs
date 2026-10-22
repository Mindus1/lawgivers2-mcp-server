using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace McpServer
{
    /// <summary>
    /// The objects under a UI object with what decides their look and layout: size, layout groups, images, fonts.
    /// For fitting a mod's UI into the game's windows. Hidden objects are included and marked.
    /// </summary>
    static class Inspector
    {
        const int MaxChildren = 25;

        public static string Describe(string target, int depth)
        {
            var root = Resolve(target);
            var lines = new StringBuilder();
            lines.AppendLine(UiElements.PathOf(root));
            Add(lines, root, 0, depth);
            return lines.ToString().TrimEnd();
        }

        /// <summary>An element's target, or a full path like the ids. Hidden objects only by path.</summary>
        static Transform Resolve(string target)
        {
            var (element, _) = UiElements.Find(target);
            if (element != null) return element.Target.transform;

            var names = target.Split('/');
            var current = Resources.FindObjectsOfTypeAll<Transform>()
                .FirstOrDefault(transform => transform.parent == null && transform.name == names[0] && transform.gameObject.scene.IsValid());
            foreach (var name in names.Skip(1))
            {
                if (current == null) break;
                current = Child(current, name);
            }
            return current ?? throw new InvalidOperationException($"Nothing at \"{target}\". Give an element's target or a full path from ui_list's ids.");
        }

        /// <summary>A child by name, or by "Name[2]" when siblings share the name, as in the ids.</summary>
        static Transform Child(Transform parent, string name)
        {
            var match = Regex.Match(name, @"^(.*)\[(\d+)\]$");
            var index = match.Success ? int.Parse(match.Groups[2].Value) : 0;
            if (match.Success) name = match.Groups[1].Value;
            for (var i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child.name == name && index-- == 0) return child;
            }
            return null;
        }

        static void Add(StringBuilder lines, Transform transform, int level, int depth)
        {
            lines.Append(new string(' ', level * 2)).AppendLine(Line(transform));
            if (level >= depth)
            {
                if (transform.childCount > 0) lines.Append(new string(' ', level * 2 + 2)).AppendLine($"({transform.childCount} children)");
                return;
            }
            for (var i = 0; i < Math.Min(transform.childCount, MaxChildren); i++) Add(lines, transform.GetChild(i), level + 1, depth);
            if (transform.childCount > MaxChildren)
                lines.Append(new string(' ', level * 2 + 2)).AppendLine($"... {transform.childCount - MaxChildren} more");
        }

        static string Line(Transform transform)
        {
            var parts = new List<string> { transform.name + (transform.gameObject.activeSelf ? "" : " (hidden)") };
            var rect = transform.TryCast<RectTransform>();
            if (rect != null)
            {
                parts.Add($"{Number(rect.rect.width)}x{Number(rect.rect.height)} at {Number(rect.anchoredPosition.x)},{Number(rect.anchoredPosition.y)}");
                if (Mathf.Abs(transform.localScale.x - 1) > 0.001f) parts.Add($"scale {Number(transform.localScale.x)}");
            }
            foreach (var component in transform.GetComponents<Component>())
            {
                var about = About(component);
                if (about != null) parts.Add(about);
            }
            return string.Join(" | ", parts);
        }

        static string About(Component component)
        {
            if (component.TryCast<RectTransform>() != null || component.TryCast<CanvasRenderer>() != null) return null;
            if (component.TryCast<Transform>() != null) return null;

            var layout = component.TryCast<HorizontalOrVerticalLayoutGroup>();
            if (layout != null)
            {
                var padding = layout.padding;
                return $"{(layout.TryCast<VerticalLayoutGroup>() != null ? "vertical" : "horizontal")} layout " +
                    $"padding {padding.left},{padding.right},{padding.top},{padding.bottom} spacing {Number(layout.spacing)} {layout.childAlignment}, " +
                    $"sizes children: width {YesNo(layout.childControlWidth)} height {YesNo(layout.childControlHeight)}, " +
                    $"stretches children: width {YesNo(layout.childForceExpandWidth)} height {YesNo(layout.childForceExpandHeight)}";
            }
            var grid = component.TryCast<GridLayoutGroup>();
            if (grid != null) return $"grid cells {Number(grid.cellSize.x)}x{Number(grid.cellSize.y)} spacing {Number(grid.spacing.x)},{Number(grid.spacing.y)}";
            var element = component.TryCast<LayoutElement>();
            if (element != null)
                return $"layout element min {Number(element.minWidth)}x{Number(element.minHeight)} preferred {Number(element.preferredWidth)}x{Number(element.preferredHeight)} " +
                    $"flexible {Number(element.flexibleWidth)}x{Number(element.flexibleHeight)}" + (element.ignoreLayout ? " ignores layout" : "");
            var fitter = component.TryCast<ContentSizeFitter>();
            if (fitter != null) return $"fits content: width {fitter.horizontalFit} height {fitter.verticalFit}";
            var text = component.TryCast<TextMeshProUGUI>();
            if (text != null)
                return $"text \"{Short(text.text)}\" font {text.font?.name} {Number(text.fontSize)}{(text.enableAutoSizing ? " (auto)" : "")} {Color(text.color)} {text.alignment}";
            var image = component.TryCast<Image>();
            if (image != null)
                return $"image {(image.sprite == null ? "no sprite" : image.sprite.name)} {image.type} {Color(image.color)}" + (image.raycastTarget ? " catches pointer" : "");
            var group = component.TryCast<CanvasGroup>();
            if (group != null) return $"canvas group alpha {Number(group.alpha)}" + (group.blocksRaycasts ? "" : " lets pointer through");
            var behaviour = component.TryCast<Behaviour>();
            return component.GetIl2CppType().Name + (behaviour != null && !behaviour.enabled ? " (off)" : "");
        }

        static string Short(string text)
        {
            var line = (text ?? "").Replace('\n', ' ');
            return line.Length > 40 ? line[..37] + "..." : line;
        }

        static string Color(Color color) => "#" + ColorUtility.ToHtmlStringRGBA(color);

        static string YesNo(bool yes) => yes ? "yes" : "no";

        static string Number(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
