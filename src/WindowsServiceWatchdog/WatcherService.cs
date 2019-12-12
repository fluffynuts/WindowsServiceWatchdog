using System;
using System.IO;
using System.Linq;
using System.Reflection;
using log4net;
using log4net.Appender;
using log4net.Core;
using log4net.Layout;
using log4net.Repository.Hierarchy;
using PeanutButter.INIFile;
using PeanutButter.ServiceShell;
// ReSharper disable MemberCanBePrivate.Global

namespace WindowsServiceWatchdog
{
    public class WatcherService : Shell
    {
        public const int DEFAULT_RESET_SECONDS = 30;
        public const string DEFAULT_BACKOFF = "5, 10, 15, 30";
        public const int DEFAULT_POLL_INTERVAL = 1;

        public const string CONFIG_FILE = "config.ini";
        public const string LOG_FILE = "watchdog.log";
        
        private DateTime _configLastLoaded = DateTime.MinValue;
        private DateTime _nextPoll = DateTime.MinValue;

        private IINIFile _config;
        private ServiceWrapper[] _services;
        private readonly ILog _logger;
        private readonly IDateTimeProvider _dateTimeProvider;
        private DateTime Now => _dateTimeProvider.Now;

        public WatcherService() : this(
            LogManager.GetLogger(typeof(WatcherService)),
            new DateTimeProvider()
        )
        {
        }

        public WatcherService(
            ILog logger,
            IDateTimeProvider dateTimeProvider)
        {
            ServiceName = "WindowsServiceWatchdog";
            DisplayName = "Windows Service Watchdog";
            Interval = 1;
            _logger = logger;
            _dateTimeProvider = dateTimeProvider;
            HackDisableRepeatedLogConfig();
            ConfigureFileAppender();
        }

        private void HackDisableRepeatedLogConfig()
        {
            var field = typeof(Shell).GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault(pi => pi.Name == "_haveConfiguredLogging");
            field?.SetValue(this, true);
        }

        private void ConfigureFileAppender()
        {
            var repository = LogManager.GetRepository() as Hierarchy;
            if (repository == null)
            {
                throw new InvalidCastException("Can't get repository from LogManager");
            }
            var root = repository.Root;
            if (root.Appenders.ToArray().Any(a => a is RollingFileAppender))
            {
                return;
            }

            var appender = new RollingFileAppender()
            {
                Layout = new PatternLayout("%date %-5level: %message%newline"),
                File = MyLogFile,
                Threshold = Level.Info,
                AppendToFile = true,
                ImmediateFlush = true,
                RollingStyle = RollingFileAppender.RollingMode.Size,
                MaxSizeRollBackups = 5,
                SecurityContext = SecurityContextProvider.DefaultProvider.CreateSecurityContext(this)
            };
            root.AddAppender(appender);
            root.Level = Level.Info;
            repository.RaiseConfigurationChanged(EventArgs.Empty);
            
            LogInfo("Should go to file");
        }

        protected override void RunOnce()
        {
            ConfigureFileAppender();
            try
            {
                if (!ShouldCheckServices())
                {
                    return;
                }
            }
            catch (Exception ex)
            {
                LogError(
                    "Exception whilst attempting config load",
                    ex
                );
                return;
            }

            foreach (var service in _services)
            {
                service.CheckState();
            }

            SetNextPollTime();
        }

        private bool ShouldCheckServices()
        {
            var configChanged = ConfigChanged();
            if (configChanged)
            {
                LoadServicesFromConfig();
            }
            
            return configChanged 
                || TimeToRun()
                || AnyWatchedServicesHavePendingRestartInThePast();
        }

        private bool AnyWatchedServicesHavePendingRestartInThePast()
        {
            return _services.Any(s => s.ShouldBeRestarted());
        }

        private void LogError(
            string message,
            Exception ex)
        {
            _logger.Error(
                message,
                ex
            );
        }

        private void SetNextPollTime()
        {
            var pollInterval = ParsePollInterval();
            _nextPoll = Now.AddSeconds(pollInterval);
        }

        private int ParsePollInterval()
        {
            try
            {
                var configured = int.Parse(
                    _config[Sections.GENERAL][Settings.POLL]
                );
                if (configured < 1)
                {
                    throw new ArgumentOutOfRangeException(
                        $"Configured poll {configured} cannot be < 1"
                    );
                }

                return configured;
            }
            catch (Exception ex)
            {
                LogError("Error parsing poll interval", ex);
                return DEFAULT_POLL_INTERVAL;
            }
        }

        private void LoadServicesFromConfig()
        {
            var dateTimeProvider = new DateTimeProvider();
            var backoff = ParseBackOffFromConfig();
            var reset = ParseResetFromConfig();
            var section = _config.GetSection(Sections.SERVICES);
            var logger = LogManager.GetLogger(
                GetType()
            );

            _services = section.Keys.Select(
                k => new ServiceWrapper(
                    new ServiceWrapperConfig(
                        k,
                        backoff,
                        reset
                    ),
                    dateTimeProvider,
                    logger
                )
            ).ToArray();

            if (!_services.Any())
            {
                LogInfo($"No services are configured for watching; add one per line to the [{Sections.SERVICES}] section of {MyConfig}");
            }

            _nextPoll = DateTime.MinValue;
        }

        private int[] ParseBackOffFromConfig()
        {
            var stringValue = ReadBackoffSetting();
            var result = ParseNumbers(stringValue);
            return result.Any()
                ? result
                : GenerateDefaultBackoff();
        }

        private int[] ParseNumbers(string stringValue)
        {
            var result = stringValue.Split(new[] { ",", ";" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(IsInt)
                .Select(int.Parse)
                .Where(i => i >= 0)
                .ToArray();
            return result;
        }

        private int[] GenerateDefaultBackoff()
        {
            return ParseNumbers(DEFAULT_BACKOFF);
        }

        private bool IsInt(string arg)
        {
            return int.TryParse(arg, out var _);
        }

        private int ParseResetFromConfig()
        {
            try
            {
                return int.Parse(
                    _config[Sections.GENERAL][Settings.RESET]
                );
            }
            catch
            {
                return DEFAULT_RESET_SECONDS;
            }
        }

        private string ReadBackoffSetting()
        {
            try
            {
                return _config[Sections.GENERAL][Settings.BACKOFF];
            }
            catch
            {
                return DEFAULT_BACKOFF;
            }
        }

        private bool TimeToRun()
        {
            return _nextPoll < Now;
        }

        private bool ConfigChanged()
        {
            if (GeneratedFirstTimeConfig())
            {
                return true;
            }

            var fileInfo = new FileInfo(CONFIG_FILE);
            if (fileInfo.LastWriteTime <= _configLastLoaded)
            {
                return false;
            }

            LogInfo("Config changed; reloading.");
            LoadConfig();
            return true;
        }

        private void LoadConfig()
        {
            _config = new INIFile(MyConfig);
            _configLastLoaded = Now;
        }

        private static readonly string MyFolder = Path.GetDirectoryName(
            new Uri(Assembly.GetExecutingAssembly().Location).LocalPath
        );
        private static readonly string MyConfig = Path.Combine(MyFolder, CONFIG_FILE);
        private static readonly string MyLogFile = Path.Combine(MyFolder, LOG_FILE);

        private bool GeneratedFirstTimeConfig()
        {
            if (File.Exists(MyConfig))
            {
                return false;
            }

            var config = new INIFile();
            config.AddSection(Sections.GENERAL);
            var general = config.GetSection(Sections.GENERAL);
            general[Settings.POLL] = "1";
            general[Settings.BACKOFF] = "1,1,1,5,10";
            general[Settings.RESET] = "30";

            config.AddSection(Sections.SERVICES);
            config.Persist(MyConfig);
            _config = config;
            _configLastLoaded = Now;
            return true;
        }

        public static class Settings
        {
            public const string POLL = "poll";
            public const string BACKOFF = "backoff";
            public const string RESET = "reset";
        }

        public static class Sections
        {
            public const string GENERAL = "general";
            public const string SERVICES = "services";
        }
    }
}