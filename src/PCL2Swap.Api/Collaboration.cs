using System;
using System.Collections.Generic;

namespace PCL2Swap.Api
{
    /// <summary>One in-process message published between Swap modules.</summary>
    public sealed class SwapMessage
    {
        public string Topic { get; set; }
        public string Source { get; set; }
        public object Payload { get; set; }
        public DateTime TimestampUtc { get; set; }
    }

    /// <summary>
    /// Lightweight module-to-module message bus. Payloads stay inside the current PCL process and
    /// are not serialized; use JSON strings when modules must not share CLR types.
    /// </summary>
    public interface IAddonMessaging
    {
        /// <summary>Topics that currently have at least one live subscriber.</summary>
        IReadOnlyList<string> Topics { get; }

        /// <summary>
        /// Subscribes to one topic, or to "*" for every topic. The returned handle removes the
        /// subscription. Handler failures are isolated and logged against the publishing module.
        /// </summary>
        IDisposable Subscribe(string topic, Action<SwapMessage> handler);

        /// <summary>Publishes synchronously and returns the number of successful deliveries.</summary>
        int Publish(string topic, object payload = null);
    }
}

// GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover
