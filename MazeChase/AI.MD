# AI Autoplay Fix Plan

## Root Cause Analysis

The oscillation bug has persisted through 5+ rewrites because the fix was always in the wrong place. The actual root cause is in **PlayerController.AdvanceLerp()**, not in ExpertBot.

### The Bug (traced step by step):

1. AutoplayManager.Update() runs EVERY FRAME
2. It calls `ExpertBot.GetBestDirection()` EVERY FRAME
3. ExpertBot returns `_lastDir` when tile hasn't changed — but `_lastDir` may be set to a direction from the PREVIOUS route plan
4. AutoplayManager calls `player.SetQueuedDirection(dir)` EVERY FRAME
5. **PlayerController.AdvanceLerp() (line 244)**: While the player is BETWEEN tiles (mid-lerp), if `QueuedDirection == Opposite(CurrentDirection)`, it **instantly reverses** the player — swaps origin/target, inverts lerp progress
6. This means: if ExpertBot returns UP, then replans and returns DOWN on the next frame (even while the player is mid-tile moving UP), the player physically reverses direction mid-tile
7. Next frame: the bot sees the player is now at the same tile going DOWN, plans route again, maybe returns UP → instant reversal again
8. Result: **oscillation at frame rate, locked to one tile**

### Why previous fixes failed:
- Corridor detection: Didn't help because the reversal happens at the PlayerController level, not the decision level
- Anti-oscillation penalties: Didn't help because the bot DID commit to a direction, but the next frame's replan changed it
- Route-based planning: Didn't help because replanning (triggered by ghost proximity checks) could happen every frame, generating a new route with a different first step

## The Fix (3 changes needed)

### Fix 1: AutoplayManager — Don't set direction every frame
**File:** `Assets/Scripts/AI/Autoplay/AutoplayManager.cs`

**Change:** Only call `SetQueuedDirection` when the player ARRIVES at a tile center (CurrentTile changes), not every frame. Track the last tile we set a direction for.

```
private Vector2Int _lastInjectedTile;

void Update() {
    ...
    // Only inject a new direction when player reaches a new tile
    if (_player.CurrentTile == _lastInjectedTile)
        return; // Already set direction for this tile, don't override

    Direction bestDir = _bot.GetBestDirection(...);
    if (bestDir != Direction.None) {
        _player.SetQueuedDirection(bestDir);
        _lastInjectedTile = _player.CurrentTile;
    }
}
```

This is the CRITICAL fix. By not re-injecting a direction while the player is mid-tile, we prevent the AdvanceLerp reversal from ever triggering from AI input.

### Fix 2: ExpertBot — Replan cooldown
**File:** `Assets/Scripts/AI/Autoplay/ExpertBot.cs`

**Change:** After planning a route, don't allow replanning for at least 3 tiles (unless emergency ghost contact within 2 tiles). This prevents the bot from thrashing between plans.

```
private int _tilesSinceReplan;
private const int MinTilesBetweenReplans = 3;

// In GetBestDirection:
_tilesSinceReplan++;
bool canReplan = _tilesSinceReplan >= MinTilesBetweenReplans;
// Emergency override: ghost within 2 BFS tiles
bool emergency = (nearestGhostDist >= 0 && nearestGhostDist <= 2);
if (canReplan || emergency) {
    // ... do replan
    _tilesSinceReplan = 0;
}
```

### Fix 3: ExpertBot — Never output opposite of current movement
**File:** `Assets/Scripts/AI/Autoplay/ExpertBot.cs`

**Change:** As a final safety net, never return a direction that is the exact opposite of the player's current movement direction (which would trigger the AdvanceLerp reversal). If the planned route requires a reversal, output Direction.None for one frame (the player will stop, then the next tile evaluation will handle it naturally).

```
Direction result = ... // whatever the route says
if (result == DirectionHelper.Opposite(currentDir) && currentDir != Direction.None) {
    // Don't reverse — let the player stop naturally, then turn
    return Direction.None; // or return currentDir to keep going
}
```

## Implementation Order

1. Fix 1 (AutoplayManager) — Most critical, eliminates the oscillation mechanism
2. Fix 2 (Replan cooldown) — Prevents plan thrashing
3. Fix 3 (No-reverse guard) — Safety net

## Verification

After implementing, test these scenarios:
- [ ] AI in a straight corridor: should go forward without any oscillation
- [ ] AI at a T-junction: should pick one direction and commit
- [ ] AI near a ghost: should flee without jittering
- [ ] AI sees blue ghost: should chase it directly
- [ ] AI can clear level 1 without dying
