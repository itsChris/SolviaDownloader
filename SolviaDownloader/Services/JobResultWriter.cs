using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;

namespace SolviaDownloader.Services
{
    internal static class JobResultWriter
    {
        public static async Task WriteAsync(bool success, string errorMessage, string downloadedFile, long fileSize, double durationSeconds, string destinationDirectory, double avgSpeedMBps)
        {
            try
            {
                if (string.IsNullOrEmpty(destinationDirectory))
                {
                    destinationDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SolviaDownloader");
                    if (!Directory.Exists(destinationDirectory))
                    {
                        Directory.CreateDirectory(destinationDirectory);
                    }
                }

                var resultFile = Path.Combine(destinationDirectory, "JobResult.json");

                string json = "{\n" +
                              $"  \"Success\": {success.ToString().ToLower()},\n" +
                              $"  \"ErrorMessage\": \"{EscapeJsonString(errorMessage)}\",\n" +
                              $"  \"DownloadedFile\": \"{EscapeJsonString(downloadedFile)}\",\n" +
                              $"  \"DownloadedSizeBytes\": {fileSize},\n" +
                              $"  \"DurationSeconds\": {durationSeconds.ToString(CultureInfo.InvariantCulture)},\n" +
                              $"  \"AverageSpeedMBps\": {avgSpeedMBps.ToString("F2", CultureInfo.InvariantCulture)}\n" +
                              "}";

                File.WriteAllText(resultFile, json);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error writing JobResult.json: {ex.Message}");
            }
        }

        private static string EscapeJsonString(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            return value.Replace("\\", "\\\\")
                        .Replace("\"", "\\\"")
                        .Replace("\n", "\\n")
                        .Replace("\r", "\\r")
                        .Replace("\t", "\\t");
        }
    }
}
