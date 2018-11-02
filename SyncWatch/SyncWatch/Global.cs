namespace SyncWatch
{
    using System.IO;
    using System.Reflection;

    public static class Global
    {
        public const string SshHostKeyFingerprintMessage =
            "Put your the host key fingerprint here or leave this empty (except any key - unsecure)";

        private const string AppConfigFileName = "app-settings.json";

        public static AppConfig AppConfig { get; set; }

        public static string AppConfigLocation => Path.Combine(AssemblyDirectory, AppConfigFileName);

        public static string AssemblyDirectory => Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
    }

    public class AppConfig
    {
        public string SentryDsn { get; set; }
    }
}