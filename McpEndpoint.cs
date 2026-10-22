using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace McpServer
{
    /// <summary>
    /// MCP over HTTP ("Streamable HTTP" without the optional event stream): one JSON-RPC request per POST,
    /// answered with plain JSON. Only reachable from this PC.
    /// </summary>
    static class McpEndpoint
    {
        public const string Url = "http://127.0.0.1:47120/mcp";

        public static void Start()
        {
            var listener = new HttpListener();
            listener.Prefixes.Add("http://127.0.0.1:47120/");
            try
            {
                listener.Start();
            }
            catch (HttpListenerException e)
            {
                Plugin.Log.LogError($"Can't listen on {Url}: {e.Message}");
                return;
            }
            Plugin.Log.LogInfo($"Listening on {Url}");

            _ = Listen(listener);
        }

        static async Task Listen(HttpListener listener)
        {
            while (listener.IsListening) _ = Answer(await listener.GetContextAsync());
        }

        static async Task Answer(HttpListenerContext context)
        {
            var response = context.Response;
            try
            {
                var request = context.Request;
                if (request.Url?.AbsolutePath.TrimEnd('/') != "/mcp") response.StatusCode = 404;
                // A web page in the browser could otherwise reach the server (DNS rebinding).
                else if (!IsFromThisPc(request.Headers["Origin"])) response.StatusCode = 403;
                else if (request.HttpMethod != "POST") response.StatusCode = 405;
                else
                {
                    using var reader = new StreamReader(request.InputStream, Encoding.UTF8);
                    var message = JsonNode.Parse(await reader.ReadToEndAsync())?.AsObject();
                    var reply = message?["id"] == null ? null : await Reply(message);
                    if (reply == null) response.StatusCode = 202;
                    else await Send(response, reply);
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Request failed: {e}");
                response.StatusCode = 500;
            }
            response.Close();
        }

        static bool IsFromThisPc(string origin) =>
            origin == null || Uri.TryCreate(origin, UriKind.Absolute, out var uri) && (uri.IsLoopback || uri.Host == "localhost");

        static async Task<JsonObject> Reply(JsonObject message)
        {
            var reply = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = JsonNode.Parse(message["id"].ToJsonString()) };
            var parameters = message["params"] as JsonObject;
            switch ((string)message["method"])
            {
                case "initialize":
                    reply["result"] = new JsonObject
                    {
                        ["protocolVersion"] = (string)parameters?["protocolVersion"] ?? "2025-06-18",
                        ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
                        ["serverInfo"] = new JsonObject { ["name"] = "lawgivers2", ["version"] = "1.0.0" },
                        ["instructions"] = Tools.Instructions,
                    };
                    break;
                case "ping":
                    reply["result"] = new JsonObject();
                    break;
                case "tools/list":
                    reply["result"] = new JsonObject { ["tools"] = new JsonArray(Tools.All.Select(tool => (JsonNode)tool.Describe()).ToArray()) };
                    break;
                case "tools/call":
                    reply["result"] = await Tools.Call((string)parameters?["name"], parameters?["arguments"] as JsonObject ?? new JsonObject());
                    break;
                default:
                    reply["error"] = new JsonObject { ["code"] = -32601, ["message"] = $"Unknown method {message["method"]}" };
                    break;
            }
            return reply;
        }

        static async Task Send(HttpListenerResponse response, JsonNode reply)
        {
            var bytes = Encoding.UTF8.GetBytes(reply.ToJsonString());
            response.ContentType = "application/json";
            response.ContentLength64 = bytes.Length;
            await response.OutputStream.WriteAsync(bytes);
        }
    }
}
