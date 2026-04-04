using System;
using UnityEngine;

namespace MazeChase.Core
{
    /// <summary>
    /// Singleton that owns the current <see cref="GameState"/> and broadcasts transitions.
    /// Persists across scene loads via DontDestroyOnLoad.
    /// </summary>
    public class GameStateManager : MonoBehaviour
    {
        public static GameStateManager Instance { get; private set; }

        /// <summary>
        /// Fired whenever the game state changes.
        /// Parameters: previous state, new state.
        /// </summary>
        public event Action<GameState, GameState> OnStateChanged;

        [SerializeField]
        private GameState _currentState = GameState.Boot;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        /// <summary>
        /// Returns the current game state.
        /// </summary>
        public GameState GetCurrentState()
        {
            return _currentState;
        }

        /// <summary>
        /// Transitions to a new game state, logging the change and firing <see cref="OnStateChanged"/>.
        /// </summary>
        public void ChangeState(GameState newState)
        {
            if (newState == _currentState)
            {
                Debug.LogWarning($"[GameStateManager] Attempted to change to the same state: {newState}");
                return;
            }

            GameState oldState = _currentState;
            _currentState = newState;

            Debug.Log($"[GameStateManager] State changed: {oldState} -> {newState}");
            OnStateChanged?.Invoke(oldState, newState);
        }
    }
}
