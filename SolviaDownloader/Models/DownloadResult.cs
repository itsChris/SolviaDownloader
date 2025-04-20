namespace SolviaDownloader.Models
{
    internal class DownloadResult
    {
        public bool Success { get; }
        public string ErrorMessage { get; }
        public string DownloadedFile { get; }
        public long FileSize { get; }
        public double DurationSeconds { get; }
        public string DestinationDirectory { get; }
        public double AverageSpeedMBps { get; }

        public DownloadResult(bool success, string errorMessage, string downloadedFile, long fileSize, double durationSeconds, string destinationDirectory, double avgSpeedMBps)
        {
            Success = success;
            ErrorMessage = errorMessage;
            DownloadedFile = downloadedFile;
            FileSize = fileSize;
            DurationSeconds = durationSeconds;
            DestinationDirectory = destinationDirectory;
            AverageSpeedMBps = avgSpeedMBps;
        }
    }
}
