using UnityEngine;
using Doody.GameEvents;

public class GameStateManager : EventListener
    {
        private GameState currentState = GameState.WaitingForStart;
        private float stateEnterTime;
        private int errorCount = 0;
        private const int MAX_ERRORS = 3;

        void Awake()
        {
            SubscribeToEvents();
        }

        private void SubscribeToEvents()
        {
            Listen<GameStateChangedEvent>(OnGameStateChanged);
            Listen<GameErrorEvent>(OnGameError);
            Listen<GameResetEvent>(OnGameReset);
        }

        private void OnGameStateChanged(GameStateChangedEvent evt)
        {
            currentState = evt.NewState;
            stateEnterTime = Time.time;
            errorCount = 0; // Reset error count on state change

            Debug.Log($"[GameStateManager] State changed to {evt.NewState}");

            // Handle state-specific logic
            switch (evt.NewState)
            {
                case GameState.ProcessingShot:
                    // Set a timeout for shot processing
                    Invoke(nameof(CheckShotProcessingTimeout), 10f);
                    break;
                case GameState.ResettingScene:
                    // Set a timeout for scene reset
                    Invoke(nameof(CheckSceneResetTimeout), 15f);
                    break;
            }
        }

        private void OnGameError(GameErrorEvent evt)
        {
            errorCount++;
            Debug.LogError($"[GameStateManager] Error {errorCount}/{MAX_ERRORS}: {evt.Error}");

            if (errorCount >= MAX_ERRORS)
            {
                Debug.LogError("[GameStateManager] Too many errors, forcing reset");
                Events.Publish(new GameResetEvent());
                errorCount = 0;
            }
        }

        private void OnGameReset(GameResetEvent evt)
        {
            Debug.Log("[GameStateManager] Game reset initiated");
            errorCount = 0;
            CancelInvoke(); // Cancel any pending timeouts
        }

        private void CheckShotProcessingTimeout()
        {
            if (currentState == GameState.ProcessingShot)
            {
                Debug.LogWarning("[GameStateManager] Shot processing timeout - forcing state change");
                Events.Publish(new GameErrorEvent
                {
                    Error = "Shot processing timed out",
                    Exception = null
                });
            }
        }

        private void CheckSceneResetTimeout()
        {
            if (currentState == GameState.ResettingScene)
            {
                Debug.LogWarning("[GameStateManager] Scene reset timeout - forcing state change");
                Events.Publish(new GameErrorEvent
                {
                    Error = "Scene reset timed out",
                    Exception = null
                });
            }
        }

        // Public API for checking state
        public GameState CurrentState => currentState;
        public float TimeInCurrentState => Time.time - stateEnterTime;
    }