using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace DeskToggle
{
    internal sealed class TrayApplicationContext : ApplicationContext
    {
        private struct ClickPair
        {
            public int FirstX;
            public int FirstY;
            public int SecondX;
            public int SecondY;
        }

        private readonly AppSettings _settings;
        private readonly string _startupError;
        private readonly DesktopController _desktop = new DesktopController();
        private readonly Queue<ClickPair> _pendingClicks = new Queue<ClickPair>();
        private readonly uint _taskbarCreatedMessage;

        private NotifyIcon _notifyIcon;
        private ContextMenuStrip _menu;
        private ToolStripMenuItem _toggleItem;
        private ToolStripMenuItem _taskbarItem;
        private ToolStripMenuItem _startupItem;
        private HiddenMessageWindow _messageWindow;
        private MouseDoubleClickHook _hook;
        private Timer _firstRunTimer;
        private Icon _trayIcon;
        private bool _exiting;
        private bool _resourcesDisposed;

        public TrayApplicationContext(AppSettings settings, string startupError)
        {
            if (settings == null)
            {
                throw new ArgumentNullException("settings");
            }

            _settings = settings;
            _startupError = startupError;
            _taskbarCreatedMessage = NativeMethods.RegisterWindowMessage("TaskbarCreated");
        }

        public bool Initialize(out string error)
        {
            error = null;

            try
            {
                _messageWindow = new HiddenMessageWindow(this);

                _hook = new MouseDoubleClickHook(_desktop, OnDesktopDoubleClick);
                string hookError;
                if (!_hook.Install(out hookError))
                {
                    error = "无法启用桌面双击监听：" + hookError;
                    CleanupResources();
                    return false;
                }

                CreateMenu();
                CreateTrayIcon();
                UpdateUi();

                if (!string.IsNullOrEmpty(_startupError))
                {
                    ShowBalloon("开机启动设置失败：" + _startupError, ToolTipIcon.Warning, 6000);
                }

                if (_settings.FirstRun)
                {
                    _firstRunTimer = new Timer();
                    _firstRunTimer.Interval = 900;
                    _firstRunTimer.Tick += OnFirstRunTimerTick;
                    _firstRunTimer.Start();
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                CleanupResources();
                return false;
            }
        }

        public void RequestExit()
        {
            ExitApp();
        }

        private void CreateMenu()
        {
            _menu = new ContextMenuStrip();
            _menu.ShowImageMargin = false;
            _menu.ShowCheckMargin = true;

            _toggleItem = new ToolStripMenuItem("隐藏桌面图标");
            _toggleItem.Click += OnToggleMenuItemClick;

            _taskbarItem = new ToolStripMenuItem("隐藏图标时同时隐藏任务栏");
            _taskbarItem.ToolTipText = "隐藏桌面图标时，同时隐藏所有显示器上的任务栏";
            _taskbarItem.Click += OnTaskbarMenuItemClick;

            _startupItem = new ToolStripMenuItem("开机启动");
            _startupItem.ToolTipText = "登录 Windows 后自动在托盘运行";
            _startupItem.Click += OnStartupMenuItemClick;

            ToolStripMenuItem helpItem = new ToolStripMenuItem("使用说明");
            helpItem.Click += OnHelpMenuItemClick;

            ToolStripMenuItem exitItem = new ToolStripMenuItem("退出");
            exitItem.Click += OnExitMenuItemClick;

            _menu.Items.Add(_toggleItem);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(_taskbarItem);
            _menu.Items.Add(_startupItem);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(helpItem);
            _menu.Items.Add(exitItem);

            _menu.Opening += OnMenuOpening;
        }

        private void CreateTrayIcon()
        {
            _trayIcon = LoadTrayIcon();

            _notifyIcon = new NotifyIcon();
            _notifyIcon.Icon = _trayIcon;
            _notifyIcon.Text = "桌面工具";
            _notifyIcon.ContextMenuStrip = _menu;
            _notifyIcon.DoubleClick += OnTrayIconDoubleClick;
            _notifyIcon.Visible = true;
        }

        private static Icon LoadTrayIcon()
        {
            try
            {
                Icon icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (icon != null)
                {
                    return icon;
                }
            }
            catch (Exception ex)
            {
                AppSettings.LogError("读取程序图标失败", ex);
            }

            return (Icon)SystemIcons.Application.Clone();
        }

        private void OnFirstRunTimerTick(object sender, EventArgs e)
        {
            if (_firstRunTimer != null)
            {
                _firstRunTimer.Stop();
            }

            ShowBalloon(
                "双击桌面空白处即可隐藏/恢复桌面图标。右键托盘图标可配置任务栏和开机启动。",
                ToolTipIcon.Info,
                6500);

            _settings.FirstRun = false;
            string error;
            if (!_settings.Save(out error))
            {
                AppSettings.LogError("保存首次运行标记失败：" + error, null);
            }
        }

        private void OnMenuOpening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            UpdateUi();
        }

        private void OnTrayIconDoubleClick(object sender, EventArgs e)
        {
            if (_exiting)
            {
                return;
            }
            ToggleFromUser();
        }

        private void OnToggleMenuItemClick(object sender, EventArgs e)
        {
            if (_exiting)
            {
                return;
            }
            ToggleFromUser();
        }

        private void ToggleFromUser()
        {
            SetIconsHidden(!_desktop.IconsHidden);
        }

        private void OnDesktopDoubleClick(int firstX, int firstY, int secondX, int secondY)
        {
            if (_exiting)
            {
                return;
            }

            // This runs inside the low-level mouse hook. Only enqueue and post;
            // the potentially slower accessibility checks run in OnToggleMessage.
            ClickPair pair;
            pair.FirstX = firstX;
            pair.FirstY = firstY;
            pair.SecondX = secondX;
            pair.SecondY = secondY;

            if (_pendingClicks.Count >= 8)
            {
                _pendingClicks.Dequeue();
            }
            _pendingClicks.Enqueue(pair);

            if (_messageWindow == null || _messageWindow.Handle == IntPtr.Zero)
            {
                _pendingClicks.Clear();
                return;
            }

            if (!NativeMethods.PostMessage(
                _messageWindow.Handle,
                NativeMethods.WM_TOGGLE,
                IntPtr.Zero,
                IntPtr.Zero))
            {
                _pendingClicks.Clear();
                AppSettings.LogError("无法投递双击消息。", null);
            }
        }

        private void OnToggleMessage()
        {
            if (_exiting)
            {
                return;
            }

            while (_pendingClicks.Count > 0)
            {
                ClickPair pair = _pendingClicks.Dequeue();
                if (_desktop.IconsHidden)
                {
                    SetIconsHidden(false);
                    continue;
                }

                DesktopHitTest firstHit = _desktop.HitTestDesktopIcon(pair.FirstX, pair.FirstY);
                DesktopHitTest secondHit = _desktop.HitTestDesktopIcon(pair.SecondX, pair.SecondY);
                if (firstHit == DesktopHitTest.Blank && secondHit == DesktopHitTest.Blank)
                {
                    SetIconsHidden(true);
                }
            }
        }

        private void SetIconsHidden(bool hide)
        {
            if (hide)
            {
                HideIconsAndMaybeTaskbar();
            }
            else
            {
                RestoreIconsAndTaskbar();
            }
        }

        private void HideIconsAndMaybeTaskbar()
        {
            string error;
            if (!_desktop.HideIcons(out error))
            {
                ShowErrorMessage("隐藏桌面图标失败", error);
                UpdateUi();
                return;
            }

            if (_settings.HideTaskbar)
            {
                if (!_desktop.HideTaskbars(out error))
                {
                    string rollbackError;
                    _desktop.ShowIcons(out rollbackError);
                    _desktop.ShowTaskbars(out rollbackError);
                    AppSettings.SetHiddenMarker(false, out rollbackError);
                    ShowErrorMessage("隐藏任务栏失败", error);
                    UpdateUi();
                    return;
                }
            }
            else if (_desktop.TaskbarsHidden)
            {
                string unusedError;
                _desktop.ShowTaskbars(out unusedError);
            }

            AppSettings.SetHiddenMarker(true, out error);
            if (!string.IsNullOrEmpty(error))
            {
                AppSettings.LogError("写入异常状态标记失败：" + error, null);
            }

            UpdateUi();
        }

        private void RestoreIconsAndTaskbar()
        {
            string error;
            string taskbarError = null;
            bool hadHiddenTaskbar = _desktop.TaskbarsHidden;

            bool iconsOk = _desktop.ShowIcons(out error);
            bool taskbarsOk = true;
            if (hadHiddenTaskbar)
            {
                taskbarsOk = _desktop.ShowTaskbars(out taskbarError);
            }

            if (iconsOk && taskbarsOk)
            {
                string markerError;
                AppSettings.SetHiddenMarker(false, out markerError);
                if (!string.IsNullOrEmpty(markerError))
                {
                    AppSettings.LogError("删除异常状态标记失败：" + markerError, null);
                }
            }
            else
            {
                string message = !iconsOk ? error : taskbarError;
                ShowErrorMessage("恢复桌面图标失败", message);
            }

            if (hadHiddenTaskbar && taskbarsOk)
            {
                RefreshTrayIcon();
            }

            UpdateUi();
        }

        private void OnTaskbarMenuItemClick(object sender, EventArgs e)
        {
            if (_exiting)
            {
                return;
            }

            bool oldValue = _settings.HideTaskbar;
            bool newValue = !oldValue;
            string error;

            if (_desktop.IconsHidden)
            {
                bool changed = newValue
                    ? _desktop.HideTaskbars(out error)
                    : _desktop.ShowTaskbars(out error);

                if (!changed)
                {
                    ShowErrorMessage("设置任务栏失败", error);
                    UpdateUi();
                    return;
                }
            }

            _settings.HideTaskbar = newValue;
            if (!_settings.Save(out error))
            {
                _settings.HideTaskbar = oldValue;

                if (_desktop.IconsHidden)
                {
                    string rollbackError;
                    if (newValue)
                    {
                        _desktop.ShowTaskbars(out rollbackError);
                    }
                    else
                    {
                        _desktop.HideTaskbars(out rollbackError);
                    }
                }

                ShowErrorMessage("保存设置失败", error);
                UpdateUi();
                return;
            }

            if (!newValue && _desktop.IconsHidden)
            {
                RefreshTrayIcon();
            }

            UpdateUi();
        }

        private void OnStartupMenuItemClick(object sender, EventArgs e)
        {
            if (_exiting)
            {
                return;
            }

            bool oldValue = _settings.StartWithWindows;
            bool newValue = !oldValue;
            string error;

            if (!StartupManager.Apply(newValue, out error))
            {
                ShowErrorMessage("设置开机启动失败", error);
                UpdateUi();
                return;
            }

            _settings.StartWithWindows = newValue;
            if (!_settings.Save(out error))
            {
                _settings.StartWithWindows = oldValue;
                string rollbackError;
                StartupManager.Apply(oldValue, out rollbackError);
                ShowErrorMessage("保存设置失败", error);
                UpdateUi();
                return;
            }

            UpdateUi();
        }

        private void OnHelpMenuItemClick(object sender, EventArgs e)
        {
            string text =
                "1. 双击桌面空白处：隐藏或恢复桌面图标。\r\n"
                + "2. 右键托盘图标可设置是否同时隐藏任务栏、是否开机启动。\r\n"
                + "3. 双击桌面图标、任务栏或普通窗口不会触发。\r\n"
                + "4. 退出程序会自动恢复图标和任务栏。\r\n"
                + "5. 如果程序被强制结束，重新运行本程序或运行“桌面工具.exe --restore”即可恢复。\r\n\r\n"
                + "提示：任务栏被隐藏时，托盘图标也会一起消失；直接双击桌面空白处恢复。";

            MessageBox.Show(
                text,
                "桌面工具 - 使用说明",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private void OnExitMenuItemClick(object sender, EventArgs e)
        {
            ExitApp();
        }

        private void OnTaskbarCreated()
        {
            if (_exiting)
            {
                return;
            }

            _desktop.ResetStateAfterShellRestart();

            string error;
            AppSettings.SetHiddenMarker(false, out error);
            if (!string.IsNullOrEmpty(error))
            {
                AppSettings.LogError("Explorer 重启后删除标记失败：" + error, null);
            }

            RefreshTrayIcon();
            UpdateUi();
        }

        private void RefreshTrayIcon()
        {
            if (_notifyIcon == null)
            {
                return;
            }

            try
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Visible = true;
            }
            catch (Exception ex)
            {
                AppSettings.LogError("刷新托盘图标失败", ex);
            }
        }

        private void UpdateUi()
        {
            if (_menu != null)
            {
                if (_toggleItem != null)
                {
                    _toggleItem.Text = _desktop.IconsHidden ? "恢复桌面图标" : "隐藏桌面图标";
                }
                if (_taskbarItem != null)
                {
                    _taskbarItem.Checked = _settings.HideTaskbar;
                }
                if (_startupItem != null)
                {
                    _startupItem.Checked = _settings.StartWithWindows;
                }
            }

            if (_notifyIcon != null)
            {
                string text = _desktop.IconsHidden
                    ? "桌面工具：图标已隐藏，双击桌面空白处恢复"
                    : "桌面工具：双击桌面空白处切换";
                if (text.Length > 63)
                {
                    text = text.Substring(0, 63);
                }
                _notifyIcon.Text = text;
            }
        }

        private void ShowBalloon(string text, ToolTipIcon icon, int timeout)
        {
            try
            {
                if (_notifyIcon != null && _notifyIcon.Visible)
                {
                    _notifyIcon.ShowBalloonTip(timeout, "桌面工具", text, icon);
                }
            }
            catch (Exception ex)
            {
                AppSettings.LogError("显示通知失败", ex);
            }
        }

        private void ShowErrorMessage(string title, string detail)
        {
            AppSettings.LogError(title + "：" + (detail ?? string.Empty), null);
            try
            {
                MessageBox.Show(
                    string.IsNullOrEmpty(detail) ? title : title + "\r\n\r\n" + detail,
                    "桌面工具",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            catch (Exception)
            {
            }
        }

        private void ExitApp()
        {
            if (_exiting)
            {
                return;
            }

            _exiting = true;

            try
            {
                if (_desktop.HasHiddenState)
                {
                    string error;
                    bool restored = _desktop.ShowAll(out error);
                    if (restored)
                    {
                        string markerError;
                        AppSettings.SetHiddenMarker(false, out markerError);
                    }
                    else
                    {
                        AppSettings.LogError("退出时恢复桌面失败：" + error, null);
                        try
                        {
                            MessageBox.Show(
                                "恢复桌面图标或任务栏失败。\r\n\r\n"
                                + "可重新运行本程序，或执行“桌面工具.exe --restore”进行恢复。\r\n\r\n"
                                + error,
                                "桌面工具",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Warning);
                        }
                        catch (Exception)
                        {
                        }
                    }
                }
                else
                {
                    string error;
                    AppSettings.SetHiddenMarker(false, out error);
                }
            }
            catch (Exception ex)
            {
                AppSettings.LogError("退出恢复流程异常", ex);
            }
            finally
            {
                CleanupResources();
                ExitThread();
            }
        }

        private void CleanupResources()
        {
            if (_resourcesDisposed)
            {
                return;
            }
            _resourcesDisposed = true;

            try
            {
                if (_firstRunTimer != null)
                {
                    _firstRunTimer.Stop();
                    _firstRunTimer.Tick -= OnFirstRunTimerTick;
                    _firstRunTimer.Dispose();
                    _firstRunTimer = null;
                }

                if (_hook != null)
                {
                    _hook.Dispose();
                    _hook = null;
                }

                if (_notifyIcon != null)
                {
                    try
                    {
                        _notifyIcon.Visible = false;
                    }
                    catch (Exception)
                    {
                    }
                    _notifyIcon.DoubleClick -= OnTrayIconDoubleClick;
                    _notifyIcon.Dispose();
                    _notifyIcon = null;
                }

                if (_menu != null)
                {
                    _menu.Opening -= OnMenuOpening;
                    _menu.Dispose();
                    _menu = null;
                }

                if (_messageWindow != null)
                {
                    _messageWindow.DestroyHandle();
                    _messageWindow = null;
                }

                if (_trayIcon != null)
                {
                    _trayIcon.Dispose();
                    _trayIcon = null;
                }
            }
            catch (Exception ex)
            {
                AppSettings.LogError("清理资源失败", ex);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                CleanupResources();
            }
            base.Dispose(disposing);
        }

        private sealed class HiddenMessageWindow : NativeWindow
        {
            private const int WS_POPUP = unchecked((int)0x80000000);
            private readonly TrayApplicationContext _owner;

            public HiddenMessageWindow(TrayApplicationContext owner)
            {
                _owner = owner;

                CreateParams createParams = new CreateParams();
                createParams.Caption = "DeskToggle.HiddenWindow";
                createParams.X = 0;
                createParams.Y = 0;
                createParams.Width = 0;
                createParams.Height = 0;
                createParams.Style = WS_POPUP;
                createParams.ExStyle = 0;
                CreateHandle(createParams);
            }

            protected override void WndProc(ref Message m)
            {
                if (_owner._taskbarCreatedMessage != 0
                    && m.Msg == (int)_owner._taskbarCreatedMessage)
                {
                    _owner.OnTaskbarCreated();
                    return;
                }

                if (m.Msg == NativeMethods.WM_TOGGLE)
                {
                    _owner.OnToggleMessage();
                    return;
                }

                if (m.Msg == NativeMethods.WM_CLOSE)
                {
                    _owner.ExitApp();
                    return;
                }

                base.WndProc(ref m);
            }
        }
    }
}
