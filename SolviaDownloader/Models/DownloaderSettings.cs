using System.Collections.Generic;
using System;

namespace SolviaDownloader.Models
{
    internal class DownloaderSettings
    {
        public string Url { get; set; }
        public string SaveToBasePath { get; set; }

        public static DownloaderSettings Parse(string[] args)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i].StartsWith("-"))
                {
                    var key = args[i].TrimStart('-');
                    var value = args[i + 1];
                    if (!value.StartsWith("-"))
                    {
                        dict[key] = value;
                        i++;
                    }
                }
            }

            if (!dict.ContainsKey("url") || !dict.ContainsKey("saveto"))
            {
                return null;
            }

            return new DownloaderSettings
            {
                Url = dict["url"],
                SaveToBasePath = dict["saveto"]
            };
        }
    }
}
