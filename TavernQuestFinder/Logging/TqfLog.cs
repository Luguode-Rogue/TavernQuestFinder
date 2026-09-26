using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;

namespace TavernQuestFinder.Logging
{
    internal static class TqfLog
    {
        private const string ModuleId = "TavernQuestFinder";
        private const string LogFileName = "TavernQuestFinder.log";

        private static readonly object Sync = new object();

        private static bool _initialized;
        private static string? _logPath;

        public static string? LogPath => _logPath;

        public static void Initialize()
        {
            lock (Sync)
            {
                if (_initialized)
                {
                    return;
                }

                _initialized = true;

                try
                {
                    string moduleRoot = ResolveInstalledModuleRoot();
                    _logPath = Path.Combine(moduleRoot, LogFileName);

                    var header = new StringBuilder();
                    header.AppendLine("TavernQuestFinder log");
                    header.AppendLine(
                        $"SessionStart={DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
                    header.AppendLine($"ModuleRoot={moduleRoot}");
                    header.AppendLine(
                        $"Assembly={Assembly.GetExecutingAssembly().Location}");
                    header.AppendLine(new string('-', 80));

                    // Intentionally overwrite the previous session on every
                    // Bannerlord process start.
                    File.WriteAllText(
                        _logPath,
                        header.ToString(),
                        Encoding.UTF8);
                }
                catch (Exception exception)
                {
                    _logPath = null;
                    Debug.WriteLine(
                        "[TavernQuestFinder] Failed to initialize file log: " +
                        exception);
                }
            }
        }

        public static void Info(string message)
        {
            Write("INFO", message);
        }

        public static void Warn(string message)
        {
            Write("WARN", message);
        }

        public static void Error(string message, Exception? exception = null)
        {
            Write(
                "ERROR",
                exception == null
                    ? message
                    : message + Environment.NewLine + exception);
        }

        private static void Write(string level, string message)
        {
            if (!_initialized)
            {
                Initialize();
            }

            string line =
                $"[{DateTime.Now:HH:mm:ss.fff}] [{level}] {message}";

            lock (Sync)
            {
                if (_logPath == null)
                {
                    Debug.WriteLine("[TavernQuestFinder] " + line);
                    return;
                }

                try
                {
                    File.AppendAllText(
                        _logPath,
                        line + Environment.NewLine,
                        Encoding.UTF8);
                }
                catch (Exception exception)
                {
                    Debug.WriteLine(
                        "[TavernQuestFinder] Failed to write log: " +
                        exception);
                }
            }
        }

        private static string ResolveInstalledModuleRoot()
        {
            // Preferred route: start from the Bannerlord process directory and
            // walk upward until <game root>/Modules/TavernQuestFinder exists.
            DirectoryInfo? directory =
                new DirectoryInfo(AppContext.BaseDirectory);

            while (directory != null)
            {
                string candidate = Path.Combine(
                    directory.FullName,
                    "Modules",
                    ModuleId);

                if (IsInstalledModuleRoot(candidate))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }

            // Secondary route: only accept an assembly ancestor that is
            // literally .../Modules/TavernQuestFinder. This intentionally
            // rejects a similarly named project/source directory.
            string? assemblyDirectoryPath =
                Path.GetDirectoryName(
                    Assembly.GetExecutingAssembly().Location);

            directory = string.IsNullOrWhiteSpace(assemblyDirectoryPath)
                ? null
                : new DirectoryInfo(assemblyDirectoryPath);

            while (directory != null)
            {
                if (directory.Name.Equals(
                        ModuleId,
                        StringComparison.OrdinalIgnoreCase) &&
                    directory.Parent != null &&
                    directory.Parent.Name.Equals(
                        "Modules",
                        StringComparison.OrdinalIgnoreCase) &&
                    IsInstalledModuleRoot(directory.FullName))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException(
                "Installed Bannerlord module root was not found. " +
                "The logger will not fall back to a project or working directory.");
        }

        private static bool IsInstalledModuleRoot(string path)
        {
            return Directory.Exists(path) &&
                   File.Exists(Path.Combine(path, "SubModule.xml"));
        }
    }
}
