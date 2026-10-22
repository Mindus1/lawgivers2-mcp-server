using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using UnityEngine;

namespace McpServer
{
    /// <summary>Runs work from the server's threads on Unity's main thread, the only one allowed to touch the game.</summary>
    public class MainThread : MonoBehaviour
    {
        static readonly ConcurrentQueue<Action> queue = new();

        public MainThread(IntPtr pointer) : base(pointer) { }

        public static async Task<T> Run<T>(Func<T> work)
        {
            var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            queue.Enqueue(() =>
            {
                try { done.SetResult(work()); }
                catch (Exception e) { done.SetException(e); }
            });
            if (await Task.WhenAny(done.Task, Task.Delay(TimeSpan.FromSeconds(10))) != done.Task)
                throw new TimeoutException("The game didn't respond within 10 seconds. Is it minimized or stuck loading?");
            return await done.Task;
        }

        void Update()
        {
            while (queue.TryDequeue(out var work)) work();
        }
    }
}
