using System.Collections.Generic;
using UnityEngine;
using MazeChase.Game;

namespace MazeChase.AI.Autoplay
{
    /// <summary>
    /// Simple, reliable AI for Pac-Man. Makes decisions ONLY at intersections
    /// (tiles with 3+ walkable neighbors). Between intersections, always goes
    /// forward. At intersections, picks the direction with the most pellets
    /// while avoiding ghosts.
    ///
    /// NO route planning. NO BFS pathfinding. NO reversal logic.
    /// Just: "which way has the most food and fewest ghosts?"
    /// </summary>
    public class ExpertBot
    {
        private Vector2Int _lastTile = new Vector2Int(-1, -1);
        private Direction _lastDir = Direction.None;
        // Anti-oscillation: remember decisions for recent tiles
        private readonly Dictionary<Vector2Int, Direction> _tileDecisions = new Dictionary<Vector2Int, Direction>(32);
        private int _decisionAge;
        private System.IO.StreamWriter _log;

        public ExpertBot()
        {
            try
            {
                string dir = System.IO.Path.Combine(Application.persistentDataPath, "Logs");
                if (!System.IO.Directory.Exists(dir))
                    System.IO.Directory.CreateDirectory(dir);
                string path = System.IO.Path.Combine(dir, "ai-decisions.log");
                _log = new System.IO.StreamWriter(path, false) { AutoFlush = true };
                _log.WriteLine($"[AI] Started {System.DateTime.Now:HH:mm:ss}");
                Debug.Log($"[ExpertBot] AI log: {path}");
            }
            catch { }
        }

        private void Log(string msg) { _log?.WriteLine(msg); }

        public Direction GetBestDirection(
            Vector2Int tile,
            Direction curDir,
            Ghost[] ghosts,
            PelletManager pellets)
        {
            // Only decide when arriving at a new tile
            if (tile == _lastTile && _lastDir != Direction.None)
            {
                if (Walkable(Step(tile, _lastDir)))
                    return _lastDir;
            }
            _lastTile = tile;

            // Anti-oscillation: if we already decided for this tile recently,
            // use that decision (prevents A→B→A→B loops between two tiles)
            if (_tileDecisions.TryGetValue(tile, out Direction prevDecision))
            {
                if (Walkable(Step(tile, prevDecision)))
                {
                    _lastDir = prevDecision;
                    return prevDecision;
                }
            }

            // Clear old decisions periodically
            _decisionAge++;
            if (_decisionAge > 30)
            {
                _tileDecisions.Clear();
                _decisionAge = 0;
            }

            // Get walkable directions
            var dirs = new List<Direction>(4);
            foreach (Direction d in DirectionHelper.AllDirections)
                if (Walkable(Step(tile, d))) dirs.Add(d);

            if (dirs.Count == 0) { _lastDir = Direction.None; return curDir; }
            if (dirs.Count == 1) { _lastDir = dirs[0]; return dirs[0]; }

            // === FRIGHTENED GHOST? Chase it! ===
            Ghost blueGhost = null;
            int blueDist = 999;
            foreach (Ghost g in ghosts)
            {
                if (g != null && g.CurrentState == GhostState.Frightened)
                {
                    int d = Manhattan(tile, g.CurrentTile);
                    if (d < blueDist) { blueDist = d; blueGhost = g; }
                }
            }
            if (blueGhost != null && blueDist <= 15)
            {
                Direction chaseDir = DirectionToward(tile, blueGhost.CurrentTile, dirs);
                if (chaseDir != Direction.None)
                {
                    Log($"CHASE blue ghost at ({blueGhost.CurrentTile.x},{blueGhost.CurrentTile.y}) dir={chaseDir}");
                    _lastDir = chaseDir;
                    return chaseDir;
                }
            }

            // === SCORE EACH DIRECTION ===
            Direction bestDir = dirs[0];
            float bestScore = float.MinValue;

            foreach (Direction d in dirs)
            {
                float score = ScoreDirection(tile, d, curDir, ghosts, pellets);
                if (score > bestScore) { bestScore = score; bestDir = d; }
            }

            Log($"tile=({tile.x},{tile.y}) cur={curDir} pick={bestDir} score={bestScore:F0} dirs=[{string.Join(",", dirs)}]");
            _lastDir = bestDir;
            _tileDecisions[tile] = bestDir;
            return bestDir;
        }

        private float ScoreDirection(Vector2Int from, Direction dir, Direction curDir,
            Ghost[] ghosts, PelletManager pellets)
        {
            float score = 0f;
            Vector2Int next = Step(from, dir);

            // ── PELLET SCORING ──────────────────────────────────────
            // BFS flood from 'next' but BLOCK 'from' tile so we only count
            // pellets in the FORWARD direction. This sees around corners.
            int pelletsAhead = CountPelletsDirectional(next, from, pellets, 15);
            score += pelletsAhead * 10f;

            // Immediate pellet bonus
            if (pellets != null && pellets.HasPellet(next.x, next.y))
                score += 20f;

            // Energizer in this direction?
            if (HasEnergizerDirectional(next, from, pellets, 12))
                score += 40f;

            // Penalty for zero pellets
            if (pelletsAhead == 0)
                score -= 50f;

            // ── GHOST DANGER ────────────────────────────────────────
            foreach (Ghost g in ghosts)
            {
                if (g == null) continue;
                if (g.CurrentState != GhostState.Chase && g.CurrentState != GhostState.Scatter) continue;
                int dist = Manhattan(next, g.CurrentTile);
                if (dist <= 1) score -= 500f;
                else if (dist <= 3) score -= 200f;
                else if (dist <= 5) score -= 50f;

                // Extra penalty if moving toward ghost
                int distNow = Manhattan(from, g.CurrentTile);
                if (dist < distNow && dist <= 6)
                    score -= 80f;
            }

            // ── MOMENTUM ────────────────────────────────────────────
            // Prefer continuing forward, avoid reversing
            if (dir == curDir)
                score += 5f;
            if (curDir != Direction.None && dir == DirectionHelper.Opposite(curDir))
                score -= 30f;

            // ── PREFER TURNS at intersections when current path is empty ──
            // If forward has no pellets but a perpendicular does, boost the turn
            if (curDir != Direction.None && dir != curDir && dir != DirectionHelper.Opposite(curDir))
            {
                // This is a turn (perpendicular) — give a small bonus to encourage
                // exploring new corridors instead of staying in cleared ones
                score += 8f;
            }

            return score;
        }

        /// <summary>
        /// BFS flood from 'start', blocking 'exclude' tile so we only count
        /// pellets reachable in the forward direction. This sees around corners
        /// and into side corridors — the key advantage over straight-line counting.
        /// </summary>
        private int CountPelletsDirectional(Vector2Int start, Vector2Int exclude, PelletManager pellets, int maxDepth)
        {
            if (pellets == null) return 0;
            var q = new Queue<Vector2Int>(128);
            var visited = new HashSet<Vector2Int>();
            q.Enqueue(start);
            visited.Add(start);
            visited.Add(exclude); // Block backward direction

            int count = 0;
            if (pellets.HasPellet(start.x, start.y)) count++;

            while (q.Count > 0)
            {
                Vector2Int cur = q.Dequeue();
                // Use manhattan from start as depth (cheaper than tracking BFS depth)
                if (Manhattan(cur, start) >= maxDepth) continue;

                foreach (Direction d in DirectionHelper.AllDirections)
                {
                    Vector2Int n = Step(cur, d);
                    if (!Walkable(n) || visited.Contains(n)) continue;
                    visited.Add(n);
                    q.Enqueue(n);
                    if (pellets.HasPellet(n.x, n.y)) count++;
                }
            }
            return count;
        }

        private bool HasEnergizerDirectional(Vector2Int start, Vector2Int exclude, PelletManager pellets, int maxDepth)
        {
            if (pellets == null) return false;
            var q = new Queue<Vector2Int>(64);
            var visited = new HashSet<Vector2Int>();
            q.Enqueue(start);
            visited.Add(start);
            visited.Add(exclude);

            if (MazeData.GetTile(start.x, start.y) == MazeTile.Energizer && pellets.HasPellet(start.x, start.y))
                return true;

            while (q.Count > 0)
            {
                Vector2Int cur = q.Dequeue();
                if (Manhattan(cur, start) >= maxDepth) continue;

                foreach (Direction d in DirectionHelper.AllDirections)
                {
                    Vector2Int n = Step(cur, d);
                    if (!Walkable(n) || visited.Contains(n)) continue;
                    visited.Add(n);
                    q.Enqueue(n);
                    if (MazeData.GetTile(n.x, n.y) == MazeTile.Energizer && pellets.HasPellet(n.x, n.y))
                        return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Pick the direction from candidates that moves closest to target (manhattan).
        /// </summary>
        private Direction DirectionToward(Vector2Int from, Vector2Int target, List<Direction> candidates)
        {
            int bestDist = int.MaxValue;
            Direction bestDir = Direction.None;
            foreach (Direction d in candidates)
            {
                int dist = Manhattan(Step(from, d), target);
                if (dist < bestDist) { bestDist = dist; bestDir = d; }
            }
            return bestDir;
        }

        private static int Manhattan(Vector2Int a, Vector2Int b)
            => Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);

        private static Vector2Int Step(Vector2Int t, Direction d)
        {
            Vector2Int n = t + DirectionHelper.ToVector(d);
            if (n.x < 0) n.x = MazeData.Width - 1;
            else if (n.x >= MazeData.Width) n.x = 0;
            return n;
        }

        private static bool Walkable(Vector2Int t)
        {
            if (t.x < 0 || t.x >= MazeData.Width || t.y < 0 || t.y >= MazeData.Height)
                return false;
            MazeTile m = MazeData.GetTile(t.x, t.y);
            return m != MazeTile.Wall && m != MazeTile.GhostHouse && m != MazeTile.GhostDoor;
        }
    }
}
