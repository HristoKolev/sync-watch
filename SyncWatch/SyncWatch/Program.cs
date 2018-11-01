namespace SyncWatch
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Reflection;

    using CommandLine;

    using Newtonsoft.Json;

    public class Program
    {
        private const string SettingsFileName = "sync-settings.json";

        private const string SshHostKeyFingerprintMessage =
            "Put your the host key fingerprint here or leave this empty (except any key - unsecure)";

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
                SshHostKeyFingerprint = SshHostKeyFingerprintMessage,
                LocalPath = "./",
                FileMask = "* | */node_modules/; */.git/; */.idea/; sync-settings.json;",
                RemotePath = string.IsNullOrWhiteSpace(remotePath) ? "/home/example" : remotePath,
            };

            if (File.Exists(SettingsFileName))
            {
                MainLogger.Instance.LogDebug($"`{SettingsFileName}` already exists.");
            }
            else
            {
                File.WriteAllText(SettingsFileName, JsonConvert.SerializeObject(defaultSettings, Formatting.Indented));

                MainLogger.Instance.LogDebug($"Example `{SettingsFileName}` created in {Environment.CurrentDirectory}.");
            }
        }

        private static void Main(string[] args)
        {
            MainLogger.Initialize(Assembly.GetEntryAssembly());

            while (true)
            {
                var options = ReadCliOptions(args);

                if (options.Create)
                {
                    CreateSyncFile();
                }
            }
        }

        private static CliOptions ReadCliOptions(string[] args)
        {
            CliOptions options = null;

            IEnumerable<Error> errors = null;

            var result = Parser.Default.ParseArguments<CliOptions>(args);

            result.WithParsed(opt => options = opt).WithNotParsed(e => errors = e);

            if (errors != null)
            {
                Environment.Exit(0);
            }

            return options;
        }

        private static SyncSettings ReadSettings(string path)
        {
            SyncSettings settings;

            if (File.Exists(path))
            {
                settings = JsonConvert.DeserializeObject<SyncSettings>(File.ReadAllText(path));
            }
            else
            {
                MainLogger.Instance.LogDebug($"Cannot find file `{path}`.");
                MainLogger.Instance.LogDebug("Run `sync-watch -c` to create a new one.");

                return null;
            }

            if (!Path.IsPathRooted(settings.LocalPath))
            {
                MainLogger.Instance.LogDebug($"The path is not relative `{path}`.");
                return null;
            }

            if (string.IsNullOrWhiteSpace(settings.SshHostKeyFingerprint) || settings.SshHostKeyFingerprint == SshHostKeyFingerprintMessage)
            {
                settings.SshHostKeyFingerprint = null;
            }

            return settings;
        }
    }

    public class SyncSettings
    {
        public string FileMask { get; set; }

        public string HostName { get; set; }

        public string LocalPath { get; set; }

        public string RemotePath { get; set; }

        public string SshHostKeyFingerprint { get; set; }

        public string UserName { get; set; }
    }

    public class CliOptions
    {
        [Option('c', "create", HelpText = "Create new sync-settings.json file.", Required = false)]
        public bool Create { get; set; }
    }
}