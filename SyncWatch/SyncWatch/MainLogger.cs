namespace SyncWatch
{
    using System;
    using System.IO;
    using System.Reflection;
    using System.Threading.Tasks;

    using log4net;
    using log4net.Config;

    using SharpRaven;
    using SharpRaven.Data;

    public class MainLogger
    {
        public static readonly MainLogger Instance = new MainLogger();

        private const string LoggerFilePath = "log4net-config.xml";

        private static readonly object SyncLock = new object();

        private static bool IsInitialized;

        private static ILog Log4NetLogger;

        private static RavenClient RavenClient;

        public static void Initialize(Assembly assembly)
        {
            if (IsInitialized)
            {
                return;
            }

            lock (SyncLock)
            {
                if (IsInitialized)
                {
                    return;
                }

                // Configure log4net.
                var logRepository = LogManager.GetRepository(assembly);

                var configFile = new FileInfo(Path.Combine(Global.DataDirectory, LoggerFilePath));

                XmlConfigurator.ConfigureAndWatch(logRepository, configFile);

                Log4NetLogger = LogManager.GetLogger(assembly, "Global logger");

                // Configure Sentry.
                RavenClient = new RavenClient(Global.AppConfig.SentryDsn);

                IsInitialized = true;
            }
        }

        public Task LogError(Exception exception)
        {
            Log4NetLogger.Error(
                $"Exception was handled. (ExceptionMessage: {exception.Message}, ExceptionName: {exception.GetType().Name})");

            return RavenClient.CaptureAsync(new SentryEvent(exception)
            {
                Level = ErrorLevel.Error
            });
        }

        public void LogDebug(string message)
        {
            Log4NetLogger.Debug(message);
        }
    }
}