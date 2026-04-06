# MazeChase

A modern take on the classic maze-chase arcade game, built entirely in **Unity 6** with **C#**. Every visual and audio asset is generated procedurally at runtime — zero imported sprites, textures, or sound files.

![Unity](https://img.shields.io/badge/Unity-6000.0.72f1-blue) ![C#](https://img.shields.io/badge/Language-C%23-green) ![Platform](https://img.shields.io/badge/Platform-Windows%20x64-lightgrey) ![License](https://img.shields.io/badge/License-MIT-yellow)

## Features

### Gameplay
- Classic 28x31 maze with 246 pellets, 4 energizers, and warp tunnels
- 4 ghosts with distinct AI personalities and targeting strategies
- Ghost state machine: Scatter, Chase, Frightened, Eaten, InHouse, ExitingHouse
- Elroy mode (Blinky speeds up as pellets decrease)
- Data-driven difficulty progression across rounds
- Fruit bonuses (Cherry, Strawberry, Orange, Apple, Grape, Galaxian, Bell, Key)
- Ghost-eat combo scoring (200, 400, 800, 1600)
- High score persistence via PlayerPrefs

### Visuals (All Procedural)
- Neon-styled maze with wall glow halos and shadow depth
- Pac-Man with 3-frame mouth animation and directional eye
- Ghost sprites with dome tops, wavy scalloped bottoms, and direction-tracking eyes
- Pulsing energizers with glow effects
- Pellet collection particle bursts
- Screen flash on energizer/ghost eat, camera shake on death
- 8 distinct pixel-art fruit sprites
- Score popups and combo text

### Audio (All Procedural)
- 11 synthesized sound effects generated via `AudioClip.Create`
- Pellet eat (alternating pitch), energizer bass drop, ghost eat crunch
- Death descending tone, round clear arpeggio, game start jingle
- Continuous siren and frightened mode warble loops
- UI click feedback

### AI Autoplay
- Toggle with **F2** — watch the AI play the game
- **F3** cycles between `NeuralPolicy`, `ResearchPlanner`, `ExpertLegacy`, and `Attract`
- **F4** toggles the AI debug panel
- Event-driven control loop: one committed move per tile, no frame-rate reversal thrash
- Graph-based planner with ghost pressure forecasting, dead-end awareness, energizer timing, and tunnel escape weighting
- Neural policy runtime path with offline training/export support
- Dataset recorder for planner imitation training

### Infrastructure
- Structured logging with file + JSONL sinks
- Crash handler with unhandled exception capture
- Debug diagnostics overlay (F1)
- Session metadata logging (version, platform, GPU, etc.)
- AI decision log for debugging (`ai-decisions.log`)

## Controls

| Key | Action |
|-----|--------|
| **WASD** / **Arrow Keys** | Move |
| **F2** | Toggle AI Autoplay |
| **F3** | Cycle AI Mode |
| **F4** | Toggle AI Debug |
| **F1** | Toggle Debug Overlay |
| **ESC** | Quit |

## Building from Source

### Prerequisites
- **Unity 6** (6000.0.72f1) with Windows Build Support
- **Git**
- **Windows 10/11**

### Quick Start
```powershell
# 1. Clone the repo
git clone https://github.com/SomaliSkinnyAI/MazeChase.git
cd MazeChase

# 2. Verify environment
powershell -ExecutionPolicy Bypass -File tools/verify-environment.ps1

# 3. Build
powershell -ExecutionPolicy Bypass -File tools/build-win64.ps1

# 4. Run
powershell -ExecutionPolicy Bypass -File tools/run-game.ps1

# 5. Or run the smoke test (auto-quits after 3 seconds)
powershell -ExecutionPolicy Bypass -File tools/smoke-test.ps1
```

### Build Scripts
| Script | Purpose |
|--------|---------|
| `tools/verify-environment.ps1` | Check all tools are installed |
| `tools/build-win64.ps1` | Build Win64 standalone player |
| `tools/run-game.ps1` | Launch the built game |
| `tools/smoke-test.ps1` | Build + launch + verify |
| `tools/clean-rebuild.ps1` | Delete Library cache + full rebuild |
| `tools/run-editor-tests.ps1` | Run EditMode unit tests |
| `tools/run-playmode-tests.ps1` | Run PlayMode tests |
| `tools/collect-diagnostics.ps1` | Gather all logs into a report |

The Unity batch scripts are watchdog-driven: they stream the Unity log, fail fast on compiler errors, and enforce hard timeouts so build/test automation does not sit silent when Unity breaks.

## Project Structure

```
MazeChase/
  Assets/
    Scripts/
      AI/                  Ghost AI, targeting strategies, autoplay bot
      Audio/               Procedural audio generation
      Core/                Bootstrap, state management, scoring
      Game/                Player, maze, pellets, collisions, fruit
      Infrastructure/      Logging, crash handling, diagnostics
      UI/                  HUD (OnGUI-based)
      VFX/                 Particles, screen effects
    Editor/                Build scripts, scene setup
    Scenes/                BootScene (single scene)
  Packages/                Unity package manifest
  ProjectSettings/         Unity project configuration
tools/                     PowerShell build automation
ARCHITECTURE.md            Detailed 1087-line architecture document
```

## Architecture

See [ARCHITECTURE.md](ARCHITECTURE.md) for a comprehensive deep-dive covering:
- System architecture diagrams
- Data flow and event wiring
- Ghost AI targeting algorithms
- Maze encoding and coordinate system
- Tuning tables and configuration
- Build pipeline

Autoplay/training status lives in [MazeChase/AI.MD](MazeChase/AI.MD), and offline training scripts live in [tools/ai/README.md](tools/ai/README.md).

## Ghost Personalities

| Ghost | Name | Color | Chase Behavior |
|-------|------|-------|---------------|
| Shadow | Blinky | Red | Targets player directly; speeds up as pellets decrease (Elroy mode) |
| Speedy | Pinky | Pink | Targets 4 tiles ahead of player (with classic up-direction overflow bug) |
| Bashful | Inky | Cyan | Uses vector from Blinky to 2 tiles ahead of player, doubled |
| Pokey | Clyde | Orange | Chases when far (>8 tiles), retreats to corner when close |

## Tech Stack

- **Engine:** Unity 6 (6000.0.72f1)
- **Language:** C# (.NET Standard)
- **Render Pipeline:** Built-in (2D Orthographic)
- **Input:** Unity Input System (New)
- **Build Target:** Windows x64 Standalone
- **Asset Pipeline:** Zero imported assets — all runtime-generated

## License

MIT License. This is an original game inspired by classic maze-chase arcade mechanics. All code, visuals, and audio are original works.

---

*Built with Unity 6 and Claude Code*
