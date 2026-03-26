// Only compiles if you have Unity Netcode for GameObjects installed
#if UNITY_NETCODE_FOR_GAMEOBJECTS

using System;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using Unity.Netcode;
using UnityEngine;
using Doody.GameEvents;

namespace Doody.GameEvents.Netcode
{
    /// <summary>
    /// Unity Netcode for GameObjects implementation with delivery guarantees.
    /// 
    /// SETUP:
    /// 1. Add this component to a GameObject with NetworkObject
    /// 2. Make sure it spawns when your network session starts
    /// 3. Configure serialization method in inspector
    /// </summary>
    public class NetcodeEventTransport : NetworkBehaviour, INetworkEventTransport
    {
        public bool IsConnected => NetworkManager.Singleton != null && NetworkManager.Singleton.IsConnectedClient;
        public event Action<NetworkEventPacket> OnEventReceived;

        [Header("Settings")]
        [Tooltip("Automatically initialize NetworkEvents when this spawns")]
        public bool autoInitialize = true;

        [Tooltip("Serialization method to use")]
        public SerializationMethod serializationMethod = SerializationMethod.BinaryFormatter;

        [Header("Debug")]
        [Tooltip("Log all sent/received events")]
        public bool enableDebugLogging = false;

        [Tooltip("Show statistics in inspector")]
        public bool showStats = true;

        // Statistics
        private int sentCount = 0;
        private int receivedCount = 0;
        private int droppedCount = 0;

        public enum SerializationMethod
        {
            BinaryFormatter,    // Default, works with any [Serializable] type
            JsonUtility,        // Faster, requires Unity-serializable types
            // MessagePack       // Uncomment if you add MessagePack package
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            
            if (autoInitialize)
            {
                NetworkEvents.Initialize(this);
                NetworkEvents.EnableLogging = enableDebugLogging;
                Debug.Log($"[NetcodeEventTransport] Initialized on {(IsServer ? "Server" : "Client")}");
            }
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            NetworkEvents.Shutdown();
        }

        public void SendEvent(NetworkEventPacket packet)
        {
            if (!IsConnected)
            {
                Debug.LogWarning("[NetcodeEventTransport] Cannot send - not connected");
                return;
            }

            try
            {
                // Choose RPC based on delivery mode and role
                if (IsServer)
                {
                    // Server broadcasts to all clients
                    SendToClientsRpc(packet);
                }
                else if (IsClient)
                {
                    // Client sends to server
                    SendToServerRpc(packet);
                }

                sentCount++;

                if (enableDebugLogging)
                {
                    Debug.Log($"[NetcodeEventTransport] Sent event ID {packet.EventId} ({packet.Delivery})");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NetcodeEventTransport] Failed to send event: {ex}");
            }
        }

        // Reliable ordered delivery (default)
        [ClientRpc(RequireOwnership = false)]
        private void SendToClientsRpc(NetworkEventPacket packet)
        {
            // Don't process on server that sent it
            if (IsServer) return;
            
            ReceiveEvent(packet);
        }

        [ServerRpc(RequireOwnership = false, Delivery = RpcDelivery.Reliable)]
        private void SendToServerRpc(NetworkEventPacket packet)
        {
            // Server receives from client
            ReceiveEvent(packet);
            
            // Broadcast to all other clients based on delivery mode
            SendToClientsRpc(packet);
        }

        // Alternative: Unreliable delivery for high-frequency events
        [ClientRpc(Delivery = RpcDelivery.Unreliable)]
        private void SendToClientsUnreliableRpc(NetworkEventPacket packet)
        {
            if (IsServer) return;
            ReceiveEvent(packet);
        }

        private void ReceiveEvent(NetworkEventPacket packet)
        {
            try
            {
                receivedCount++;
                OnEventReceived?.Invoke(packet);

                if (enableDebugLogging)
                {
                    Debug.Log($"[NetcodeEventTransport] Received event ID {packet.EventId}");
                }
            }
            catch (Exception ex)
            {
                droppedCount++;
                Debug.LogError($"[NetcodeEventTransport] Failed to process received event: {ex}");
            }
        }

        // Serialization implementations
        public byte[] Serialize(object eventData)
        {
            switch (serializationMethod)
            {
                case SerializationMethod.BinaryFormatter:
                    return SerializeBinary(eventData);

                case SerializationMethod.JsonUtility:
                    return SerializeJson(eventData);

                default:
                    throw new NotImplementedException($"Serialization method {serializationMethod} not implemented");
            }
        }

        public object Deserialize(byte[] data, Type type)
        {
            switch (serializationMethod)
            {
                case SerializationMethod.BinaryFormatter:
                    return DeserializeBinary(data);

                case SerializationMethod.JsonUtility:
                    return DeserializeJson(data, type);

                default:
                    throw new NotImplementedException($"Deserialization method {serializationMethod} not implemented");
            }
        }

        // BinaryFormatter (works with any [Serializable] type)
        private byte[] SerializeBinary(object obj)
        {
            using (var ms = new MemoryStream())
            {
                var formatter = new BinaryFormatter();
                formatter.Serialize(ms, obj);
                return ms.ToArray();
            }
        }

        private object DeserializeBinary(byte[] data)
        {
            using (var ms = new MemoryStream(data))
            {
                var formatter = new BinaryFormatter();
                return formatter.Deserialize(ms);
            }
        }

        // JsonUtility (faster, but requires Unity-compatible types)
        private byte[] SerializeJson(object obj)
        {
            string json = JsonUtility.ToJson(obj);
            return System.Text.Encoding.UTF8.GetBytes(json);
        }

        private object DeserializeJson(byte[] data, Type type)
        {
            string json = System.Text.Encoding.UTF8.GetString(data);
            return JsonUtility.FromJson(json, type);
        }

        // Debug GUI
        void OnGUI()
        {
            if (!showStats || !IsConnected) return;

            GUILayout.BeginArea(new Rect(10, 10, 300, 150));
            GUILayout.Box("Network Events Stats");
            GUILayout.Label($"Role: {(IsServer ? "Server" : "Client")}");
            GUILayout.Label($"Sent: {sentCount}");
            GUILayout.Label($"Received: {receivedCount}");
            GUILayout.Label($"Dropped: {droppedCount}");
            GUILayout.Label($"Queue: {NetworkEvents.GetStats()}");
            GUILayout.EndArea();
        }

        // Editor helpers
#if UNITY_EDITOR
        [ContextMenu("Clear Statistics")]
        void ClearStats()
        {
            sentCount = 0;
            receivedCount = 0;
            droppedCount = 0;
            NetworkEvents.Reset();
        }

        [ContextMenu("Test Send Event")]
        void TestSendEvent()
        {
            if (!IsConnected)
            {
                Debug.LogWarning("Not connected to network");
                return;
            }

            var testPacket = new NetworkEventPacket(
                999,
                0,
                "Test.Event",
                new byte[] { 1, 2, 3 },
                DeliveryMode.Reliable
            );

            SendEvent(testPacket);
        }
#endif
    }
}

#endif