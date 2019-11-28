using System;
using log4net;
using log4net.Appender;
using log4net.Config;
using log4net.Core;
using log4net.Layout;
using log4net.Repository.Hierarchy;
using PeanutButter.ServiceShell;

namespace WindowsServiceWatchdog
{
    public class Program
    {
        public static void Main(string[] args)
        {
            XmlConfigurator.Configure();
            ConfigureConsoleAppender();
            LogInfo("Starting up");
            Shell.RunMain<WatcherService>(args);
        }

        private static void LogInfo(string message)
        {
            LogManager.GetLogger(typeof(Program)).Info(message);
        }

        private static void ConfigureConsoleAppender()
        {
            var repository = LogManager.GetRepository() as Hierarchy;
            var root = repository.Root;
            var consoleAppender = new ConsoleAppender()
            {
                Layout = new PatternLayout("%date %-5level: %message%newline"),
                Target = "Console.Out",
                Threshold = Level.Debug
            };
            root.AddAppender(consoleAppender);
            root.Level = Level.Info;
            repository.RaiseConfigurationChanged(EventArgs.Empty);
        }
    }
}