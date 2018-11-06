namespace SyncWatch
{
    using System;
    using System.IO;
    using System.Reflection;
    using System.Threading;
    using System.Threading.Tasks;

    using CommandLine;

    using Newtonsoft.Json;

    public static class Program
    {
        private static void CreateSyncFile()
        {
            Console.Write("server: ");
            string server = Console.ReadLine();

            Console.Write("username: (default is root): ");
            string username = Console.ReadLine();

            Console.Write("remote path: ");
            string remotePath = Console.ReadLine();

            var defaultSettings = new SyncSettings
            {
                HostName = string.IsNullOrWhiteSpace(server) ? "example.com" : server,
                UserName = string.IsNullOrWhiteSpace(username) ? "root" : username,
                SshHostKeyFingerprint = Global.SshHostKeyFingerprintMessage,
                LocalPath = "./",
                FileMask = "* | */node_modules/; */.git/; */.idea/; sync-settings.json;",
                RemotePath = string.IsNullOrWhiteSpace(remotePath) ? "/home/example" : remotePath,
            };

            File.WriteAllText(Global.SyncSettingsFileName, JsonConvert.SerializeObject(defaultSettings, Formatting.Indented));

            MainLogger.Instance.LogDebug($"Example `{Global.SyncSettingsFileName}` created in {Environment.CurrentDirectory}.");
        }

        private static async Task<int> Main(string[] args)
        {
            // Read the app-config.
            if (!File.Exists(Global.AppConfigLocation))
            {
                Console.WriteLine("`app-config.json` is missing...");
                return 1;
            }

            Global.AppConfig = JsonConvert.DeserializeObject<AppConfig>(File.ReadAllText(Global.AppConfigLocation));

            // Logging.
            MainLogger.Initialize(new LoggerConfigModel
            {
                Assembly = Assembly.GetEntryAssembly(),
                LogRootDirectory = Global.AssemblyDirectory,
                SentryDsn = Global.AppConfig.SentryDsn
            });
            
            try
            {
                // Reading the CLI arguments
                var options = ReadArguments<CliArguments>(args);

                // If we there ware errors during argument parsing.
                if (options == null)
                {
                    return 1;
                }

                // Create new sync-settings.json file.
                if (options.Create)
                {
                    if (File.Exists(Global.SyncSettingsFileName))
                    {
                        MainLogger.Instance.LogDebug($"`{Global.SyncSettingsFileName}` already exists.");
                        return 1;
                    }

                    CreateSyncFile();

                    return 0;
                }

                // Managing of many sessions.
                if (!string.IsNullOrWhiteSpace(options.SyncSetupFile))
                {
                    return SyncManager.Run(options.SyncSetupFile);
                }

                // Single session mode.
                RunSyncInstance();

                return 0;
            }
            catch (Exception exception)
            {
                await MainLogger.Instance.LogError(exception);
                return 1;
            }
        }

        private static void RunSyncInstance()
        {
            MainLogger.Instance.LogDebug($"sync-watch started in single instance mode...");

            // The path to the settings file.
            string path = Path.Combine(Environment.CurrentDirectory, Global.SyncSettingsFileName);

            // Local path relative to the current working directory.
            string ResolveLocalPath(string localPath)
            {
                return Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, localPath));
            }

            var settings = SyncSession.ReadSettings(path, ResolveLocalPath);

            if (settings == null)
            {
                return;
            }

            using (var session = new SyncSession(settings))
            {
                session.LogLocation = false;

                session.Start();

                // Just sleep. The session will never close.
                Thread.Sleep(-1);
            }
        }

        private static T ReadArguments<T>(string[] args)
        {
            T cliArgs = default;

            Parser.Default.ParseArguments<T>(args)
                  .WithParsed(x => cliArgs = x);

            return cliArgs;
        }
    }

    public class CliArguments
    {
        [Option('c', "create", HelpText = "Create new sync-settings.json file.", Required = false)]
        public bool Create { get; set; }

        [Option('f', "setup-file", HelpText = "Run sync setup file.", Required = false)]
        public string SyncSetupFile { get; set; }
    }
}