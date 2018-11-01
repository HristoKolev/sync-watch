namespace SyncWatch
{
    public static class Global
    {
        public static AppConfig AppConfig { get; set; }
    }

    public class AppConfig
    {
        public string SentryDsn { get; set; }
    }
}