using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using log4net;
using PeanutButter.WindowsServiceManagement;

namespace WindowsServiceWatchdog
{
    [DebuggerDisplay("{" + nameof(Name) + "}")]
    public class ServiceWrapper
    {
        private readonly IDateTimeProvider _dateTimeProvider;
        private readonly ILog _logger;

        // ReSharper disable once MemberCanBePrivate.Global
        public string Name { get; }
        private readonly int[] _backoff;
        private readonly int _resetTime;
        private readonly WindowsServiceUtil _util;
        private Queue<int> _backoffQueue;
        private DateTime? _restartAt;

        public ServiceWrapper(
            ServiceWrapperConfig config,
            IDateTimeProvider dateTimeProvider,
            ILog logger
        )
        {
            Validate(config);

            _dateTimeProvider = dateTimeProvider;
            _logger = logger;
            _backoff = config.BackoffSeconds;
            _resetTime = config.ResetAfterSeconds;
            Name = config.Name;
            _util = new WindowsServiceUtil(config.Name);
        }

        private void Validate(ServiceWrapperConfig config)
        {
            if (!config.BackoffSeconds.Any())
            {
                LogFatal(BackoffConfigError);
                throw new ArgumentException(BackoffConfigError, nameof(config));
            }

            if (config.ResetAfterSeconds >= 1)
            {
                return;
            }

            {
                LogFatal(ResetSecondsError);
                throw new ArgumentException(ResetSecondsError, nameof(config));
            }
        }
        
        private const string ResetSecondsError = "Reset seconds cannot be < 1";
        private const string BackoffConfigError = "Backoff config contains no items";

        private DateTime Now => _dateTimeProvider.Now;

        private bool CurrentlyRunning =>
            _util.State == ServiceState.Running;

        private bool CurrentlyStarting =>
            _util.State == ServiceState.StartPending;
        
        private bool NotFound =>
            _util.State == ServiceState.NotFound;

        public void CheckState()
        {
            try
            {
                if (NotFound)
                {
                    LogError($"{Name} is not a known service", null);
                    return;
                }

                if (CurrentlyStarting)
                {
                    // come back and check again
                    LogInfo($"{Name} is currently starting");
                    return;
                }

                if (CurrentlyRunning)
                {
                    // may have been started by something else
                    ResetNextRestart();
                    ResetBackOffQueueIfServiceRunningPastResetTime();
                    return;
                }

                if (RestartedService())
                {
                    return;
                }

                if (RestartScheduled())
                {
                    return;
                }

                ScheduleRestart();
            }
            catch (Exception ex)
            {
                LogError($"Exception whilst attempting to check state of {Name}", ex);
            }
        }

        private bool RestartScheduled()
        {
            return _restartAt.HasValue;
        }

        private void LogFatal(string error)
        {
            _logger.Fatal(error);
        }

        private void LogInfo(string message)
        {
            _logger.Info(message);
        }

        private void LogWarn(string message)
        {
            _logger.Warn(message);
        }

        private void LogError(string error, Exception ex)
        {
            _logger.Error(error, ex);
        }

        private void ResetBackOffQueueIfServiceRunningPastResetTime()
        {
            var serviceRunTime = TryFetchServiceRunTime();
            if (serviceRunTime == null)
            {
                return;
            }

            if (serviceRunTime < _resetTime)
            {
                return;
            }

            if (_backoffQueue == null)
            {
                return;
            }

            LogInfo($"Resetting backoff queue for {Name}");
            _backoffQueue = null;
        }

        private double? TryFetchServiceRunTime()
        {
            try
            {
                using (var process = Process.GetProcessById(_util.ServicePID))
                {
                    return (Now - process.StartTime).TotalSeconds;
                }
            }
            catch (Exception ex)
            {
                LogError($"Unable to query service process runtime for {Name}", ex);
                return null;
            }
        }

        private void ScheduleRestart()
        {
            if (_backoffQueue == null)
            {
                _backoffQueue = new Queue<int>(_backoff);
            }

            var currentBackoff = _backoffQueue.Dequeue();
            _restartAt = Now.AddSeconds(currentBackoff);
            LogInfo($"Scheduled restart of {Name} for {_restartAt}");
            if (!_backoffQueue.Any())
            {
                _backoffQueue.Enqueue(currentBackoff);
            }
        }

        private bool RestartedService()
        {
            if (!_restartAt.HasValue ||
                _restartAt.Value > Now)
            {
                return false;
            }

            LogWarn($"Attempting to start: {Name}");
            _util.Start(false);
            LogInfo($"Start signal sent for: {Name}");
            ResetNextRestart();
            return true;
        }

        private void ResetNextRestart()
        {
            _restartAt = null;
        }

        public bool ShouldBeRestarted()
        {
            return _restartAt.HasValue &&
                _restartAt.Value <= Now;
        }
    }
}