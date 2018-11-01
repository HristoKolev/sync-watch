namespace SyncWatch
{
    using System;
    using System.IO;
    using System.Reactive;
    using System.Reactive.Linq;

    using WinSCP;

    public class SyncSession : IDisposable
    {
        public SyncSession(SyncSettings syncSettings)
        {
            this.SyncSettings = syncSettings;
            this.SetupInstance();
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

        private FileSystemWatcher FsWatcher { get; set; }

        private Session Session { get; set; }

        private SessionOptions SessionOptions { get; set; }

        private SyncSettings SyncSettings { get; }

        private IDisposable Subscription { get; set; }

        public void Dispose()
        {
            this.FsWatcher.Dispose();
            this.Session.Dispose();

            if (this.IsRunning)
            {
                this.Stop();
            }
        }

        public void Start()
        {
            this.Session.Open(this.SessionOptions);
            this.Session.ExecuteCommand($"mkdir -p {this.SyncSettings.RemotePath}");

            this.SyncFiles();

            this.Subscription = this.FsObservable.Subscribe(_ => this.SyncFiles());

            this.FsWatcher.EnableRaisingEvents = true;

            MainLogger.Instance.LogDebug($"Sync watching: {this.SyncSettings.LocalPath}");

            this.IsRunning = true;
        }

        public void Stop()
        {
            this.FsWatcher.EnableRaisingEvents = false;
            this.Subscription.Dispose();

            this.Session.FileTransferred -= FileTransferred;
            this.Session.Close();

            this.IsRunning = false;
        }

        private void SetupInstance()
        {
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

            this.Session.FileTransferred += FileTransferred;

            this.FsWatcher = new FileSystemWatcher
            {
                Path = this.SyncSettings.LocalPath,
                NotifyFilter = NotifyFilters.LastAccess | NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName,
                Filter = "*",
                IncludeSubdirectories = true
            };
        }

        private static void FileTransferred(object sender, TransferEventArgs e)
        {
            if (e.Error == null)
            {
                MainLogger.Instance.LogDebug($"Upload of {e.FileName} succeeded");
            }
            else
            {
                MainLogger.Instance.LogDebug($"Upload of {e.FileName} failed: {e.Error}");
            }

            if (e.Chmod != null)
            {
                if (e.Chmod.Error == null)
                {
                    MainLogger.Instance.LogDebug($"Permissions of {e.Chmod.FileName} set to {e.Chmod.FilePermissions}");
                }
                else
                {
                    MainLogger.Instance.LogDebug($"Setting permissions of {e.Chmod.FileName} failed: {e.Chmod.Error}");
                }
            }
            else
            {
                MainLogger.Instance.LogDebug($"Permissions of {e.Destination} kept with their defaults");
            }

            if (e.Touch != null)
            {
                if (e.Touch.Error == null)
                {
                    MainLogger.Instance.LogDebug($"Timestamp of {e.Touch.FileName} set to {e.Touch.LastWriteTime}");
                }
                else
                {
                    MainLogger.Instance.LogDebug($"Setting timestamp of {e.Touch.FileName} failed: {e.Touch.Error}");
                }
            }
            else
            {
                // This should never happen during "local to remote" synchronization
                MainLogger.Instance.LogDebug($"Timestamp of {e.Destination} kept with its default (current time)");
            }

            if (e.Removal != null)
            {
                if (e.Removal.Error == null)
                {
                    MainLogger.Instance.LogDebug($"Removed of {e.Removal.FileName} succeeded.");
                }
                else
                {
                    MainLogger.Instance.LogDebug($"Removed of {e.Removal.FileName} failed: {e.Removal.Error}.");
                }
            }
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
                MainLogger.Instance.LogError(failure);
            }
        }
    }
}