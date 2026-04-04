using UnityEngine;

namespace MazeChase.Game
{
    /// <summary>
    /// Static class containing the classic 28x31 Pac-Man maze layout.
    /// Coordinate system: x = column (0-27, left to right), y = row (0-30, top to bottom).
    /// The layout is stored row-major for visual readability, then accessed as [x,y] via GetTile.
    /// </summary>
    public static class MazeData
    {
        public static int Width => 28;
        public static int Height => 31;

        // Shorthand aliases for readability in the layout
        private const MazeTile W = MazeTile.Wall;
        private const MazeTile P = MazeTile.Pellet;
        private const MazeTile E = MazeTile.Energizer;
        private const MazeTile O = MazeTile.Empty;
        private const MazeTile H = MazeTile.GhostHouse;
        private const MazeTile D = MazeTile.GhostDoor;
        private const MazeTile T = MazeTile.Tunnel;
        private const MazeTile S = MazeTile.PlayerSpawn;
        private const MazeTile F = MazeTile.FruitSpawn;
        private const MazeTile G = MazeTile.GhostSpawn;

        /// <summary>
        /// The classic Pac-Man maze laid out row-by-row for visual clarity.
        /// Each inner array is one row (28 tiles wide). There are 31 rows.
        /// Row 0 = top, Row 30 = bottom.
        ///
        /// Classic maze features:
        ///   - Outer walls forming the border
        ///   - Pellets filling all corridors
        ///   - 4 energizers near the corners (rows 3 and 21)
        ///   - Warp tunnel on row 14 at columns 0 and 27
        ///   - Ghost house centered around rows 12-15, cols 10-17
        ///   - Ghost door at row 12, cols 13-14
        ///   - Ghost spawns at row 13, cols 12 and 15
        ///   - Player spawn at row 23, cols 13-14
        ///   - Fruit spawn at row 17, cols 13-14
        /// </summary>
        private static readonly MazeTile[][] _rows = new MazeTile[][]
        {
            //                0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7
            // Row 0: top border
            new[] { W,W,W,W,W,W,W,W,W,W,W,W,W,W,W,W,W,W,W,W,W,W,W,W,W,W,W,W },
            // Row 1: pellet corridor
            new[] { W,P,P,P,P,P,P,P,P,P,P,P,P,W,W,P,P,P,P,P,P,P,P,P,P,P,P,W },
            // Row 2: vertical wall segments
            new[] { W,P,W,W,W,W,P,W,W,W,W,W,P,W,W,P,W,W,W,W,W,P,W,W,W,W,P,W },
            // Row 3: energizer row (top pair) — energizers at cols 1 and 26
            new[] { W,E,W,W,W,W,P,W,W,W,W,W,P,W,W,P,W,W,W,W,W,P,W,W,W,W,E,W },
            // Row 4
            new[] { W,P,W,W,W,W,P,W,W,W,W,W,P,W,W,P,W,W,W,W,W,P,W,W,W,W,P,W },
            // Row 5: full pellet row
            new[] { W,P,P,P,P,P,P,P,P,P,P,P,P,P,P,P,P,P,P,P,P,P,P,P,P,P,P,W },
            // Row 6
            new[] { W,P,W,W,W,W,P,W,W,P,W,W,W,W,W,W,W,W,P,W,W,P,W,W,W,W,P,W },
            // Row 7
            new[] { W,P,W,W,W,W,P,W,W,P,W,W,W,W,W,W,W,W,P,W,W,P,W,W,W,W,P,W },
            // Row 8: pellet row with center wall blocks
            new[] { W,P,P,P,P,P,P,W,W,P,P,P,P,W,W,P,P,P,P,W,W,P,P,P,P,P,P,W },
            // Row 9: start of side tunnels area
            new[] { W,W,W,W,W,W,P,W,W,W,W,W,O,W,W,O,W,W,W,W,W,P,W,W,W,W,W,W },
            // Row 10
            new[] { W,W,W,W,W,W,P,W,W,W,W,W,O,W,W,O,W,W,W,W,W,P,W,W,W,W,W,W },
            // Row 11: above ghost house — open corridor cols 9-18
            new[] { W,W,W,W,W,W,P,W,W,O,O,O,O,O,O,O,O,O,O,W,W,P,W,W,W,W,W,W },
            // Row 12: ghost house top wall with door at cols 13-14
            new[] { W,W,W,W,W,W,P,W,W,O,W,W,W,D,D,W,W,W,O,W,W,P,W,W,W,W,W,W },
            // Row 13: ghost house interior — ghost spawns at cols 12 and 15
            new[] { W,W,W,W,W,W,P,W,W,O,W,H,G,H,H,G,H,W,O,W,W,P,W,W,W,W,W,W },
            // Row 14: tunnel row — tunnels at cols 0 and 27, ghost house interior
            new[] { T,O,O,O,O,O,P,O,O,O,W,H,H,H,H,H,H,W,O,O,O,P,O,O,O,O,O,T },
            // Row 15: ghost house bottom wall
            new[] { W,W,W,W,W,W,P,W,W,O,W,W,W,W,W,W,W,W,O,W,W,P,W,W,W,W,W,W },
            // Row 16: below ghost house — open corridor cols 9-18
            new[] { W,W,W,W,W,W,P,W,W,O,O,O,O,O,O,O,O,O,O,W,W,P,W,W,W,W,W,W },
            // Row 17: fruit spawn row — F tiles at cols 13-14
            new[] { W,W,W,W,W,W,P,W,W,O,W,W,W,F,F,W,W,W,O,W,W,P,W,W,W,W,W,W },
            // Row 18: pellet corridor
            new[] { W,P,P,P,P,P,P,P,P,P,P,P,P,W,W,P,P,P,P,P,P,P,P,P,P,P,P,W },
            // Row 19
            new[] { W,P,W,W,W,W,P,W,W,W,W,W,P,W,W,P,W,W,W,W,W,P,W,W,W,W,P,W },
            // Row 20
            new[] { W,P,W,W,W,W,P,W,W,W,W,W,P,W,W,P,W,W,W,W,W,P,W,W,W,W,P,W },
            // Row 21: energizer row (bottom pair) — cols 13-14 are O for spawn access
            new[] { W,E,P,P,W,W,P,P,P,P,P,P,P,O,O,P,P,P,P,P,P,P,W,W,P,P,E,W },
            // Row 22: T-junction connectors at cols 3, 9, 18, 24
            new[] { W,W,W,P,W,W,P,W,W,P,W,W,W,W,W,W,W,W,P,W,W,P,W,W,P,W,W,W },
            // Row 23: player spawn row — cols 9-18 fully open for movement
            new[] { W,W,W,P,W,W,P,W,W,P,P,P,P,S,S,P,P,P,P,W,W,P,W,W,P,W,W,W },
            // Row 24
            new[] { W,P,P,P,P,P,P,W,W,P,P,P,P,W,W,P,P,P,P,W,W,P,P,P,P,P,P,W },
            // Row 25
            new[] { W,P,W,W,W,W,W,W,W,W,W,W,P,W,W,P,W,W,W,W,W,W,W,W,W,W,P,W },
            // Row 26
            new[] { W,P,W,W,W,W,W,W,W,W,W,W,P,W,W,P,W,W,W,W,W,W,W,W,W,W,P,W },
            // Row 27: full pellet row
            new[] { W,P,P,P,P,P,P,P,P,P,P,P,P,P,P,P,P,P,P,P,P,P,P,P,P,P,P,W },
            // Row 28: bottom border
            new[] { W,W,W,W,W,W,W,W,W,W,W,W,W,W,W,W,W,W,W,W,W,W,W,W,W,W,W,W },
            // Row 29
            new[] { W,W,W,W,W,W,W,W,W,W,W,W,W,W,W,W,W,W,W,W,W,W,W,W,W,W,W,W },
            // Row 30: bottom border
            new[] { W,W,W,W,W,W,W,W,W,W,W,W,W,W,W,W,W,W,W,W,W,W,W,W,W,W,W,W },
        };

        /// <summary>
        /// Get the tile at grid position (x, y) where x is column, y is row.
        /// Out-of-bounds returns Wall.
        /// </summary>
        public static MazeTile GetTile(int x, int y)
        {
            if (x < 0 || x >= Width || y < 0 || y >= Height)
                return MazeTile.Wall;
            return _rows[y][x];
        }

        /// <summary>
        /// Returns true if the tile at (x, y) is not a wall.
        /// Out-of-bounds returns false.
        /// </summary>
        public static bool IsWalkable(int x, int y)
        {
            if (x < 0 || x >= Width || y < 0 || y >= Height)
                return false;
            return _rows[y][x] != MazeTile.Wall;
        }

        /// <summary>
        /// Returns true if the tile at (x, y) has 3 or more walkable neighbors,
        /// making it an intersection where direction decisions can be made.
        /// </summary>
        public static bool IsIntersection(int x, int y)
        {
            if (!IsWalkable(x, y))
                return false;

            int walkableNeighbors = 0;
            if (IsWalkable(x, y - 1)) walkableNeighbors++;
            if (IsWalkable(x, y + 1)) walkableNeighbors++;
            if (IsWalkable(x - 1, y)) walkableNeighbors++;
            if (IsWalkable(x + 1, y)) walkableNeighbors++;

            return walkableNeighbors >= 3;
        }

        /// <summary>
        /// Returns the player spawn position (center of row 23, between cols 13-14).
        /// </summary>
        public static Vector2Int GetPlayerSpawn()
        {
            return new Vector2Int(14, 23);
        }

        /// <summary>
        /// Returns the 4 ghost spawn positions.
        /// Index 0 = Blinky (above house), 1 = Pinky (center), 2 = Inky (left), 3 = Clyde (right).
        /// </summary>
        public static Vector2Int[] GetGhostSpawns()
        {
            return new Vector2Int[]
            {
                new Vector2Int(13, 11), // Blinky -- starts above house
                new Vector2Int(13, 14), // Pinky -- center of house
                new Vector2Int(12, 13), // Inky -- left ghost spawn (G tile)
                new Vector2Int(15, 13), // Clyde -- right ghost spawn (G tile)
            };
        }

        /// <summary>
        /// Returns the fruit spawn position (row 17, between cols 13-14).
        /// </summary>
        public static Vector2Int GetFruitSpawn()
        {
            return new Vector2Int(14, 17);
        }

        /// <summary>
        /// Returns the two warp tunnel entrance positions on row 14.
        /// </summary>
        public static Vector2Int[] GetTunnelEntrances()
        {
            return new Vector2Int[]
            {
                new Vector2Int(0, 14),
                new Vector2Int(27, 14)
            };
        }

        /// <summary>
        /// Counts the total number of pellets and energizers in the maze.
        /// </summary>
        public static int CountPellets()
        {
            int count = 0;
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    MazeTile tile = _rows[y][x];
                    if (tile == MazeTile.Pellet || tile == MazeTile.Energizer)
                        count++;
                }
            }
            return count;
        }

        /// <summary>
        /// Returns a fresh copy of the maze layout as a 2D array [x, y].
        /// Used when resetting the maze for a new round.
        /// </summary>
        public static MazeTile[,] CloneLayout()
        {
            MazeTile[,] clone = new MazeTile[Width, Height];
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    clone[x, y] = _rows[y][x];
                }
            }
            return clone;
        }
    }
}
