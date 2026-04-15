using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using MazeChase.Core;
using MazeChase.Game;

namespace MazeChase.AI.Autoplay
{
    /// <summary>
    /// TCP server that exposes the game as a step-by-step RL environment.
    /// At each tile arrival the game freezes, sends (obs, reward, done, info)
    /// to the Python trainer, and waits for an action response before resuming.
    /// Activated by the --rl-server command-line flag.
    /// </summary>
    public sealed class RLEnvironmentServer : MonoBehaviour
    {
        public static RLEnvironmentServer Instance { get; private set; }

        private const int StallTimeoutSteps = 500;
        private const float DeathPenalty = -5f;
        private const float RoundClearBonus = 10f;
        private const float PelletBonus = 0.5f;
        private const float ScoreDeltaScale = 1f / 100f;
        private const float TimePenalty = -0.01f;
        private const float StallPenaltyPerStep = -0.05f;
        private const int StallGracePeriod = 10;

        private PlayerController _player;
        private Ghost[] _ghosts;
        private PelletManager _pelletManager;
        private FruitSpawner _fruitSpawner;
        private GhostModeTimer _ghostModeTimer;

        private MazeGraph _graph;
        private GhostForecastEngine _forecastEngine;
        private AutoplayContext _context;
        private ObservationEncoder _encoder;

        private TcpListener _listener;
        private TcpClient _client;
        private NetworkStream _stream;
        private Thread _listenThread;

        private volatile int _pendingAction = -1;
        private volatile bool _actionReady;
        private volatile bool _resetRequested;
        private volatile bool _closeRequested;
        private bool _waitingForAction;
        private float _savedTimeScale;

        // Reward accumulation.
        private float _pendingReward;
        private int _lastScore;
        private int _lastLives;
        private int _lastPelletCount;
        private int _stepsWithoutProgress;
        private int _stepCount;
        private bool _deathOccurred;
        private bool _roundCleared;
        private bool _episodeDone;
        private bool _doneSent; // True after we've sent a done=true state to Python.

        private bool _tcpStarted;
        private bool _awaitingClient;
        private bool _freshEpisode; // True right after reinitialize; avoids needless reset on first connect.
        private bool _needsInitialState; // True when we need to send the first obs for this episode.
        private int _framesWaitingForTile; // Frames since action applied, waiting for OnTileArrived.

        // Reset callback (fired when Python sends a reset command).
        public event Action OnResetRequested;

        private void Awake()
        {
            Instance = this;
        }

        /// <summary>
        /// Called by GameplaySceneSetup each time a new gameplay session starts
        /// (including after RL resets). Wires up game references and event subscriptions.
        /// </summary>
        public void Reinitialize(
            PlayerController player,
            Ghost[] ghosts,
            PelletManager pelletManager,
            FruitSpawner fruitSpawner,
            GhostModeTimer ghostModeTimer,
            MazeRenderer mazeRenderer)
        {
            // Unsubscribe from old references if any.
            UnsubscribeEvents();

            _player = player;
            _ghosts = ghosts;
            _pelletManager = pelletManager;
            _fruitSpawner = fruitSpawner;
            _ghostModeTimer = ghostModeTimer;

            _graph = new MazeGraph();
            _forecastEngine = new GhostForecastEngine(_graph);
            _context = new AutoplayContext();
            _context.Initialize(player, ghosts, pelletManager, fruitSpawner, ghostModeTimer, _graph, _forecastEngine, null);
            _encoder = new ObservationEncoder(_context, 1); // No frame stacking for RL.

            // Subscribe to game events.
            _player.OnTileArrived += OnPlayerTileArrived;
            if (ScoreManager.Instance != null) ScoreManager.Instance.OnScoreChanged += OnScoreChanged;
            if (ScoreManager.Instance != null) ScoreManager.Instance.OnLivesChanged += OnLivesChanged;
            if (GameStateManager.Instance != null) GameStateManager.Instance.OnStateChanged += OnGameStateChanged;

            ResetEpisodeState();
            _freshEpisode = true;
            _needsInitialState = true;

            if (!_tcpStarted)
            {
                StartTcpServer();
                _tcpStarted = true;
            }

            // Freeze the game until the Python client is ready.
            // OnPlayerTileArrived won't fire until the player has a direction queued,
            // so we must send the initial observation proactively from Update().
            _savedTimeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
            Time.timeScale = 0f;
            _awaitingClient = _stream == null;
        }

        private void UnsubscribeEvents()
        {
            if (_player != null) _player.OnTileArrived -= OnPlayerTileArrived;
            if (ScoreManager.Instance != null) ScoreManager.Instance.OnScoreChanged -= OnScoreChanged;
            if (ScoreManager.Instance != null) ScoreManager.Instance.OnLivesChanged -= OnLivesChanged;
            if (GameStateManager.Instance != null) GameStateManager.Instance.OnStateChanged -= OnGameStateChanged;
        }

        private void ResetEpisodeState()
        {
            _pendingReward = 0f;
            _lastScore = ScoreManager.Instance != null ? ScoreManager.Instance.Score : 0;
            _lastLives = ScoreManager.Instance != null ? ScoreManager.Instance.Lives : 3;
            _lastPelletCount = _pelletManager != null ? _pelletManager.RemainingPellets : 244;
            _stepsWithoutProgress = 0;
            _stepCount = 0;
            _deathOccurred = false;
            _roundCleared = false;
            _episodeDone = false;
            _doneSent = false;
            _context?.ResetRuntimeMemory();
            _encoder?.ResetFrameBuffer();
        }

        /// <summary>
        /// Sends the initial observation for a new episode (game is frozen at spawn).
        /// After sending, queues a background read for the first action/reset.
        /// </summary>
        private void SendInitialState()
        {
            Debug.Log("[RLServer] Sending initial state...");
            GameStateSnapshot snapshot = CaptureSnapshot();
            Debug.Log($"[RLServer] Snapshot: player={snapshot.PlayerTile}, pellets={snapshot.RemainingPellets}, lives={snapshot.Lives}");
            _context?.ObserveDecisionSnapshot(snapshot);
            PolicyFeatureFrame frame = _encoder.Encode(snapshot);

            string stateJson = BuildStateMessage(frame, 0f, false, false);
            SendMessage(stateJson);
            Debug.Log("[RLServer] Initial state sent. Waiting for action/reset...");

            // Game stays frozen. Wait for action/reset from Python.
            _waitingForAction = true;
            _actionReady = false;
            _pendingAction = -1;
            ThreadPool.QueueUserWorkItem(_ => ReadActionFromClient());
        }

        // ---------------------------------------------------------------
        // TCP Server
        // ---------------------------------------------------------------

        private void StartTcpServer()
        {
            int port = RuntimeExecutionMode.RLServerPort;
            _listener = new TcpListener(IPAddress.Loopback, port);
            _listener.Start();
            Debug.Log($"[RLServer] Listening on port {port}");

            _listenThread = new Thread(AcceptClientLoop) { IsBackground = true };
            _listenThread.Start();
        }

        private void AcceptClientLoop()
        {
            try
            {
                _client = _listener.AcceptTcpClient();
                _client.NoDelay = true;
                _stream = _client.GetStream();
                Debug.Log("[RLServer] Client connected.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[RLServer] Accept failed: {ex.Message}");
            }
        }

        // ---------------------------------------------------------------
        // Game Event Handlers
        // ---------------------------------------------------------------

        private void OnScoreChanged(int newScore)
        {
            int delta = newScore - _lastScore;
            if (delta > 0)
            {
                _pendingReward += delta * ScoreDeltaScale;
                _stepsWithoutProgress = 0;
            }
            _lastScore = newScore;
        }

        private void OnLivesChanged(int newLives)
        {
            if (newLives < _lastLives)
            {
                _pendingReward += DeathPenalty;
                _deathOccurred = true;
            }
            _lastLives = newLives;
        }

        private void OnGameStateChanged(GameState oldState, GameState newState)
        {
            if (newState == GameState.RoundClear)
            {
                _pendingReward += RoundClearBonus;
                _roundCleared = true;
            }

            if (newState == GameState.GameOver)
                _episodeDone = true;

            // Death or game over: the step response should include the death penalty.
            // Don't send immediately — let the death/game-over sequence play out.
            // For death (lives > 0): HandleDeath will eventually change to Playing.
            // For game over: _episodeDone is true, and we send the final state below.
            if (newState == GameState.Dying)
            {
                // Death is in progress. The step response will be sent either:
                // (a) from the Dying→Playing transition (respawn) below, or
                // (b) from Dying→GameOver above (_episodeDone=true).
            }

            // Game over: send done=true state immediately as the step response,
            // unless we're still waiting for an action (Update will handle it).
            // Game over: send done=true state immediately as the step response,
            // unless we're still waiting for an action (Update will handle it).
            if (newState == GameState.GameOver && !_waitingForAction)
            {
                _framesWaitingForTile = 0;
                OnPlayerTileArrived(_player.CurrentTile);
                return;
            }

            // After death respawn, the game returns to Playing.
            // Send state (with accumulated death penalty) as the step response.
            if (newState == GameState.Playing && oldState == GameState.Dying)
            {
                _context?.ResetRuntimeMemory();
                if (_stream != null && _stream.CanWrite && _framesWaitingForTile > 0)
                {
                    _framesWaitingForTile = 0;
                    OnPlayerTileArrived(_player.CurrentTile);
                }
                return;
            }

            // When a new round starts after round clear, send state as step response.
            if (newState == GameState.Playing && oldState == GameState.RoundClear)
            {
                _context?.ResetRuntimeMemory();
                if (_stream != null && _stream.CanWrite && _framesWaitingForTile > 0)
                {
                    _framesWaitingForTile = 0;
                    OnPlayerTileArrived(_player.CurrentTile);
                }
                return;
            }

            if (newState == GameState.Playing)
            {
                _context?.ResetRuntimeMemory();
            }
        }

        private void OnPlayerTileArrived(Vector2Int tile)
        {
            if (_stream == null || !_stream.CanWrite)
                return;

            _framesWaitingForTile = 0; // Cancel the fallback timer.

            if (_doneSent)
                return; // Already sent done=true, waiting for reset.

            _context?.RememberVisitedTile(tile);
            _stepCount++;

            // Pellet tracking for bonus reward.
            int currentPellets = _pelletManager.RemainingPellets;
            int pelletsEaten = _lastPelletCount - currentPellets;
            if (pelletsEaten > 0)
            {
                _pendingReward += pelletsEaten * PelletBonus;
                _stepsWithoutProgress = 0;
            }
            else if (Mathf.Abs(_lastScore - (ScoreManager.Instance != null ? ScoreManager.Instance.Score : _lastScore)) < 1)
            {
                _stepsWithoutProgress++;
            }
            _lastPelletCount = currentPellets;

            // Time penalty.
            _pendingReward += TimePenalty;

            // Stall penalty (after grace period).
            if (_stepsWithoutProgress > StallGracePeriod)
                _pendingReward += StallPenaltyPerStep;

            // Stall timeout -> episode done.
            bool timeout = _stepsWithoutProgress >= StallTimeoutSteps;
            if (timeout)
                _episodeDone = true;

            // Capture observation.
            GameStateSnapshot snapshot = CaptureSnapshot();
            _context?.ObserveDecisionSnapshot(snapshot);
            PolicyFeatureFrame frame = _encoder.Encode(snapshot);
            float reward = _pendingReward;
            _pendingReward = 0f;
            bool done = _episodeDone;

            // Build and send state message.
            string stateJson = BuildStateMessage(frame, reward, done, timeout);
            SendMessage(stateJson);
            if (done)
                _doneSent = true;

            // Freeze the game and wait for action.
            _savedTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            _waitingForAction = true;
            _actionReady = false;
            _pendingAction = -1;

            // Read action on background thread (expects reset command if done).
            ThreadPool.QueueUserWorkItem(_ => ReadActionFromClient());
        }

        // ---------------------------------------------------------------
        // Update: poll for pending action or reset
        // ---------------------------------------------------------------

        private void Update()
        {
            // If we froze waiting for a client and the client has now connected, send initial state.
            if (_awaitingClient && _stream != null && _stream.CanWrite)
            {
                _awaitingClient = false;
                // Fall through to _needsInitialState handling below.
            }

            // Send the initial observation for this episode (game is frozen).
            if (_needsInitialState && _stream != null && _stream.CanWrite)
            {
                _needsInitialState = false;
                SendInitialState();
                return;
            }

            if (_closeRequested)
            {
                Debug.Log("[RLServer] Close requested. Quitting.");
                Application.Quit(0);
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#endif
                return;
            }

            if (_resetRequested)
            {
                _resetRequested = false;
                _waitingForAction = false;
                Time.timeScale = _savedTimeScale > 0f ? _savedTimeScale : 1f;
                // OnResetRequested triggers GameBootstrap to destroy+recreate gameplay,
                // which calls Reinitialize(). The first OnTileArrived in the new
                // gameplay session will send the initial observation to Python.
                OnResetRequested?.Invoke();
                return;
            }

            if (_waitingForAction && _actionReady)
            {
                _waitingForAction = false;
                Time.timeScale = _savedTimeScale > 0f ? _savedTimeScale : 1f;

                int action = _pendingAction;
                _pendingAction = -1;
                _actionReady = false;

                // If the episode ended while we were waiting for an action
                // (e.g. game-over happened in the same frame as tile arrival),
                // send the done state immediately instead of applying the action.
                if (_episodeDone)
                {
                    _framesWaitingForTile = 0;
                    OnPlayerTileArrived(_player.CurrentTile);
                    return;
                }

                _framesWaitingForTile = 1; // Start counting frames until OnTileArrived.

                if (action >= 1 && action <= 4)
                {
                    Direction direction = (Direction)action;
                    _player.SetQueuedDirection(direction);
                }
                return;
            }

            // If an action was applied but the player hasn't reached a new tile,
            // send state after a short wait. This handles:
            // (a) blocked directions (player stopped immediately)
            // (b) death/round-clear interrupting movement
            if (_framesWaitingForTile > 0)
            {
                _framesWaitingForTile++;
                bool playerStopped = _player.State == PlayerController.MovementState.Stopped;
                bool playerFrozen = _player.State == PlayerController.MovementState.Frozen;
                // Send state when:
                // (a) blocked direction — player goes Stopped within 2 frames
                // (b) episode ended (game-over/death made player Frozen)
                if (_framesWaitingForTile > 2 && (playerStopped || _episodeDone || playerFrozen))
                {
                    _framesWaitingForTile = 0;
                    OnPlayerTileArrived(_player.CurrentTile);
                }
                // No hard timeout — let the player finish moving. At 5 tiles/sec,
                // one tile can take 100+ frames at high batch-mode framerates.
            }
        }

        // ---------------------------------------------------------------
        // TCP Message I/O
        // ---------------------------------------------------------------

        private void SendMessage(string json)
        {
            try
            {
                byte[] payload = Encoding.UTF8.GetBytes(json);
                byte[] lengthPrefix = BitConverter.GetBytes((int)payload.Length);
                if (!BitConverter.IsLittleEndian)
                    Array.Reverse(lengthPrefix);

                _stream.Write(lengthPrefix, 0, 4);
                _stream.Write(payload, 0, payload.Length);
                _stream.Flush();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[RLServer] Send failed: {ex.Message}");
            }
        }

        private string ReadMessage()
        {
            byte[] lengthBuf = new byte[4];
            int read = 0;
            while (read < 4)
            {
                int n = _stream.Read(lengthBuf, read, 4 - read);
                if (n <= 0) throw new Exception("Connection closed.");
                read += n;
            }

            if (!BitConverter.IsLittleEndian)
                Array.Reverse(lengthBuf);
            int length = BitConverter.ToInt32(lengthBuf, 0);

            byte[] payloadBuf = new byte[length];
            read = 0;
            while (read < length)
            {
                int n = _stream.Read(payloadBuf, read, length - read);
                if (n <= 0) throw new Exception("Connection closed.");
                read += n;
            }

            return Encoding.UTF8.GetString(payloadBuf);
        }

        private void ReadActionFromClient()
        {
            try
            {
                string msg = ReadMessage();
                bool wasFresh = _freshEpisode;
                _freshEpisode = false; // Any message clears the fresh flag.

                // Minimal JSON parsing without external dependencies.
                if (msg.Contains("\"close\""))
                {
                    _closeRequested = true;
                    return;
                }
                if (msg.Contains("\"reset\""))
                {
                    if (wasFresh)
                    {
                        // Game is already fresh — no need to restart.
                        // The state we just sent IS the initial obs for this episode.
                        // Now read the NEXT message which should be the first action.
                        ReadActionFromClient(); // Recursive: read the actual action.
                        return;
                    }
                    _resetRequested = true;
                    return;
                }

                // Parse action value: {"type":"action","action":2}
                int idx = msg.IndexOf("\"action\"", StringComparison.Ordinal);
                if (idx >= 0)
                {
                    // Find the colon after "action", then parse the integer.
                    int colonIdx = msg.IndexOf(':', idx + 8);
                    if (colonIdx >= 0)
                    {
                        int start = colonIdx + 1;
                        while (start < msg.Length && (msg[start] == ' ' || msg[start] == '\t'))
                            start++;
                        int end = start;
                        while (end < msg.Length && char.IsDigit(msg[end]))
                            end++;
                        if (end > start && int.TryParse(msg.Substring(start, end - start), out int action))
                        {
                            _pendingAction = action;
                            _actionReady = true;
                            return;
                        }
                    }
                }

                Debug.LogWarning($"[RLServer] Unrecognized message: {msg}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[RLServer] Read failed: {ex.Message}");
                _closeRequested = true;
            }
        }

        // ---------------------------------------------------------------
        // State message construction
        // ---------------------------------------------------------------

        private string BuildStateMessage(PolicyFeatureFrame frame, float reward, bool done, bool timeout)
        {
            var sb = new StringBuilder(4096);
            sb.Append("{\"type\":\"state\",\"obs\":[");

            float[] obs = frame.Input;
            for (int i = 0; i < obs.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(obs[i].ToString("G6", System.Globalization.CultureInfo.InvariantCulture));
            }

            sb.Append("],\"legal\":[");
            for (int i = 0; i < 5; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(frame.LegalMask[i] ? "true" : "false");
            }

            sb.Append("],\"reward\":");
            sb.Append(reward.ToString("G6", System.Globalization.CultureInfo.InvariantCulture));
            sb.Append(",\"done\":");
            sb.Append(done ? "true" : "false");

            var score = ScoreManager.Instance;
            sb.Append(",\"info\":{");
            sb.Append($"\"score\":{(score != null ? score.Score : 0)},");
            sb.Append($"\"lives\":{(score != null ? score.Lives : 0)},");
            sb.Append($"\"round\":{(score != null ? score.CurrentRound : 1)},");
            sb.Append($"\"remaining_pellets\":{(_pelletManager != null ? _pelletManager.RemainingPellets : 0)},");
            sb.Append($"\"total_pellets\":{(_pelletManager != null ? _pelletManager.TotalPellets : 0)},");
            sb.Append($"\"step\":{_stepCount},");
            sb.Append($"\"death\":{(_deathOccurred ? "true" : "false")},");
            sb.Append($"\"round_clear\":{(_roundCleared ? "true" : "false")},");
            sb.Append($"\"timeout\":{(timeout ? "true" : "false")}");
            sb.Append("}}");

            // Reset per-step flags.
            _deathOccurred = false;
            _roundCleared = false;

            return sb.ToString();
        }

        // ---------------------------------------------------------------
        // Snapshot capture (mirrors AutoplayManager.CaptureSnapshot)
        // ---------------------------------------------------------------

        private GameStateSnapshot CaptureSnapshot()
        {
            GhostSnapshot[] ghostSnapshots = new GhostSnapshot[_ghosts.Length];
            for (int i = 0; i < _ghosts.Length; i++)
            {
                Ghost ghost = _ghosts[i];
                ghostSnapshots[i] = new GhostSnapshot
                {
                    GhostIndex = i,
                    Tile = ghost.CurrentTile,
                    Direction = ghost.CurrentDirection,
                    State = ghost.CurrentState
                };
            }

            var score = ScoreManager.Instance;
            return new GameStateSnapshot
            {
                FrameIndex = Time.frameCount,
                Round = score != null ? score.CurrentRound : 1,
                Lives = score != null ? score.Lives : 3,
                Score = score != null ? score.Score : 0,
                RemainingPellets = _pelletManager.RemainingPellets,
                TotalPellets = _pelletManager.TotalPellets,
                PlayerTile = _player.CurrentTile,
                PlayerDirection = _player.CurrentDirection,
                GlobalGhostMode = _ghostModeTimer != null ? _ghostModeTimer.CurrentMode : GhostState.Scatter,
                ModeTimeRemaining = _ghostModeTimer != null ? _ghostModeTimer.TimeRemainingInPhase : 0f,
                FruitActive = _fruitSpawner != null && _fruitSpawner.IsFruitActive,
                FruitTile = _fruitSpawner != null ? _fruitSpawner.FruitTile : Vector2Int.zero,
                Ghosts = ghostSnapshots
            };
        }

        // ---------------------------------------------------------------
        // Cleanup
        // ---------------------------------------------------------------

        private void OnDestroy()
        {
            UnsubscribeEvents();
            Time.timeScale = 1f;

            try { _stream?.Close(); } catch { }
            try { _client?.Close(); } catch { }
            try { _listener?.Stop(); } catch { }

            if (Instance == this) Instance = null;
        }
    }
}
