using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace FocusTracker.Core.Services
{
    public class ProgramNameResolverService
    {
        private static readonly Dictionary<string, string> FallbackNames = new()
        {
            { "chrome", "Google Chrome" },
            { "notion", "Notion" },
            { "devenv", "Visual Studio" },
            { "explorer", "Провідник Windows" },
            { "discord", "Discord" },
            { "telegram", "Telegram" },
            { "steamwebhelper", "Steam" },
            { "FocusTracker.App", "FocusTracker" }
        };

        public string Resolve(string processName, string exePath)
        {
            // 1. Пытаемся получить FileDescription
            if (!string.IsNullOrWhiteSpace(exePath) && File.Exists(exePath))
            {
                try
                {
                    var version = FileVersionInfo.GetVersionInfo(exePath);
                    if (!string.IsNullOrWhiteSpace(version.FileDescription))
                        return version.FileDescription;
                }
                catch
                {
                    // доступ может быть ограничен
                }
            }

            // 2. Пытаемся из fallback-словаря
            if (FallbackNames.TryGetValue(processName.ToLower(), out var fallbackName))
                return fallbackName;

            // 3. Если ничего не нашли — возвращаем processName
            return processName;
        }
    }
}
