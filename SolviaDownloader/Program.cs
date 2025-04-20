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

        static async Task<int> Main(string[] args)
        {
            var stopwatch = new Stopwatch();
            stopwatch.Start();

            SetupLogging();

            LogInfo("SolviaDownloader gestartet.");
            LogInfo($"Hostname: {Environment.MachineName}");
            LogInfo($"Benutzername: {Environment.UserName}");
            LogInfo($"Startzeit: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

            LogInfo("\u00dcbergebene Argumente:");
            foreach (var arg in args)
            {
                LogInfo($"  ARG: {arg}");
            }

            var parsedArgs = ParseArguments(args);

            if (!parsedArgs.ContainsKey("url") || !parsedArgs.ContainsKey("saveto"))
            {
                LogError("Ungueltige Parameter. Erwartet: -url <DownloadUrl> -saveto <SaveToBasePath>");
                WriteJobResult(false, "Ungueltige Parameter.", null, 0, 0, null, 0);
                return 1;
            }

            string sourceUrl = parsedArgs["url"];
            string baseSaveToPath = parsedArgs["saveto"];

            try
            {
                LogInfo($"Starte Download von URL: {sourceUrl}");

                var uri = new Uri(sourceUrl);
                var relativePath = uri.AbsolutePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
                var destinationPath = Path.Combine(baseSaveToPath, relativePath);
                var destinationDirectory = Path.GetDirectoryName(destinationPath);

                LogInfo($"Zielverzeichnis: {destinationDirectory}");
                LogInfo($"Zieldatei: {destinationPath}");

                if (!Directory.Exists(destinationDirectory))
                {
                    Directory.CreateDirectory(destinationDirectory);
                    LogInfo("Zielverzeichnis erstellt.");
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
                        LogInfo($"Verbindung erfolgreich. Content-Length: {response.Content.Headers.ContentLength ?? -1} Bytes");

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

                Console.WriteLine("\nDownload abgeschlossen!");

                LogInfo($"Download abgeschlossen: Datei '{destinationPath}' erfolgreich geladen.");
                LogInfo($"Dateigroesse: {fileSize} Bytes");
                LogInfo($"Dauer: {stopwatch.Elapsed.TotalSeconds:F2} Sekunden");
                LogInfo($"Durchschnittliche Geschwindigkeit: {avgSpeedInMBps:F2} MB/s");

                WriteJobResult(true, "", destinationPath, fileSize, stopwatch.Elapsed.TotalSeconds, destinationDirectory, avgSpeedInMBps);

                LogInfo("SolviaDownloader erfolgreich abgeschlossen.");
                return 0;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                downloadStopwatch.Stop();
                LogError($"Fehler beim Download: {ex.Message}");
                WriteJobResult(false, ex.Message, null, 0, stopwatch.Elapsed.TotalSeconds, null, 0);
                LogError("SolviaDownloader mit Fehler beendet.");
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
                Console.Write($"\rDownloadfortschritt: {percentage}% ({receivedMB} MB von {totalMB} MB) - Speed: {speedInMBs:F2} MB/s");

                if (forceLog || (DateTime.Now - lastProgressLogTime).TotalSeconds >= 5)
                {
                    LogInfo($"Downloadfortschritt: {percentage}% ({receivedMB} MB von {totalMB} MB) - Speed: {speedInMBs:F2} MB/s");
                    lastProgressLogTime = DateTime.Now;
                }
            }
            else
            {
                Console.Write($"\rDownloadfortschritt: {receivedMB} MB - Speed: {speedInMBs:F2} MB/s");

                if (forceLog || (DateTime.Now - lastProgressLogTime).TotalSeconds >= 5)
                {
                    LogInfo($"Downloadfortschritt: {receivedMB} MB - Speed: {speedInMBs:F2} MB/s");
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

            File.AppendAllText(logFilePath, $"=== SolviaDownloader Log gestartet am {DateTime.Now:yyyy-MM-dd HH:mm:ss} ==={Environment.NewLine}");
        }

        private static void LogInfo(string message)
        {
            string logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - INFO  - {message}";
            Console.WriteLine(logEntry);
            try
            {
                using (var stream = new FileStream(logFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                using (var writer = new StreamWriter(stream))
                {
                    writer.WriteLine(logEntry);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Fehler beim Schreiben ins Logfile: {ex.Message}");
            }
        }

        private static void LogError(string message)
        {
            string logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - ERROR - {message}";
            Console.WriteLine(logEntry);
            try
            {
                using (var stream = new FileStream(logFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                using (var writer = new StreamWriter(stream))
                {
                    writer.WriteLine(logEntry);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Fehler beim Schreiben ins Logfile: {ex.Message}");
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
                LogError($"Fehler beim Schreiben von JobResult.json: {ex.Message}");
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
