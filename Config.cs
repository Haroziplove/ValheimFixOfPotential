using System;
using System.IO;
using BepInEx.Configuration;

namespace SafeRefinement
{
    public enum FailureOutcome
    {
        KeepItem,
        Downgrade,
        Vanilla
    }

    internal static class ModConfig
    {
        public static ConfigEntry<bool> Enabled { get; private set; }
        public static ConfigEntry<FailureOutcome> Outcome { get; private set; }
        public static ConfigEntry<bool> LogAttempts { get; private set; }

        private static ConfigFile _file;
        private static FileSystemWatcher _watcher;
        private static volatile bool _reload;
        private static int _retryFrames;

        public static void Bind(ConfigFile file)
        {
            _file = file;
            Enabled = file.Bind(
                "General",
                "Enabled",
                true,
                "Apply FailureOutcome on this client. When false, refinement stays vanilla.");
            Outcome = file.Bind(
                "General",
                "FailureOutcome",
                FailureOutcome.KeepItem,
                "Used when the vanilla roll would break the item. KeepItem puts the same item back and still consumes the idol, with no material refund. Downgrade uses the game's one-level-down branch. Vanilla breaks the item and refunds materials. The success roll is not changed.");
            LogAttempts = file.Bind(
                "Debug",
                "LogAttempts",
                false,
                "Log each forge attempt: player name, item, current level, roll, and the outcome applied.");
        }

        public static void InstallWatcher()
        {
            string path = _file.ConfigFilePath;
            string directory = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(directory))
            {
                return;
            }

            _watcher = new FileSystemWatcher(directory, Path.GetFileName(path))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName
            };
            _watcher.Changed += OnFileChanged;
            _watcher.Created += OnFileChanged;
            _watcher.Renamed += OnFileChanged;
            _watcher.EnableRaisingEvents = true;
        }

        public static void DisposeWatcher()
        {
            if (_watcher == null)
            {
                return;
            }

            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;
        }

        public static void Tick()
        {
            if (!_reload || _file == null)
            {
                return;
            }

            if (_retryFrames > 0)
            {
                _retryFrames--;
                return;
            }

            try
            {
                _file.Reload();
                _reload = false;
                Log().LogInfo("Reloaded config. FailureOutcome=" + Outcome.Value + " Enabled=" + Enabled.Value);
            }
            catch (IOException)
            {
                _retryFrames = 30;
            }
            catch (Exception ex)
            {
                _reload = false;
                Log().LogError("Config reload failed: " + ex);
            }
        }

        private static void OnFileChanged(object sender, FileSystemEventArgs args)
        {
            _reload = true;
        }

        private static BepInEx.Logging.ManualLogSource Log()
        {
            return Plugin.Log;
        }
    }
}
