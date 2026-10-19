using System;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DeskToggle
{
    internal static class StartupManager
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "DeskToggle";

        public static bool Apply(bool enabled, out string error)
        {
            error = null;

            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
                {
                    if (key == null)
                    {
                        error = "无法打开当前用户的启动项注册表位置。";
                        return false;
                    }

                    if (enabled)
                    {
                        string command = "\"" + Application.ExecutablePath + "\"";
                        key.SetValue(ValueName, command, RegistryValueKind.String);
                    }
                    else
                    {
                        key.DeleteValue(ValueName, false);
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }
}
