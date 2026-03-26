using System;
using System.Collections.Generic;
using UnityEngine;

namespace Doody.GameEvents
{

    public static class Events
    {
        private static Dictionary<Type, List<Delegate>> handlers = new Dictionary<Type, List<Delegate>>();
        private static Dictionary<object, List<(Type eventType, Delegate handler)>> ownerTracking =
            new Dictionary<object, List<(Type, Delegate)>>();

        /// <summary>
        /// Subscribe to an event type.
        /// Optionally pass 'owner' for automatic cleanup when owner is destroyed.
        /// Returns IDisposable - call .Dispose() to unsubscribe.
        /// </summary>
        public static IDisposable Subscribe<T>(Action<T> handler, object owner = null)
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            Type eventType = typeof(T);

            if (!handlers.ContainsKey(eventType))
                handlers[eventType] = new List<Delegate>();

            if (handlers[eventType].Contains(handler))
            {
                Debug.LogWarning($"[Events] Duplicate subscription to {eventType.Name} - ignoring");
                return new NullSubscription();
            }

            handlers[eventType].Add(handler);

            // Track owner for automatic cleanup
            if (owner != null)
            {
                if (!ownerTracking.ContainsKey(owner))
                    ownerTracking[owner] = new List<(Type, Delegate)>();

                ownerTracking[owner].Add((eventType, handler));
            }

            return new Subscription<T>(handler, owner);
        }

        /// <summary>
        /// Publish an event to all subscribers.
        /// Each handler is invoked independently - exceptions won't stop other handlers.
        /// </summary>
        public static void Publish<T>(T eventData)
        {
            Type eventType = typeof(T);

            if (!handlers.TryGetValue(eventType, out List<Delegate> handlerList))
                return;

            if (handlerList.Count == 0)
                return;

            // Create a copy to allow modifications during iteration
            Delegate[] handlersCopy = handlerList.ToArray();

            // Invoke each handler independently
            foreach (Delegate d in handlersCopy)
            {
                // Skip if handler was removed during iteration
                if (!handlerList.Contains(d))
                    continue;

                try
                {
                    // Safe cast and invoke
                    Action<T> typedHandler = d as Action<T>;
                    if (typedHandler != null)
                    {
                        typedHandler.Invoke(eventData);
                    }
                    else
                    {
                        Debug.LogError($"[Events] Handler type mismatch for {eventType.Name}");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[Events] Exception in handler for {eventType.Name}: {ex}");
                    // Continue to next handler - one bad handler shouldn't break others
                }
            }
        }

        /// <summary>
        /// Unsubscribe from an event (usually you use Dispose instead).
        /// </summary>
        public static void Unsubscribe<T>(Action<T> handler)
        {
            if (handler == null)
                return;

            Type eventType = typeof(T);

            if (handlers.TryGetValue(eventType, out List<Delegate> handlerList))
            {
                handlerList.Remove(handler);

                // Clean up empty list
                if (handlerList.Count == 0)
                    handlers.Remove(eventType);
            }
        }

        /// <summary>
        /// Unsubscribe all handlers owned by a specific object.
        /// Called automatically when EventListener is destroyed.
        /// </summary>
        public static void UnsubscribeAll(object owner)
        {
            if (owner == null)
                return;

            if (!ownerTracking.TryGetValue(owner, out var subscriptions))
                return;

            // Unsubscribe all handlers for this owner
            foreach (var (eventType, handler) in subscriptions)
            {
                if (handlers.TryGetValue(eventType, out List<Delegate> handlerList))
                {
                    handlerList.Remove(handler);

                    if (handlerList.Count == 0)
                        handlers.Remove(eventType);
                }
            }

            ownerTracking.Remove(owner);
        }

        /// <summary>
        /// Remove all dead Unity Object references.
        /// Called automatically by EventProcessor, but can be called manually.
        /// </summary>
        public static void CleanupDeadOwners()
        {
            var deadOwners = new List<object>();

            foreach (var owner in ownerTracking.Keys)
            {
                // Check if Unity Object is destroyed
                if (owner is UnityEngine.Object unityObj && unityObj == null)
                {
                    deadOwners.Add(owner);
                }
            }

            foreach (var deadOwner in deadOwners)
            {
                UnsubscribeAll(deadOwner);
            }

            if (deadOwners.Count > 0)
            {
                Debug.Log($"[Events] Cleaned up {deadOwners.Count} dead Unity Object subscriptions");
            }
        }

        /// <summary>
        /// Clear all event subscriptions (useful for scene transitions).
        /// </summary>
        public static void Clear()
        {
            handlers.Clear();
            ownerTracking.Clear();
        }

        /// <summary>
        /// Get subscriber count for debugging.
        /// </summary>
        public static int GetSubscriberCount<T>()
        {
            Type eventType = typeof(T);
            return handlers.TryGetValue(eventType, out var list) ? list.Count : 0;
        }

        // Internal subscription class for IDisposable pattern
        private class Subscription<T> : IDisposable
        {
            private Action<T> handler;
            private object owner;
            private bool disposed = false;

            public Subscription(Action<T> handler, object owner)
            {
                this.handler = handler;
                this.owner = owner;
            }

            public void Dispose()
            {
                if (disposed)
                    return;

                if (handler != null)
                {
                    Unsubscribe(handler);

                    // Remove from owner tracking
                    if (owner != null && ownerTracking.TryGetValue(owner, out var subscriptions))
                    {
                        subscriptions.RemoveAll(sub => sub.handler.Equals(handler));

                        if (subscriptions.Count == 0)
                            ownerTracking.Remove(owner);
                    }

                    handler = null;
                    owner = null;
                }

                disposed = true;
            }
        }

        private class NullSubscription : IDisposable
        {
            public void Dispose() { }
        }
    }

    /// <summary>
    /// Helper base class that auto-manages event subscriptions.
    /// Inherit from this instead of MonoBehaviour for automatic cleanup.
    /// </summary>
    public abstract class EventListener : MonoBehaviour
    {
        private List<IDisposable> subscriptions = new List<IDisposable>();

        /// <summary>
        /// Subscribe with automatic cleanup on destroy.
        /// </summary>
        protected void Listen<T>(Action<T> handler)
        {
            // Pass 'this' as owner for dead reference cleanup
            subscriptions.Add(Events.Subscribe(handler, this));
        }

        protected virtual void OnDestroy()
        {
            // Double cleanup - both explicit disposal and owner-based
            foreach (var sub in subscriptions)
                sub?.Dispose();

            subscriptions.Clear();
            Events.UnsubscribeAll(this);
        }
    }

    /// <summary>
    /// Automatically handles periodic cleanup of dead Unity Object references.
    /// Add this component once to your scene or it will be created automatically.
    /// </summary>
    public class EventProcessor : MonoBehaviour
    {
        private static EventProcessor instance;
        private static bool applicationQuitting = false;

        [SerializeField] private float cleanupInterval = 30f; // Cleanup every 30 seconds
        private float nextCleanupTime;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            if (instance != null)
                return;

            GameObject go = new GameObject("[EventProcessor]");
            instance = go.AddComponent<EventProcessor>();
            DontDestroyOnLoad(go);
        }

        void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);
            nextCleanupTime = Time.time + cleanupInterval;
        }

        void Update()
        {
            // Process network events every frame (main thread safety)
            NetworkEvents.ProcessReceivedEvents();

            // Periodic cleanup
            if (Time.time >= nextCleanupTime)
            {
                Events.CleanupDeadOwners();
                nextCleanupTime = Time.time + cleanupInterval;
            }
        }

        void OnApplicationQuit()
        {
            applicationQuitting = true;
        }

        void OnDestroy()
        {
            if (instance == this)
                instance = null;

            // Don't cleanup on application quit to avoid Unity Object access errors
            if (!applicationQuitting)
            {
                Events.Clear();
                NetworkEvents.Shutdown();
            }
        }
    }

}

