using System;
using System.Collections.Generic;
using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// Global publish/subscribe event system for decoupled inter-system communication.
    /// Uses struct-based event types for zero-allocation publishing.
    /// </summary>
    /// <remarks>
    /// Layer: Core | Genre: Puzzle | Role: Handler | Phase: 0
    /// DB Reference: No DB match found — generated from L3 YAML logicFlow
    ///
    /// Usage:
    ///   EventBus.Subscribe&lt;OnBalloonPopped&gt;(HandleBalloonPopped);
    ///   EventBus.Publish(new OnBalloonPopped { balloonId = 1, color = 2 });
    ///   EventBus.Unsubscribe&lt;OnBalloonPopped&gt;(HandleBalloonPopped);
    /// </remarks>
    public static class EventBus
    {
        #region Fields

        private static readonly Dictionary<Type, Delegate> _handlers = new Dictionary<Type, Delegate>();

        #endregion

        #region Public Methods

        /// <summary>
        /// Subscribes a handler to an event type.
        /// </summary>
        public static void Subscribe<T>(Action<T> handler) where T : struct
        {
            Type eventType = typeof(T);

            if (_handlers.TryGetValue(eventType, out Delegate existing))
            {
                _handlers[eventType] = Delegate.Combine(existing, handler);
            }
            else
            {
                _handlers[eventType] = handler;
            }
        }

        /// <summary>
        /// Unsubscribes a handler from an event type.
        /// </summary>
        public static void Unsubscribe<T>(Action<T> handler) where T : struct
        {
            Type eventType = typeof(T);

            if (_handlers.TryGetValue(eventType, out Delegate existing))
            {
                Delegate updated = Delegate.Remove(existing, handler);
                if (updated == null)
                {
                    _handlers.Remove(eventType);
                }
                else
                {
                    _handlers[eventType] = updated;
                }
            }
        }

        /// <summary>
        /// Publishes an event to all subscribers.
        /// </summary>
        public static void Publish<T>(T eventData) where T : struct
        {
            Type eventType = typeof(T);

            if (_handlers.TryGetValue(eventType, out Delegate existing))
            {
                if (existing is Action<T> action)
                {
                    try
                    {
                        action.Invoke(eventData);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[EventBus] Exception while publishing {eventType.Name}: {ex}");
                    }
                }
            }
        }

        /// <summary>
        /// Removes all subscribers for a specific event type.
        /// </summary>
        public static void ClearEvent<T>() where T : struct
        {
            _handlers.Remove(typeof(T));
        }

        /// <summary>
        /// Removes all subscribers for all event types.
        /// Use with caution — typically on scene transitions.
        /// </summary>
        public static void ClearAll()
        {
            _handlers.Clear();
        }

        /// <summary>
        /// Whether any handler is registered for the event type.
        /// </summary>
        public static bool HasSubscribers<T>() where T : struct
        {
            return _handlers.ContainsKey(typeof(T));
        }

        /// <summary>
        /// Gets the subscriber count for an event type (for debugging).
        /// </summary>
        public static int GetSubscriberCount<T>() where T : struct
        {
            if (_handlers.TryGetValue(typeof(T), out Delegate existing))
            {
                return existing?.GetInvocationList().Length ?? 0;
            }
            return 0;
        }

        #endregion
    }
}
