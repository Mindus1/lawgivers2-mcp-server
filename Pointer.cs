using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace McpServer
{
    /// <summary>
    /// Pointer events sent the way Unity's input module sends them, to whatever is on top at a point.
    /// So a click only reaches what the player could click.
    /// </summary>
    static class Pointer
    {
        static GameObject hovered;

        public static string Click(UiElements.Element element)
        {
            if (element.Registered?.Click != null)
            {
                element.Registered.Click();
                return $"Clicked {element.Id}";
            }
            if (!element.Enabled) throw new InvalidOperationException($"{element.Id} is disabled.");
            EnsureReachable(element);
            Send(element.Center, click: true);
            return $"Clicked {element.Id}";
        }

        public static string Hover(UiElements.Element element)
        {
            EnsureReachable(element);
            Send(element.Center, click: false);
            return $"Pointer is on {element.Id}";
        }

        /// <summary>Clicks the UI at a point, or the map when there's no UI there.</summary>
        public static string ClickAt(Vector2 point)
        {
            var position = FromTopLeft(point);
            if (TopHit(position, out _) != null) return $"Clicked {UiElements.PathOf(Send(position, click: true).transform)}";
            Leave();
            VirtualInput.Click(position);
            return $"Clicked the map at {point.x:0},{point.y:0}";
        }

        public static string HoverAt(Vector2 point) => $"Pointer is on {UiElements.PathOf(Send(FromTopLeft(point), click: false).transform)}";

        public static string Drag(UiElements.Element element, Vector2 to)
        {
            if (!element.Enabled) throw new InvalidOperationException($"{element.Id} is disabled.");
            EnsureReachable(element);
            return DragBetween(element.Center, FromTopLeft(to));
        }

        public static string DragAt(Vector2 from, Vector2 to) => DragBetween(FromTopLeft(from), FromTopLeft(to));

        /// <summary>Presses, moves in small steps and lets go, sending the events in the order Unity's input module does.</summary>
        static string DragBetween(Vector2 from, Vector2 to)
        {
            Leave();
            var hit = TopHit(from, out var raycast) ?? throw new InvalidOperationException("There's no UI at that point.");
            var dragged = ExecuteEvents.GetEventHandler<IDragHandler>(hit) ??
                throw new InvalidOperationException($"{UiElements.PathOf(hit.transform)} doesn't take drags.");
            var data = new PointerEventData(EventSystem.current)
            {
                position = from,
                pressPosition = from,
                button = PointerEventData.InputButton.Left,
                pointerCurrentRaycast = raycast,
                pointerPressRaycast = raycast,
                pointerDrag = dragged,
            };

            ExecuteEvents.ExecuteHierarchy(hit, data, ExecuteEvents.pointerEnterHandler);
            data.pointerPress = ExecuteEvents.ExecuteHierarchy(hit, data, ExecuteEvents.pointerDownHandler);
            data.rawPointerPress = hit;
            ExecuteEvents.Execute(dragged, data, ExecuteEvents.initializePotentialDrag);
            ExecuteEvents.Execute(dragged, data, ExecuteEvents.beginDragHandler);
            data.dragging = true;
            const int steps = 10;
            for (var step = 1; step <= steps; step++)
            {
                var position = Vector2.Lerp(from, to, (float)step / steps);
                data.delta = position - data.position;
                data.position = position;
                ExecuteEvents.Execute(dragged, data, ExecuteEvents.dragHandler);
            }
            if (data.pointerPress != null) ExecuteEvents.Execute(data.pointerPress, data, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.Execute(dragged, data, ExecuteEvents.endDragHandler);
            data.dragging = false;
            ExecuteEvents.ExecuteHierarchy(hit, data, ExecuteEvents.pointerExitHandler);
            return $"Dragged {UiElements.PathOf(dragged.transform)}";
        }

        /// <summary>The topmost UI object at a point in Unity's screen coordinates, or null.</summary>
        public static GameObject TopHit(Vector2 position, out RaycastResult raycast)
        {
            var events = EventSystem.current ?? throw new InvalidOperationException("The game has no EventSystem right now.");
            var results = new Il2CppSystem.Collections.Generic.List<RaycastResult>();
            events.RaycastAll(new PointerEventData(events) { position = position }, results);
            raycast = results.Count > 0 ? results[0] : default;
            return results.Count > 0 ? results[0].gameObject : null;
        }

        static void EnsureReachable(UiElements.Element element)
        {
            if (!element.Covered) return;
            var hit = TopHit(element.Center, out _);
            throw new InvalidOperationException(hit == null
                ? $"{element.Id} doesn't take pointer events."
                : $"{element.Id} is covered by {UiElements.PathOf(hit.transform)}.");
        }

        static GameObject Send(Vector2 position, bool click)
        {
            Leave();
            var hit = TopHit(position, out var raycast) ?? throw new InvalidOperationException("There's no UI at that point. The map only takes clicks.");
            var data = new PointerEventData(EventSystem.current)
            {
                position = position,
                button = PointerEventData.InputButton.Left,
                pointerCurrentRaycast = raycast,
                pointerPressRaycast = raycast,
            };

            ExecuteEvents.ExecuteHierarchy(hit, data, ExecuteEvents.pointerEnterHandler);
            if (!click)
            {
                hovered = hit;
                return hit;
            }

            data.pointerPress = ExecuteEvents.ExecuteHierarchy(hit, data, ExecuteEvents.pointerDownHandler);
            data.rawPointerPress = hit;
            data.eligibleForClick = true;
            data.clickCount = 1;
            data.clickTime = Time.unscaledTime;
            ExecuteEvents.ExecuteHierarchy(hit, data, ExecuteEvents.pointerUpHandler);
            var clicked = ExecuteEvents.ExecuteHierarchy(hit, data, ExecuteEvents.pointerClickHandler);
            // Leave again, so no hover highlight or tooltip stays behind.
            if (hit != null) ExecuteEvents.ExecuteHierarchy(hit, data, ExecuteEvents.pointerExitHandler);

            if (clicked == null) throw new InvalidOperationException($"{UiElements.PathOf(hit.transform)} doesn't react to clicks.");
            return hit;
        }

        static void Leave()
        {
            if (hovered != null)
                ExecuteEvents.ExecuteHierarchy(hovered, new PointerEventData(EventSystem.current), ExecuteEvents.pointerExitHandler);
            hovered = null;
        }

        static Vector2 FromTopLeft(Vector2 point) => new(point.x, Screen.height - point.y);
    }
}
