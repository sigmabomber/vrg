using System;
using System.Collections.Generic;
using UnityEngine;

namespace Doody.GameEvents
{
    /// <summary>
    /// Mark events that should be synchronized over network.
    /// Events without this interface remain local-only.
    /// </summary>
    public interface INetworkEvent
    {
        // Marker interface - no methods needed
    }

    /// <summary>
    /// Optional: Control how events are delivered over network.
    /// </summary>
    public enum DeliveryMode
    {
        Unreliable,     // Fast, may drop packets (movement updates)
        Reliable,       // Guaranteed delivery (damage, pickups)
        ReliableOrdered // Guaranteed + in-order (chat messages)
    }

    /// <summary>
    /// Optional: Add to events that need specific delivery guarantees.
    /// </summary>
    public interface IReliableEvent : INetworkEvent
    {
        DeliveryMode DeliveryMode { get; }
    }

    /// <summary>
    /// Wrapper for network event transmission with metadata.
    /// </summary>
    [Serializable]
    public struct NetworkEventPacket
    {
        public ulong EventId;           // Unique ID for deduplication
        public uint SequenceNumber;     // For ordering
        public string TypeName;         // Full type name
        public byte[] Data;             // Serialized event data
        public DeliveryMode Delivery;   // How to send it
        public float Timestamp;         // When it was sent

        public NetworkEventPacket(ulong id, uint sequence, string typeName, byte[] data, DeliveryMode delivery)
        {
            EventId = id;
            SequenceNumber = sequence;
            TypeName = typeName;
            Data = data;
            Delivery = delivery;
            Timestamp = Time.time;
        }
    }

    /// <summary>
    /// Extension to Events class for multiplayer support with performance optimizations.
    /// </summary>
    public static class NetworkEvents
    {
        private static INetworkEventTransport transport;

        // Performance: Cache delegates to avoid reflection on every receive
        private static Dictionary<Type, Action<object>> publishDelegates = new Dictionary<Type, Action<object>>();

        // Thread safety: Queue events from network thread to main thread
        private static Queue<object> receiveQueue = new Queue<object>();
        private static object queueLock = new object();

        // Event tracking for deduplication and ordering
        private static ulong nextEventId = 1;
        private static uint sequenceNumber = 0;
        private static HashSet<ulong> processedEventIds = new HashSet<ulong>();
        private static Dictionary<Type, uint> lastSequenceByType = new Dictionary<Type, uint>();

        // Settings
        public static bool EnableLogging { get; set; } = false;
        public static bool EnableDeduplication { get; set; } = true;
        public static bool EnableSequenceValidation { get; set; } = true;
        public static int MaxProcessedIdsTracked { get; set; } = 1000;

        /// <summary>
        /// Initialize with your networking solution.
        /// Must be called on main Unity thread.
        /// </summary>
        public static void Initialize(INetworkEventTransport networkTransport)
        {
            transport = networkTransport;

            if (transport != null)
            {
                transport.OnEventReceived += HandleReceivedEventThreadSafe;
                Debug.Log("[NetworkEvents] Initialized with transport: " + networkTransport.GetType().Name);
            }
        }

        /// <summary>
        /// Process queued network events. Call this from Update() or it happens automatically
        /// if using EventProcessor.
        /// </summary>
        public static void ProcessReceivedEvents()
        {
            lock (queueLock)
            {
                while (receiveQueue.Count > 0)
                {
                    var eventData = receiveQueue.Dequeue();
                    ProcessReceivedEvent(eventData);
                }
            }
        }

        /// <summary>
        /// Publish an event - automatically routes to network if it's INetworkEvent.
        /// </summary>
        public static void Publish<T>(T eventData)
        {
            // Always publish locally first
            Events.Publish(eventData);

            // If it's a network event and we have a transport, send it
            if (eventData is INetworkEvent && transport != null && transport.IsConnected)
            {
                SendToNetwork(eventData);
            }
        }

        /// <summary>
        /// Publish event only to network (skip local).
        /// Useful when you've already handled it locally.
        /// </summary>
        public static void PublishRemote<T>(T eventData) where T : INetworkEvent
        {
            if (transport != null && transport.IsConnected)
            {
                try
                {
                    SendToNetwork(eventData);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[NetworkEvents] PublishRemote failed for {typeof(T).Name}: {ex}");
                }
            }
        }

        private static void SendToNetwork<T>(T eventData)
        {
            try
            {
                // Generate unique event ID and sequence
                ulong eventId = nextEventId++;
                uint sequence = sequenceNumber++;

                // Determine delivery mode
                DeliveryMode delivery = DeliveryMode.Reliable;
                if (eventData is IReliableEvent reliableEvent)
                {
                    delivery = reliableEvent.DeliveryMode;
                }

                // Serialize event data
                byte[] data = transport.Serialize(eventData);
                string typeName = typeof(T).AssemblyQualifiedName;

                // Create packet
                var packet = new NetworkEventPacket(eventId, sequence, typeName, data, delivery);

                // Send via transport
                transport.SendEvent(packet);

                if (EnableLogging)
                {
                    Debug.Log($"[NetworkEvents] Sent {typeof(T).Name} (ID: {eventId}, Seq: {sequence}, Mode: {delivery})");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NetworkEvents] Failed to send {typeof(T).Name}: {ex}");
            }
        }

        /// <summary>
        /// Thread-safe event receive handler. Queues event to main thread.
        /// </summary>
        private static void HandleReceivedEventThreadSafe(NetworkEventPacket packet)
        {
            // Deduplication check
            if (EnableDeduplication)
            {
                if (processedEventIds.Contains(packet.EventId))
                {
                    if (EnableLogging)
                        Debug.Log($"[NetworkEvents] Dropped duplicate event ID {packet.EventId}");
                    return;
                }

                processedEventIds.Add(packet.EventId);

                // Cleanup old IDs to prevent memory leak
                if (processedEventIds.Count > MaxProcessedIdsTracked)
                {
                    processedEventIds.Clear();
                }
            }

            try
            {
                // Deserialize on receive thread (or defer if needed)
                Type eventType = Type.GetType(packet.TypeName);
                if (eventType == null)
                {
                    Debug.LogError($"[NetworkEvents] Unknown event type: {packet.TypeName}");
                    return;
                }

                object eventData = transport.Deserialize(packet.Data, eventType);

                // Sequence validation (optional)
                if (EnableSequenceValidation)
                {
                    if (lastSequenceByType.TryGetValue(eventType, out uint lastSeq))
                    {
                        // Allow some out-of-order for unreliable events
                        if (packet.Delivery == DeliveryMode.ReliableOrdered && packet.SequenceNumber <= lastSeq)
                        {
                            if (EnableLogging)
                                Debug.LogWarning($"[NetworkEvents] Out-of-order event {eventType.Name} (expected >{lastSeq}, got {packet.SequenceNumber})");
                            return;
                        }
                    }
                    lastSequenceByType[eventType] = packet.SequenceNumber;
                }

                // Queue for main thread processing
                lock (queueLock)
                {
                    receiveQueue.Enqueue(eventData);
                }

                if (EnableLogging)
                {
                    Debug.Log($"[NetworkEvents] Queued {eventType.Name} (ID: {packet.EventId}, Seq: {packet.SequenceNumber})");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NetworkEvents] Failed to process received event: {ex}");
            }
        }

        /// <summary>
        /// Process a received event on the main thread using cached delegates.
        /// </summary>
        private static void ProcessReceivedEvent(object eventData)
        {
            if (eventData == null)
                return;

            Type eventType = eventData.GetType();

            // Get or create cached publish delegate
            if (!publishDelegates.TryGetValue(eventType, out var publishDelegate))
            {
                // Create delegate once and cache it
                var publishMethod = typeof(Events).GetMethod("Publish");
                var genericMethod = publishMethod.MakeGenericMethod(eventType);

                publishDelegate = (obj) => genericMethod.Invoke(null, new[] { obj });
                publishDelegates[eventType] = publishDelegate;
            }

            // Invoke cached delegate
            try
            {
                publishDelegate(eventData);

                if (EnableLogging)
                {
                    Debug.Log($"[NetworkEvents] Published received event {eventType.Name}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NetworkEvents] Failed to publish received {eventType.Name}: {ex}");
            }
        }

        /// <summary>
        /// Clear all tracking data (useful for scene transitions).
        /// </summary>
        public static void Reset()
        {
            lock (queueLock)
            {
                receiveQueue.Clear();
            }
            processedEventIds.Clear();
            lastSequenceByType.Clear();
            nextEventId = 1;
            sequenceNumber = 0;
        }

        /// <summary>
        /// Cleanup - call when disconnecting from network session.
        /// </summary>
        public static void Shutdown()
        {
            if (transport != null)
            {
                transport.OnEventReceived -= HandleReceivedEventThreadSafe;
                transport = null;
            }
            Reset();
        }

        /// <summary>
        /// Get statistics for debugging.
        /// </summary>
        public static string GetStats()
        {
            lock (queueLock)
            {
                return $"Queue: {receiveQueue.Count}, Processed: {processedEventIds.Count}, " +
                       $"NextID: {nextEventId}, Seq: {sequenceNumber}";
            }
        }
    }

    /// <summary>
    /// Interface for networking solutions with improved responsibilities.
    /// </summary>
    public interface INetworkEventTransport
    {
        bool IsConnected { get; }
        event Action<NetworkEventPacket> OnEventReceived;

        /// <summary>
        /// Send event packet over network with specified delivery mode.
        /// </summary>
        void SendEvent(NetworkEventPacket packet);

        /// <summary>
        /// Serialize event data to bytes. Implement your preferred serialization.
        /// Default: BinaryFormatter, JSON, MessagePack, etc.
        /// </summary>
        byte[] Serialize(object eventData);

        /// <summary>
        /// Deserialize event data from bytes.
        /// </summary>
        object Deserialize(byte[] data, Type type);
    }
}