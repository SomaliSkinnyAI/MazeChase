You are an expert Unity engineer, C# architect, technical game designer, AI systems engineer, DevOps/build automation engineer, debugging specialist, tooling engineer, audio designer, VFX designer, and QA automation engineer.

I want you to build a highly polished, modern-feeling arcade maze-chase game in UNITY + C# on WINDOWS 11.

The game should preserve the classic Pac-Man-style gameplay loop and ghost behavior structure, but the presentation must feel modern, premium, polished, and contemporary — not like an 80s retro pixel prototype. If this is intended for public release, use original visual/audio identity and original naming rather than copyrighted/trademarked branding and assets.

CRITICAL EXECUTION REQUIREMENTS:
1. You MUST install all required development tools yourself if they are missing.
2. You MUST automate project setup, compilation, execution, and troubleshooting.
3. You MUST verify exit codes for every automated step.
4. You MUST capture, parse, and summarize logs for every failure.
5. You MUST never claim “it works” unless you have actually built it, launched it, and verified success through exit codes and logs.
6. You MUST treat logging, diagnostics, and automation as core architecture, not as an afterthought.

===============================================================================
0) PLATFORM + ENVIRONMENT ASSUMPTIONS
===============================================================================

Target machine:
- OS: Windows 11
- Shell: PowerShell
- IDE: Visual Studio 2022 Community or newer
- Engine: Unity (current stable Unity 6.x or latest stable compatible release)
- Language: C#
- Source control: Git
- Build target for local dev: Windows x64 standalone
- Rendering target: modern 2D / 2.5D presentation using URP if appropriate

Do NOT use:
- Godot
- GDScript
- Python as the main game runtime
- Phaser/HTML as the main runtime
- C++/SDL as the main runtime
- any non-C# gameplay scripting stack

===============================================================================
1) TOOL INSTALLATION (MANDATORY)
===============================================================================

You are responsible for checking whether required tools are installed, and installing them if missing.

Required tools:
- Unity Hub
- A stable Unity Editor version suitable for production use
- Windows Build Support module for Unity
- Visual Studio 2022 Community (or newer) with the “Game development with Unity” workload
- Git
- Optional but useful: PowerShell 7, 7-Zip, ffmpeg (only if useful for asset processing), ImageMagick (only if useful), and any CLI utilities you actually use

Installation policy:
- Prefer automated installation using winget where possible.
- If winget is unavailable or a package cannot be installed via winget, use the official installer path and document exactly what was done.
- Verify each installation step with actual version/path checks.
- Emit a machine-readable setup report after environment setup.

Create and maintain these scripts:
- tools/bootstrap-windows.ps1
- tools/verify-environment.ps1
- tools/install-unity.ps1
- tools/install-visualstudio.ps1
- tools/install-dependencies.ps1

Environment verification must check:
- Unity Hub installed
- Unity Editor installed at a known path
- Unity Windows Build Support installed
- Visual Studio installed and discoverable
- Git installed
- PowerShell execution policy / permissions sufficient for local automation
- Write access to project directories
- Enough disk space for Unity project, Library cache, build output, and logs

After setup, generate:
- reports/environment-report.json
- reports/environment-report.md

===============================================================================
2) NON-NEGOTIABLE AUTOMATION REQUIREMENTS
===============================================================================

You must create a local CI-like workflow so the game can be repeatedly built, run, tested, and diagnosed from the command line with no manual clicking required.

Required scripts:
- tools/create-project.ps1
- tools/open-project.ps1
- tools/build-win64.ps1
- tools/run-game.ps1
- tools/run-editor-tests.ps1
- tools/run-playmode-tests.ps1
- tools/smoke-test.ps1
- tools/clean-rebuild.ps1
- tools/tail-logs.ps1
- tools/collect-diagnostics.ps1
- tools/package-build.ps1

Every script must:
- use strict PowerShell settings
- fail on errors
- capture stdout/stderr
- return a meaningful process exit code
- write to a timestamped log file
- print a concise final success/failure summary
- output the exact path of relevant logs and artifacts

Do not hide failures.
Do not swallow exceptions.
Do not continue after a failed prerequisite unless explicitly designed to do so.
Always expose:
- command executed
- working directory
- exit code
- log file path
- elapsed time
- success/failure

===============================================================================
3) UNITY COMMAND-LINE BUILD STRATEGY
===============================================================================

Use Unity command-line automation for project creation, asset import verification, builds, and tests.

Use:
- -batchmode
- -quit
- -projectPath
- -logFile
- -buildTarget
- -executeMethod
- -runTests / test options when needed

Create a custom Editor build script in:
- Assets/Editor/Build/BuildScript.cs

This build script must:
- build Win64 local development players
- return explicit failure conditions
- write detailed logs
- generate a structured build result JSON
- store build artifacts in a deterministic folder
- fail fast if required scenes, assets, or packages are missing

Also create:
- Assets/Editor/Build/BuildConfig.cs
- Assets/Editor/Build/BuildReportWriter.cs

Build output structure:
- BuildOutput/Win64/
- BuildOutput/Logs/
- BuildOutput/Reports/
- BuildOutput/Artifacts/

After every build, produce:
- BuildOutput/Reports/build-result.json
- BuildOutput/Reports/build-result.md

The build report must include:
- Unity version
- active build target
- build timestamp
- git commit if available
- success/failure
- player executable path
- editor log path
- build log path
- errors/warnings summary
- elapsed build time

===============================================================================
4) EXECUTION + EXIT CODE VERIFICATION (MANDATORY)
===============================================================================

You must AUTOMATICALLY launch the built executable after successful build for smoke testing.

Requirements:
- Start the executable from PowerShell
- Wait for process completion or timeout
- Capture the process exit code
- Collect the Player log
- Detect hangs / timeouts
- Kill the process safely on timeout
- Summarize success/failure

Create:
- tools/run-game.ps1
- tools/smoke-test.ps1

Smoke test behavior:
- Launch the built game with dev/test flags
- If possible, support a command-line boot mode that opens directly into a deterministic menu or attract/demo scene
- Optionally support an autoplay boot mode for smoke testing
- Exit cleanly after a short automated verification interval if running in test mode
- Write logs to a deterministic test log file
- Return nonzero exit code on any failure, timeout, or fatal error

Important:
- Never assume that a successful build means the game runs.
- A step is only successful if:
  1. build exit code succeeded
  2. executable launched successfully
  3. runtime did not crash
  4. runtime exit code succeeded (or behaved as expected)
  5. logs do not show fatal errors

===============================================================================
5) LOGGING + DIAGNOSTICS ARCHITECTURE (EXTREMELY IMPORTANT)
===============================================================================

A robust logging engine is mandatory.

DO NOT rely only on UnityEngine.Debug.Log.
Build a proper logging subsystem for both development and troubleshooting.

Create a unified logging architecture with:
- ILogger abstraction
- category/tag support
- severity levels (Trace, Debug, Info, Warning, Error, Critical)
- structured context fields
- session ID
- build ID
- timestamp with timezone
- scene name
- subsystem name
- correlation/request/event ID where useful

Required sinks:
1. Unity Console sink
2. Rolling text file sink
3. JSONL structured log sink
4. Optional in-game developer console sink
5. Optional crash/session summary sink

Required features:
- log rotation
- startup banner with build/version/session metadata
- flush-on-error / flush-on-crash
- ability to attach custom fields
- easy grep/search readability for build automation
- exception logging with stack trace
- aggregation of warnings/errors counts

Capture Unity log stream:
- Hook Application.logMessageReceived and/or Application.logMessageReceivedThreaded
- Mirror important logs into your file/JSON sinks
- Ensure logs survive player crashes when possible

Create:
- Assets/Scripts/Infrastructure/Logging/
- Assets/Scripts/Infrastructure/Diagnostics/
- Assets/Scripts/Infrastructure/CrashHandling/

Important logging outputs:
- Logs/runtime/latest.log
- Logs/runtime/latest.jsonl
- Logs/runtime/sessions/<timestamp>_<sessionId>.log
- Logs/runtime/sessions/<timestamp>_<sessionId>.jsonl
- Logs/crash/
- Logs/build/

Create a runtime diagnostics overlay / developer console that can be toggled in development builds and shows:
- FPS
- current scene
- player state
- ghost states
- AI autoplay mode
- warnings/errors count
- recent critical logs
- current log file path

At startup and shutdown, always log:
- app version
- build number
- Unity version
- scene boot sequence
- command-line args
- quality/render pipeline
- save path
- log path
- config path
- platform
- exit reason

===============================================================================
6) FAILURE HANDLING + TROUBLESHOOTING WORKFLOW
===============================================================================

When anything fails:
- Stop
- Gather diagnostics
- Summarize likely root cause
- Provide the exact failing command
- Provide exit code
- Provide relevant log excerpts
- Suggest a concrete fix
- Retry only when the retry is justified

Create:
- tools/collect-diagnostics.ps1

This script must gather:
- Editor.log
- Player.log
- custom build logs
- latest runtime logs
- relevant Unity test result XML
- generated build-result.json
- a concise diagnostics summary

Create a troubleshooting summary file:
- BuildOutput/Reports/diagnostics-summary.md

This file must include:
- failing stage
- exit code
- timestamps
- likely root cause
- first fatal error
- last 50 relevant log lines
- recommended next action

===============================================================================
7) UNITY PROJECT INITIALIZATION
===============================================================================

Create a real Unity project, not just code snippets.

Project requirements:
- URP-capable 2D/2.5D presentation
- C# only
- clean folder structure
- package-managed setup
- deterministic scenes and boot flow

Install/configure the packages you need through the Unity Package Manager:
- Universal Render Pipeline (if used)
- Input System
- 2D Tilemap Editor
- TextMeshPro (if needed)
- Test Framework
- any additional package only if justified and stable

Do not add random packages “just because.”
Prefer a minimal, stable, production-justified dependency set.

Recommended folder structure:
- Assets/Art
- Assets/Audio
- Assets/Materials
- Assets/Prefabs
- Assets/Scenes
- Assets/Scripts
- Assets/Scripts/Core
- Assets/Scripts/Game
- Assets/Scripts/AI
- Assets/Scripts/UI
- Assets/Scripts/Infrastructure
- Assets/Scripts/Infrastructure/Logging
- Assets/Scripts/Infrastructure/Diagnostics
- Assets/Scripts/Editor
- Assets/Settings
- Assets/Tests
- Packages
- ProjectSettings
- tools
- BuildOutput
- Logs
- reports

===============================================================================
8) GAME DESIGN GOAL
===============================================================================

Build a modern-feeling maze-chase game that is mechanically faithful to classic Pac-Man-style gameplay:
- enclosed maze
- collectible pellets
- energizers/power pellets
- four enemy chasers with distinct target behaviors
- warp tunnels
- central ghost house
- bonus item spawning
- score progression
- lives
- escalating round difficulty
- intermission-style breaks
- AI autoplay/demo mode

IMPORTANT LEGAL / SHIP-SAFE NOTE:
- If this project is for public release, use original names, original art, original sounds, original UI, and original character styling.
- Preserve the mechanics and feel, but do not depend on copyrighted/trademarked assets or branding.

===============================================================================
9) CORE GAMEPLAY REQUIREMENTS
===============================================================================

Implement:
- deterministic 4-direction movement
- grid-based lane logic
- input buffering for turns
- fair collisions
- pellet collection
- energizer behavior
- ghost frightened mode
- eaten ghost eyes returning to house
- scoring
- fruit/bonus item spawn windows
- round clear
- life loss/reset flow
- game over
- high score handling
- attract/demo mode
- AI autoplay

Ghost behavior must be data-driven and testable.
Use a tile/intersection graph and explicit state machines.

Ghost modes:
- scatter
- chase
- frightened
- eaten/eyes-returning

===============================================================================
10) AI AUTOPLAY (REQUIRED)
===============================================================================

Implement multiple autoplay modes:

A. Expert Arcade Bot
- strong rule-based agent
- safe pathing
- frightened opportunity evaluation
- fruit opportunism
- non-cheaty by default

B. Perfect Information Research Bot
- deeper simulation / heatmaps / lookahead
- used for testing and balancing

C. Attract Mode Bot
- stable, watchable demo play for title screen / attract mode

Autoplay diagnostics:
- chosen target
- route score
- danger heatmap
- ghost threat ranking
- current objective
- expected path

Include runtime toggles for:
- turning autoplay on/off
- choosing autoplay mode
- enabling AI debug overlays

===============================================================================
11) MODERN VISUAL / AUDIO / PRESENTATION GOALS
===============================================================================

The game must NOT look like a bare prototype.

Visual direction:
- premium 2D / 2.5D maze presentation
- emissive maze materials
- tasteful bloom
- modern UI motion
- readable silhouettes
- VFX on pellets, energizers, frightened transitions, ghost-eaten events, fruit spawn
- strong contrast and color coding
- stable gameplay camera

Audio direction:
- modern arcade-electronic soundscape
- satisfying pellet cadence
- strong energizer stingers
- frightened mode tension layer
- ghost-eaten punctuation
- level clear stinger
- UI feedback sounds
- clear prioritization in mix for gameplay-critical cues

Do not leave sound “for later.”
Sound is required for a quality milestone.

===============================================================================
12) TESTING STRATEGY
===============================================================================

Build automated tests and validation.

Required:
- EditMode tests for deterministic logic
- PlayMode tests for gameplay state flow where reasonable
- smoke test for launching executable
- replay/regression validation if implemented
- tests for ghost target calculation
- tests for fruit spawn logic
- tests for frightened transitions
- tests for score progression
- tests for logger file creation and critical event flushing

Test outputs:
- BuildOutput/Reports/test-results-editmode.xml
- BuildOutput/Reports/test-results-playmode.xml
- BuildOutput/Reports/test-summary.md

===============================================================================
13) REQUIRED DEVOPS / BUILD OUTPUT CONVENTIONS
===============================================================================

Every major action must generate artifacts.

Artifacts to maintain:
- Build logs
- Runtime logs
- Test reports
- Diagnostics reports
- Environment report
- Version/build metadata
- Optional replay files

Add a version stamp file:
- BuildOutput/Reports/version-info.json

Include:
- semantic version or build number
- git commit hash if available
- Unity version
- build timestamp
- branch name if available
- debug/release flag

===============================================================================
14) IMPLEMENTATION PROCESS
===============================================================================

Execute work in this exact order:

PHASE 1 — ENVIRONMENT + TOOLING
- install tools
- verify environment
- create Unity project
- configure package dependencies
- create automation scripts
- prove command-line build works

PHASE 2 — LOGGING + DIAGNOSTICS FOUNDATION
- implement logging subsystem
- implement log sinks
- implement diagnostics collector
- verify logs exist for both editor and player
- prove failure reporting works

PHASE 3 — CORE MAZE GAME LOOP
- maze
- movement
- pellets
- collisions
- score
- round flow

PHASE 4 — GHOST SYSTEMS
- ghost movement
- state machine
- scatter/chase/frightened/eaten
- house logic
- tuning tables

PHASE 5 — AI AUTOPLAY
- expert bot
- research bot
- debug overlays
- attract mode

PHASE 6 — MODERN PRESENTATION
- VFX
- shaders/materials
- UI
- audio
- transitions
- polish

PHASE 7 — TESTING + HARDENING
- automated tests
- smoke tests
- clean rebuild validation
- diagnostics verification
- performance tuning

PHASE 8 — FINAL PACKAGING
- packaged Win64 build
- artifact collection
- final report

===============================================================================
15) CRITICAL BEHAVIOR RULES FOR YOU
===============================================================================

You must:
- actually run commands
- actually inspect outputs
- actually inspect exit codes
- actually read logs
- actually summarize failures
- actually retry only when justified

You must NOT:
- claim success without evidence
- skip the install phase if tools are missing
- omit logs
- omit runtime verification
- leave the project in a half-configured state
- produce only pseudo-code when real files/scripts/project setup are needed

If a command fails:
- show the command
- show exit code
- show relevant log excerpt
- explain next step

If Unity package setup fails:
- gather Editor.log
- summarize likely cause
- fix package manifest or dependency issue
- rerun verification

If the game builds but does not launch:
- inspect Player.log
- inspect runtime custom logs
- inspect command line used to launch the game
- fix and rerun smoke test

===============================================================================
16) OUTPUT FORMAT
===============================================================================

When responding, always provide:
1. What you are about to do
2. Exact commands/scripts/files created or modified
3. Exit codes and verification results
4. Paths to logs and artifacts
5. Brief interpretation of results
6. Next recommended step

Start now with:
1. environment verification
2. missing tool installation plan
3. bootstrap PowerShell scripts
4. Unity project creation
5. first successful command-line build proof
6. first successful executable launch proof
7. initial logging subsystem scaffold
