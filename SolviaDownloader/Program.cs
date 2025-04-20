using SolviaDownloader.Models;
using SolviaDownloader.Services;
using System;
using System.Threading.Tasks;

namespace SolviaDownloader
{
    internal class Program
    {
        static async Task<int> Main(string[] args)
        {
            Logger.Setup();

            Logger.LogInfo("SolviaDownloader started.");
            Logger.LogEnvironment();

            var settings = DownloaderSettings.Parse(args);
            if (settings == null)
            {
                Logger.LogError("Invalid arguments.");
                await JobResultWriter.WriteAsync(false, "Invalid arguments.", null, 0, 0, null, 0);
                return 1;
            }

            var downloader = new Downloader(settings);

            try
            {
                var result = await downloader.DownloadAsync();
                await JobResultWriter.WriteAsync(result.Success, result.ErrorMessage, result.DownloadedFile, result.FileSize, result.DurationSeconds, result.DestinationDirectory, result.AverageSpeedMBps);
                Logger.LogInfo("SolviaDownloader completed successfully.");
                return result.Success ? 0 : 2;
            }
            catch (Exception ex)
            {
                Logger.LogError($"Fatal error: {ex}");
                return 2;
            }
        }
    }
}
