using UnityEngine;

namespace MazeChase.Game
{
    public enum Direction
    {
        None = 0,
        Up = 1,
        Down = 2,
        Left = 3,
        Right = 4
    }

    public static class DirectionHelper
    {
        public static readonly Direction[] AllDirections = new Direction[]
        {
            Direction.Up,
            Direction.Down,
            Direction.Left,
            Direction.Right
        };

        public static Vector2Int ToVector(Direction dir)
        {
            switch (dir)
            {
                case Direction.Up:    return new Vector2Int(0, -1);
                case Direction.Down:  return new Vector2Int(0, 1);
                case Direction.Left:  return new Vector2Int(-1, 0);
                case Direction.Right: return new Vector2Int(1, 0);
                default:              return Vector2Int.zero;
            }
        }

        public static Direction Opposite(Direction dir)
        {
            switch (dir)
            {
                case Direction.Up:    return Direction.Down;
                case Direction.Down:  return Direction.Up;
                case Direction.Left:  return Direction.Right;
                case Direction.Right: return Direction.Left;
                default:              return Direction.None;
            }
        }
    }
}
