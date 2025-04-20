using SolviaDownloader.Models;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace SolviaDownloader
{
    internal class Downloader
    {
        private readonly DownloaderSettings settings;
        private readonly Stopwatch stopwatch = new Stopwatch();
        private readonly Stopwatch downloadStopwatch = new Stopwatch();
        private DateTime lastProgressLogTime = DateTime.MinValue;

        public Downloader(DownloaderSettings settings)
        {
            this.settings = settings;
        }

        public async Task<DownloadResult> DownloadAsync()
        {
            stopwatch.Start();

            var uri = new Uri(settings.Url);
            var relativePath = uri.AbsolutePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            var destinationPath = Path.Combine(settings.SaveToBasePath, relativePath);
            var destinationDirectory = Path.GetDirectoryName(destinationPath);

            Logger.LogInfo($"Destination directory: {destinationDirectory}");
            Logger.LogInfo($"Destination file: {destinationPath}");

            if (!Directory.Exists(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
                Logger.LogInfo("Destination directory created.");
            }

            downloadStopwatch.Start();

            try
            {
                var handler = new HttpClientHandler
                {
                    AllowAutoRedirect = true,
                    MaxConnectionsPerServer = 10
                };

                using (var httpClient = new HttpClient(handler))
                {
                    httpClient.Timeout = TimeSpan.FromMinutes(30);
                    using (var response = await httpClient.GetAsync(settings.Url, HttpCompletionOption.ResponseHeadersRead))
                    {
                        response.EnsureSuccessStatusCode();
                        Logger.LogInfo($"Connection successful. Content-Length: {response.Content.Headers.ContentLength ?? -1} Bytes");

                        var totalBytes = response.Content.Headers.ContentLength ?? -1L;

                        using (var contentStream = await response.Content.ReadAsStreamAsync())
                        using (var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, true))
                        {
                            await ProcessContentStream(totalBytes, contentStream, fileStream);
                        }
                    }
                }

                downloadStopwatch.Stop();
                stopwatch.Stop();

                var fileSize = new FileInfo(destinationPath).Length;
                double avgSpeedInMBps = fileSize / downloadStopwatch.Elapsed.TotalSeconds / (1024 * 1024);

                Logger.LogInfo($"Download completed: {destinationPath}, Size: {fileSize} Bytes, Speed: {avgSpeedInMBps:F2} MB/s");

                return new DownloadResult(true, "", destinationPath, fileSize, stopwatch.Elapsed.TotalSeconds, destinationDirectory, avgSpeedInMBps);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error while downloading: {ex.Message}");
                downloadStopwatch.Stop();
                stopwatch.Stop();
                return new DownloadResult(false, ex.Message, null, 0, stopwatch.Elapsed.TotalSeconds, null, 0);
            }
        }

        private async Task ProcessContentStream(long totalBytes, Stream contentStream, FileStream fileStream)
        {
            var buffer = new byte[1024 * 1024];
            long totalRead = 0;
            int read;
            bool downloadCompleted = false;

            var progressLogger = Task.Run(async () =>
            {
                while (!downloadCompleted)
                {
                    UpdateProgress(totalRead, totalBytes, forceLog: true);
                    await Task.Delay(5000);
                }
            });

            try
            {
                do
                {
                    read = await contentStream.ReadAsync(buffer, 0, buffer.Length);
                    if (read > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, read);
                        totalRead += read;
                        UpdateProgress(totalRead, totalBytes, forceLog: false);
                    }
                } while (read > 0);
            }
            finally
            {
                downloadCompleted = true;
                await progressLogger;
            }
        }

        private void UpdateProgress(long bytesReceived, long totalBytes, bool forceLog)
        {
            double elapsedSeconds = downloadStopwatch.Elapsed.TotalSeconds;
            double speedInMBs = elapsedSeconds > 0 ? (bytesReceived / elapsedSeconds) / (1024 * 1024) : 0;

            var receivedMB = bytesReceived / (1024 * 1024);
            var totalMB = totalBytes > 0 ? totalBytes / (1024 * 1024) : 0;

            if (forceLog || (DateTime.Now - lastProgressLogTime).TotalSeconds >= 5)
            {
                Logger.LogInfo($"Progress: {receivedMB}/{totalMB} MB ({speedInMBs:F2} MB/s)");
                lastProgressLogTime = DateTime.Now;
            }

            if (totalBytes > 0)
            {
                int percentage = (int)(bytesReceived * 100 / totalBytes);
                Console.Write($"\rProgress: {percentage}% ({receivedMB} MB / {totalMB} MB) - {speedInMBs:F2} MB/s");
            }
            else
            {
                Console.Write($"\rProgress: {receivedMB} MB - {speedInMBs:F2} MB/s");
            }
        }
    }
}
