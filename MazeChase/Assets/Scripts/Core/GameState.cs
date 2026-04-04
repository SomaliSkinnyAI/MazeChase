namespace MazeChase.Core
{
    /// <summary>
    /// All high-level states the game can be in.
    /// </summary>
    public enum GameState
    {
        Boot,
        Menu,
        Playing,
        Dying,
        RoundClear,
        GameOver,
        Intermission,
        Paused
    }
}
