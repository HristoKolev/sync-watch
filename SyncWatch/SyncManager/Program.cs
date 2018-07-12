namespace SyncManager
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;

    using CommandLine;

    using Newtonsoft.Json;

    public class Program
    {
        private static void Main(string[] args)
        {
            var appSettings = JsonConvert.DeserializeObject<AppSettings>(File.ReadAllText("settings.json"));

            var options = ReadCliOptions(args);

            string logsDirectory = Path.Combine(Environment.CurrentDirectory, "logs");

            if (!Directory.Exists(logsDirectory))
            {
                Directory.CreateDirectory(logsDirectory);
            }

            var processes = appSettings.Connections.Where(x => x.IsEnabled && (x.RunOnStartup || !options.Startup))
                                       .Select(connection => new Process
                                       {
                                           StartInfo =
                                           {
                                               FileName = @"c:\windows\system32\cmd.exe",
                                               Arguments = $"/k sync-watch > {Path.Combine(logsDirectory, connection.LogFilePath)}",
                                               WorkingDirectory = connection.Path,
                                               UseShellExecute = false,
                                               CreateNoWindow = true
                                           }
                                       })
                                       .ToList();

            foreach (var process in processes)
            {
                Console.WriteLine($"Starting watcher in '{process.StartInfo.WorkingDirectory}' ...");
                process.Start();
            }

            do
            {
                Console.WriteLine("Enter 'q' to exit.");
            }
            while (Console.ReadLine() != "q");

            foreach (var process in processes)
            {
                if (!process.HasExited)
                {
                    process.Kill();
                }
            }

            foreach (var process in Process.GetProcessesByName("sync-watch"))
            {
                if (!process.HasExited)
                {
                    process.Kill();
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
    }

    public class AppSettings
    {
        public List<Connection> Connections { get; set; } = new List<Connection>();
    }

    public class Connection
    {
        public bool IsEnabled { get; set; }

        public string LogFilePath { get; set; }

        public string Path { get; set; }

        public bool RunOnStartup { get; set; }
    }

    public class CliOptions
    {
        [Option('s', "startup", HelpText = "Signifies that the application is starting on OS boot.", Required = false)]
        public bool Startup { get; set; }
    }
}