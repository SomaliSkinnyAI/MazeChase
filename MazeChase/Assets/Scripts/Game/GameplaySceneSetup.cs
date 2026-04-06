using System.Collections;
using UnityEngine;
using MazeChase.Core;
using MazeChase.AI;
using MazeChase.VFX;

namespace MazeChase.Game
{
    /// <summary>
    /// Sets up and manages the gameplay scene. Creates all game objects,
    /// wires up references, and manages the round lifecycle.
    /// </summary>
    public class GameplaySceneSetup : MonoBehaviour
    {
        private MazeRenderer _mazeRenderer;
        private PlayerController _player;
        private PelletManager _pelletManager;
        private FruitSpawner _fruitSpawner;
        private CollisionManager _collisionManager;
        private GhostModeTimer _ghostModeTimer;
        private GhostHouse _ghostHouse;
        private Ghost[] _ghosts;
        private Audio.AudioManager _audio;

        private bool _roundActive;
        private bool _simulationQuitRequested;
        private Coroutine _frightenedCoroutine;

        private void Start()
        {
            SetupGameplay();
        }

        public void SetupGameplay()
        {
            Debug.Log("[GameplaySceneSetup] Setting up gameplay...");

            // Configure camera for 2D maze view
            if (RuntimeExecutionMode.PresentationEnabled)
                SetupCamera();

            // Create maze
            var mazeObj = new GameObject("Maze");
            _mazeRenderer = mazeObj.AddComponent<MazeRenderer>();

            // Create pellet manager
            var pelletObj = new GameObject("PelletManager");
            _pelletManager = pelletObj.AddComponent<PelletManager>();
            _pelletManager.ResetPellets();

            // Create player — it auto-finds MazeRenderer in Start()
            var playerObj = new GameObject("Player");
            _player = playerObj.AddComponent<PlayerController>();

            // Create ghosts
            CreateGhosts();

            // Create fruit spawner
            var fruitObj = new GameObject("FruitSpawner");
            _fruitSpawner = fruitObj.AddComponent<FruitSpawner>();
            _fruitSpawner.Init(_mazeRenderer, _pelletManager);
            _fruitSpawner.SetRound(1);

            // Create collision manager
            var collisionObj = new GameObject("CollisionManager");
            _collisionManager = collisionObj.AddComponent<CollisionManager>();
            _collisionManager.SetGhosts(_ghosts);

            // Create ghost mode timer
            var timerObj = new GameObject("GhostModeTimer");
            _ghostModeTimer = timerObj.AddComponent<GhostModeTimer>();
            _ghostModeTimer.OnModeChanged += OnGhostModeChanged;

            // Create ghost house
            var houseObj = new GameObject("GhostHouse");
            _ghostHouse = houseObj.AddComponent<GhostHouse>();
            _ghostHouse.Init(_ghosts, _mazeRenderer);

            // Create audio manager if not present
            if (RuntimeExecutionMode.PresentationEnabled && Audio.AudioManager.Instance == null)
            {
                var audioObj = new GameObject("AudioManager");
                _audio = audioObj.AddComponent<Audio.AudioManager>();
            }
            else
            {
                _audio = Audio.AudioManager.Instance;
            }

            // Create HUD
            if (RuntimeExecutionMode.PresentationEnabled)
            {
                var hudObj = new GameObject("HUD");
                hudObj.AddComponent<UI.HUDController>();
            }

            // Create autoplay manager
            var autoplayObj = new GameObject("AutoplayManager");
            var autoplay = autoplayObj.AddComponent<MazeChase.AI.Autoplay.AutoplayManager>();
            autoplay.Initialize(_player, _ghosts, _pelletManager, _fruitSpawner, _ghostModeTimer);

            // Create screen effects singleton
            if (RuntimeExecutionMode.PresentationEnabled && ScreenEffects.Instance == null)
            {
                var sfxObj = new GameObject("ScreenEffects");
                sfxObj.AddComponent<ScreenEffects>();
            }

            // Wire up events
            _pelletManager.OnPelletCollected += OnPelletCollected;
            _pelletManager.OnAllPelletsCleared += OnRoundCleared;
            _player.OnPelletCollected += OnPlayerPelletCollected;
            _collisionManager.OnPlayerCaught += OnPlayerCaught;
            _collisionManager.OnGhostEaten += OnGhostEaten;

            // Start first round
            StartRound(1);
        }

        private void CreateGhosts()
        {
            _ghosts = new Ghost[4];
            string[] names = { "Shadow", "Speedy", "Bashful", "Pokey" };
            Vector2Int[] spawns = MazeData.GetGhostSpawns();

            for (int i = 0; i < 4; i++)
            {
                var ghostObj = new GameObject($"Ghost_{names[i]}");
                // Add SpriteRenderer before Ghost (Ghost requires it)
                ghostObj.AddComponent<SpriteRenderer>();
                var ghost = ghostObj.AddComponent<Ghost>();
                ghost.Initialize(i, spawns[i], outsideHouse: i == 0);
                _ghosts[i] = ghost;
            }
        }

        private void StartRound(int round)
        {
            Debug.Log($"[GameplaySceneSetup] Starting round {round}");
            CancelFrightenedCoroutine();

            var score = ScoreManager.Instance;
            if (score != null) score.CurrentRound = round;

            // Reset pellets and renderer
            _pelletManager.ResetPellets();
            _mazeRenderer.RebuildMaze();

            // Reset positions
            _player.ResetToSpawn();
            Vector2Int[] spawns = MazeData.GetGhostSpawns();
            for (int i = 0; i < _ghosts.Length; i++)
            {
                _ghosts[i].ResetToSpawn();
                _ghosts[i].Initialize(i, spawns[i], outsideHouse: i == 0);
            }

            // Apply round tuning
            var tuning = RoundTuningData.GetTuning(round);
            _player.SetSpeed(tuning.PlayerSpeed);
            for (int i = 0; i < _ghosts.Length; i++)
                _ghosts[i].ApplyTuning(tuning);

            // Set fruit for this round
            _fruitSpawner.SetRound(round);

            // Start ghost mode timer
            _ghostModeTimer.StartTimer(round);

            // Release ghosts from house
            _ghostHouse.ResetForNewRound();

            // Show ready message then unfreeze
            var hud = UI.HUDController.Instance;
            if (hud != null && RuntimeExecutionMode.PresentationEnabled)
                hud.ShowMessage("READY!", RuntimeExecutionMode.ReadyDelaySeconds);
            if (_audio != null) _audio.PlayGameStart();

            StartCoroutine(UnfreezeAfterDelay(RuntimeExecutionMode.ReadyDelaySeconds));
            _roundActive = true;

            if (GameStateManager.Instance != null)
                GameStateManager.Instance.ChangeState(GameState.Playing);
        }

        private IEnumerator UnfreezeAfterDelay(float delay)
        {
            _player.Freeze();
            foreach (var g in _ghosts) g.Freeze();

            if (delay > 0f)
                yield return new WaitForSeconds(delay);

            _player.Unfreeze();
            foreach (var g in _ghosts) g.Unfreeze();
            if (_audio != null) _audio.StartSiren();
        }

        private void OnPlayerPelletCollected(Vector2Int tile, MazeTile type)
        {
            // Player fires this when arriving at a pellet tile.
            // PelletManager also needs to know for its counter.
            _pelletManager.TryCollect(tile.x, tile.y);

            // Notify ghost house for release counter
            _ghostHouse.OnPelletEaten();

            // Update Elroy mode for Blinky (ghost 0)
            if (_ghosts != null && _ghosts.Length > 0 && _pelletManager != null)
            {
                _ghosts[0].UpdateElroyState(_pelletManager.RemainingPellets, _pelletManager.TotalPellets);
            }

            // Check fruit collection
            int fruitScore = _fruitSpawner.TryCollectFruit(tile);
            if (fruitScore > 0)
            {
                ScoreManager.Instance?.AddScore(fruitScore);
                if (_audio != null) _audio.PlayFruitCollect();
            }
        }

        private void OnPelletCollected(Vector2Int tile, MazeTile type)
        {
            var score = ScoreManager.Instance;
            if (score == null) return;

            if (type == MazeTile.Pellet)
            {
                score.AddScore(10);
                if (_audio != null) _audio.PlayPelletEat();
            }
            else if (type == MazeTile.Energizer)
            {
                score.AddScore(50);
                score.ResetGhostCombo();
                if (_audio != null) _audio.PlayEnergizerEat();
                ActivateFrightenedMode();
            }
        }

        private void ActivateFrightenedMode()
        {
            int round = ScoreManager.Instance != null ? ScoreManager.Instance.CurrentRound : 1;
            var tuning = RoundTuningData.GetTuning(round);

            if (tuning.FrightenedDuration <= 0f) return;

            _ghostModeTimer.PauseTimer();

            if (_ghostHouse != null)
                _ghostHouse.FrightenAll(tuning.FrightenedDuration, tuning.FrightenedFlashCount);
            else
            {
                foreach (var ghost in _ghosts)
                {
                    if (ghost != null)
                        ghost.StartFrightened(tuning.FrightenedDuration, tuning.FrightenedFlashCount);
                }
            }

            _player.SetSpeed(tuning.FrightenedPlayerSpeed);
            if (_audio != null) _audio.StartFrightened();

            // Screen flash for energizer activation
            if (RuntimeExecutionMode.PresentationEnabled && ScreenEffects.Instance != null)
                ScreenEffects.Instance.Flash(new Color(0.3f, 0.5f, 1f, 0.4f), 0.2f);

            CancelFrightenedCoroutine();
            _frightenedCoroutine = StartCoroutine(EndFrightenedAfterDelay(tuning.FrightenedDuration));
        }

        private IEnumerator EndFrightenedAfterDelay(float duration)
        {
            yield return new WaitForSeconds(duration);
            _frightenedCoroutine = null;
            EndFrightenedMode();
        }

        private void EndFrightenedMode()
        {
            int round = ScoreManager.Instance != null ? ScoreManager.Instance.CurrentRound : 1;
            var tuning = RoundTuningData.GetTuning(round);
            _player.SetSpeed(tuning.PlayerSpeed);

            foreach (var ghost in _ghosts)
            {
                if (ghost.CurrentState == GhostState.Frightened)
                    ghost.SetState(_ghostModeTimer.CurrentMode);
            }

            _ghostModeTimer.ResumeTimer();
            if (_audio != null) _audio.ResumeNormalAudio();
        }

        private void OnGhostModeChanged(GhostState newMode)
        {
            foreach (var ghost in _ghosts)
            {
                if (ghost.CurrentState == GhostState.Scatter ||
                    ghost.CurrentState == GhostState.Chase)
                {
                    ghost.SetState(newMode);
                }
            }
        }

        private void OnGhostEaten(Ghost ghost)
        {
            var score = ScoreManager.Instance;
            int points = 0;
            if (score != null)
            {
                points = score.GetNextGhostComboValue();
                score.AddScore(points);
                Debug.Log($"[Gameplay] Ghost {ghost.GhostIndex} eaten! +{points}");
            }
            ghost.SetState(GhostState.Eaten);
            if (_audio != null) _audio.PlayGhostEat();

            // VFX: screen flash and ghost eat particles
            if (RuntimeExecutionMode.PresentationEnabled && ScreenEffects.Instance != null)
                ScreenEffects.Instance.Flash(new Color(1f, 1f, 1f, 0.3f), 0.12f);

            if (RuntimeExecutionMode.PresentationEnabled)
                SimpleParticles.SpawnGhostEatEffect(ghost.transform.position, Color.cyan);

            // HUD: score popup and combo text
            var hud = UI.HUDController.Instance;
            if (RuntimeExecutionMode.PresentationEnabled && hud != null && points >= 200)
            {
                hud.ShowScorePopup(points);
                hud.ShowComboText(points);
            }
        }

        private void OnPlayerCaught(Ghost ghost)
        {
            if (!_roundActive) return;
            _roundActive = false;
            Debug.Log($"[Gameplay] Player caught by ghost {ghost.GhostIndex}!");

            // VFX: screen shake and death particles
            if (RuntimeExecutionMode.PresentationEnabled && ScreenEffects.Instance != null)
                ScreenEffects.Instance.Shake(0.15f, 0.3f);

            if (RuntimeExecutionMode.PresentationEnabled && _player != null)
                SimpleParticles.SpawnDeathEffect(_player.transform.position);

            StartCoroutine(HandleDeath());
        }

        private IEnumerator HandleDeath()
        {
            if (GameStateManager.Instance != null)
                GameStateManager.Instance.ChangeState(GameState.Dying);

            CancelFrightenedCoroutine();
            _player.Freeze();
            foreach (var g in _ghosts) g.Freeze();
            _ghostModeTimer.PauseTimer();
            if (_audio != null) _audio.PlayDeath();

            if (RuntimeExecutionMode.DeathPauseSeconds > 0f)
                yield return new WaitForSeconds(RuntimeExecutionMode.DeathPauseSeconds);

            var score = ScoreManager.Instance;
            if (score != null)
            {
                score.LoseLife();
                if (score.Lives <= 0)
                {
                    Debug.Log("[Gameplay] Game Over!");
                    if (GameStateManager.Instance != null)
                        GameStateManager.Instance.ChangeState(GameState.GameOver);
                    var hud = UI.HUDController.Instance;
                    if (hud != null && RuntimeExecutionMode.PresentationEnabled) hud.ShowMessage("GAME OVER", 0f);

                    if (RuntimeExecutionMode.QuitOnGameOver)
                    {
                        RequestSimulationExit("game over");
                    }
                    yield break;
                }
            }

            // Reset positions and continue
            _player.ResetToSpawn();
            foreach (var g in _ghosts) g.ResetToSpawn();
            _ghostHouse.ResetAfterDeath();
            if (_ghosts != null && _ghosts.Length > 0 && _pelletManager != null)
                _ghosts[0].UpdateElroyState(_pelletManager.RemainingPellets, _pelletManager.TotalPellets);
            _ghostModeTimer.ResetTimer();

            var readyHud = UI.HUDController.Instance;
            if (readyHud != null && RuntimeExecutionMode.PresentationEnabled)
                readyHud.ShowMessage("READY!", RuntimeExecutionMode.DeathReadyDelaySeconds);

            if (RuntimeExecutionMode.DeathReadyDelaySeconds > 0f)
                yield return new WaitForSeconds(RuntimeExecutionMode.DeathReadyDelaySeconds);
            _roundActive = true;
            _player.Unfreeze();
            foreach (var g in _ghosts) g.Unfreeze();
            _ghostModeTimer.StartTimer(score != null ? score.CurrentRound : 1);
            if (_audio != null) _audio.StartSiren();

            if (GameStateManager.Instance != null)
                GameStateManager.Instance.ChangeState(GameState.Playing);
        }

        private void OnRoundCleared()
        {
            if (!_roundActive) return;
            _roundActive = false;
            Debug.Log("[Gameplay] Round cleared!");
            StartCoroutine(HandleRoundClear());
        }

        private IEnumerator HandleRoundClear()
        {
            if (GameStateManager.Instance != null)
                GameStateManager.Instance.ChangeState(GameState.RoundClear);

            CancelFrightenedCoroutine();
            _player.Freeze();
            foreach (var g in _ghosts) g.Freeze();
            _ghostModeTimer.PauseTimer();
            if (_audio != null) _audio.PlayRoundClear();

            if (RuntimeExecutionMode.PresentationEnabled && _mazeRenderer != null)
                yield return _mazeRenderer.FlashMaze();

            if (RuntimeExecutionMode.RoundClearPauseSeconds > 0f)
                yield return new WaitForSeconds(RuntimeExecutionMode.RoundClearPauseSeconds);

            var score = ScoreManager.Instance;
            int nextRound = (score != null ? score.CurrentRound : 1) + 1;

            if (RuntimeExecutionMode.MaxRounds > 0 && nextRound > RuntimeExecutionMode.MaxRounds)
            {
                RequestSimulationExit($"cleared round {RuntimeExecutionMode.MaxRounds}");
                yield break;
            }

            StartRound(nextRound);
        }

        private void SetupCamera()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                var camObj = new GameObject("MainCamera");
                camObj.tag = "MainCamera";
                cam = camObj.AddComponent<Camera>();
                camObj.AddComponent<AudioListener>();
            }

            cam.orthographic = true;
            // Maze is 28x31 tiles at 0.5 units each = 14x15.5 world units, centered at origin
            // Orthographic size = half the vertical extent we want visible
            cam.orthographicSize = 9f;
            cam.transform.position = new Vector3(0f, -0.5f, -10f);
            cam.backgroundColor = new Color(0.05f, 0.05f, 0.1f); // dark blue-black
            cam.clearFlags = CameraClearFlags.SolidColor;
        }

        private void OnDestroy()
        {
            CancelFrightenedCoroutine();

            if (_pelletManager != null)
            {
                _pelletManager.OnPelletCollected -= OnPelletCollected;
                _pelletManager.OnAllPelletsCleared -= OnRoundCleared;
            }
            if (_player != null)
                _player.OnPelletCollected -= OnPlayerPelletCollected;
            if (_collisionManager != null)
            {
                _collisionManager.OnPlayerCaught -= OnPlayerCaught;
                _collisionManager.OnGhostEaten -= OnGhostEaten;
            }
            if (_ghostModeTimer != null)
                _ghostModeTimer.OnModeChanged -= OnGhostModeChanged;
        }

        private void CancelFrightenedCoroutine()
        {
            if (_frightenedCoroutine == null)
                return;

            StopCoroutine(_frightenedCoroutine);
            _frightenedCoroutine = null;
        }

        private void RequestSimulationExit(string reason)
        {
            if (_simulationQuitRequested)
                return;

            _simulationQuitRequested = true;
            StartCoroutine(QuitSimulationAfterFrame(reason));
        }

        private IEnumerator QuitSimulationAfterFrame(string reason)
        {
            Debug.Log($"[GameplaySceneSetup] Simulation run complete ({reason}). Exiting.");
            yield return null;
            Application.Quit(0);

#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }
    }
}
