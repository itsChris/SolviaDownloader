using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Globalization;
using System.Collections.Generic;

namespace SolviaDownloader
{
    internal class Program
    {
        private static Stopwatch downloadStopwatch = new Stopwatch();
        private static string logDirectory = "";
        private static string logFilePath = "";
        private static DateTime lastProgressLogTime = DateTime.MinValue;

        private static readonly Queue<string> logQueue = new Queue<string>();
        private static readonly object logLock = new object();
        private static bool isLogging = false;


        static async Task<int> Main(string[] args)
        {
            var stopwatch = new Stopwatch();
            stopwatch.Start();

            SetupLogging();

            LogInfo("SolviaDownloader started.");
            LogInfo($"Hostname: {Environment.MachineName}");
            LogInfo($"Username: {Environment.UserName}");
            LogInfo($"Start time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

            LogInfo("Provided arguments:");
            foreach (var arg in args)
            {
                LogInfo($"  ARG: {arg}");
            }

            var parsedArgs = ParseArguments(args);

            if (!parsedArgs.ContainsKey("url") || !parsedArgs.ContainsKey("saveto"))
            {
                LogError("Invalid arguments. Expected: -url <DownloadUrl> -saveto <SaveToBasePath>");
                WriteJobResult(false, "Invalid argument.", null, 0, 0, null, 0);
                return 1;
            }

            string sourceUrl = parsedArgs["url"];
            string baseSaveToPath = parsedArgs["saveto"];

            try
            {
                LogInfo($"Starting download from URL: {sourceUrl}");

                var uri = new Uri(sourceUrl);
                var relativePath = uri.AbsolutePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
                var destinationPath = Path.Combine(baseSaveToPath, relativePath);
                var destinationDirectory = Path.GetDirectoryName(destinationPath);

                LogInfo($"Destination directory: {destinationDirectory}");
                LogInfo($"Destination file: {destinationPath}");

                if (!Directory.Exists(destinationDirectory))
                {
                    Directory.CreateDirectory(destinationDirectory);
                    LogInfo("Destination directory created.");
                }

                downloadStopwatch.Start();

                var handler = new HttpClientHandler
                {
                    AllowAutoRedirect = true,
                    MaxConnectionsPerServer = 10
                };

                using (var httpClient = new HttpClient(handler))
                {
                    httpClient.Timeout = TimeSpan.FromMinutes(30);

                    using (var response = await httpClient.GetAsync(sourceUrl, HttpCompletionOption.ResponseHeadersRead))
                    {
                        response.EnsureSuccessStatusCode();
                        LogInfo($"Connection successful. Content-Length: {response.Content.Headers.ContentLength ?? -1} Bytes");

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

                Console.WriteLine("\nDownload completed!");

                LogInfo($"Download completed: File '{destinationPath}' successfully downloaded.");
                LogInfo($"File size: {fileSize} Bytes");
                LogInfo($"Time taken: {stopwatch.Elapsed.TotalSeconds:F2} seconds");
                LogInfo($"Average speed: {avgSpeedInMBps:F2} MB/s");

                WriteJobResult(true, "", destinationPath, fileSize, stopwatch.Elapsed.TotalSeconds, destinationDirectory, avgSpeedInMBps);

                LogInfo("SolviaDownloader completed successfully.");
                return 0;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                downloadStopwatch.Stop();
                LogError($"Error while downloading: {ex.Message}");
                WriteJobResult(false, ex.Message, null, 0, stopwatch.Elapsed.TotalSeconds, null, 0);
                LogError("SolviaDownloader exited with errors -> will return <> 0 return code.");
                return 2;
            }
        }

        private static async Task ProcessContentStream(long totalBytes, Stream contentStream, FileStream fileStream)
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
                }
                while (read > 0);
            }
            finally
            {
                downloadCompleted = true;
                await progressLogger;
            }
        }

        private static void UpdateProgress(long bytesReceived, long totalBytes, bool forceLog)
        {
            double elapsedSeconds = downloadStopwatch.Elapsed.TotalSeconds;
            double speedInMBs = elapsedSeconds > 0 ? (bytesReceived / elapsedSeconds) / (1024 * 1024) : 0;

            var receivedMB = bytesReceived / (1024 * 1024);
            var totalMB = totalBytes > 0 ? totalBytes / (1024 * 1024) : 0;

            if (totalBytes > 0)
            {
                int percentage = (int)(bytesReceived * 100 / totalBytes);
                Console.Write($"\rDownload progress: {percentage}% ({receivedMB} MB von {totalMB} MB) - Speed: {speedInMBs:F2} MB/s");

                if (forceLog || (DateTime.Now - lastProgressLogTime).TotalSeconds >= 5)
                {
                    LogInfo($"Download progress: {percentage}% ({receivedMB} MB von {totalMB} MB) - Speed: {speedInMBs:F2} MB/s");
                    lastProgressLogTime = DateTime.Now;
                }
            }
            else
            {
                Console.Write($"\rDownload progress: {receivedMB} MB - Speed: {speedInMBs:F2} MB/s");

                if (forceLog || (DateTime.Now - lastProgressLogTime).TotalSeconds >= 5)
                {
                    LogInfo($"Download progress: {receivedMB} MB - Speed: {speedInMBs:F2} MB/s");
                    lastProgressLogTime = DateTime.Now;
                }
            }
        }

        private static Dictionary<string, string> ParseArguments(string[] args)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i].StartsWith("-"))
                {
                    var key = args[i].TrimStart('-');
                    var value = args[i + 1];

                    if (!value.StartsWith("-"))
                    {
                        result[key] = value;
                        i++;
                    }
                }
            }

            return result;
        }

        private static void SetupLogging()
        {
            string exePath = AppDomain.CurrentDomain.BaseDirectory;
            logDirectory = Path.Combine(exePath, "Logs");

            if (!Directory.Exists(logDirectory))
            {
                Directory.CreateDirectory(logDirectory);
            }

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            logFilePath = Path.Combine(logDirectory, $"SolviaDownloaderLog_{timestamp}.txt");

            LogInfo("Logging initialized.");
        }

        private static void LogInfo(string message)
        {
            string logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - INFO  - {message}";
            Console.WriteLine(logEntry);
            EnqueueLog(logEntry);
        }

        private static void LogError(string message)
        {
            string logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - ERROR - {message}";
            Console.WriteLine(logEntry);
            EnqueueLog(logEntry);
        }

        private static void EnqueueLog(string message)
        {
            lock (logLock)
            {
                logQueue.Enqueue(message);
                if (!isLogging)
                {
                    isLogging = true;
                    Task.Run(ProcessLogQueue);
                }
            }
        }

        private static async Task ProcessLogQueue()
        {
            while (true)
            {
                string message = null;

                lock (logLock)
                {
                    if (logQueue.Count > 0)
                    {
                        message = logQueue.Dequeue();
                    }
                    else
                    {
                        isLogging = false;
                        break;
                    }
                }

                if (message != null)
                {
                    try
                    {
                        using (var stream = new FileStream(logFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                        using (var writer = new StreamWriter(stream))
                        {
                            await writer.WriteLineAsync(message);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error writing to log file: {ex.Message}");
                    }
                }
            }
        }


        private static void WriteJobResult(bool success, string errorMessage, string downloadedFile, long fileSize, double durationSeconds, string destinationDirectory, double avgSpeedMBps)
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
                LogError($"Error when trying to write JobResult.json: {ex.Message}");
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
