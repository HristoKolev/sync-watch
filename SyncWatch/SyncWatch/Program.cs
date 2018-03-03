namespace SyncWatch
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;

    using CommandLine;

    using Newtonsoft.Json;

    using WinSCP;

    public class Program
    {
        private const string SettingsFileName = "sync-settings.json";

        private static void CreateSyncFile()
        {
            var defaultSettings = new SyncSettings
            {
                HostName = "example.com",
                UserName = "username",
                SshHostKeyFingerprint = "Put your the host key fingerprint here or leave this empty (except any key - unsecure)",
                LocalPath = "./",
                FileMask = "*",
                RemotePath = "/home/example"
            };

            if (File.Exists(SettingsFileName))
            {
                Console.WriteLine($"`{SettingsFileName}` already exists.");
            }
            else
            {
                File.WriteAllText(SettingsFileName, JsonConvert.SerializeObject(defaultSettings, Formatting.Indented));

                Console.WriteLine($"Example `{SettingsFileName}` created in {Environment.CurrentDirectory}.");
            }

            Environment.Exit(0);
        }

        private static void FileTransferred(object sender, TransferEventArgs e)
        {
            if (e.Error == null)
            {
                Console.WriteLine("Upload of {0} succeeded", e.FileName);
            }
            else
            {
                Console.WriteLine("Upload of {0} failed: {1}", e.FileName, e.Error);
            }

            if (e.Chmod != null)
            {
                if (e.Chmod.Error == null)
                {
                    Console.WriteLine("Permissions of {0} set to {1}", e.Chmod.FileName, e.Chmod.FilePermissions);
                }
                else
                {
                    Console.WriteLine("Setting permissions of {0} failed: {1}", e.Chmod.FileName, e.Chmod.Error);
                }
            }
            else
            {
                Console.WriteLine("Permissions of {0} kept with their defaults", e.Destination);
            }

            if (e.Touch != null)
            {
                if (e.Touch.Error == null)
                {
                    Console.WriteLine("Timestamp of {0} set to {1}", e.Touch.FileName, e.Touch.LastWriteTime);
                }
                else
                {
                    Console.WriteLine("Setting timestamp of {0} failed: {1}", e.Touch.FileName, e.Touch.Error);
                }
            }
            else
            {
                // This should never happen during "local to remote" synchronization
                Console.WriteLine("Timestamp of {0} kept with its default (current time)", e.Destination);
            }

            if (e.Removal != null)
            {
                if (e.Removal.Error == null)
                {
                    Console.WriteLine("Removed of {0} succeeded.", e.Removal.FileName);
                }
                else
                {
                    Console.WriteLine("Removed of {0} failed: {1}.", e.Removal.FileName, e.Removal.Error);
                }
            }
        }

        private static void Main(string[] args)
        {
            var options = ReadCliOptions(args);

            if (options.Create)
            {
                CreateSyncFile();
            }

            var settings = ReadSettings();
            settings.LocalPath = settings.LocalPath ?? Environment.CurrentDirectory;

            var sessionOptions = new SessionOptions
            {
                Protocol = Protocol.Sftp,
                HostName = settings.HostName,
                UserName = settings.UserName,
                SshHostKeyFingerprint = string.IsNullOrWhiteSpace(settings.SshHostKeyFingerprint) ? null : settings.SshHostKeyFingerprint,
                GiveUpSecurityAndAcceptAnySshHostKey = string.IsNullOrWhiteSpace(settings.SshHostKeyFingerprint),
            };

            using (var session = new Session())
            {
                session.FileTransferred += FileTransferred;

                session.Open(sessionOptions);

                var fsWatcher = new FileSystemWatcher
                {
                    Path = settings.LocalPath,
                    NotifyFilter =
                        NotifyFilters.LastAccess | NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName,
                    Filter = "*",
                    IncludeSubdirectories = true
                };

                void OnChanged(object sender, FileSystemEventArgs e)
                {
                    TriggerSync(session, settings);
                }

                fsWatcher.Changed += OnChanged;
                fsWatcher.Created += OnChanged;
                fsWatcher.Deleted += OnChanged;
                fsWatcher.Renamed += OnChanged;

                fsWatcher.EnableRaisingEvents = true;

                Console.WriteLine($"Sync watching: {Path.GetFullPath(settings.LocalPath)}");

                Thread.Sleep(-1);
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

        private static SyncSettings ReadSettings()
        {
            SyncSettings settings = null;

            if (File.Exists(SettingsFileName))
            {
                settings = JsonConvert.DeserializeObject<SyncSettings>(File.ReadAllText(SettingsFileName));
            }
            else
            {
                Console.WriteLine($"Cannot find `{SettingsFileName}` in `{Environment.CurrentDirectory}`.");
                Console.WriteLine("Run `sync-watch -c` to create a new one.");

                Environment.Exit(0);
            }

            return settings;
        }

        private static void TriggerSync(Session session, SyncSettings settings)
        {
            try
            {
                var result = session.SynchronizeDirectories(mode: SynchronizationMode.Remote, localPath: Path.Combine(Environment.CurrentDirectory, settings.LocalPath) ,
                    remotePath: settings.RemotePath, removeFiles: true, mirror: true, criteria: SynchronizationCriteria.Time,
                    options: new TransferOptions
                    {
                        FilePermissions = new FilePermissions(777),
                        OverwriteMode = OverwriteMode.Overwrite,
                        TransferMode = TransferMode.Automatic,
                        FileMask = settings.FileMask,
                    });

                result.Check();
            }
            catch (SessionRemoteException e)
            {
                Console.WriteLine("Error: {0}", e);
            }
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