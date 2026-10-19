using System;
using System.Threading;
using System.Windows.Forms;

namespace DeskToggle
{
    internal static class Program
    {
        private const string MutexName = "Local\\DeskToggle.SingleInstance";

        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (HasRestoreArgument(args))
            {
                Environment.ExitCode = RestoreNow() ? 0 : 1;
                return;
            }

            bool createdNew;
            using (Mutex mutex = new Mutex(true, MutexName, out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show(
                        "桌面工具已经在运行。\r\n\r\n"
                        + "双击桌面空白处可隐藏/恢复桌面图标；右键任务栏通知区域的图标可设置或退出。",
                        "桌面工具",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }

                try
                {
                    RunApplication();
                }
                catch (Exception ex)
                {
                    HandleFatalError(ex);
                }
                finally
                {
                    try
                    {
                        mutex.ReleaseMutex();
                    }
                    catch (Exception)
                    {
                    }
                }
            }
        }

        private static void RunApplication()
        {
            string loadError;
            AppSettings settings = AppSettings.Load(out loadError);
            if (!string.IsNullOrEmpty(loadError))
            {
                AppSettings.LogError("读取配置失败：" + loadError, null);
            }

            TryRecoverStaleState();

            string startupError = null;
            if (!StartupManager.Apply(settings.StartWithWindows, out startupError))
            {
                AppSettings.LogError("校准开机启动项失败：" + startupError, null);
            }

            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += OnThreadException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

            TrayApplicationContext context = null;

            try
            {
                context = new TrayApplicationContext(settings, startupError);

                string initializeError;
                if (!context.Initialize(out initializeError))
                {
                    AppSettings.LogError("初始化失败：" + initializeError, null);
                    MessageBox.Show(
                        "桌面工具无法启动。\r\n\r\n" + initializeError,
                        "桌面工具",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    context.Dispose();
                    Environment.ExitCode = 1;
                    return;
                }

                Application.Run(context);
            }
            finally
            {
                if (context != null)
                {
                    try
                    {
                        context.Dispose();
                    }
                    catch (Exception ex)
                    {
                        AppSettings.LogError("释放程序资源失败", ex);
                    }
                }

                TryRecoverStaleState();
            }
        }

        private static bool HasRestoreArgument(string[] args)
        {
            if (args == null)
            {
                return false;
            }

            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "--restore", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool RestoreNow()
        {
            try
            {
                DesktopController controller = new DesktopController();
                string error;
                bool restored = controller.ShowAll(out error);

                if (restored)
                {
                    string markerError;
                    AppSettings.SetHiddenMarker(false, out markerError);
                    return string.IsNullOrEmpty(markerError);
                }

                AppSettings.LogError("命令行恢复桌面失败：" + error, null);
                return false;
            }
            catch (Exception ex)
            {
                AppSettings.LogError("命令行恢复桌面异常", ex);
                return false;
            }
        }

        private static void TryRecoverStaleState()
        {
            if (!AppSettings.HiddenMarkerExists())
            {
                return;
            }

            try
            {
                DesktopController controller = new DesktopController();
                string error;
                if (controller.ShowAll(out error))
                {
                    string markerError;
                    AppSettings.SetHiddenMarker(false, out markerError);
                    if (!string.IsNullOrEmpty(markerError))
                    {
                        AppSettings.LogError("清理异常状态标记失败：" + markerError, null);
                    }
                }
                else
                {
                    AppSettings.LogError("检测到上次异常退出，但恢复失败：" + error, null);
                }
            }
            catch (Exception ex)
            {
                AppSettings.LogError("异常退出恢复流程失败", ex);
            }
        }

        private static void OnThreadException(object sender, ThreadExceptionEventArgs e)
        {
            HandleFatalError(e.Exception);
        }

        private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Exception exception = e.ExceptionObject as Exception;
            AppSettings.LogError("未处理的进程异常", exception);
            TryRecoverForFatalError();
        }

        private static void HandleFatalError(Exception exception)
        {
            AppSettings.LogError("程序遇到致命错误", exception);
            TryRecoverForFatalError();

            try
            {
                MessageBox.Show(
                    "桌面工具遇到错误，将退出。\r\n"
                    + "程序已尝试恢复桌面图标和任务栏；如仍未恢复，可运行“桌面工具.exe --restore”。\r\n\r\n"
                    + (exception == null ? string.Empty : exception.Message),
                    "桌面工具",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            catch (Exception)
            {
            }

            Environment.Exit(1);
        }

        private static void TryRecoverForFatalError()
        {
            try
            {
                DesktopController controller = new DesktopController();
                string error;
                if (controller.ShowAll(out error))
                {
                    string markerError;
                    AppSettings.SetHiddenMarker(false, out markerError);
                }
                else
                {
                    AppSettings.LogError("致命错误恢复桌面失败：" + error, null);
                }
            }
            catch (Exception ex)
            {
                AppSettings.LogError("致命错误恢复流程异常", ex);
            }
        }
    }
}
