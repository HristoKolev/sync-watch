namespace SyncWatch
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;

    using Newtonsoft.Json;

    public class SyncManager
    {
        public static int Run(string syncSetupFile)
        {
            MainLogger.Instance.LogDebug($"sync-watch started in manager mode...");

            var setup = JsonConvert.DeserializeObject<SetupConnection[]>(File.ReadAllText(syncSetupFile));

            var sessions = new List<SyncSession>();

            foreach (var entry in setup)
            {
                if (!entry.IsEnabled)
                {
                    continue;
                }

                // The path to the settings file.
                string path = Path.Combine(entry.Path, Global.SyncSettingsFileName);

                // Local path relative to the current working directory.
                string ResolveLocalPath(string localPath)
                {
                    return Path.GetFullPath(Path.Combine(entry.Path, localPath));
                }

                var syncSettings = SyncSession.ReadSettings(path, ResolveLocalPath);

                if (syncSettings == null)
                {
                    return 1;
                }

                sessions.Add(new SyncSession(syncSettings));

                MainLogger.Instance.LogDebug($"Connection added: {syncSettings.LocalPath}");
            }

            MainLogger.Instance.LogDebug("Successfully read the connection list.");

            foreach (var session in sessions)
            {
                try
                {
                    session.Start();
                }
                catch (Exception exception)
                {
                    MainLogger.Instance.LogError(exception);
                    MainLogger.Instance.LogDebug($"Connection start failed: {session.SyncSettings.LocalPath}");
                }
            }
            
            Thread.Sleep(-1);

            return 0;
        }
    }

    public class SetupConnection
    {
        public bool RunOnStartup { get; set; }

        public bool IsEnabled { get; set; }

        public string Path { get; set; }
    }
}