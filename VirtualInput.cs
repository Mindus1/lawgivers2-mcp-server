using System;
using System.Linq;
using HarmonyLib;
using Lawgivers;
using UnityEngine;
using UnityEngine.InputSystem;
using Time = UnityEngine.Time;

namespace McpServer
{
    /// <summary>
    /// Key presses and map clicks the game sees as real, without touching the player's keyboard or mouse.
    /// The game reads its keys through <see cref="Controls"/>, which is patched here.
    /// UI clicks don't go through here but through <see cref="Pointer"/>.
    /// </summary>
    static class VirtualInput
    {
        // Frames after the start of a key press: press, hold, release.
        const int PressFrame = 1, ReleaseFrame = 3, DoneFrame = 4;

        static int startFrame = -1;
        static Controls.EID? action;
        static Key? key;

        public static bool IsBusy => startFrame >= 0 && Time.frameCount - startFrame < DoneFrame;

        /// <summary>The game's map camera, or null outside a game.</summary>
        public static CameraWorld Map => Resources.FindObjectsOfTypeAll<CameraWorld>().FirstOrDefault(camera => camera.isActiveAndEnabled);

        /// <summary>Clicks the map at a point in Unity's screen coordinates.</summary>
        public static void Click(Vector2 at)
        {
            // The game's own selection, which a map click ends up in.
            var map = Map ?? throw new InvalidOperationException("There's no map right now.");
            map.Selection(at);
        }

        /// <summary>Presses one of the game's actions (Close, Turn, Party, ZoomIn...) or a keyboard key (Escape, Space, A...).</summary>
        public static string Press(string name)
        {
            if (IsBusy) throw new InvalidOperationException("Another key press is still running.");
            action = null;
            key = null;
            if (Enum.TryParse<Controls.EID>(name, ignoreCase: true, out var gameAction)) action = gameAction;
            else if (Enum.TryParse<Key>(name, ignoreCase: true, out var keyboardKey)) key = keyboardKey;
            else throw new ArgumentException($"\"{name}\" is neither a game action ({string.Join(", ", Enum.GetNames(typeof(Controls.EID)))}) " +
                "nor a key of Unity's input system (Escape, Space, A, Digit1, F5...).");
            startFrame = Time.frameCount;
            return action != null ? $"Pressed the game's {action} action" : $"Pressed {key}";
        }

        static int Frame => startFrame < 0 ? -1 : Time.frameCount - startFrame;
        static bool Down => Frame == PressFrame;
        static bool Held => Frame >= PressFrame && Frame < ReleaseFrame;
        static bool Up => Frame == ReleaseFrame;

        // Each patch answers for the game only while our key is pressed, otherwise the real keyboard counts.

        [HarmonyPatch(typeof(Controls), nameof(Controls.GetKeyDown))]
        static class KeyDown
        {
            static bool Prefix(Key key, ref bool __result) => Answer(VirtualInput.key == key, Down, ref __result);
        }

        [HarmonyPatch(typeof(Controls), nameof(Controls.GetKey))]
        static class KeyHeld
        {
            static bool Prefix(Key key, ref bool __result) => Answer(VirtualInput.key == key, Held, ref __result);
        }

        [HarmonyPatch(typeof(Controls), nameof(Controls.GetKeyUp))]
        static class KeyUp
        {
            static bool Prefix(Key key, ref bool __result) => Answer(VirtualInput.key == key, Up, ref __result);
        }

        [HarmonyPatch(typeof(Controls), nameof(Controls.Down))]
        static class ActionDown
        {
            static bool Prefix(Controls.EID id, ref bool __result) => Answer(action == id, Down, ref __result);
        }

        [HarmonyPatch(typeof(Controls), nameof(Controls.Get))]
        static class ActionHeld
        {
            static bool Prefix(Controls.EID id, ref bool __result) => Answer(action == id, Held, ref __result);
        }

        [HarmonyPatch(typeof(Controls), nameof(Controls.Up))]
        static class ActionUp
        {
            static bool Prefix(Controls.EID id, ref bool __result) => Answer(action == id, Up, ref __result);
        }

        static bool Answer(bool ours, bool pressed, ref bool result)
        {
            if (!ours || !pressed) return true;
            result = true;
            return false;
        }
    }
}
