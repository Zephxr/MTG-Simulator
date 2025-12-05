using System;
using System.IO;
using System.Text.Json;

namespace MTGSimulator.Settings
{
    public class AppSettings
    {
        public bool StartMaximized { get; set; } = true;
        public double CardWidth { get; set; } = 120;
        public double CardHeight { get; set; } = 168;
        public string DrawCardKey { get; set; } = "Ctrl+D";
        public string TapCardKey { get; set; } = "T";
        public string ShowHandKey { get; set; } = "H";
        public string ShowCardInfoKey { get; set; } = "I";
        public string ShuffleKey { get; set; } = "Ctrl+S";
        public string AddPlusOnePlusOneKey { get; set; } = "Ctrl+Plus";
        public string RemovePlusOnePlusOneKey { get; set; } = "Ctrl+Minus";
        public string AddOtherCounterKey { get; set; } = "Ctrl+Shift+Plus";
        public string RemoveOtherCounterKey { get; set; } = "Ctrl+Shift+Minus";
        public string AddLoyaltyKey { get; set; } = "Ctrl+Up";
        public string RemoveLoyaltyKey { get; set; } = "Ctrl+Down";
        public bool ShowFpsCounter { get; set; } = true;
        public bool ShowSelectionCount { get; set; } = true;
        public int MaxCachedImages { get; set; } = 1000;
        public string? BulkDataDirectory { get; set; }

        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MTGSimulator",
            "settings.json");

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json);
                    return settings ?? new AppSettings();
                }
            }
            catch
            {
                // If loading fails, return default settings
            }
            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                var directory = Path.GetDirectoryName(SettingsPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(this, options);
                File.WriteAllText(SettingsPath, json);
            }
            catch
            {
                // If saving fails, silently ignore
            }
        }
    }
}

