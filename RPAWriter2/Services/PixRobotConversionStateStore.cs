using System;
using System.IO;
using Newtonsoft.Json;

namespace RPAWriter2.Services
{
    /// <summary>Last used folders of the PIX → MWF conversion panel.</summary>
    public class PixRobotConversionState
    {
        public string PixFolder { get; set; } = "";
        public string MwfFolder { get; set; } = "";
    }

    /// <summary>
    /// Loads/saves the last used PIX → MWF conversion folders at
    /// %AppData%\RPAWriter2\pix-robot-conversion.json so they survive Visual Studio sessions.
    /// </summary>
    public static class PixRobotConversionStateStore
    {
        public static string StateFile => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RPAWriter2",
            "pix-robot-conversion.json");

        public static PixRobotConversionState Load()
        {
            try
            {
                if (File.Exists(StateFile))
                {
                    var json = File.ReadAllText(StateFile);
                    var state = JsonConvert.DeserializeObject<PixRobotConversionState>(json) ?? new PixRobotConversionState();
                    EnsureDefaults(state);
                    return state;
                }
            }
            catch
            {
                // ignore read errors — fall back to defaults
            }

            var defaults = new PixRobotConversionState();
            EnsureDefaults(defaults);
            return defaults;
        }

        public static void Save(PixRobotConversionState state)
        {
            if (state == null) return;
            try
            {
                var directory = Path.GetDirectoryName(StateFile);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);
                File.WriteAllText(StateFile, JsonConvert.SerializeObject(state, Formatting.Indented));
            }
            catch
            {
                // ignore write errors
            }
        }

        private static void EnsureDefaults(PixRobotConversionState state)
        {
            if (state.PixFolder == null) state.PixFolder = "";
            if (state.MwfFolder == null) state.MwfFolder = "";
        }
    }
}
