using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using log4net;
using log4net.Appender;
using log4net.Config;
using log4net.Core;
using log4net.Layout;
using log4net.Repository.Hierarchy;
using PeanutButter.ServiceShell;

[assembly: AssemblyVersion("1.0.1")]
[assembly: AssemblyFileVersion("1.0.1")]
namespace WindowsServiceWatchdog
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var argsList = new List<string>(args);
            var versionFlag = argsList.Remove("--version");
            if (versionFlag)
            {
                Console.WriteLine($"Windows Service Watchdog\nversion {GetVersion()}");
                return;
            }

            XmlConfigurator.Configure();
            ConfigureConsoleAppender();
            LogInfo("Starting up");
            Shell.RunMain<WatcherService>(argsList.ToArray());
        }

        private static string GetVersion()
        {
            var asm = Assembly.GetExecutingAssembly();
            var fileVersionInfo = FileVersionInfo.GetVersionInfo(
                asm.Location
            );
            return fileVersionInfo.FileVersion;
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