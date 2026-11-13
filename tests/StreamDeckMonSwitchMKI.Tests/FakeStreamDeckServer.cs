using Fleck;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace StreamDeckMonSwitch.Tests
{
    /**
     * <summary>
     * A minimal fake of the Stream Deck application: a WebSocket server that
     * speaks the Stream Deck SDK protocol. Plugin messages are queued for
     * assertions; `getGlobalSettings` requests are answered automatically.
     * </summary>
     */
    public class FakeStreamDeckServer : IDisposable
    {
        private readonly WebSocketServer server;
        private readonly ManualResetEventSlim pluginConnected = new ManualResetEventSlim(false);
        private volatile IWebSocketConnection pluginSocket;

        public int Port { get; private set; }

        /** Messages received from the plugin, awaiting assertion. */
        public BlockingCollection<JObject> Received { get; } = new BlockingCollection<JObject>();

        /** Every message received from the plugin (diagnostics). */
        public List<string> RawLog { get; } = new List<string>();

        public FakeStreamDeckServer(int port)
        {
            Port = port;
            FleckLog.Level = LogLevel.Error;

            server = new WebSocketServer($"ws://127.0.0.1:{port}")
            {
                RestartAfterListenError = true
            };

            server.Start(socket =>
            {
                socket.OnOpen = () =>
                {
                    pluginSocket = socket;
                    pluginConnected.Set();
                };

                socket.OnMessage = message =>
                {
                    try
                    {
                        HandleMessage(socket, message);
                    }
                    catch (Exception)
                    {
                        // Never let a protocol error escape onto Fleck's background
                        // thread - an unhandled exception there crashes the test host.
                    }
                };
            });
        }

        private void HandleMessage(IWebSocketConnection socket, string message)
        {
            JObject parsed;
            try
            {
                parsed = JObject.Parse(message);
            }
            catch (JsonReaderException)
            {
                return;
            }

            lock (RawLog)
            {
                RawLog.Add(message);
            }

            // The tools library requests global settings on connect; answer it
            // so the plugin does not sit on a pending request.
            if (parsed.Value<string>("event") == "getGlobalSettings")
            {
                var response = new JObject
                {
                    ["event"] = "didReceiveGlobalSettings",
                    ["context"] = parsed.Value<string>("context"),
                    ["payload"] = new JObject { ["settings"] = new JObject() }
                };
                socket.Send(response.ToString(Formatting.None));
                return;
            }

            Received.Add(parsed);
        }

        public bool WaitForPluginConnection(TimeSpan timeout)
        {
            return pluginConnected.Wait(timeout);
        }

        /** Sends an event (as the Stream Deck application would) to the plugin. */
        public void SendToPlugin(JObject message)
        {
            var socket = pluginSocket;
            if (socket == null)
            {
                throw new InvalidOperationException("Plugin is not connected to the fake Stream Deck server");
            }

            socket.Send(message.ToString(Formatting.None)).Wait();
        }

        /**
         * Waits for a plugin message matching the predicate; non-matching messages
         * are consumed and discarded. Throws TimeoutException with the raw protocol
         * log on failure to ease diagnostics.
         */
        public JObject AwaitMessage(Func<JObject, bool> predicate, TimeSpan timeout, string description)
        {
            var deadline = DateTime.UtcNow + timeout;

            while (true)
            {
                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero)
                {
                    break;
                }

                if (!Received.TryTake(out var message, remaining))
                {
                    break;
                }

                if (predicate(message))
                {
                    return message;
                }
            }

            string log;
            lock (RawLog)
            {
                log = string.Join(Environment.NewLine, RawLog);
            }

            throw new TimeoutException(
                $"Timed out after {timeout.TotalSeconds}s waiting for plugin message: {description}" +
                Environment.NewLine + "--- protocol log ---" + Environment.NewLine + log);
        }

        /** Consumes all currently queued messages matching the predicate. */
        public int DrainQueued(Func<JObject, bool> predicate)
        {
            var count = 0;
            while (Received.TryTake(out var message, TimeSpan.FromMilliseconds(50)))
            {
                if (predicate(message))
                {
                    count++;
                }
            }
            return count;
        }

        public void Dispose()
        {
            try
            {
                pluginSocket?.Close();
            }
            catch
            {
                // best effort
            }

            server.Dispose();
            Received.Dispose();
        }
    }
}
