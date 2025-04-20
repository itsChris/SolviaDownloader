using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace SolviaDownloader
{
    internal static class Logger
    {
        private static readonly Queue<string> logQueue = new Queue<string>();
        private static readonly object logLock = new object();
        private static bool isLogging = false;
        private static string logFilePath = "";

        public static void Setup()
        {
            string exePath = AppDomain.CurrentDomain.BaseDirectory;
            var logDirectory = Path.Combine(exePath, "Logs");

            if (!Directory.Exists(logDirectory))
            {
                Directory.CreateDirectory(logDirectory);
            }

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            logFilePath = Path.Combine(logDirectory, $"SolviaDownloaderLog_{timestamp}.txt");

            LogInfo("Logging initialized.");
        }

        public static void LogEnvironment()
        {
            LogInfo($"Hostname: {Environment.MachineName}");
            LogInfo($"Username: {Environment.UserName}");
            LogInfo($"Start time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        }

        public static void LogInfo(string message) => EnqueueLog("INFO", message);

        public static void LogError(string message) => EnqueueLog("ERROR", message);

        private static void EnqueueLog(string level, string message)
        {
            string logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {level} - {message}";
            //Console.WriteLine(logEntry);

            lock (logLock)
            {
                logQueue.Enqueue(logEntry);
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
    }
}
