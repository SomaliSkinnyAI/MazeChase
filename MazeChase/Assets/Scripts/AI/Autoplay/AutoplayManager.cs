using UnityEngine;
using MazeChase.AI;
using MazeChase.Core;
using MazeChase.Game;
using System.Globalization;

namespace MazeChase.AI.Autoplay
{
    /// <summary>
    /// Event-driven autoplay manager. Decisions are committed only when the player
    /// reaches a tile center, which prevents mid-tile reversals while also giving
    /// the active bot a consistent decision cadence.
    /// </summary>
    public class AutoplayManager : MonoBehaviour
    {
        private const float StatusPanelX = 12f;
        private const float StatusPanelY = 56f;
        private const float StatusPanelWidth = 620f;
        private const float StatusPanelHeight = 54f;

        public static AutoplayManager Instance { get; private set; }

        private bool _autoplayActive;
        private bool _recordDataset;
        private AutoplayMode _currentMode = AutoplayMode.NeuralPolicy;

        private PlayerController _player;
        private Ghost[] _ghosts;
        private PelletManager _pellets;
        private FruitSpawner _fruitSpawner;
        private GhostModeTimer _ghostModeTimer;

        private MazeGraph _graph;
        private GhostForecastEngine _forecastEngine;
        private AutoplayContext _context;
        private AIDatasetRecorder _datasetRecorder;
        private ObservationEncoder _observationEncoder;

        private IAutoplayBot _neuralBot;
        private IAutoplayBot _researchBot;
        private IAutoplayBot _expertBot;
        private IAutoplayBot _attractBot;

        private PlayerController _subscribedPlayer;
        private BotDecision _lastDecision = BotDecision.None;
        private GUIStyle _bannerStyle;
        private GUIStyle _statusStyle;
        private GUIStyle _hintStyle;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            ParseCommandLineConfiguration();
        }

        private void Start()
        {
            FindReferences();
            EnsureContext();
            SubscribeToPlayer();
            SeedRecentTileMemory();

            if (GameStateManager.Instance != null)
                GameStateManager.Instance.OnStateChanged += OnGameStateChanged;

            if (_autoplayActive && _player != null && _player.State != PlayerController.MovementState.Moving && IsPlayingState())
                QueueDecisionForCurrentTile();
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F2))
                ActivateMode(AutoplayMode.NeuralPolicy);

            if (Input.GetKeyDown(KeyCode.F3))
                ActivateMode(AutoplayMode.ResearchPlanner);

            if (Input.GetKeyDown(KeyCode.F4))
                DisableAutoplay();

            if (_player == null || _ghosts == null || _pellets == null || _ghostModeTimer == null)
            {
                FindReferences();
                EnsureContext();
                SubscribeToPlayer();
            }
        }

        private void OnDestroy()
        {
            if (_subscribedPlayer != null)
                _subscribedPlayer.OnTileArrived -= OnPlayerTileArrived;

            if (GameStateManager.Instance != null)
                GameStateManager.Instance.OnStateChanged -= OnGameStateChanged;

            _datasetRecorder?.Dispose();
        }

        public void Initialize(
            PlayerController player,
            Ghost[] ghosts,
            PelletManager pellets,
            FruitSpawner fruitSpawner,
            GhostModeTimer ghostModeTimer)
        {
            _player = player;
            _ghosts = ghosts;
            _pellets = pellets;
            _fruitSpawner = fruitSpawner;
            _ghostModeTimer = ghostModeTimer;

            EnsureContext();
            SubscribeToPlayer();
        }

        private void ActivateMode(AutoplayMode mode)
        {
            bool modeChanged = _currentMode != mode;
            bool wasInactive = !_autoplayActive;
            _autoplayActive = true;
            _currentMode = mode;

            if (modeChanged)
                ResetBots();

            if (modeChanged || wasInactive)
                _lastDecision = BotDecision.None;

            Debug.Log($"[AutoplayManager] AI autoplay ENABLED in mode {_currentMode}");

            if (_autoplayActive && _player != null && _player.State != PlayerController.MovementState.Moving && IsPlayingState())
                QueueDecisionForCurrentTile();
        }

        private void DisableAutoplay()
        {
            if (!_autoplayActive)
            {
                Debug.Log("[AutoplayManager] Manual mode already active");
                return;
            }

            _autoplayActive = false;
            _lastDecision = BotDecision.None;
            Debug.Log("[AutoplayManager] Manual mode ENABLED");
        }

        private void OnPlayerTileArrived(Vector2Int tile)
        {
            _context?.RememberVisitedTile(tile);

            if (!_autoplayActive || !IsPlayingState())
                return;

            QueueDecisionForCurrentTile();
        }

        private void OnGameStateChanged(GameState oldState, GameState newState)
        {
            if (newState == GameState.Playing)
            {
                ResetBots();
                _context?.ResetRuntimeMemory();
                SeedRecentTileMemory();
                _datasetRecorder?.BeginEpisode();

                if (_autoplayActive && _player != null && _player.State != PlayerController.MovementState.Moving)
                    QueueDecisionForCurrentTile();
            }
            else
            {
                _lastDecision = BotDecision.None;
            }
        }

        private void QueueDecisionForCurrentTile()
        {
            EnsureContext();
            if (_context == null || _player == null)
                return;

            GameStateSnapshot snapshot = CaptureSnapshot();
            _context.ObserveDecisionSnapshot(snapshot);
            IAutoplayBot bot = GetActiveBot();
            if (bot == null)
                return;

            BotDecision decision = bot.Evaluate(snapshot) ?? BotDecision.None;
            if (decision.FeatureFrame == null && _observationEncoder != null)
                decision.FeatureFrame = _observationEncoder.Encode(snapshot);
            _lastDecision = decision;

            if (decision.Direction != Direction.None)
                _player.SetQueuedDirection(decision.Direction);

            if (_recordDataset)
                _datasetRecorder?.Record(snapshot, decision);
        }

        private GameStateSnapshot CaptureSnapshot()
        {
            GhostSnapshot[] ghostSnapshots = new GhostSnapshot[_ghosts.Length];
            for (int i = 0; i < _ghosts.Length; i++)
            {
                Ghost ghost = _ghosts[i];
                ghostSnapshots[i] = new GhostSnapshot
                {
                    GhostIndex = ghost.GhostIndex,
                    Tile = ghost.CurrentTile,
                    Direction = ghost.CurrentDirection,
                    State = ghost.CurrentState
                };
            }

            ScoreManager score = ScoreManager.Instance;
            return new GameStateSnapshot
            {
                FrameIndex = Time.frameCount,
                Round = score != null ? score.CurrentRound : 1,
                Lives = score != null ? score.Lives : 3,
                Score = score != null ? score.Score : 0,
                RemainingPellets = _pellets != null ? _pellets.RemainingPellets : MazeData.CountPellets(),
                TotalPellets = _pellets != null ? _pellets.TotalPellets : MazeData.CountPellets(),
                PlayerTile = _player.CurrentTile,
                PlayerDirection = _player.CurrentDirection,
                GlobalGhostMode = _ghostModeTimer != null ? _ghostModeTimer.CurrentMode : GhostState.Scatter,
                ModeTimeRemaining = _ghostModeTimer != null ? _ghostModeTimer.TimeRemainingInPhase : 0f,
                FruitActive = _fruitSpawner != null && _fruitSpawner.IsFruitActive,
                FruitTile = _fruitSpawner != null ? _fruitSpawner.FruitTile : MazeData.GetFruitSpawn(),
                Ghosts = ghostSnapshots
            };
        }

        private IAutoplayBot GetActiveBot()
        {
            switch (_currentMode)
            {
                case AutoplayMode.ResearchPlanner:
                    return _researchBot;
                case AutoplayMode.ExpertLegacy:
                    return _expertBot;
                case AutoplayMode.Attract:
                    return _attractBot;
                default:
                    return _neuralBot;
            }
        }

        private void ResetBots()
        {
            _neuralBot?.ResetForRound();
            _researchBot?.ResetForRound();
            _expertBot?.ResetForRound();
            _attractBot?.ResetForRound();
        }

        private void EnsureContext()
        {
            if (_player == null || _ghosts == null || _pellets == null || _ghostModeTimer == null)
                return;

            if (_graph == null)
                _graph = new MazeGraph();

            if (_forecastEngine == null)
                _forecastEngine = new GhostForecastEngine(_graph);

            if (_datasetRecorder == null)
            {
                _datasetRecorder = new AIDatasetRecorder();
                _datasetRecorder.Initialize(_recordDataset);
            }

            if (_context == null)
                _context = new AutoplayContext();

            _context.Initialize(_player, _ghosts, _pellets, _fruitSpawner, _ghostModeTimer, _graph, _forecastEngine, _datasetRecorder);

            if (_observationEncoder == null)
                _observationEncoder = new ObservationEncoder(_context);

            if (_neuralBot == null)
            {
                _neuralBot = new NeuralPolicyBot();
                _neuralBot.Initialize(_context);
            }

            if (_researchBot == null)
            {
                _researchBot = new ResearchPlannerBot();
                _researchBot.Initialize(_context);
            }

            if (_expertBot == null)
            {
                _expertBot = new ExpertBot();
                _expertBot.Initialize(_context);
            }

            if (_attractBot == null)
            {
                _attractBot = new ResearchPlannerBot(true);
                _attractBot.Initialize(_context);
            }
        }

        private void SubscribeToPlayer()
        {
            if (_player == null || _subscribedPlayer == _player)
                return;

            if (_subscribedPlayer != null)
                _subscribedPlayer.OnTileArrived -= OnPlayerTileArrived;

            _subscribedPlayer = _player;
            _subscribedPlayer.OnTileArrived += OnPlayerTileArrived;
        }

        private void FindReferences()
        {
            if (_player == null)
                _player = FindFirstObjectByType<PlayerController>();

            if (_ghosts == null || _ghosts.Length == 0)
                _ghosts = FindObjectsByType<Ghost>(FindObjectsSortMode.None);

            if (_pellets == null)
                _pellets = PelletManager.Instance ?? FindFirstObjectByType<PelletManager>();

            if (_fruitSpawner == null)
                _fruitSpawner = FruitSpawner.Instance ?? FindFirstObjectByType<FruitSpawner>();

            if (_ghostModeTimer == null)
                _ghostModeTimer = FindFirstObjectByType<GhostModeTimer>();
        }

        private bool IsPlayingState()
        {
            return GameStateManager.Instance == null || GameStateManager.Instance.GetCurrentState() == GameState.Playing;
        }

        private void SeedRecentTileMemory()
        {
            if (_context == null || _player == null)
                return;

            _context.RememberVisitedTile(_player.CurrentTile);
        }

        private void ParseCommandLineConfiguration()
        {
            // Default to AI autoplay on. Use F4 to switch to manual, or --no-autoplay to start manual.
            _autoplayActive = !CommandLineArgs.HasFlag("--no-autoplay");
            _recordDataset = CommandLineArgs.HasFlag("--ai-record");

            string modeArg = CommandLineArgs.GetValue("--autoplay-mode");
            if (!string.IsNullOrWhiteSpace(modeArg))
            {
                if (System.Enum.TryParse(modeArg, true, out AutoplayMode parsedMode))
                    _currentMode = parsedMode;
            }

            string fastForward = CommandLineArgs.GetValue("--ai-fast-forward");
            if (!string.IsNullOrWhiteSpace(fastForward) &&
                float.TryParse(fastForward, NumberStyles.Float, CultureInfo.InvariantCulture, out float timeScale))
            {
                Time.timeScale = RuntimeExecutionMode.ClampTimeScale(timeScale);
            }
        }

        private void OnGUI()
        {
            if (RuntimeExecutionMode.SuppressPresentation)
                return;

            if (_bannerStyle == null)
            {
                _bannerStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 18,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleLeft
                };
            }

            if (_statusStyle == null)
            {
                _statusStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 14,
                    alignment = TextAnchor.MiddleLeft
                };
                _statusStyle.normal.textColor = Color.white;
            }

            if (_hintStyle == null)
            {
                _hintStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 12,
                    alignment = TextAnchor.MiddleLeft
                };
                _hintStyle.normal.textColor = new Color(0.82f, 0.82f, 0.82f, 0.95f);
            }

            string statusTitle = GetStatusTitle();
            string statusBody = GetStatusBody();
            string statusHint = "F2 Neural AI   F3 Rules AI   F4 Manual";

            Color previousColor = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.72f);
            GUI.DrawTexture(new Rect(StatusPanelX, StatusPanelY, StatusPanelWidth, StatusPanelHeight), Texture2D.whiteTexture);
            GUI.color = previousColor;

            _bannerStyle.normal.textColor = _autoplayActive
                ? (_currentMode == AutoplayMode.NeuralPolicy ? new Color(0f, 1f, 0.4f, 0.95f) : new Color(1f, 0.76f, 0.2f, 0.95f))
                : new Color(0.92f, 0.92f, 0.92f, 0.95f);

            GUI.Label(new Rect(StatusPanelX + 10f, StatusPanelY + 4f, StatusPanelWidth - 20f, 22f), statusTitle, _bannerStyle);
            GUI.Label(new Rect(StatusPanelX + 10f, StatusPanelY + 24f, StatusPanelWidth - 20f, 18f), statusBody, _statusStyle);
            GUI.Label(new Rect(StatusPanelX + 10f, StatusPanelY + 39f, StatusPanelWidth - 20f, 16f), statusHint, _hintStyle);
        }

        private string GetStatusTitle()
        {
            if (!_autoplayActive)
                return "MODE: MANUAL";

            return _currentMode == AutoplayMode.NeuralPolicy
                ? "MODE: NEURAL AI"
                : "MODE: RULES AI";
        }

        private string GetStatusBody()
        {
            if (!_autoplayActive)
                return "Brain: Human control";

            if (_currentMode == AutoplayMode.NeuralPolicy)
            {
                string modelName = GetNeuralModelDisplayName();
                if (_lastDecision != null && _lastDecision.UsedFallback)
                    return $"Brain: {modelName}   Step control: {GetFallbackDisplayName()}";

                return $"Brain: {modelName}";
            }

            return "Brain: ResearchPlanner";
        }

        private string GetNeuralModelDisplayName()
        {
            EnsureContext();
            if (_neuralBot is NeuralPolicyBot neuralBot)
                return neuralBot.ActiveModelName;

            return "NeuralPolicy";
        }

        private string GetFallbackDisplayName()
        {
            string source = _lastDecision != null ? _lastDecision.Source ?? string.Empty : string.Empty;
            if (source.Contains("ExpertLegacy"))
                return "ExpertLegacy fallback";
            if (source.Contains("ResearchPlanner"))
                return "ResearchPlanner rescue";

            return "Fallback active";
        }
    }
}
