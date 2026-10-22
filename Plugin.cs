using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using ModLibrary;
using UnityEngine;

namespace McpServer
{
    [BepInPlugin(Guid, "MCP Server", "1.0.0")]
    [BepInDependency(ModLibrary.Plugin.Guid)]
    [ModDescription("Lets AI tools see and drive the game over MCP, for testing mods. Adds nothing visible.")]
    public class Plugin : BasePlugin
    {
        public const string Guid = "mindus.lawgivers2.mcpserver";

        internal static new ManualLogSource Log;

        public override void Load()
        {
            Log = base.Log;
            // Tools run on Unity's main thread, which stops while the game is in the background.
            Application.runInBackground = true;
            AddComponent<MainThread>();
            new Harmony(Guid).PatchAll();
            McpEndpoint.Start();
        }
    }
}
