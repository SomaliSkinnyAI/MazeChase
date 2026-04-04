# MazeChase Architecture Document

## 1. Project Overview

**MazeChase** is a faithful recreation of the classic Pac-Man arcade game built in **Unity 6000.0.72f1** (Unity 6) using C# and the built-in render pipeline. The game targets **Windows x64 (StandaloneWindows64)** as its primary build platform.

All visual assets (sprites, particles, UI) and audio clips are generated procedurally at runtime -- the project has zero imported asset dependencies. The maze layout, ghost AI targeting strategies, and difficulty tuning closely follow the original 1980 Pac-Man arcade game.

- **Engine:** Unity 6 (6000.0.72f1)
- **Language:** C# (.NET Standard)
- **Render Pipeline:** Built-in (2D orthographic)
- **Input:** Unity Input System (new) -- WASD and Arrow keys
- **Build Target:** Windows x64 standalone
- **Version:** 0.1.0
- **Bundle Identifier:** com.indiearcade.mazechase

---

## 2. Directory Structure

```
MazeChase/                          Unity project root
  Assets/
    Art/
      Materials/                    (placeholder -- materials not used at runtime)
      Shaders/                      (placeholder -- no custom shaders)
      Sprites/                      (placeholder -- sprites generated at runtime)
    Audio/
      Music/                        (placeholder -- audio is procedural)
      SFX/                          (placeholder -- audio is procedural)
    Editor/
      Build/
        BuildConfig.cs              Build constants (name, version, company)
        BuildScript.cs              Command-line build entry point
        BuildReportWriter.cs        JSON + Markdown build report generation
      SceneSetup.cs                 Editor utility to create BootScene
    Prefabs/
      Characters/                   (placeholder -- characters created at runtime)
      Maze/                         (placeholder -- maze created at runtime)
      UI/                           (placeholder -- UI is OnGUI-based)
    Resources/                      (empty -- no Resources.Load usage)
    Scenes/
      BootScene.unity               Single scene; entry point for the game
    Scripts/
      AI/
        Autoplay/
          AutoplayManager.cs        F2-toggled AI driver for the player character
          ExpertBot.cs              BFS-based decision-making AI bot
        BashfulTarget.cs            Inky chase/scatter targeting (vector doubling)
        Ghost.cs                    Core ghost MonoBehaviour (movement, state, visuals)
        GhostHouse.cs               Ghost release order and pellet threshold logic
        GhostModeTimer.cs           Global scatter/chase phase timer
        GhostState.cs               Ghost state enum
        IGhostTargetStrategy.cs     Interface for ghost targeting strategies
        PokeyTarget.cs              Clyde chase/scatter targeting (proximity toggle)
        RoundTuningData.cs          Per-round speed, duration, and fruit tables
        ShadowTarget.cs             Blinky chase/scatter targeting (direct pursuit)
        SpeedyTarget.cs             Pinky chase/scatter targeting (4-tile look-ahead)
      Audio/
        AudioManager.cs             Singleton managing all audio sources and playback
        ProceduralAudio.cs          Static AudioClip.Create generators for all sounds
      Core/
        CommandLineArgs.cs          Static utility for parsing --flag and --key=value args
        GameBootstrap.cs            Root MonoBehaviour; singleton setup, smoke test, ESC quit
        GameState.cs                Enum: Boot, Menu, Playing, Dying, RoundClear, GameOver, etc.
        GameStateManager.cs         Singleton owning current GameState with change events
        RoundManager.cs             Pellet tracking, round-clear detection, round advancement
        ScoreManager.cs             Score, high score, lives, round number, ghost combo
      Game/
        CollisionManager.cs         Per-frame tile-distance ghost/player collision checks
        Direction.cs                Enum + DirectionHelper (ToVector, Opposite, AllDirections)
        FruitSpawner.cs             Bonus fruit spawning at pellet thresholds (70, 170)
        GameplaySceneSetup.cs       Master orchestrator: creates all objects, wires events
        MazeData.cs                 Static 28x31 maze layout with tile queries
        MazeRenderer.cs             Runtime sprite creation and maze rendering
        MazeTile.cs                 Tile type enum (Wall, Pellet, Energizer, Tunnel, etc.)
        PelletManager.cs            Runtime pellet state tracking and collection events
        PlayerController.cs         Input-driven grid movement with lerp and mouth animation
      Infrastructure/
        CrashHandling/
          CrashHandler.cs           Unhandled exception capture and crash marker files
        Diagnostics/
          DiagnosticsOverlay.cs     F1-toggled debug HUD (FPS, session, errors, version)
          SessionInfo.cs            Static session metadata (GUID, timestamps, versions)
        Logging/
          ConsoleSink.cs            ILogSink writing to Unity Debug.Log
          FileSink.cs               ILogSink writing to .log text files
          GameLogger.cs             Thread-safe multi-sink logger implementation
          IGameLogger.cs            Logger interface with severity-level methods
          ILogSink.cs               Sink interface (Write, Flush, Dispose)
          JsonlSink.cs              ILogSink writing JSON Lines format
          LogEntry.cs               Log entry struct with ToLogLine() and ToJsonLine()
          LogManager.cs             Singleton bootstrapping logging with 5 sinks
          LogSeverity.cs            Enum: Trace, Debug, Info, Warning, Error, Critical
      UI/
        HUDController.cs            OnGUI-based HUD (score, lives, round, messages, popups)
      VFX/
        ScreenEffects.cs            Full-screen flash and camera shake singleton
        SimpleParticles.cs          Lightweight particle bursts (pellet, ghost eat, death)
    Settings/                       Unity project settings
    Tests/
      EditMode/                     EditMode test assembly (placeholder)
      PlayMode/                     PlayMode test assembly (placeholder)
  BuildOutput/                      Build artifacts (generated)
  Library/                          Unity cache (generated)
  Logs/                             Unity editor logs
  Packages/                         Unity package manifest
  ProjectSettings/                  Unity project settings
  UserSettings/                     Per-user Unity editor settings

tools/                              PowerShell build and CI scripts (project-root level)
  build-win64.ps1                   Builds Win64 player via Unity batchmode
  clean-rebuild.ps1                 Deletes Library cache and rebuilds
  collect-diagnostics.ps1           Gathers logs into a diagnostics bundle
  create-project.ps1                Creates the Unity project from scratch
  run-editor-tests.ps1              Runs EditMode tests via batchmode
  run-game.ps1                      Launches built executable with monitoring
  run-playmode-tests.ps1            Runs PlayMode tests via batchmode
  smoke-test.ps1                    End-to-end: build, launch, verify
  verify-environment.ps1            Checks Unity, Git, disk space, VS
```

---

## 3. File Inventory

### Core (Assets/Scripts/Core/)

| File | Description |
|------|-------------|
| `GameBootstrap.cs` | Root MonoBehaviour on the BootScene. Creates LogManager and CrashHandler singletons in Awake, supports `--smoke-test` mode for CI, handles ESC-to-quit, and instantiates GameplaySceneSetup to start the game. |
| `GameState.cs` | Enum defining all high-level game states: Boot, Menu, Playing, Dying, RoundClear, GameOver, Intermission, Paused. |
| `GameStateManager.cs` | Singleton MonoBehaviour that owns the current GameState. Fires `OnStateChanged(oldState, newState)` on transitions. Persists via DontDestroyOnLoad. |
| `ScoreManager.cs` | Singleton tracking score, high score (persisted via PlayerPrefs), lives (default 3), round number, and the ghost-eat combo multiplier (200/400/800/1600). Fires OnScoreChanged, OnLivesChanged, OnRoundChanged. |
| `RoundManager.cs` | Manages a single round lifecycle: pellet counting, round-clear detection, and advancement to next round. Fires OnRoundStarted and OnRoundCleared. |
| `CommandLineArgs.cs` | Static utility for parsing command-line arguments. Supports `--flag` (HasFlag) and `--key=value` or `--key value` (GetValue). Caches args on first access. |

### Game (Assets/Scripts/Game/)

| File | Description |
|------|-------------|
| `MazeTile.cs` | Enum for tile types: Empty, Wall, Pellet, Energizer, GhostHouse, GhostDoor, Tunnel, PlayerSpawn, FruitSpawn, GhostSpawn. |
| `Direction.cs` | Direction enum (None, Up, Down, Left, Right) and DirectionHelper static class providing ToVector (returns Vector2Int offsets), Opposite, and AllDirections array. Up maps to (0, -1) since row 0 is at the top. |
| `MazeData.cs` | Static class encoding the classic 28x31 Pac-Man maze as a jagged array of MazeTile rows. Provides GetTile(x,y), IsWalkable, IsIntersection, CloneLayout, CountPellets, and spawn position queries. |
| `MazeRenderer.cs` | Creates the entire maze visual at runtime using SpriteRenderer GameObjects. Generates rounded-rect wall sprites with neon glow/shadow effects, glow-circle pellet/energizer sprites, and a dark background quad. Handles energizer pulse animation, pellet removal with particle effects, and maze flash on round clear. |
| `PlayerController.cs` | Grid-based player movement with Unity Input System. Buffers desired direction (QueuedDirection), lerps between tile centers at configurable speed, handles tunnel warping, fires OnPelletCollected when arriving at pellet/energizer tiles. Generates 48x48 Pac-Man sprites with 3 mouth animation frames (closed, half-open, wide-open). Supports Freeze/Unfreeze for death/round-clear sequences. |
| `PelletManager.cs` | Singleton maintaining a runtime copy of the maze layout. Tracks pellets eaten vs remaining, fires OnPelletCollected and OnAllPelletsCleared. Provides HasPellet and GetRuntimeTile for AI queries. |
| `CollisionManager.cs` | Checks tile-distance overlap between player and each ghost every frame (only during Playing state). Fires OnGhostEaten for frightened ghosts and OnPlayerCaught for scatter/chase ghosts. Skips Eaten/InHouse/ExitingHouse ghosts. |
| `FruitSpawner.cs` | Spawns bonus fruit at the designated tile (row 17, col 14) when 70 and 170 pellets have been eaten. Each fruit type is drawn procedurally as a 24x24 pixel art sprite (Cherry through Key). Despawns after 10 seconds if not collected. |
| `GameplaySceneSetup.cs` | Master orchestrator created by GameBootstrap. Creates MazeRenderer, PelletManager, PlayerController, 4 Ghosts, FruitSpawner, CollisionManager, GhostModeTimer, GhostHouse, AudioManager, HUDController, AutoplayManager, and ScreenEffects. Wires all events, manages round lifecycle including frightened mode activation/deactivation, death sequence, and round clear sequence. Configures the orthographic camera. |

### AI (Assets/Scripts/AI/)

| File | Description |
|------|-------------|
| `GhostState.cs` | Enum: Scatter, Chase, Frightened, Eaten, InHouse, ExitingHouse. |
| `IGhostTargetStrategy.cs` | Interface with GetChaseTarget and GetScatterTarget methods. Each ghost type implements its own targeting logic. |
| `ShadowTarget.cs` | Blinky (Ghost 0) strategy. Chase: targets player's current tile directly. Scatter: top-right corner (27, 30). |
| `SpeedyTarget.cs` | Pinky (Ghost 1) strategy. Chase: targets 4 tiles ahead of player. Includes the classic overflow bug where facing Up also offsets 4 tiles left. Scatter: top-left corner (0, 30). |
| `BashfulTarget.cs` | Inky (Ghost 2) strategy. Chase: computes a pivot 2 tiles ahead of player, then doubles the vector from Blinky to that pivot. Includes the Up-direction overflow bug. Scatter: bottom-right corner (27, 0). |
| `PokeyTarget.cs` | Clyde (Ghost 3) strategy. Chase: targets player directly when >8 tiles away; retreats to scatter corner when within 8 tiles. Scatter: bottom-left corner (0, 0). |
| `Ghost.cs` | Core ghost MonoBehaviour (1031 lines). Handles tile-based movement with lerp interpolation, direction choice via Euclidean distance minimization at intersections (with Up > Left > Down > Right tie-breaking), state machine transitions with direction reversal, tunnel wrapping, ghost house door access rules, frightened mode visuals with flashing, Elroy speed mode, and full sprite generation including body frames (2-frame wavy bottom animation), frightened frames, and eye/pupil child objects that shift with movement direction. |
| `GhostModeTimer.cs` | Drives the global scatter/chase phase alternation. Uses data-driven timing tables per round group (Round 1, Rounds 2-4, Round 5+). Each table has 8 phases ending with Chase forever. Supports Pause/Resume for frightened mode interruptions. Fires OnModeChanged. |
| `GhostHouse.cs` | Manages ghost release order. Ghost 0 (Blinky) always starts outside. Ghosts 1-3 start inside and release based on per-ghost pellet thresholds (0, 30, 60) or a 4-second global inactivity timer. Handles re-entry of eaten ghosts. Provides FreezeAll/UnfreezeAll/FrightenAll convenience methods. |
| `RoundTuningData.cs` | Static class with per-round tuning tables. Returns RoundTuning structs containing player speed, ghost speed, frightened speeds, tunnel speed, eaten speed, frightened duration, flash count, fruit type, and fruit score. Speed progression ramps from easy (round 1: ghost 1.8 t/s) to expert (round 13+: ghost 3.4 t/s). |

### AI Autoplay (Assets/Scripts/AI/Autoplay/)

| File | Description |
|------|-------------|
| `AutoplayManager.cs` | Singleton MonoBehaviour toggled with F2. When active, calls ExpertBot.GetBestDirection each frame and injects the result into PlayerController via ForceDirection. Displays "AI PLAYING" label via OnGUI. Finds player, ghosts, and PelletManager references lazily. |
| `ExpertBot.cs` | Stateless-per-tile AI decision maker. Only re-evaluates when arriving at a new tile. Uses directional BFS pellet counting (blocking the backward tile to only count forward-reachable pellets), ghost danger scoring with distance-based penalties, frightened ghost chasing via Manhattan distance, energizer detection via BFS, momentum bonuses, and anti-oscillation via a tile-decision cache. Logs decisions to a file. |

### Audio (Assets/Scripts/Audio/)

| File | Description |
|------|-------------|
| `AudioManager.cs` | Singleton managing 3 AudioSources (SFX one-shots, siren loop, frightened loop). Generates all clips via ProceduralAudio on Awake. Provides Play methods for each game event and manages transitions between siren/frightened loops. Persists via DontDestroyOnLoad. |
| `ProceduralAudio.cs` | Static class generating AudioClips via AudioClip.Create with computed PCM sample data. All sounds use sine wave synthesis with envelopes. |

### UI (Assets/Scripts/UI/)

| File | Description |
|------|-------------|
| `HUDController.cs` | Singleton OnGUI-based HUD. Renders score (top-left), high score (top-center), round (top-right), lives (bottom-left), FPS (bottom-right), center messages (READY!, GAME OVER), floating score popups that drift upward and fade, and combo text for ghost-eating chains. Uses semi-transparent background bars for readability. |

### VFX (Assets/Scripts/VFX/)

| File | Description |
|------|-------------|
| `SimpleParticles.cs` | Static utility for lightweight particle effects. Creates a cached 4x4 white square sprite. SpawnBurst creates N particles moving outward with fade/shrink. SpawnGhostEatEffect and SpawnDeathEffect are specialized variants (spiral movement for death). Each burst is driven by an internal ParticleBurstDriver MonoBehaviour that self-destructs. |
| `ScreenEffects.cs` | Singleton providing screen-wide Flash (OnGUI color overlay that fades) and Shake (camera position jitter with decay). Flash uses unscaled time for consistency during Time.timeScale changes. |

### Infrastructure (Assets/Scripts/Infrastructure/)

| File | Description |
|------|-------------|
| `LogSeverity.cs` | Enum: Trace, Debug, Info, Warning, Error, Critical. |
| `ILogSink.cs` | Interface extending IDisposable with Write(LogEntry) and Flush() methods. |
| `IGameLogger.cs` | Logger interface with Log(severity, category, message, fields) and convenience methods for each severity level. |
| `LogEntry.cs` | Struct containing Timestamp, Severity, Category, Message, SessionId, SceneName, Fields (dictionary), Exception. Provides ToLogLine() (human-readable) and ToJsonLine() (JSON Lines format with manual serialization). |
| `GameLogger.cs` | Thread-safe IGameLogger implementation dispatching entries to multiple ILogSink instances. Swallows individual sink exceptions to prevent cascade failures. |
| `ConsoleSink.cs` | Writes log entries to Unity's Debug.Log/LogWarning/LogError based on severity. |
| `FileSink.cs` | Appends formatted log lines to a text file. Auto-flushes on Error or higher. Thread-safe with lock. |
| `JsonlSink.cs` | Appends JSON Lines entries to a .jsonl file. Same flush and threading behavior as FileSink. |
| `LogManager.cs` | Singleton MonoBehaviour bootstrapping the logging system. Creates 5 sinks: ConsoleSink, two FileSinks (latest.log + session-specific), two JsonlSinks (latest.jsonl + session-specific). Hooks Unity's logMessageReceivedThreaded to capture third-party logs. Logs a startup banner with system info. |
| `SessionInfo.cs` | Static class generating a GUID session ID on first access and exposing AppVersion, UnityVersion, and Platform. |
| `CrashHandler.cs` | Singleton hooking AppDomain.UnhandledException and Unity log exceptions. Logs through LogManager and writes timestamped crash marker files to persistentDataPath/Logs/crashes/. |
| `DiagnosticsOverlay.cs` | F1-toggled debug overlay showing FPS, scene name, session ID, warning/error counts, app version, Unity version, and log file path. Rendered via OnGUI with a semi-transparent black background box. |

### Editor (Assets/Editor/)

| File | Description |
|------|-------------|
| `SceneSetup.cs` | Editor utility (menu item "MazeChase/Create Boot Scene") that creates BootScene with GameBootstrap, infrastructure singletons, GameStateManager, ScoreManager, and Main Camera. Also callable from command line via -executeMethod. |
| `BuildConfig.cs` | Static constants: GameName="MazeChase", Version="0.1.0", CompanyName="IndieArcade", BundleIdentifier="com.indiearcade.mazechase". |
| `BuildScript.cs` | Command-line build entry point (BuildSystem.BuildScript.BuildWin64). Configures PlayerSettings, builds StandaloneWindows64 with StrictMode, writes JSON + Markdown build reports via BuildReportWriter. Supports -devBuild flag. |
| `BuildReportWriter.cs` | Writes build-result.json and build-result.md to the Reports directory. Includes build success, timing, error/warning counts, Unity version, git commit hash, and build step details. |

---

## 4. System Architecture Diagram

```
+------------------------------------------------------------------+
|                        GAME BOOTSTRAP                             |
|  GameBootstrap (BootScene)                                        |
|    |-- EnsureSingleton<LogManager>                                |
|    |-- EnsureSingleton<CrashHandler>                              |
|    +-- Creates GameplaySceneSetup (runtime)                       |
+---------|--------------------------------------------------------+
          |
          v
+------------------------------------------------------------------+
|                    GAMEPLAY SCENE SETUP                            |
|  (Master orchestrator -- creates and wires everything)            |
+---------|-----------|-----------|-----------|---------------------+
          |           |           |           |
  +-------v---+ +----v------+ +-v--------+ +-v-----------+
  |   CORE    | |   GAME    | |    AI    | |   AUDIO     |
  +-----------+ +-----------+ +----------+ +-------------+
  |GameState- | |MazeData   | |Ghost (x4)| |AudioManager |
  | Manager   | |MazeRender-| |GhostState| |Procedural-  |
  |ScoreMan-  | | er        | | Machine  | | Audio       |
  | ager      | |PlayerCon- | |IGhostTar-| +-------------+
  |RoundMan-  | | troller   | | getStrat-|
  | ager      | |PelletMan- | | egy      |     +--------+
  |CommandLi- | | ager      | |ShadowTar-|     |   UI   |
  | neArgs    | |Collision- | | get      |     +--------+
  +-----------+ | Manager   | |SpeedyTar-|     |HUDCon- |
                |FruitSpaw- | | get      |     | troller|
                | ner       | |BashfulTa-|     +--------+
                |Direction  | | rget     |
                |MazeTile   | |PokeyTar- |     +--------+
                +-----------+ | get      |     |  VFX   |
                              |GhostMode-|     +--------+
                              | Timer    |     |Simple- |
                              |GhostHou- |     | Partic-|
                              | se       |     | les    |
                              |RoundTun- |     |Screen- |
                              | ingData  |     | Effects|
                              +----------+     +--------+

                              +-------------+
                              | AI AUTOPLAY |
                              +-------------+
                              |AutoplayMan- |
                              | ager (F2)   |
                              |ExpertBot    |
                              +-------------+

+------------------------------------------------------------------+
|                      INFRASTRUCTURE                               |
|  LogManager --> GameLogger --> [ConsoleSink, FileSink, JsonlSink] |
|  CrashHandler --> LogManager (crash markers)                      |
|  DiagnosticsOverlay (F1 debug HUD)                                |
|  SessionInfo (GUID, versions)                                     |
+------------------------------------------------------------------+

+------------------------------------------------------------------+
|                       BUILD SYSTEM                                |
|  Editor/Build/: BuildScript, BuildConfig, BuildReportWriter       |
|  Editor/: SceneSetup                                              |
|  tools/*.ps1: build-win64, smoke-test, run-game, etc.             |
+------------------------------------------------------------------+
```

### Event Wiring Diagram

```
PlayerController.OnPelletCollected ----> GameplaySceneSetup.OnPlayerPelletCollected
                                              |
                                              +--> PelletManager.TryCollect
                                              +--> GhostHouse.OnPelletEaten
                                              +--> Ghost[0].UpdateElroyState
                                              +--> FruitSpawner.TryCollectFruit

PelletManager.OnPelletCollected ---------> GameplaySceneSetup.OnPelletCollected
                                              |
                                              +--> ScoreManager.AddScore (10 or 50)
                                              +--> AudioManager.PlayPelletEat/PlayEnergizerEat
                                              +--> ActivateFrightenedMode (if energizer)

PelletManager.OnAllPelletsCleared -------> GameplaySceneSetup.OnRoundCleared
                                              |
                                              +--> HandleRoundClear (freeze, flash, next round)

CollisionManager.OnGhostEaten -----------> GameplaySceneSetup.OnGhostEaten
                                              |
                                              +--> ScoreManager.GetNextGhostComboValue + AddScore
                                              +--> Ghost.SetState(Eaten)
                                              +--> AudioManager.PlayGhostEat
                                              +--> ScreenEffects.Flash + SimpleParticles

CollisionManager.OnPlayerCaught ---------> GameplaySceneSetup.OnPlayerCaught
                                              |
                                              +--> HandleDeath (freeze, lose life, reset or game over)

GhostModeTimer.OnModeChanged ------------> GameplaySceneSetup.OnGhostModeChanged
                                              |
                                              +--> Each Ghost.SetState(Scatter or Chase)
```

---

## 5. Data Flow

### 5.1 Typical Game Frame

```
Update Loop:
  1. PlayerController.Update()
     a. ReadInput() -- polls WASD/Arrow via InputAction
     b. If Moving: AdvanceLerp() -- interpolate position, check for mid-tile reversal
     c. If Stopped: TryStartMoving() -- prefer QueuedDirection, fall back to CurrentDirection
     d. AnimateMouth() -- cycle through 3 frames (closed/half/wide)

  2. AutoplayManager.Update() (if F2 active)
     a. ExpertBot.GetBestDirection() -- score each walkable direction
     b. PlayerController.ForceDirection(bestDir)

  3. Ghost[0..3].Update() (each ghost)
     a. UpdateFrightenedVisuals() -- flash timer, pulse scale
     b. UpdateBodyAnimation() -- alternate body frames for wavy bottom
     c. UpdateEyeDirection() -- shift pupils based on movement direction
     d. Move() -- if InHouse: bob; else: lerp toward next tile, choose direction at center

  4. CollisionManager.Update()
     a. For each ghost: compute tile distance to player
     b. If overlap with Frightened ghost: fire OnGhostEaten
     c. If overlap with Scatter/Chase ghost: fire OnPlayerCaught

  5. GhostModeTimer.Update()
     a. Advance phase timer
     b. If phase elapsed: advance to next phase, fire OnModeChanged

  6. GhostHouse.Update()
     a. Increment inactivity timer
     b. If timeout (4s): release next waiting ghost

  7. FruitSpawner (via PelletManager event)
     a. Check pellet thresholds (70, 170) for fruit spawn

  8. ScreenEffects.Update()
     a. Advance flash fade and camera shake decay

  9. OnGUI() calls (each frame):
     - HUDController: score, lives, round, messages
     - DiagnosticsOverlay (if F1): debug info
     - AutoplayManager (if F2): "AI PLAYING" label
     - ScreenEffects: flash overlay
```

### 5.2 Input to Movement to Collision to Score/Death

```
Keyboard Input (WASD/Arrows)
    |
    v
PlayerController.ReadInput() sets QueuedDirection
    |
    v
PlayerController.TryStartMoving() / AdvanceLerp()
    |-- QueuedDirection valid? Begin move toward that direction
    |-- At tile center? Fire ArriveAtTile()
    |     |-- Tunnel? HandleTunnelWarp()
    |     |-- Pellet/Energizer? Fire OnPelletCollected
    |     +-- Continue moving or stop at wall
    |
    v
CollisionManager.Update() checks each ghost tile distance
    |
    +-- Frightened ghost overlaps --> OnGhostEaten
    |     |-- ScoreManager.AddScore(200/400/800/1600)
    |     +-- Ghost.SetState(Eaten) -- eyes-only, fast return to house
    |
    +-- Chase/Scatter ghost overlaps --> OnPlayerCaught
          |-- GameStateManager -> Dying
          |-- ScoreManager.LoseLife()
          |-- Lives > 0? Reset positions, continue
          +-- Lives == 0? GameStateManager -> GameOver
```

### 5.3 Ghost AI Decision Cycle

```
Ghost.Move() called each frame
    |
    +-- State == InHouse? BobInHouse() -- vertical sine wave
    |
    +-- Otherwise: lerp toward nextTile at current speed
          |
          +-- Reached tile center (moveProgress >= 1)?
                |
                +-- OnReachedTileCenter()
                |     |-- State == Eaten && at ghost house? -> ExitingHouse
                |     +-- State == ExitingHouse && at exit tile? -> Scatter
                |
                +-- ChooseDirection()
                      |
                      +-- GetTargetTile() based on state:
                      |     Chase     -> targetStrategy.GetChaseTarget()
                      |     Scatter   -> targetStrategy.GetScatterTarget()
                      |     Frightened -> GetRandomTarget() (pseudo-random based on position+time)
                      |     Eaten     -> HouseExitTile (13, 19)
                      |     Exiting   -> HouseExitTile (13, 19)
                      |
                      +-- For each direction (Up, Left, Down, Right priority):
                      |     Skip reverse direction (no U-turns)
                      |     Skip if neighbor tile not enterable
                      |     Compute squared Euclidean distance to target
                      |     Pick direction with minimum distance
                      |
                      +-- StartMoveInDirection(bestDir)
```

### 5.4 Pellet Collection Flow

```
Player arrives at tile with Pellet or Energizer
    |
    v
PlayerController fires OnPelletCollected(tile, type)
    |
    v
GameplaySceneSetup.OnPlayerPelletCollected
    |
    +-- PelletManager.TryCollect(x, y)
    |     |-- Marks tile as Empty in runtime layout
    |     |-- Decrements remaining count
    |     |-- Fires OnPelletCollected event
    |     +-- If remaining == 0: fires OnAllPelletsCleared
    |
    +-- MazeRenderer.RemovePellet(tile)
    |     |-- Destroys pellet/energizer GameObject
    |     +-- Spawns particle burst (5 for pellet, 8 for energizer)
    |
    +-- GhostHouse.OnPelletEaten()
    |     |-- Increments counter, resets inactivity timer
    |     +-- Checks release thresholds for ghosts 1-3
    |
    +-- Ghost[0].UpdateElroyState() -- check Elroy speed thresholds
    |
    +-- FruitSpawner.TryCollectFruit() -- check if at fruit tile
```

### 5.5 Energizer to Frightened Mode Flow

```
Player collects Energizer (MazeTile.Energizer)
    |
    v
GameplaySceneSetup.OnPelletCollected (type == Energizer)
    |
    +-- ScoreManager.AddScore(50) + ResetGhostCombo()
    +-- AudioManager.PlayEnergizerEat()
    +-- ActivateFrightenedMode()
          |
          +-- Get RoundTuningData for current round
          +-- If FrightenedDuration <= 0: skip (some rounds have no frightened)
          +-- GhostModeTimer.PauseTimer()
          +-- Each ghost not Eaten/InHouse: SetState(Frightened)
          |     |-- Ghost reverses direction
          |     |-- Body turns blue, eyes hidden
          |     |-- Speed reduces to frightenedSpeed
          |     +-- Uses GetRandomTarget() for pseudo-random movement
          +-- Player speed increases to FrightenedPlayerSpeed
          +-- AudioManager.StartFrightened() (warbly loop)
          +-- ScreenEffects.Flash (blue tint)
          +-- Start EndFrightenedAfterDelay coroutine
                |
                +-- After duration: EndFrightenedMode()
                      |-- Restore player speed
                      |-- Each Frightened ghost: revert to current timer mode
                      |-- GhostModeTimer.ResumeTimer()
                      +-- AudioManager.ResumeNormalAudio()

During Frightened:
  Ghost flashes white near end (frightenedFlashCount * 0.4s before timeout)
  Ghost eaten? -> ScoreManager combo (200/400/800/1600), Ghost -> Eaten state
```

### 5.6 Round Lifecycle

```
GameplaySceneSetup.StartRound(roundNumber)
    |
    +-- ScoreManager.CurrentRound = roundNumber
    +-- PelletManager.ResetPellets() -- clone fresh maze layout
    +-- MazeRenderer.RebuildMaze() -- destroy and recreate all tile objects
    +-- PlayerController.ResetToSpawn() -- (14, 23)
    +-- Each Ghost.ResetToSpawn() + Initialize()
    +-- Apply RoundTuningData speeds to player and ghosts
    +-- FruitSpawner.SetRound(round) -- select fruit type
    +-- GhostModeTimer.StartTimer(round) -- begin scatter/chase phases
    +-- GhostHouse.ResetForNewRound() -- reset pellet counters
    +-- HUD: "READY!" for 2 seconds
    +-- AudioManager.PlayGameStart()
    +-- Freeze player and ghosts for 2 seconds
    +-- Unfreeze all, start siren
    +-- GameStateManager -> Playing

    ... gameplay ...

All pellets cleared:
    +-- GameStateManager -> RoundClear
    +-- Freeze player and ghosts
    +-- GhostModeTimer.PauseTimer()
    +-- AudioManager.PlayRoundClear()
    +-- MazeRenderer.FlashMaze() -- 4 white/blue flashes
    +-- Wait 1 second
    +-- StartRound(currentRound + 1) -- loop
```

---

## 6. Key Classes Deep Dive

### GameBootstrap
- **Public API:** (none -- lifecycle-only)
- **Key Dependencies:** LogManager, CrashHandler, GameplaySceneSetup
- **Singleton Pattern:** Not a singleton itself; ensures LogManager and CrashHandler singletons exist via `EnsureSingleton<T>()`
- **Creation:** Scene object -- placed in BootScene (or created by SceneSetup editor script)
- **Notes:** Entry point for the entire game. Checks `--smoke-test` flag for CI: waits 3 seconds then quits with code 0.

### GameStateManager
- **Public API:** `GetCurrentState()`, `ChangeState(GameState)`, `OnStateChanged` event
- **Key Dependencies:** None
- **Singleton Pattern:** Classic `Instance` static property, DontDestroyOnLoad, duplicate destruction in Awake
- **Creation:** Scene object or runtime via GameplaySceneSetup

### ScoreManager
- **Public API:** `Score`, `HighScore`, `Lives`, `CurrentRound` (properties); `AddScore(int)`, `GetNextGhostComboValue()`, `ResetGhostCombo()`, `LoseLife()`, `GainLife()`, `NextRound()`, `ResetGame()`; Events: `OnScoreChanged`, `OnLivesChanged`, `OnRoundChanged`
- **Key Dependencies:** PlayerPrefs for high score persistence
- **Singleton Pattern:** Classic Instance + DontDestroyOnLoad
- **Creation:** Scene object or runtime
- **Notes:** Ghost combo sequence is 200, 400, 800, 1600 (capped at 1600 for subsequent eats in same energizer)

### PlayerController
- **Public API:** `CurrentTile`, `CurrentDirection`, `QueuedDirection`, `State` (properties); `ResetToSpawn()`, `SetSpeed(float)`, `Freeze()`, `Unfreeze()`, `SetQueuedDirection(Direction)`, `ForceDirection(Direction)`, `Die()`; Events: `OnPelletCollected`, `OnDeath`
- **Key Dependencies:** MazeRenderer (for TileToWorld), MazeData (for walkability), Unity Input System
- **Singleton Pattern:** None -- single instance created by GameplaySceneSetup
- **Creation:** Runtime (new GameObject by GameplaySceneSetup)
- **Notes:** Generates 3 mouth-frame sprites (0/15/30 degree half-angles). ForceDirection used by AI autoplay to bypass queuing when stopped. Mid-tile direction reversal is supported for human players but the AI avoids triggering it.

### Ghost
- **Public API:** `CurrentTile`, `CurrentDirection`, `CurrentState`, `GhostIndex` (properties); `Initialize(index, tile, outsideHouse)`, `ApplyTuning(RoundTuning)`, `SetState(GhostState)`, `ReverseDirection()`, `ResetToSpawn()`, `Freeze()`, `Unfreeze()`, `StartFrightened(duration, flashCount)`, `UpdateElroyState(pelletsRemaining, totalPellets)`, `ChooseDirection()`, `GetTargetTile()`, `SetTargetStrategy(IGhostTargetStrategy)`
- **Key Dependencies:** MazeRenderer, MazeData, PlayerController, IGhostTargetStrategy, all other Ghost instances (for Bashful's strategy)
- **Singleton Pattern:** None -- 4 instances
- **Creation:** Runtime (new GameObject by GameplaySceneSetup)
- **Notes:** RequireComponent(typeof(SpriteRenderer)). Creates body sprites (2 frames, wavy bottom), frightened frames, eye white ovals, and pupil ovals as child GameObjects. 1031 lines -- largest file in the codebase.

### GhostModeTimer
- **Public API:** `CurrentMode`, `IsPaused`, `TimeRemainingInPhase` (properties); `StartTimer(round)`, `PauseTimer()`, `ResumeTimer()`, `ResetTimer()`, `GetFrightenedDuration()`, `GetFrightenedFlashCount()`; Event: `OnModeChanged`
- **Key Dependencies:** RoundTuningData
- **Singleton Pattern:** None
- **Creation:** Runtime (new GameObject by GameplaySceneSetup)

### GhostHouse
- **Public API:** `PelletsEaten`, `HouseCenter`, `HouseDoor` (properties); `Init(ghosts, renderer)`, `OnPelletEaten()`, `ShouldRelease(ghostIndex)`, `ReleaseGhost(ghost)`, `GhostReturningToHouse(index)`, `GhostEnteredHouse(index)`, `ResetForNewRound()`, `ResetAfterDeath()`, `ApplyRoundTuning(round)`, `FreezeAll()`, `UnfreezeAll()`, `FrightenAll(duration, flashCount)`, `IsReleased(index)`
- **Key Dependencies:** Ghost[], MazeRenderer
- **Singleton Pattern:** None
- **Creation:** Runtime (new GameObject by GameplaySceneSetup)
- **Notes:** Pellet thresholds: Ghost 1 = 0 (immediate), Ghost 2 = 30, Ghost 3 = 60. Global release timeout = 4 seconds.

### MazeData
- **Public API:** `Width` (28), `Height` (31); `GetTile(x, y)`, `IsWalkable(x, y)`, `IsIntersection(x, y)`, `GetPlayerSpawn()`, `GetGhostSpawns()`, `GetFruitSpawn()`, `GetTunnelEntrances()`, `CountPellets()`, `CloneLayout()`
- **Key Dependencies:** None (fully static)
- **Singleton Pattern:** Static class
- **Creation:** N/A -- compile-time constant data

### ExpertBot
- **Public API:** `GetBestDirection(tile, curDir, ghosts, pellets)`
- **Key Dependencies:** MazeData, PelletManager, Ghost[], DirectionHelper
- **Singleton Pattern:** None -- plain C# class, instantiated by AutoplayManager
- **Creation:** `new ExpertBot()` in AutoplayManager.Awake
- **Notes:** Logs all decisions to a file at persistentDataPath/Logs/ai-decisions.log

### AudioManager
- **Public API:** `PlayPelletEat()`, `PlayEnergizerEat()`, `PlayGhostEat()`, `PlayDeath()`, `PlayRoundClear()`, `PlayFruitCollect()`, `PlayUIClick()`, `PlayGameStart()`, `StartSiren()`, `StopSiren()`, `StartFrightened()`, `StopFrightened()`, `ResumeNormalAudio()`, `StopAll()`, `SetMasterVolume(float)`, `SetSFXVolume(float)`
- **Key Dependencies:** ProceduralAudio
- **Singleton Pattern:** Classic Instance + DontDestroyOnLoad
- **Creation:** Runtime (by GameplaySceneSetup if not already present)

### GameplaySceneSetup
- **Public API:** `SetupGameplay()`
- **Key Dependencies:** All game systems (creates and wires them)
- **Singleton Pattern:** None
- **Creation:** Runtime (new GameObject by GameBootstrap.Start)
- **Notes:** The central nervous system of the game. All event subscriptions and lifecycle management flow through this class.

---

## 7. Maze System

### Layout Encoding

MazeData stores the classic 28x31 Pac-Man maze as a jagged array `MazeTile[][] _rows` indexed by row (y), then column (x). The layout is defined inline as C# array initializers using shorthand aliases (W=Wall, P=Pellet, E=Energizer, etc.) for visual readability.

### Coordinate System

- **x** = column, 0-27, left to right
- **y** = row, 0-30, top to bottom
- **Access:** `GetTile(x, y)` returns `_rows[y][x]`
- **Direction.Up** maps to Vector2Int(0, -1) -- decreasing y moves toward the top
- **World coordinates:** MazeRenderer converts grid to world via `TileToWorld(x, y)`:
  - X world = x * tileSize + offset (centered horizontally)
  - Y world = -y * tileSize + offset (row 0 at top = positive Y)
  - tileSize = 0.5 world units

### Key Positions

| Feature | Grid Position |
|---------|---------------|
| Player Spawn | (14, 23) |
| Ghost Spawns | Blinky: (13, 11) above house; Pinky: (13, 14) center; Inky: (12, 13) left; Clyde: (15, 13) right |
| Ghost House | Rows 12-15, columns 10-17 |
| Ghost Door | (13, 12) and (14, 12) |
| Fruit Spawn | (14, 17) |
| Tunnel Entrances | (0, 14) and (27, 14) |
| Energizers | (1, 3), (26, 3), (1, 21), (26, 21) |

### Tile Types

| Type | Value | Description |
|------|-------|-------------|
| Empty | 0 | Open corridor (no pellet) |
| Wall | 1 | Impassable barrier |
| Pellet | 2 | Small dot worth 10 points |
| Energizer | 3 | Large pulsing dot worth 50 points, triggers frightened mode |
| GhostHouse | 4 | Interior of ghost house (restricted access) |
| GhostDoor | 5 | Ghost house entrance (only ghosts in certain states can pass) |
| Tunnel | 6 | Warp tunnel (teleports to opposite side) |
| PlayerSpawn | 7 | Player starting position |
| FruitSpawn | 8 | Bonus fruit appears here |
| GhostSpawn | 9 | Ghost starting position inside house |

### MazeRenderer Rendering

MazeRenderer creates all tile visuals at runtime (no prefabs):
1. Computes an offset to center the 28x31 grid at world origin
2. Creates a dark background quad covering the entire maze area
3. Iterates all tiles and creates GameObjects with SpriteRenderers:
   - **Walls:** 3-layer effect -- dark shadow offset, glow halo at 125% scale, main rounded-rect sprite with neon edge
   - **Pellets:** Glow-circle sprite at 30% tile size, tracked in dictionary for removal
   - **Energizers:** Glow-circle at 60% tile size + separate glow halo at 200% scale, both pulsing via coroutine
   - **Ghost Door:** Rounded-rect, pink color, 32x6 pixel aspect ratio
   - **Non-wall tiles:** Corridor background layer at sort order -1
4. All sprites use Bilinear filtering for smooth edges

---

## 8. Ghost AI System

### The Four Targeting Strategies

Each ghost has a unique personality defined by its IGhostTargetStrategy implementation:

**Shadow (Blinky) -- Ghost 0 -- Red**
- Chase: Targets the player's current tile directly
- Scatter: Top-right corner (27, 30)
- Personality: The most aggressive; always knows exactly where the player is
- Special: Elroy mode -- speeds up when few pellets remain (15% at 40%, 30% at 20%)

**Speedy (Pinky) -- Ghost 1 -- Pink**
- Chase: Targets 4 tiles ahead of the player in the player's current direction
- Includes the classic overflow bug: when facing Up, target also offsets 4 tiles left
- Scatter: Top-left corner (0, 30)
- Personality: Tries to ambush by getting ahead of the player

**Bashful (Inky) -- Ghost 2 -- Cyan**
- Chase: Computes a pivot 2 tiles ahead of the player, then calculates target = 2 * pivot - Blinky's position (doubles the vector from Blinky to the pivot)
- Also includes the Up-direction overflow bug on the 2-tile look-ahead
- Scatter: Bottom-right corner (27, 0)
- Personality: Unpredictable flanking behavior that complements Blinky

**Pokey (Clyde) -- Ghost 3 -- Orange**
- Chase: When more than 8 tiles from player, targets player directly; when within 8 tiles, retreats to scatter corner
- Scatter: Bottom-left corner (0, 0)
- Personality: Shy and unpredictable; creates a circling pattern

### State Machine

```
                    +----------+
                    | InHouse  |   (bobbing animation)
                    +----+-----+
                         |  pellet threshold or timeout
                         v
                  +--------------+
                  | ExitingHouse |   (navigates to exit tile)
                  +------+-------+
                         |  reached exit tile
                         v
         +----------+         +---------+
         | Scatter  | <-----> |  Chase  |   (GhostModeTimer alternates)
         +----+-----+         +----+----+
              |   reversal         |  reversal
              +--------+-----------+
                       |
                       | energizer eaten
                       v
                 +-----------+
                 | Frightened|   (blue body, random movement, slower)
                 +-----+-----+
                       |
              +--------+--------+
              |                 |
              v                 v
         (timer ends)     (player eats ghost)
              |                 |
              v                 v
         Scatter/Chase    +---------+
         (revert to       |  Eaten  |   (eyes only, fast, targets house)
          current mode)   +----+----+
                               |  reached ghost house
                               v
                          +---------+
                          | InHouse |   (re-enters, then exits again)
                          +---------+
```

### Direction Choice Algorithm

At each tile center, the ghost evaluates all 4 cardinal directions:
1. **Cannot reverse:** The direction opposite to current movement is excluded
2. **Cannot enter walls:** Checks CanEnterTile (respects ghost door access rules)
3. **Minimize distance:** Picks the direction whose neighbor tile has the smallest squared Euclidean distance to the target tile
4. **Tie-breaking:** Directions are evaluated in priority order (Up > Left > Down > Right); first minimum wins

### Mode Timer

The scatter/chase alternation follows per-round timing tables:

**Round 1:**
Scatter(7s) -> Chase(20s) -> Scatter(7s) -> Chase(20s) -> Scatter(5s) -> Chase(20s) -> Scatter(5s) -> Chase(forever)

**Rounds 2-4:**
Scatter(7s) -> Chase(20s) -> Scatter(7s) -> Chase(20s) -> Scatter(5s) -> Chase(1033s) -> Scatter(1/60s) -> Chase(forever)

**Round 5+:**
Scatter(5s) -> Chase(20s) -> Scatter(5s) -> Chase(20s) -> Scatter(5s) -> Chase(1037s) -> Scatter(1/60s) -> Chase(forever)

Mode transitions cause all Scatter/Chase ghosts to reverse direction.

### Ghost House Release Logic

- Ghost 0 (Blinky): Always starts outside
- Ghost 1 (Pinky): Pellet threshold = 0 (released immediately at round start)
- Ghost 2 (Inky): Pellet threshold = 30
- Ghost 3 (Clyde): Pellet threshold = 60
- Global inactivity timer: If no pellets eaten for 4 seconds, release next waiting ghost
- Eaten ghosts returning to house re-enter and exit without waiting for thresholds

### Elroy Mode

Blinky (Ghost 0) gets progressively faster as pellets are cleared:
- **Elroy 1:** Activates when 40% of pellets remain -- 15% speed increase
- **Elroy 2:** Activates when 20% of pellets remain -- 30% speed increase

---

## 9. AI Autoplay System

### Overview

Toggle with F2. AutoplayManager drives the player by calling `ExpertBot.GetBestDirection()` each frame and injecting the result via `PlayerController.ForceDirection()`. Only operates during GameState.Playing.

### ExpertBot Decision Algorithm

ExpertBot makes decisions **only at new tiles** (when CurrentTile changes). Between tiles, it returns the previously decided direction. The algorithm:

1. **Anti-oscillation check:** If a decision was already made for this tile (stored in `_tileDecisions` cache), reuse it. Cache clears every 30 decisions.

2. **Get walkable directions:** Filter AllDirections for those with walkable neighbors.

3. **Frightened ghost chase:** If any ghost is in Frightened state within 15 Manhattan distance, pick the direction that moves closest to it.

4. **Score each direction:** For each walkable direction, compute a composite score:

   - **Pellet counting (BFS):** Flood-fill from the neighbor tile, blocking the current tile (so we only count pellets reachable in the forward direction). Count pellets within 15 tiles. Score += count * 10.

   - **Immediate pellet:** If the next tile has a pellet, +20.

   - **Energizer bonus:** BFS search for energizers within 12 tiles: +40 if found.

   - **Empty penalty:** If no pellets ahead: -50.

   - **Ghost danger:** For each Chase/Scatter ghost:
     - Distance 0-1 tiles: -500
     - Distance 2-3 tiles: -200
     - Distance 4-5 tiles: -50
     - Moving toward ghost within 6 tiles: -80 extra

   - **Momentum:** Continuing current direction: +5. Reversing: -30.

   - **Turn bonus:** Perpendicular turns at intersections: +8 (encourages exploration).

5. **Pick highest-scoring direction**, store in tile decision cache.

### Decision Logging

All decisions are logged to `{persistentDataPath}/Logs/ai-decisions.log` with tile position, current direction, chosen direction, score, and available directions.

---

## 10. Audio System

### Architecture

AudioManager creates 3 AudioSource components on its GameObject:
- **_sfxSource:** For one-shot sound effects (PlayOneShot)
- **_sirenSource:** Looping siren during normal gameplay
- **_frightenedSource:** Looping warbly sound during frightened mode

All AudioClips are generated procedurally via `ProceduralAudio` static methods using `AudioClip.Create()` with computed PCM float arrays at 44100 Hz sample rate.

### Sound Effects

| Sound | Method | Description |
|-------|--------|-------------|
| Pellet Eat (High) | `CreatePelletEat(1.2f)` | 50ms sine blip at 960 Hz with decay envelope |
| Pellet Eat (Low) | `CreatePelletEat(1.0f)` | 50ms sine blip at 800 Hz with decay (alternates with high) |
| Energizer Eat | `CreateEnergizerEat()` | 300ms rising tone (200-600 Hz) with dual harmonics |
| Ghost Eat | `CreateGhostEat()` | 200ms descending tone (600-200 Hz) with noise crunch |
| Death | `CreateDeath()` | 1.0s descending tone (800-160 Hz) with linear decay |
| Round Clear | `CreateRoundClear()` | 800ms ascending arpeggio (C5-E5-G5-C6) |
| Fruit Collect | `CreateFruitCollect()` | 150ms dual-frequency chime (1200+1500 Hz) |
| Siren | `CreateSiren()` | 2.0s looping sine with FM modulation (300 +/- 50 Hz at 2 Hz) |
| Frightened Loop | `CreateFrightenedLoop()` | 1.0s looping warble (150 +/- 50 Hz at 8 Hz) |
| UI Click | `CreateUIClick()` | 30ms 1000 Hz sine blip with decay |
| Game Start | `CreateGameStart()` | 1.5s ascending melody (C4-E4-G4-C5-E5-G5) with harmonics |

---

## 11. Visual System

### Runtime Sprite Generation

All sprites are generated at runtime using `Texture2D` with per-pixel computation:

**Pac-Man (PlayerController):**
- 3 frames at 48x48 pixels: closed mouth (0 deg), half-open (15 deg half-angle), wide-open (30 deg half-angle)
- Anti-aliased circle with wedge cutout facing right
- Small dark eye dot in upper-right quadrant
- Oriented via SpriteRenderer.flipX and Z-rotation

**Ghosts (Ghost):**
- 2 body frames at 48x48 pixels with wavy scalloped bottom (phase-shifted between frames for wobble)
- Dome top (semicircle) + rectangular lower body
- Frightened frames include wavy mouth and dot eyes baked into texture
- Eye whites: 8x10 oval sprites as child GameObjects
- Pupils: 4x5 oval sprites as child GameObjects, shift by 0.025 units based on direction
- Each ghost colored via SpriteRenderer.color: Red, Pink, Cyan, Orange

**Maze (MazeRenderer):**
- Walls: 32x32 rounded rectangles with 6px corner radius, neon bright edges (2px), bilinear filtered
- Wall rendering: 3 layers per wall tile (shadow, glow halo, main body)
- Pellets: 12px glow circles (bright center + soft falloff) at 30% of tile size
- Energizers: 16px glow circles at 60% of tile size + 200% scale glow halo
- Ghost door: 32x6 rounded rectangle in pink

**Fruits (FruitSpawner):**
- 24x24 pixel art drawn with FillCircle, FillRect, DrawLine helpers
- 8 distinct fruits: Cherry (two red circles + green stems), Strawberry (tapered body + seeds), Orange, Apple, Grape (cluster), Galaxian (diamond), Bell (flared shape), Key (ring + shaft + teeth)

### Visual Effects

**Energizer Pulsing:**
- Coroutine oscillates energizer scale between 0.8 and 1.2 (sin wave at 3x speed)
- Glow halos oscillate between 1.6 and 2.4 in sync

**Frightened Ghost Visuals:**
- Blue body color, eyes/pupils hidden
- Scale pulses between 0.95 and 1.05 (sin wave at 8x speed)
- Near timeout: alternates between blue and white body color every 0.2s

**Wall Flash (Round Clear):**
- 4 flashes alternating between bright cyan/white and original wall color
- 150ms per flash phase (1.2s total)

**Particle Effects (SimpleParticles):**
- Pellet collection: 5 particles, 0.3s, speed 2, pellet color
- Energizer collection: 8 particles, 0.4s, speed 2.5, energizer color
- Ghost eat: 10 particles, 0.5s, speed 3, cyan
- Player death: 12 particles, 0.6s, speed 2.5, yellow, spiral movement

**Screen Effects (ScreenEffects):**
- Flash: OnGUI overlay with color + alpha fade over duration
- Shake: Camera position jitter with linear intensity decay
- Energizer activation: blue flash (0.3, 0.5, 1.0 @ 0.4 alpha), 0.2s
- Ghost eaten: white flash (1, 1, 1 @ 0.3 alpha), 0.12s
- Player caught: shake (0.15 intensity, 0.3s duration)

---

## 12. Build System

### PowerShell Scripts (tools/)

| Script | Purpose |
|--------|---------|
| `verify-environment.ps1` | Checks for Unity Hub, Unity Editor 6000.0.72f1, Windows Build Support, Git, PowerShell, disk space (10 GB min), write access, and optionally Visual Studio. Writes JSON + Markdown reports. |
| `create-project.ps1` | Creates the Unity project via batchmode if it doesn't exist. |
| `build-win64.ps1` | Builds Win64 player via `Unity.exe -batchmode -executeMethod BuildSystem.BuildScript.BuildWin64`. Writes timestamped build logs and build-result.json. |
| `clean-rebuild.ps1` | Deletes Library/ cache and previous build output, then runs build-win64.ps1. |
| `run-game.ps1` | Launches the built MazeChase.exe with optional `--smoke-test` flag and timeout monitoring. Checks Player.log for fatal errors. |
| `smoke-test.ps1` | End-to-end: build -> run with `--smoke-test` flag (15s timeout) -> check build reports. Exit code 0 = pass, 1 = fail, 2 = timeout (acceptable). |
| `run-editor-tests.ps1` | Runs EditMode tests via Unity batchmode, outputs XML results. |
| `run-playmode-tests.ps1` | Runs PlayMode tests via Unity batchmode, outputs XML results. |
| `collect-diagnostics.ps1` | Gathers Editor.log, Player.log, and other diagnostics into a summary report. |

### BuildScript.cs Flow

```
Unity.exe -batchmode -nographics
    -executeMethod BuildSystem.BuildScript.BuildWin64
    -projectPath MazeChase/
    -logFile build.log
    -buildTarget Win64
    |
    +-- Apply PlayerSettings (company, product, version, bundle ID)
    +-- BuildOptions.StrictMode (+ Development if -devBuild flag)
    +-- Scenes: ["Assets/Scenes/BootScene.unity"]
    +-- BuildPipeline.BuildPlayer()
    +-- BuildReportWriter.Write() -> build-result.json + build-result.md
    +-- EditorApplication.Exit(0 or 1)
```

### Smoke Test Flow

```
smoke-test.ps1
    |
    +-- Step 1: build-win64.ps1 (unless -SkipBuild)
    +-- Step 2: run-game.ps1 -SmokeTest -TimeoutSeconds 15
    |     |-- Launches MazeChase.exe --smoke-test
    |     |-- GameBootstrap detects --smoke-test flag
    |     |-- Waits 3 seconds, then Application.Quit(0)
    |     +-- run-game.ps1 checks exit code and Player.log
    +-- Step 3: Check build-result.json for success
    +-- Exit 0 (pass) or 1 (fail)
```

---

## 13. Configuration and Tuning

### RoundTuningData Speed Tables

All speeds in tiles per second:

| Parameter | Round 1 | Rounds 2-3 | Rounds 4-6 | Rounds 7-12 | Round 13+ |
|-----------|---------|------------|------------|-------------|-----------|
| Player Speed | 3.8 | 3.8 | 4.0 | 4.0 | 4.2 |
| Ghost Speed | 1.8 | 2.2 | 2.6 | 3.0 | 3.4 |
| Frightened Player | 4.2 | 4.2 | 4.5 | 4.5 | 4.8 |
| Frightened Ghost | 1.0 | 1.2 | 1.5 | 1.8 | 2.0 |
| Tunnel Ghost | 0.8 | 1.0 | 1.2 | 1.5 | 1.8 |
| Eaten Ghost | 4.5 | 4.5 | 4.5 | 4.5 | 4.5 |

### Frightened Duration

| Round | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10 | 11 | 12 | 13 | 14 | 15 | 16 | 17 | 18 | 19+ |
|-------|---|---|---|---|---|---|---|---|---|----|----|----|----|----|----|----|----|----|-----|
| Duration (s) | 12 | 10 | 8 | 6 | 5 | 10 | 4 | 4 | 3 | 10 | 4 | 3 | 3 | 6 | 3 | 3 | 0 | 2 | 0 |
| Flashes | 5 | 5 | 5 | 5 | 5 | 5 | 5 | 5 | 3 | 5 | 5 | 3 | 3 | 5 | 3 | 3 | 0 | 3 | 0 |

Note: Durations are doubled from classic values for more satisfying gameplay.

### Fruit Types and Scores

| Round | Fruit | Score |
|-------|-------|-------|
| 1 | Cherry | 100 |
| 2 | Strawberry | 300 |
| 3-4 | Orange | 500 |
| 5-6 | Apple | 700 |
| 7-8 | Grape | 1000 |
| 9-10 | Galaxian | 2000 |
| 11-12 | Bell | 3000 |
| 13+ | Key | 5000 |

Fruit appears at tile (14, 17) after 70 and 170 pellets eaten. Despawns after 10 seconds if not collected.

### Ghost Release Thresholds

| Ghost | Pellet Threshold | Notes |
|-------|-----------------|-------|
| Shadow (Blinky) | N/A | Always starts outside |
| Speedy (Pinky) | 0 | Released immediately |
| Bashful (Inky) | 30 | Released after 30 pellets eaten |
| Pokey (Clyde) | 60 | Released after 60 pellets eaten |

Global inactivity timer: 4 seconds without eating a pellet releases the next ghost.

### Scoring

| Event | Points |
|-------|--------|
| Pellet | 10 |
| Energizer | 50 |
| Ghost (1st in chain) | 200 |
| Ghost (2nd in chain) | 400 |
| Ghost (3rd in chain) | 800 |
| Ghost (4th in chain) | 1600 |
| Cherry | 100 |
| Strawberry | 300 |
| Orange | 500 |
| Apple | 700 |
| Grape | 1000 |
| Galaxian | 2000 |
| Bell | 3000 |
| Key | 5000 |

### Other Constants

| Constant | Value | Location |
|----------|-------|----------|
| Default Lives | 3 | ScoreManager |
| Tile Size | 0.5 world units | MazeRenderer |
| Camera Ortho Size | 9.0 | GameplaySceneSetup |
| Collision Threshold | 0.4 tiles | CollisionManager |
| Mouth Animation Angles | 0, 15, 30 degrees | PlayerController |
| Ghost Body Animation Rate | 0.15s per frame | Ghost |
| Pupil Offset Amount | 0.025 units | Ghost |

---

## 14. Known Issues and Future Work

### Known Issues

- **AI oscillation history:** The ExpertBot has undergone multiple rewrites to fix oscillation bugs caused by the interaction between per-frame direction injection and PlayerController's mid-tile reversal logic. The current design uses ForceDirection (which only works at tile centers) and a tile-decision cache to prevent oscillation, but edge cases may still exist near ghost encounters.
- **No menu screen:** The game starts directly into gameplay. The GameState enum defines Menu and Paused states but they are unused.
- **No persistent high score display:** High score is persisted via PlayerPrefs but there is no dedicated display beyond the HUD.
- **Placeholder asset directories:** Art/, Audio/, Prefabs/, Resources/ directories exist but are empty since everything is generated at runtime.
- **Tests are placeholder:** EditMode and PlayMode test assemblies exist but contain no test cases.
- **Single scene:** The game uses only BootScene; there is no scene transition system.

### Future Work

- Implement a title/menu screen and pause functionality
- Add proper death animation (Pac-Man shrinking/dissolving)
- Implement extra life at 10,000 points (classic behavior)
- Add intermission cutscenes between certain rounds
- Create actual unit and integration tests
- Add sound volume controls in a settings menu
- Support multiple platforms (macOS, Linux, WebGL)
- Add a 2-player alternating mode
- Implement the classic level 256 kill screen as an easter egg
- Replace OnGUI-based UI with Unity UI (Canvas/TextMeshPro) for better scaling

---

## 15. Key Controls

| Key | Action |
|-----|--------|
| W / Up Arrow | Move Up |
| S / Down Arrow | Move Down |
| A / Left Arrow | Move Left |
| D / Right Arrow | Move Right |
| F1 | Toggle diagnostics overlay (FPS, session, errors) |
| F2 | Toggle AI autoplay |
| ESC | Quit game |
| `--smoke-test` | Command-line flag: auto-quit after 3 seconds (CI mode) |
