namespace SyncWatch
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Reflection;
    using System.Threading;

    using CommandLine;

    using log4net;
    using log4net.Config;

    using Newtonsoft.Json;

    using WinSCP;

    public class Program
    {
        private const string SettingsFileName = "sync-settings.json";

        private static ErrorHandler ErrorHandler { get; set; }

        private static ILog Log { get; set; }

        private static string RootDirectory => Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);

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
                LogDebug($"`{SettingsFileName}` already exists.");
            }
            else
            {
                File.WriteAllText(SettingsFileName, JsonConvert.SerializeObject(defaultSettings, Formatting.Indented));

                LogDebug($"Example `{SettingsFileName}` created in {Environment.CurrentDirectory}.");
            }

            Environment.Exit(0);
        }

        private static void FileTransferred(object sender, TransferEventArgs e)
        {
            if (e.Error == null)
            {
                LogDebug($"Upload of {e.FileName} succeeded");
            }
            else
            {
                LogDebug($"Upload of {e.FileName} failed: {e.Error}");
            }

            if (e.Chmod != null)
            {
                if (e.Chmod.Error == null)
                {
                    LogDebug($"Permissions of {e.Chmod.FileName} set to {e.Chmod.FilePermissions}");
                }
                else
                {
                    LogDebug($"Setting permissions of {e.Chmod.FileName} failed: {e.Chmod.Error}");
                }
            }
            else
            {
                LogDebug($"Permissions of {e.Destination} kept with their defaults");
            }

            if (e.Touch != null)
            {
                if (e.Touch.Error == null)
                {
                    LogDebug($"Timestamp of {e.Touch.FileName} set to {e.Touch.LastWriteTime}");
                }
                else
                {
                    LogDebug($"Setting timestamp of {e.Touch.FileName} failed: {e.Touch.Error}");
                }
            }
            else
            {
                // This should never happen during "local to remote" synchronization
                LogDebug($"Timestamp of {e.Destination} kept with its default (current time)");
            }

            if (e.Removal != null)
            {
                if (e.Removal.Error == null)
                {
                    LogDebug($"Removed of {e.Removal.FileName} succeeded.");
                }
                else
                {
                    LogDebug($"Removed of {e.Removal.FileName} failed: {e.Removal.Error}.");
                }
            }
        }

        private static void Main(string[] args)
        {
            // Log4Net
            var assembly = Assembly.GetEntryAssembly();
            var logRepository = LogManager.GetRepository(assembly);
            XmlConfigurator.ConfigureAndWatch(logRepository, new FileInfo(Path.Combine(RootDirectory, "log4net-config.xml")));

            Log = LogManager.GetLogger(assembly, "Global logger");

            ErrorHandler = new ErrorHandler(Log);

            try
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

                    LogDebug($"Sync watching: {Path.GetFullPath(settings.LocalPath)}");

                    Thread.Sleep(-1);
                }
            }
            catch (Exception e)
            {
                ErrorHandler.HandleError(e);
                throw;
            }
        }

        private static void LogDebug(string message)
        {
            Log.Debug(message);
            Console.WriteLine(message);
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
                LogDebug($"Cannot find `{SettingsFileName}` in `{Environment.CurrentDirectory}`.");
                LogDebug("Run `sync-watch -c` to create a new one.");

                Environment.Exit(0);
            }

            return settings;
        }

        private static void TriggerSync(Session session, SyncSettings settings)
        {
            try
            {
                var result = session.SynchronizeDirectories(mode: SynchronizationMode.Remote,
                    localPath: Path.Combine(Environment.CurrentDirectory, settings.LocalPath), remotePath: settings.RemotePath,
                    removeFiles: true, mirror: true, criteria: SynchronizationCriteria.Time, options: new TransferOptions
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
                ErrorHandler.HandleError(e);
                LogDebug($"Error: {e}");
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

    public class ErrorHandler
    {
        public ErrorHandler(ILog log)
        {
            this.Log = log;
        }

        private ILog Log { get; }

        public void HandleError(Exception ex)
        {
            try
            {
                this.Log.Error($"Exception was handled. (ExceptionMessage: {ex.Message}, ExceptionName: {ex.GetType().Name})");
            }
            catch (Exception exception)
            {
                this.Log.Error("\r\n\r\n" + "Exception occured while handling an exception.\r\n\r\n" + $"Original exception: {ex}\r\n\r\n"
                               + $"Error handler exception: {exception}");
            }
        }
    }
}