using System.Collections.Generic;
using UnityEngine;
using MazeChase.Game;

namespace MazeChase.AI.Autoplay
{
    /// <summary>
    /// Precomputed player navigation graph over the maze. The graph excludes
    /// walls, the ghost house interior, and the ghost door, and treats the
    /// side tunnels as portal links for pathfinding and planning.
    /// </summary>
    public sealed class MazeGraph
    {
        private sealed class Node
        {
            public int Index;
            public Vector2Int Tile;
            public int[] NeighborsByDirection = { -1, -1, -1, -1, -1 };
            public int Degree;
            public bool IsTunnel;
            public int DeadEndDepth;
        }

        private readonly List<Node> _nodes = new List<Node>(320);
        private readonly Dictionary<Vector2Int, int> _tileToIndex = new Dictionary<Vector2Int, int>(320);
        private readonly int[,] _distances;

        public MazeGraph()
        {
            BuildNodes();
            LinkNeighbors();
            ComputeDeadEnds();
            _distances = BuildDistanceMatrix();
        }

        public int NodeCount => _nodes.Count;

        public bool TryGetNodeIndex(Vector2Int tile, out int index)
        {
            return _tileToIndex.TryGetValue(tile, out index);
        }

        public int GetNodeIndex(Vector2Int tile)
        {
            int index;
            return _tileToIndex.TryGetValue(tile, out index) ? index : -1;
        }

        public Vector2Int GetTile(int nodeIndex)
        {
            if (nodeIndex < 0 || nodeIndex >= _nodes.Count)
                return Vector2Int.zero;

            return _nodes[nodeIndex].Tile;
        }

        public int GetNeighbor(int nodeIndex, Direction direction)
        {
            if (nodeIndex < 0 || nodeIndex >= _nodes.Count)
                return -1;

            return _nodes[nodeIndex].NeighborsByDirection[(int)direction];
        }

        public int GetDegree(int nodeIndex)
        {
            if (nodeIndex < 0 || nodeIndex >= _nodes.Count)
                return 0;

            return _nodes[nodeIndex].Degree;
        }

        public int GetDeadEndDepth(int nodeIndex)
        {
            if (nodeIndex < 0 || nodeIndex >= _nodes.Count)
                return 0;

            return _nodes[nodeIndex].DeadEndDepth;
        }

        public bool IsTunnel(int nodeIndex)
        {
            if (nodeIndex < 0 || nodeIndex >= _nodes.Count)
                return false;

            return _nodes[nodeIndex].IsTunnel;
        }

        public int GetDistance(Vector2Int fromTile, Vector2Int toTile)
        {
            int fromIndex = GetNodeIndex(fromTile);
            int toIndex = GetNodeIndex(toTile);

            if (fromIndex < 0 || toIndex < 0)
                return Mathf.Abs(fromTile.x - toTile.x) + Mathf.Abs(fromTile.y - toTile.y);

            return GetDistance(fromIndex, toIndex);
        }

        public int GetDistance(int fromIndex, int toIndex)
        {
            if (fromIndex < 0 || fromIndex >= _nodes.Count || toIndex < 0 || toIndex >= _nodes.Count)
                return 999;

            return _distances[fromIndex, toIndex];
        }

        public List<Direction> GetLegalDirections(Vector2Int tile, List<Direction> buffer = null)
        {
            if (buffer == null)
                buffer = new List<Direction>(4);
            else
                buffer.Clear();

            int index;
            if (!_tileToIndex.TryGetValue(tile, out index))
                return buffer;

            foreach (Direction direction in DirectionHelper.AllDirections)
            {
                if (_nodes[index].NeighborsByDirection[(int)direction] >= 0)
                    buffer.Add(direction);
            }

            return buffer;
        }

        public Direction GetBestDirectionToward(Vector2Int fromTile, Vector2Int targetTile, Direction disallowedDirection)
        {
            int fromIndex = GetNodeIndex(fromTile);
            int targetIndex = GetNodeIndex(targetTile);

            if (fromIndex < 0 || targetIndex < 0)
                return Direction.None;

            int bestDistance = int.MaxValue;
            Direction bestDirection = Direction.None;

            foreach (Direction direction in DirectionHelper.AllDirections)
            {
                if (direction == disallowedDirection)
                    continue;

                int neighbor = _nodes[fromIndex].NeighborsByDirection[(int)direction];
                if (neighbor < 0)
                    continue;

                int distance = _distances[neighbor, targetIndex];
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestDirection = direction;
                }
            }

            return bestDirection;
        }

        public int CountPelletsWithin(int startIndex, PelletManager pellets, int maxDistance)
        {
            if (startIndex < 0 || pellets == null)
                return 0;

            int count = 0;
            for (int nodeIndex = 0; nodeIndex < _nodes.Count; nodeIndex++)
            {
                if (_distances[startIndex, nodeIndex] > maxDistance)
                    continue;

                Vector2Int tile = _nodes[nodeIndex].Tile;
                if (pellets.HasPellet(tile.x, tile.y))
                    count++;
            }

            return count;
        }

        public int CountPelletsInDirection(Vector2Int startTile, Direction direction, PelletManager pellets, int maxDistance)
        {
            int startIndex = GetNodeIndex(startTile);
            if (startIndex < 0 || pellets == null)
                return 0;

            int blockedIndex = GetNeighbor(startIndex, DirectionHelper.Opposite(direction));
            int nextIndex = GetNeighbor(startIndex, direction);
            if (nextIndex < 0)
                return 0;

            var queue = new Queue<int>(64);
            var visited = new bool[_nodes.Count];
            visited[startIndex] = true;
            if (blockedIndex >= 0)
                visited[blockedIndex] = true;
            visited[nextIndex] = true;
            queue.Enqueue(nextIndex);

            int pelletCount = 0;

            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                if (_distances[nextIndex, current] > maxDistance)
                    continue;

                Vector2Int tile = _nodes[current].Tile;
                if (pellets.HasPellet(tile.x, tile.y))
                    pelletCount++;

                foreach (Direction nextDirection in DirectionHelper.AllDirections)
                {
                    int neighbor = _nodes[current].NeighborsByDirection[(int)nextDirection];
                    if (neighbor < 0 || visited[neighbor])
                        continue;

                    visited[neighbor] = true;
                    queue.Enqueue(neighbor);
                }
            }

            return pelletCount;
        }

        public int FindNearestPelletDistance(Vector2Int startTile, PelletManager pellets)
        {
            int startIndex = GetNodeIndex(startTile);
            if (startIndex < 0 || pellets == null)
                return 999;

            int bestDistance = 999;
            for (int nodeIndex = 0; nodeIndex < _nodes.Count; nodeIndex++)
            {
                Vector2Int tile = _nodes[nodeIndex].Tile;
                if (!pellets.HasPellet(tile.x, tile.y))
                    continue;

                int distance = _distances[startIndex, nodeIndex];
                if (distance < bestDistance)
                    bestDistance = distance;
            }

            return bestDistance;
        }

        public Vector2Int FindNearestPelletTile(Vector2Int startTile, PelletManager pellets)
        {
            int startIndex = GetNodeIndex(startTile);
            if (startIndex < 0 || pellets == null)
                return startTile;

            int bestDistance = 999;
            Vector2Int bestTile = startTile;
            for (int nodeIndex = 0; nodeIndex < _nodes.Count; nodeIndex++)
            {
                Vector2Int tile = _nodes[nodeIndex].Tile;
                if (!pellets.HasPellet(tile.x, tile.y))
                    continue;

                int distance = _distances[startIndex, nodeIndex];
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestTile = tile;
                }
            }

            return bestTile;
        }

        public int FindNearestEnergizerDistance(Vector2Int startTile, PelletManager pellets)
        {
            int startIndex = GetNodeIndex(startTile);
            if (startIndex < 0 || pellets == null)
                return 999;

            int bestDistance = 999;
            for (int nodeIndex = 0; nodeIndex < _nodes.Count; nodeIndex++)
            {
                Vector2Int tile = _nodes[nodeIndex].Tile;
                if (MazeData.GetTile(tile.x, tile.y) != MazeTile.Energizer)
                    continue;

                if (!pellets.HasPellet(tile.x, tile.y))
                    continue;

                int distance = _distances[startIndex, nodeIndex];
                if (distance < bestDistance)
                    bestDistance = distance;
            }

            return bestDistance;
        }

        public int FindNearestJunctionDistance(Vector2Int startTile)
        {
            int startIndex = GetNodeIndex(startTile);
            if (startIndex < 0)
                return 999;

            int bestDistance = 999;
            for (int nodeIndex = 0; nodeIndex < _nodes.Count; nodeIndex++)
            {
                if (_nodes[nodeIndex].Degree < 3)
                    continue;

                int distance = _distances[startIndex, nodeIndex];
                if (distance < bestDistance)
                    bestDistance = distance;
            }

            return bestDistance;
        }

        private void BuildNodes()
        {
            _nodes.Clear();
            _tileToIndex.Clear();

            for (int y = 0; y < MazeData.Height; y++)
            {
                for (int x = 0; x < MazeData.Width; x++)
                {
                    MazeTile tile = MazeData.GetTile(x, y);
                    if (!IsPlayerNavigable(tile))
                        continue;

                    var node = new Node
                    {
                        Index = _nodes.Count,
                        Tile = new Vector2Int(x, y),
                        IsTunnel = tile == MazeTile.Tunnel
                    };

                    _nodes.Add(node);
                    _tileToIndex[node.Tile] = node.Index;
                }
            }
        }

        private void LinkNeighbors()
        {
            foreach (Node node in _nodes)
            {
                int degree = 0;
                foreach (Direction direction in DirectionHelper.AllDirections)
                {
                    Vector2Int neighborTile = GetWrappedNeighbor(node.Tile, direction);
                    int neighborIndex;
                    if (_tileToIndex.TryGetValue(neighborTile, out neighborIndex))
                    {
                        node.NeighborsByDirection[(int)direction] = neighborIndex;
                        degree++;
                    }
                }

                node.Degree = degree;
            }
        }

        private void ComputeDeadEnds()
        {
            foreach (Node node in _nodes)
            {
                if (node.Degree != 1)
                {
                    node.DeadEndDepth = 0;
                    continue;
                }

                int depth = 0;
                int current = node.Index;
                int previous = -1;

                while (current >= 0)
                {
                    depth++;
                    int next = -1;
                    int options = 0;

                    foreach (Direction direction in DirectionHelper.AllDirections)
                    {
                        int neighbor = _nodes[current].NeighborsByDirection[(int)direction];
                        if (neighbor < 0 || neighbor == previous)
                            continue;

                        next = neighbor;
                        options++;
                    }

                    if (options != 1)
                        break;

                    previous = current;
                    current = next;

                    if (current >= 0 && _nodes[current].Degree > 2)
                        break;
                }

                node.DeadEndDepth = depth;
            }
        }

        private int[,] BuildDistanceMatrix()
        {
            int[,] matrix = new int[_nodes.Count, _nodes.Count];
            for (int from = 0; from < _nodes.Count; from++)
            {
                for (int to = 0; to < _nodes.Count; to++)
                    matrix[from, to] = from == to ? 0 : 999;

                var queue = new Queue<int>(64);
                queue.Enqueue(from);

                while (queue.Count > 0)
                {
                    int current = queue.Dequeue();
                    int currentDistance = matrix[from, current];

                    foreach (Direction direction in DirectionHelper.AllDirections)
                    {
                        int neighbor = _nodes[current].NeighborsByDirection[(int)direction];
                        if (neighbor < 0)
                            continue;

                        if (matrix[from, neighbor] <= currentDistance + 1)
                            continue;

                        matrix[from, neighbor] = currentDistance + 1;
                        queue.Enqueue(neighbor);
                    }
                }
            }

            return matrix;
        }

        private static bool IsPlayerNavigable(MazeTile tile)
        {
            switch (tile)
            {
                case MazeTile.Wall:
                case MazeTile.GhostHouse:
                case MazeTile.GhostDoor:
                    return false;
                default:
                    return true;
            }
        }

        private static Vector2Int GetWrappedNeighbor(Vector2Int tile, Direction direction)
        {
            Vector2Int neighbor = tile + DirectionHelper.ToVector(direction);
            if (neighbor.x < 0)
                neighbor.x = MazeData.Width - 1;
            else if (neighbor.x >= MazeData.Width)
                neighbor.x = 0;

            return neighbor;
        }
    }
}
