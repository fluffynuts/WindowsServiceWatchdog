namespace WindowsServiceWatchdog
{
    public class ServiceWrapperConfig
    {
        public string Name { get; }
        public int[] BackoffSeconds { get; }
        public int ResetAfterSeconds { get; }

        public ServiceWrapperConfig(
            string name,
            int[] backoffSeconds,
            int resetAfterSeconds)
        {
            Name = name;
            BackoffSeconds = backoffSeconds;
            ResetAfterSeconds = resetAfterSeconds;
        }
    }
}