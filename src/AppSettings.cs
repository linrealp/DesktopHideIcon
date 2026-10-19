using System;
using System.IO;
using System.Text;

namespace DeskToggle
{
    internal sealed class AppSettings
    {
        public bool HideTaskbar;
        public bool StartWithWindows;
        public bool FirstRun;

        private static readonly object LogSync = new object();

        public AppSettings()
        {
            HideTaskbar = false;
            StartWithWindows = false;
            FirstRun = true;
        }

        public static string DataDirectory
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "DeskToggle");
            }
        }

        public static string SettingsPath
        {
            get { return Path.Combine(DataDirectory, "settings.ini"); }
        }

        public static string HiddenMarkerPath
        {
            get { return Path.Combine(DataDirectory, "hidden.state"); }
        }

        public static string ErrorLogPath
        {
            get { return Path.Combine(DataDirectory, "error.log"); }
        }

        public static AppSettings Load(out string error)
        {
            error = null;
            AppSettings settings = new AppSettings();

            try
            {
                if (!File.Exists(SettingsPath))
                {
                    return settings;
                }

                string[] lines = File.ReadAllLines(SettingsPath, Encoding.UTF8);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    int separator = line.IndexOf('=');
                    if (separator <= 0)
                    {
                        continue;
                    }

                    string key = line.Substring(0, separator).Trim();
                    string value = line.Substring(separator + 1).Trim();

                    if (string.Equals(key, "HideTaskbar", StringComparison.OrdinalIgnoreCase))
                    {
                        settings.HideTaskbar = ParseBool(value);
                    }
                    else if (string.Equals(key, "StartWithWindows", StringComparison.OrdinalIgnoreCase))
                    {
                        settings.StartWithWindows = ParseBool(value);
                    }
                    else if (string.Equals(key, "FirstRun", StringComparison.OrdinalIgnoreCase))
                    {
                        settings.FirstRun = ParseBool(value);
                    }
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return new AppSettings();
            }

            return settings;
        }

        public bool Save(out string error)
        {
            error = null;
            string temporaryPath = SettingsPath + ".tmp";

            try
            {
                Directory.CreateDirectory(DataDirectory);

                StringBuilder builder = new StringBuilder();
                builder.AppendLine("# DeskToggle settings");
                builder.Append("HideTaskbar=").AppendLine(HideTaskbar ? "1" : "0");
                builder.Append("StartWithWindows=").AppendLine(StartWithWindows ? "1" : "0");
                builder.Append("FirstRun=").AppendLine(FirstRun ? "1" : "0");

                File.WriteAllText(temporaryPath, builder.ToString(), new UTF8Encoding(false));

                if (File.Exists(SettingsPath))
                {
                    try
                    {
                        File.Replace(temporaryPath, SettingsPath, null);
                    }
                    catch (Exception)
                    {
                        File.Copy(temporaryPath, SettingsPath, true);
                        File.Delete(temporaryPath);
                    }
                }
                else
                {
                    File.Move(temporaryPath, SettingsPath);
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                try
                {
                    if (File.Exists(temporaryPath))
                    {
                        File.Delete(temporaryPath);
                    }
                }
                catch (Exception)
                {
                }
                return false;
            }
        }

        public static bool HiddenMarkerExists()
        {
            try
            {
                return File.Exists(HiddenMarkerPath);
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static void SetHiddenMarker(bool exists, out string error)
        {
            error = null;
            try
            {
                if (exists)
                {
                    Directory.CreateDirectory(DataDirectory);
                    File.WriteAllText(
                        HiddenMarkerPath,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                        new UTF8Encoding(false));
                }
                else if (File.Exists(HiddenMarkerPath))
                {
                    File.Delete(HiddenMarkerPath);
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }
        }

        public static void LogError(string context, Exception exception)
        {
            try
            {
                lock (LogSync)
                {
                    Directory.CreateDirectory(DataDirectory);
                    StringBuilder builder = new StringBuilder();
                    builder.Append('[').Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).AppendLine("]");
                    builder.AppendLine(context ?? "Error");
                    if (exception != null)
                    {
                        builder.AppendLine(exception.GetType().FullName + ": " + exception.Message);
                        if (exception.StackTrace != null)
                        {
                            builder.AppendLine(exception.StackTrace);
                        }
                    }
                    builder.AppendLine();
                    File.AppendAllText(ErrorLogPath, builder.ToString(), new UTF8Encoding(false));
                }
            }
            catch (Exception)
            {
                // Error logging must never take down the utility.
            }
        }

        private static bool ParseBool(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            value = value.Trim();
            return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "on", StringComparison.OrdinalIgnoreCase);
        }
    }
}
