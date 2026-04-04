using System;
using UnityEngine;

namespace MazeChase.Game
{
    /// <summary>
    /// Tracks pellet state across the maze and fires events on collection.
    /// Works with a runtime copy of the maze layout so pellets can be
    /// consumed without mutating the static template.
    /// </summary>
    public class PelletManager : MonoBehaviour
    {
        public static PelletManager Instance { get; private set; }

        /// <summary>Fired when any pellet (normal or energizer) is collected.</summary>
        public event Action<Vector2Int, MazeTile> OnPelletCollected;

        /// <summary>Fired when all pellets (and energizers) are consumed.</summary>
        public event Action OnAllPelletsCleared;

        private MazeTile[,] _runtimeLayout;
        private int _remainingPellets;
        private int _totalPellets;
        private int _pelletsEaten;

        public int RemainingPellets => _remainingPellets;
        public int TotalPellets => _totalPellets;
        public int PelletsEaten => _pelletsEaten;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        /// <summary>Call at round start to reset all pellets.</summary>
        public void ResetPellets()
        {
            _runtimeLayout = MazeData.CloneLayout();
            _pelletsEaten = 0;
            _remainingPellets = 0;

            for (int y = 0; y < MazeData.Height; y++)
            {
                for (int x = 0; x < MazeData.Width; x++)
                {
                    var tile = _runtimeLayout[x, y];
                    if (tile == MazeTile.Pellet || tile == MazeTile.Energizer)
                        _remainingPellets++;
                }
            }
            _totalPellets = _remainingPellets;
            Debug.Log($"[PelletManager] Reset: {_totalPellets} pellets placed.");
        }

        /// <summary>
        /// Attempt to collect the pellet at the given tile.
        /// Returns the tile type if something was collected, or MazeTile.Empty if nothing.
        /// </summary>
        public MazeTile TryCollect(int x, int y)
        {
            if (_runtimeLayout == null) return MazeTile.Empty;
            if (x < 0 || x >= MazeData.Width || y < 0 || y >= MazeData.Height)
                return MazeTile.Empty;

            var tile = _runtimeLayout[x, y];
            if (tile != MazeTile.Pellet && tile != MazeTile.Energizer)
                return MazeTile.Empty;

            // Consume it
            _runtimeLayout[x, y] = MazeTile.Empty;
            _remainingPellets--;
            _pelletsEaten++;

            OnPelletCollected?.Invoke(new Vector2Int(x, y), tile);

            if (_remainingPellets <= 0)
            {
                Debug.Log("[PelletManager] All pellets cleared!");
                OnAllPelletsCleared?.Invoke();
            }

            return tile;
        }

        /// <summary>Check if there is a pellet or energizer at the given tile.</summary>
        public bool HasPellet(int x, int y)
        {
            if (_runtimeLayout == null) return false;
            if (x < 0 || x >= MazeData.Width || y < 0 || y >= MazeData.Height)
                return false;
            var tile = _runtimeLayout[x, y];
            return tile == MazeTile.Pellet || tile == MazeTile.Energizer;
        }

        /// <summary>Get the tile type at runtime (may have been consumed).</summary>
        public MazeTile GetRuntimeTile(int x, int y)
        {
            if (_runtimeLayout == null) return MazeData.GetTile(x, y);
            if (x < 0 || x >= MazeData.Width || y < 0 || y >= MazeData.Height)
                return MazeTile.Wall;
            return _runtimeLayout[x, y];
        }
    }
}
