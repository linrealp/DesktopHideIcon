using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace DeskToggle
{
    internal sealed class MouseDoubleClickHook : IDisposable
    {
        public delegate void DesktopDoubleClickCallback(int firstX, int firstY, int secondX, int secondY);

        private readonly DesktopController _desktop;
        private readonly DesktopDoubleClickCallback _callback;
        private readonly NativeMethods.HookProc _hookProc;
        private IntPtr _hookHandle = IntPtr.Zero;
        private bool _hasFirstClick;
        private int _firstX;
        private int _firstY;
        private uint _firstClickTime;

        public MouseDoubleClickHook(DesktopController desktop, DesktopDoubleClickCallback callback)
        {
            if (desktop == null)
            {
                throw new ArgumentNullException("desktop");
            }
            if (callback == null)
            {
                throw new ArgumentNullException("callback");
            }

            _desktop = desktop;
            _callback = callback;
            _hookProc = HookCallback;
        }

        public bool IsActive
        {
            get { return _hookHandle != IntPtr.Zero; }
        }

        public bool Install(out string error)
        {
            error = null;
            if (_hookHandle != IntPtr.Zero)
            {
                return true;
            }

            IntPtr moduleHandle = NativeMethods.GetModuleHandle(null);
            _hookHandle = NativeMethods.SetWindowsHookEx(
                NativeMethods.WH_MOUSE_LL,
                _hookProc,
                moduleHandle,
                0);

            if (_hookHandle == IntPtr.Zero)
            {
                int errorCode = Marshal.GetLastWin32Error();
                error = new Win32Exception(errorCode).Message + "（错误码 " + errorCode + "）";
                return false;
            }

            return true;
        }

        public void Dispose()
        {
            _hasFirstClick = false;
            if (_hookHandle != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_hookHandle);
                _hookHandle = IntPtr.Zero;
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode >= 0 && wParam == (IntPtr)NativeMethods.WM_LBUTTONDOWN)
                {
                    HandleLeftButtonDown(lParam);
                }
            }
            catch (Exception)
            {
                // A managed exception must never cross the native hook boundary.
            }

            return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        private void HandleLeftButtonDown(IntPtr lParam)
        {
            NativeMethods.MSLLHOOKSTRUCT hookData =
                (NativeMethods.MSLLHOOKSTRUCT)Marshal.PtrToStructure(
                    lParam,
                    typeof(NativeMethods.MSLLHOOKSTRUCT));

            int x = hookData.pt.X;
            int y = hookData.pt.Y;

            if (!_desktop.IsDesktopBackgroundAt(x, y))
            {
                _hasFirstClick = false;
                return;
            }

            uint now = unchecked((uint)Environment.TickCount);
            if (_hasFirstClick)
            {
                int doubleClickTime = NativeMethods.GetDoubleClickTime();
                int doubleClickWidth = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXDOUBLECLK);
                int doubleClickHeight = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYDOUBLECLK);

                if (doubleClickWidth <= 0)
                {
                    doubleClickWidth = 4;
                }
                if (doubleClickHeight <= 0)
                {
                    doubleClickHeight = 4;
                }

                int halfWidth = Math.Max(1, doubleClickWidth / 2);
                int halfHeight = Math.Max(1, doubleClickHeight / 2);
                bool withinTime = (now - _firstClickTime) <= (uint)doubleClickTime;
                bool withinArea = Math.Abs(x - _firstX) <= halfWidth
                    && Math.Abs(y - _firstY) <= halfHeight;

                if (withinTime && withinArea)
                {
                    _hasFirstClick = false;
                    _callback(_firstX, _firstY, x, y);
                    return;
                }
            }

            _hasFirstClick = true;
            _firstX = x;
            _firstY = y;
            _firstClickTime = now;
        }
    }
}
