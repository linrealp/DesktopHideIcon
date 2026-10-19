using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Accessibility;

namespace DeskToggle
{
    internal enum DesktopHitTest
    {
        Blank,
        Icon,
        Unknown
    }

    internal sealed class DesktopController
    {
        private const string ClassProgman = "Progman";
        private const string ClassWorkerW = "WorkerW";
        private const string ClassDefView = "SHELLDLL_DefView";
        private const string ClassListView = "SysListView32";
        private const string ClassTaskbar = "Shell_TrayWnd";
        private const string ClassSecondaryTaskbar = "Shell_SecondaryTrayWnd";

        private readonly List<IntPtr> _hiddenTaskbars = new List<IntPtr>();
        private bool _iconsHidden;
        private bool _taskbarsHidden;

        public bool IconsHidden
        {
            get { return _iconsHidden; }
        }

        public bool TaskbarsHidden
        {
            get { return _taskbarsHidden; }
        }

        public bool HasHiddenState
        {
            get { return _iconsHidden || _taskbarsHidden; }
        }

        public void ResetStateAfterShellRestart()
        {
            _iconsHidden = false;
            _taskbarsHidden = false;
            _hiddenTaskbars.Clear();
        }

        public IntPtr FindDesktopListView()
        {
            List<IntPtr> roots = new List<IntPtr>();
            NativeMethods.EnumWindows(delegate(IntPtr hWnd, IntPtr lParam)
            {
                string className = GetClassName(hWnd);
                if (string.Equals(className, ClassProgman, StringComparison.Ordinal)
                    || string.Equals(className, ClassWorkerW, StringComparison.Ordinal))
                {
                    roots.Add(hWnd);
                }
                return true;
            }, IntPtr.Zero);

            for (int i = 0; i < roots.Count; i++)
            {
                IntPtr root = roots[i];
                IntPtr defView = NativeMethods.FindWindowEx(root, IntPtr.Zero, ClassDefView, null);
                if (defView == IntPtr.Zero)
                {
                    defView = FindDescendant(root, ClassDefView);
                }
                if (defView == IntPtr.Zero)
                {
                    continue;
                }

                IntPtr listView = NativeMethods.FindWindowEx(defView, IntPtr.Zero, ClassListView, null);
                if (listView == IntPtr.Zero)
                {
                    listView = FindDescendant(defView, ClassListView);
                }
                if (listView != IntPtr.Zero)
                {
                    return listView;
                }
            }

            return IntPtr.Zero;
        }

        public bool IsDesktopBackgroundAt(int x, int y)
        {
            NativeMethods.POINT point;
            point.X = x;
            point.Y = y;

            IntPtr hWnd = NativeMethods.WindowFromPoint(point);
            if (hWnd == IntPtr.Zero)
            {
                return false;
            }

            for (int depth = 0; depth < 16; depth++)
            {
                IntPtr parent = NativeMethods.GetParent(hWnd);
                if (parent == IntPtr.Zero)
                {
                    string rootClass = GetClassName(hWnd);
                    return string.Equals(rootClass, ClassProgman, StringComparison.Ordinal)
                        || string.Equals(rootClass, ClassWorkerW, StringComparison.Ordinal);
                }
                hWnd = parent;
            }

            return false;
        }

        public DesktopHitTest HitTestDesktopIcon(int x, int y)
        {
            IntPtr listView = FindDesktopListView();
            if (listView == IntPtr.Zero)
            {
                return DesktopHitTest.Unknown;
            }

            object accessibleObject = null;
            object hitObject = null;

            try
            {
                Guid iid = NativeMethods.IID_IAccessible;
                int result = NativeMethods.AccessibleObjectFromWindow(
                    listView,
                    NativeMethods.OBJID_CLIENT,
                    ref iid,
                    out accessibleObject);

                if (result != 0 || accessibleObject == null)
                {
                    return DesktopHitTest.Unknown;
                }

                IAccessible accessible = accessibleObject as IAccessible;
                if (accessible == null)
                {
                    return DesktopHitTest.Unknown;
                }

                try
                {
                    hitObject = accessible.accHitTest(x, y);
                }
                catch (Exception)
                {
                    return DesktopHitTest.Unknown;
                }

                if (hitObject == null)
                {
                    return DesktopHitTest.Blank;
                }

                if (hitObject is int)
                {
                    return ((int)hitObject) == 0 ? DesktopHitTest.Blank : DesktopHitTest.Icon;
                }

                return DesktopHitTest.Icon;
            }
            catch (Exception)
            {
                return DesktopHitTest.Unknown;
            }
            finally
            {
                if (hitObject != null
                    && !(hitObject is int)
                    && !object.ReferenceEquals(hitObject, accessibleObject)
                    && Marshal.IsComObject(hitObject))
                {
                    try
                    {
                        Marshal.ReleaseComObject(hitObject);
                    }
                    catch (Exception)
                    {
                    }
                }

                ReleaseComObject(accessibleObject);
            }
        }

        public bool HideIcons(out string error)
        {
            error = null;
            IntPtr listView = FindDesktopListView();
            if (listView == IntPtr.Zero)
            {
                error = "未找到桌面图标窗口。";
                return false;
            }

            NativeMethods.ShowWindow(listView, NativeMethods.SW_HIDE);
            if (NativeMethods.IsWindowVisible(listView))
            {
                error = "桌面图标窗口未能隐藏。";
                return false;
            }

            _iconsHidden = true;
            return true;
        }

        public bool ShowIcons(out string error)
        {
            error = null;
            IntPtr listView = FindDesktopListView();
            if (listView == IntPtr.Zero)
            {
                error = "未找到桌面图标窗口。";
                return false;
            }

            NativeMethods.ShowWindow(listView, NativeMethods.SW_SHOW);
            if (!NativeMethods.IsWindowVisible(listView))
            {
                error = "桌面图标窗口未能恢复。";
                return false;
            }

            _iconsHidden = false;
            return true;
        }

        public bool HideTaskbars(out string error)
        {
            error = null;
            List<IntPtr> taskbars = FindTaskbars();
            if (taskbars.Count == 0)
            {
                error = "未找到任务栏窗口。";
                return false;
            }

            List<IntPtr> hiddenNow = new List<IntPtr>();
            for (int i = 0; i < taskbars.Count; i++)
            {
                IntPtr taskbar = taskbars[i];
                NativeMethods.ShowWindow(taskbar, NativeMethods.SW_HIDE);
                if (NativeMethods.IsWindowVisible(taskbar))
                {
                    for (int j = 0; j < hiddenNow.Count; j++)
                    {
                        NativeMethods.ShowWindow(hiddenNow[j], NativeMethods.SW_SHOW);
                    }
                    error = "任务栏窗口未能隐藏。";
                    return false;
                }
                hiddenNow.Add(taskbar);
            }

            _hiddenTaskbars.Clear();
            _hiddenTaskbars.AddRange(hiddenNow);
            _taskbarsHidden = true;
            return true;
        }

        public bool ShowTaskbars(out string error)
        {
            error = null;
            List<IntPtr> taskbars = FindTaskbars();

            for (int i = 0; i < _hiddenTaskbars.Count; i++)
            {
                IntPtr stored = _hiddenTaskbars[i];
                if (!taskbars.Contains(stored))
                {
                    taskbars.Add(stored);
                }
            }

            if (taskbars.Count == 0)
            {
                _hiddenTaskbars.Clear();
                _taskbarsHidden = false;
                return true;
            }

            bool allVisible = true;
            for (int i = 0; i < taskbars.Count; i++)
            {
                IntPtr taskbar = taskbars[i];
                NativeMethods.ShowWindow(taskbar, NativeMethods.SW_SHOW);
                if (!NativeMethods.IsWindowVisible(taskbar))
                {
                    allVisible = false;
                }
            }

            if (!allVisible)
            {
                error = "部分任务栏窗口未能恢复。";
                return false;
            }

            _hiddenTaskbars.Clear();
            _taskbarsHidden = false;
            return true;
        }

        public bool ShowAll(out string error)
        {
            string iconError;
            string taskbarError;

            bool iconsOk = ShowIcons(out iconError);
            bool taskbarsOk = ShowTaskbars(out taskbarError);

            if (!iconsOk && !taskbarsOk)
            {
                error = JoinErrors(iconError, taskbarError);
                return false;
            }
            if (!iconsOk)
            {
                error = iconError;
                return false;
            }
            if (!taskbarsOk)
            {
                error = taskbarError;
                return false;
            }

            error = null;
            return true;
        }

        private static List<IntPtr> FindTaskbars()
        {
            List<IntPtr> taskbars = new List<IntPtr>();
            NativeMethods.EnumWindows(delegate(IntPtr hWnd, IntPtr lParam)
            {
                string className = GetClassName(hWnd);
                if (string.Equals(className, ClassTaskbar, StringComparison.Ordinal)
                    || string.Equals(className, ClassSecondaryTaskbar, StringComparison.Ordinal))
                {
                    taskbars.Add(hWnd);
                }
                return true;
            }, IntPtr.Zero);
            return taskbars;
        }

        private static IntPtr FindDescendant(IntPtr root, string className)
        {
            IntPtr found = IntPtr.Zero;
            NativeMethods.EnumChildWindows(root, delegate(IntPtr hWnd, IntPtr lParam)
            {
                if (string.Equals(GetClassName(hWnd), className, StringComparison.Ordinal))
                {
                    found = hWnd;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        private static string GetClassName(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero)
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(256);
            NativeMethods.GetClassName(hWnd, builder, builder.Capacity);
            return builder.ToString();
        }

        private static void ReleaseComObject(object value)
        {
            if (value != null && Marshal.IsComObject(value))
            {
                try
                {
                    Marshal.ReleaseComObject(value);
                }
                catch (Exception)
                {
                }
            }
        }

        private static string JoinErrors(string first, string second)
        {
            if (string.IsNullOrEmpty(first))
            {
                return second;
            }
            if (string.IsNullOrEmpty(second))
            {
                return first;
            }
            return first + " " + second;
        }
    }
}
