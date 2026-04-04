using UnityEngine;
using MazeChase.Game;
using MazeChase.Core;

namespace MazeChase.AI.Autoplay
{
    /// <summary>
    /// Singleton MonoBehaviour that drives AI autoplay for the Pac-Man character.
    /// Toggle with F2. When active, the ExpertBot evaluates the best direction
    /// each frame and injects it into the PlayerController.
    /// </summary>
    public class AutoplayManager : MonoBehaviour
    {
        public static AutoplayManager Instance { get; private set; }

        private bool _autoplayActive;
        private ExpertBot _bot;
        private PlayerController _player;
        private Ghost[] _ghosts;
        private PelletManager _pellets;

        // Track which tile we last injected a direction for.
        // This is the CRITICAL anti-oscillation mechanism:
        // we only set QueuedDirection ONCE per tile arrival.
        private Vector2Int _lastInjectedTile = new Vector2Int(-999, -999);

        // OnGUI style for the overlay label.
        private GUIStyle _labelStyle;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            _bot = new ExpertBot();
        }

        private void Start()
        {
            FindReferences();
        }

        private void Update()
        {
            // Toggle autoplay with F2.
            if (Input.GetKeyDown(KeyCode.F2))
            {
                _autoplayActive = !_autoplayActive;
                Debug.Log($"[AutoplayManager] AI autoplay {(_autoplayActive ? "ENABLED" : "DISABLED")}");
            }

            if (!_autoplayActive)
                return;

            // Only drive the player while the game is in the Playing state.
            if (GameStateManager.Instance != null &&
                GameStateManager.Instance.GetCurrentState() != GameState.Playing)
                return;

            // Ensure references are valid.
            if (_player == null || _ghosts == null || _pellets == null)
            {
                FindReferences();
                if (_player == null) return;
            }

            // Inject direction every frame. The ExpertBot internally only
            // re-evaluates at new tiles (_lastTile check), so this is safe.
            // We need to inject every frame because the player's TryStartMoving
            // consumes QueuedDirection — if we only inject once, it may get
            // consumed before the player can act on it.

            // Ask the bot for the best direction.
            Direction bestDir = _bot.GetBestDirection(
                _player.CurrentTile,
                _player.CurrentDirection,
                _ghosts,
                _pellets);

            if (bestDir != Direction.None)
            {
                _player.ForceDirection(bestDir);
            }
        }

        private void OnGUI()
        {
            if (!_autoplayActive)
                return;

            if (_labelStyle == null)
            {
                _labelStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 24,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.UpperCenter
                };
                _labelStyle.normal.textColor = new Color(0f, 1f, 0.4f, 0.9f);
            }

            GUI.Label(
                new Rect(0f, 8f, Screen.width, 40f),
                "AI PLAYING",
                _labelStyle);
        }

        private void FindReferences()
        {
            if (_player == null)
                _player = FindFirstObjectByType<PlayerController>();

            if (_ghosts == null || _ghosts.Length == 0)
                _ghosts = FindObjectsByType<Ghost>(FindObjectsSortMode.None);

            if (_pellets == null)
                _pellets = PelletManager.Instance;
            if (_pellets == null)
                _pellets = FindFirstObjectByType<PelletManager>();
        }
    }
}
