using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BepInEx;
using UnityEngine;
using Object = UnityEngine.Object;

namespace McpServer
{
    /// <summary>Screenshots from the game's own frame: no cursor, no other windows, even when the game is covered.</summary>
    static class Screenshot
    {
        static readonly byte[] PngEnd = { 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82 };

        /// <summary>Asks Unity for a screenshot. It writes the file at the end of the frame.</summary>
        public static (string path, Vector2Int screen) Start()
        {
            var folder = Path.Combine(Paths.BepInExRootPath, "screenshots");
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss-fff}.png");
            ScreenCapture.CaptureScreenshot(path, 1);
            return (path, new Vector2Int(Screen.width, Screen.height));
        }

        public static async Task<byte[]> WaitForFile(string path)
        {
            for (var tries = 0; tries < 100; tries++)
            {
                await Task.Delay(100);
                if (!File.Exists(path)) continue;
                try
                {
                    var bytes = await File.ReadAllBytesAsync(path);
                    if (bytes.Length > PngEnd.Length && bytes[^PngEnd.Length..].SequenceEqual(PngEnd)) return bytes;
                }
                catch (IOException)
                {
                    // Still being written.
                }
            }
            throw new TimeoutException("The game didn't write the screenshot within 10 seconds.");
        }

        /// <summary>The part of a screenshot inside <paramref name="area"/> (screen pixels from the bottom left), full size.</summary>
        public static byte[] Crop(byte[] png, RectInt area)
        {
            var full = Load(png);
            var cut = Render(full, new Vector2Int(full.width, full.height), area);
            var result = cut.EncodeToPNG().ToArray();
            Object.Destroy(full);
            Object.Destroy(cut);
            return result;
        }

        /// <summary>A copy at most <paramref name="width"/> wide, small enough to send back.</summary>
        public static (byte[] png, Vector2Int size) Shrink(byte[] png, int width)
        {
            var full = Load(png);
            width = Math.Min(width, full.width);
            var size = new Vector2Int(width, Mathf.RoundToInt(full.height * (float)width / full.width));
            var small = Render(full, size, new RectInt(0, 0, size.x, size.y));
            var result = small.EncodeToPNG().ToArray();
            Object.Destroy(full);
            Object.Destroy(small);
            return (result, size);
        }

        static Texture2D Load(byte[] png)
        {
            var texture = new Texture2D(2, 2);
            ImageConversion.LoadImage(texture, png);
            return texture;
        }

        // The game's Unity has only the simplest Graphics.Blit, so scaling and cropping are two steps:
        // draw the texture at a size, then read back part of it.
        static Texture2D Render(Texture source, Vector2Int size, RectInt area)
        {
            var target = RenderTexture.GetTemporary(size.x, size.y, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(source, target);
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var result = new Texture2D(area.width, area.height, TextureFormat.RGB24, false);
            result.ReadPixels(new Rect(area.x, area.y, area.width, area.height), 0, 0);
            result.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            return result;
        }
    }
}
