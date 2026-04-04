using System;

namespace MazeChase.Core
{
    /// <summary>
    /// Static utility for parsing command-line arguments passed to the Unity player.
    /// Supports --flag and --key=value style arguments.
    /// </summary>
    public static class CommandLineArgs
    {
        private static string[] _cachedArgs;

        private static string[] GetArgs()
        {
            if (_cachedArgs == null)
            {
                _cachedArgs = Environment.GetCommandLineArgs();
            }
            return _cachedArgs;
        }

        /// <summary>
        /// Returns true if the given flag is present in the command-line arguments.
        /// Example: HasFlag("--smoke-test")
        /// </summary>
        public static bool HasFlag(string flag)
        {
            string[] args = GetArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Returns the value for a --key=value argument, or null if not found.
        /// Example: GetValue("--level") returns "3" for "--level=3"
        /// </summary>
        public static string GetValue(string key)
        {
            string prefix = key + "=";
            string[] args = GetArgs();

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return args[i].Substring(prefix.Length);
                }

                // Also support --key value (space-separated) form
                if (string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase)
                    && i + 1 < args.Length
                    && !args[i + 1].StartsWith("--"))
                {
                    return args[i + 1];
                }
            }

            return null;
        }
    }
}
