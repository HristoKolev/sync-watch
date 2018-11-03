namespace SyncWatch
{
    using System;
    using System.IO;
    using System.Reactive;
    using System.Reactive.Linq;

    using Newtonsoft.Json;

    using WinSCP;

    public class SyncSession : IDisposable
    {
        public SyncSession(SyncSettings syncSettings)
        {
            this.SyncSettings = syncSettings;

            this.SessionOptions = new SessionOptions
            {
                Protocol = Protocol.Sftp,
                HostName = this.SyncSettings.HostName,
                UserName = this.SyncSettings.UserName,
                SshHostKeyFingerprint = this.SyncSettings.SshHostKeyFingerprint,
                GiveUpSecurityAndAcceptAnySshHostKey = this.SyncSettings.SshHostKeyFingerprint == null,
            };

            this.Session = new Session
            {
                ReconnectTimeInMilliseconds = 1000
            };

            this.Session.FileTransferred += this.FileTransferred;

            this.FsWatcher = new FileSystemWatcher
            {
                Path = this.SyncSettings.LocalPath,
                NotifyFilter = NotifyFilters.LastAccess | NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName,
                Filter = "*",
                IncludeSubdirectories = true
            };
        }

        private bool IsRunning { get; set; }

        private IObservable<EventPattern<FileSystemEventArgs>> FsObservable =>
            Observable.Merge(
                Observable.FromEventPattern<FileSystemEventHandler, FileSystemEventArgs>(
                    action => this.FsWatcher.Changed += action, action => this.FsWatcher.Changed -= action),
                Observable.FromEventPattern<FileSystemEventHandler, FileSystemEventArgs>(
                    action => this.FsWatcher.Created += action, action => this.FsWatcher.Created -= action),
                Observable.FromEventPattern<FileSystemEventHandler, FileSystemEventArgs>(
                    action => this.FsWatcher.Deleted += action, action => this.FsWatcher.Deleted -= action),
                Observable.FromEventPattern<RenamedEventHandler, FileSystemEventArgs>(
                    action => this.FsWatcher.Renamed += action, action => this.FsWatcher.Renamed -= action));

        private FileSystemWatcher FsWatcher { get; }

        private Session Session { get; }

        private SessionOptions SessionOptions { get; }

        public SyncSettings SyncSettings { get; }

        private IDisposable Subscription { get; set; }

        public void Dispose()
        {
            this.FsWatcher.Dispose();

            if (this.IsRunning)
            {
                this.Stop();
            }

            this.Session.Dispose();
        }

        public void Start()
        {
            MainLogger.Instance.LogDebug($"Opening a connection to `{this.SyncSettings.HostName}:{this.SyncSettings.RemotePath}` ...");
            this.Session.Open(this.SessionOptions);

            MainLogger.Instance.LogDebug($"Creating the remote path `{this.SyncSettings.RemotePath}` if necessary...");
            this.Session.ExecuteCommand($"mkdir -p {this.SyncSettings.RemotePath}");

            MainLogger.Instance.LogDebug($"Initiating first sync to {this.SyncSettings.RemotePath}");
            this.SyncFiles();
            MainLogger.Instance.LogDebug($"First sync finished to {this.SyncSettings.RemotePath}");

            this.Subscription = this.FsObservable
                                    .Throttle(TimeSpan.FromSeconds(1))
                                    .Subscribe(_ =>
                                    {
                                        MainLogger.Instance.LogDebug($"Starting sync to {this.SyncSettings.RemotePath}");
                                        this.SyncFiles();
                                        MainLogger.Instance.LogDebug($"Sync completed to {this.SyncSettings.RemotePath}");
                                    });

            this.FsWatcher.EnableRaisingEvents = true;

            MainLogger.Instance.LogDebug($"Sync watching: {this.SyncSettings.LocalPath}");

            this.IsRunning = true;
        }

        public void Stop()
        {
            this.FsWatcher.EnableRaisingEvents = false;
            this.Subscription.Dispose();

            this.Session.FileTransferred -= this.FileTransferred;
            this.Session.Close();

            this.IsRunning = false;
        }

        private void FileTransferred(object sender, TransferEventArgs e)
        {
            if (e.Error == null)
            {
                this.LogTransfer($"Upload of {e.FileName} succeeded");
            }
            else
            {
                this.LogTransfer($"Upload of {e.FileName} failed: {e.Error}");
            }

            if (e.Chmod != null)
            {
                if (e.Chmod.Error == null)
                {
                    this.LogTransfer($"Permissions of {e.Chmod.FileName} set to {e.Chmod.FilePermissions}");
                }
                else
                {
                    this.LogTransfer($"Setting permissions of {e.Chmod.FileName} failed: {e.Chmod.Error}");
                }
            }
            else
            {
                this.LogTransfer($"Permissions of {e.Destination} kept with their defaults");
            }

            if (e.Touch != null)
            {
                if (e.Touch.Error == null)
                {
                    this.LogTransfer($"Timestamp of {e.Touch.FileName} set to {e.Touch.LastWriteTime}");
                }
                else
                {
                    this.LogTransfer($"Setting timestamp of {e.Touch.FileName} failed: {e.Touch.Error}");
                }
            }
            else
            {
                // This should never happen during "local to remote" synchronization
                this.LogTransfer($"Timestamp of {e.Destination} kept with its default (current time)");
            }

            if (e.Removal != null)
            {
                if (e.Removal.Error == null)
                {
                    this.LogTransfer($"Removed of {e.Removal.FileName} succeeded.");
                }
                else
                {
                    this.LogTransfer($"Removed of {e.Removal.FileName} failed: {e.Removal.Error}.");
                }
            }
        }

        private void LogTransfer(string message)
        {
            MainLogger.Instance.LogDebug($"{this.SyncSettings.HostName}:{this.SyncSettings.RemotePath} | {message}");
        }

        private void SyncFiles()
        {
            var result = this.Session.SynchronizeDirectories(
                mode: SynchronizationMode.Remote,
                localPath: this.SyncSettings.LocalPath,
                remotePath: this.SyncSettings.RemotePath,
                removeFiles: true,
                mirror: true,
                criteria: SynchronizationCriteria.Time,
                options: new TransferOptions
                {
                    //551 gives all rights. It was 777 before, but as it turns out, this does not use the same encoding as the chmod command.
                    FilePermissions = new FilePermissions(511),
                    OverwriteMode = OverwriteMode.Overwrite,
                    TransferMode = TransferMode.Automatic,
                    FileMask = this.SyncSettings.FileMask,
                });

            foreach (SessionRemoteException failure in result.Failures)
            {
                MainLogger.Instance.LogError(failure).GetAwaiter().GetResult();
            }
        }

        public static SyncSettings ReadSettings(string path, Func<string, string> resolveLocalPath)
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

            settings.LocalPath = resolveLocalPath(settings.LocalPath);

            if (!Path.IsPathRooted(settings.LocalPath))
            {
                MainLogger.Instance.LogDebug($"The path is not relative `{path}`.");
                return null;
            }

            if (string.IsNullOrWhiteSpace(settings.SshHostKeyFingerprint) || settings.SshHostKeyFingerprint == Global.SshHostKeyFingerprintMessage)
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
}