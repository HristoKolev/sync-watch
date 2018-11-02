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
        private const string SyncSettingsFileName = "sync-settings.json";

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

            File.WriteAllText(SyncSettingsFileName, JsonConvert.SerializeObject(defaultSettings, Formatting.Indented));

            MainLogger.Instance.LogDebug($"Example `{SyncSettingsFileName}` created in {Environment.CurrentDirectory}.");
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
                    if (File.Exists(SyncSettingsFileName))
                    {
                        MainLogger.Instance.LogDebug($"`{SyncSettingsFileName}` already exists.");
                        return 1;
                    }

                    CreateSyncFile();

                    return 0;
                }

                // REPL for managing many sessions.
                if (!string.IsNullOrWhiteSpace(options.SyncSetupFile))
                {
                    SyncSetupRepl();
                    return 0;
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
            // The path to the settings file.
            string path = Path.Combine(Environment.CurrentDirectory, SyncSettingsFileName);

            // Local path relative to the current working directory.
            var settings = SyncSession.ReadSettings(path, localPath => Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, localPath)));

            using (var session = new SyncSession(settings))
            {
                session.Start();

                // Just sleep. The session will never close.
                Thread.Sleep(-1);
            }
        }

        private static void SyncSetupRepl()
        {
            while (true)
            {
                Console.WriteLine("Enter 'q' to exit or 'r' to reload.");
                string input = Console.ReadLine() ?? string.Empty;

                if (input == "q")
                {
                    MainLogger.Instance.LogDebug("Quitting...");
                    break;
                }

                if (input == "r")
                {
                    MainLogger.Instance.LogDebug("Reloading...");
                }
            }
        }

        private static T ReadArguments<T>(string[] args)
            where T : class
        {
            T cliArgs = null;

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