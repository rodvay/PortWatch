using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Windows.Forms;

namespace PortWatch
{
    internal static class Loc
    {
        public static string DetectSystemLanguage()
        {
            try
            {
                string two = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
                if (string.Equals(two, "tr", StringComparison.OrdinalIgnoreCase)) return "tr";
                if (string.Equals(two, "zh", StringComparison.OrdinalIgnoreCase)) return "zh";
            }
            catch { }
            return "en";
        }

        public static string T(string lang, string en, string tr, string zh)
        {
            if (string.Equals(lang, "tr", StringComparison.OrdinalIgnoreCase)) return tr;
            if (string.Equals(lang, "zh", StringComparison.OrdinalIgnoreCase)) return zh;
            return en;
        }

        public static string FontFamily(string lang)
        {
            return string.Equals(lang, "zh", StringComparison.OrdinalIgnoreCase) ? "Microsoft YaHei UI" : "Segoe UI";
        }

        public static CultureInfo Culture(string lang)
        {
            try
            {
                if (string.Equals(lang, "tr", StringComparison.OrdinalIgnoreCase)) return CultureInfo.GetCultureInfo("tr-TR");
                if (string.Equals(lang, "zh", StringComparison.OrdinalIgnoreCase)) return CultureInfo.GetCultureInfo("zh-CN");
                return CultureInfo.GetCultureInfo("en-US");
            }
            catch { return CultureInfo.InvariantCulture; }
        }

        public static string F(string fmt, params object[] args)
        {
            return string.Format(CultureInfo.InvariantCulture, fmt, args);
        }
    }

    internal sealed class PortWatchSettings
    {
        public string Language = Loc.DetectSystemLanguage();
        public bool MinimizeToTray = true;
        public bool NetHudEnabled = false;
        public int NetHudMargin = 4;
        public bool NetHudCustomPos = false;
        public int NetHudX = 0;
        public int NetHudY = 0;
        public int NetHudOpacity = 88;
        public int RefreshMs = 1500;

        public static string GetSettingsPath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(Path.Combine(appData, "PortWatch"), "settings.ini");
        }

        public static PortWatchSettings Load()
        {
            PortWatchSettings s = new PortWatchSettings();
            try
            {
                string p = GetSettingsPath();
                if (!File.Exists(p)) return s;
                foreach (string raw in File.ReadAllLines(p, Encoding.UTF8))
                {
                    int eq = raw.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = raw.Substring(0, eq).Trim();
                    string v = raw.Substring(eq + 1).Trim();
                    int iv;
                    if (k == "Language" && (v == "tr" || v == "en" || v == "zh")) s.Language = v;
                    else if (k == "MinimizeToTray") s.MinimizeToTray = (v == "1");
                    else if (k == "NetHudEnabled") s.NetHudEnabled = (v == "1");
                    else if (k == "NetHudMargin" && int.TryParse(v, out iv)) s.NetHudMargin = Math.Max(0, Math.Min(120, iv));
                    else if (k == "NetHudCustomPos") s.NetHudCustomPos = (v == "1");
                    else if (k == "NetHudX" && int.TryParse(v, out iv)) s.NetHudX = iv;
                    else if (k == "NetHudY" && int.TryParse(v, out iv)) s.NetHudY = iv;
                    else if (k == "NetHudOpacity" && int.TryParse(v, out iv)) s.NetHudOpacity = Math.Max(55, Math.Min(100, iv));
                    else if (k == "RefreshMs" && int.TryParse(v, out iv)) s.RefreshMs = Math.Max(1000, Math.Min(5000, iv));
                }
            }
            catch { }
            return s;
        }

        public void Save()
        {
            try
            {
                string p = GetSettingsPath();
                string dir = Path.GetDirectoryName(p);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("Language=" + (Language ?? "en"));
                sb.AppendLine("MinimizeToTray=" + (MinimizeToTray ? "1" : "0"));
                sb.AppendLine("NetHudEnabled=" + (NetHudEnabled ? "1" : "0"));
                sb.AppendLine("NetHudMargin=" + NetHudMargin);
                sb.AppendLine("NetHudCustomPos=" + (NetHudCustomPos ? "1" : "0"));
                sb.AppendLine("NetHudX=" + NetHudX);
                sb.AppendLine("NetHudY=" + NetHudY);
                sb.AppendLine("NetHudOpacity=" + NetHudOpacity);
                sb.AppendLine("RefreshMs=" + RefreshMs);
                File.WriteAllText(p, sb.ToString(), Encoding.UTF8);
            }
            catch { }
        }
    }

    internal static class WinNative
    {
        public const int WM_DPICHANGED = 0x02E0;
        public const int EM_SETCUEBANNER = 0x1501;
        public const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
        public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        public const int DWMWA_CAPTION_COLOR = 35;
        public const int DWMWA_TEXT_COLOR = 36;

        public const int WS_EX_TOPMOST = 0x00000008;
        public const int WS_EX_TRANSPARENT = 0x00000020;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_LAYERED = 0x00080000;
        public const int WS_EX_NOACTIVATE = 0x08000000;
        public const int GWL_EXSTYLE = -20;

        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public static readonly IntPtr HWND_BROADCAST = new IntPtr(0xFFFF);
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOACTIVATE = 0x0010;
        public const uint SWP_FRAMECHANGED = 0x0020;
        public const uint MSGFLT_ALLOW = 1;
        public const int ULW_ALPHA = 0x00000002;
        public const byte AC_SRC_OVER = 0x00;
        public const byte AC_SRC_ALPHA = 0x01;

        public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        public const int AF_INET = 2;
        public const int AF_INET6 = 23;
        public const int TCP_TABLE_OWNER_PID_ALL = 5;
        public const int UDP_TABLE_OWNER_PID = 1;

        public static readonly uint WM_SHOW_EXISTING = RegisterWindowMessage("PortWatch_ShowExistingWindow_v1");
        private static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new IntPtr(-4);
        private static bool? _cachedIsAdmin;

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X; public int Y; public POINT(int x, int y) { X = x; Y = y; } }

        [StructLayout(LayoutKind.Sequential)]
        public struct SIZE { public int cx; public int cy; public SIZE(int w, int h) { cx = w; cy = h; } }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct BLENDFUNCTION
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MIB_TCPROW_OWNER_PID
        {
            public uint state;
            public uint localAddr;
            public uint localPort;
            public uint remoteAddr;
            public uint remotePort;
            public uint owningPid;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MIB_TCP6ROW_OWNER_PID
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
            public byte[] localAddr;
            public uint localScopeId;
            public uint localPort;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
            public byte[] remoteAddr;
            public uint remoteScopeId;
            public uint remotePort;
            public uint state;
            public uint owningPid;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MIB_UDPROW_OWNER_PID
        {
            public uint localAddr;
            public uint localPort;
            public uint owningPid;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MIB_UDP6ROW_OWNER_PID
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
            public byte[] localAddr;
            public uint localScopeId;
            public uint localPort;
            public uint owningPid;
        }

        [DllImport("iphlpapi.dll", SetLastError = true)]
        public static extern uint GetExtendedTcpTable(IntPtr pTcpTable, ref int pdwSize, [MarshalAs(UnmanagedType.Bool)] bool bOrder, int ulAf, int tableClass, uint reserved);

        [DllImport("iphlpapi.dll", SetLastError = true)]
        public static extern uint GetExtendedUdpTable(IntPtr pUdpTable, ref int pdwSize, [MarshalAs(UnmanagedType.Bool)] bool bOrder, int ulAf, int tableClass, uint reserved);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool QueryFullProcessImageName(IntPtr hProcess, uint dwFlags, StringBuilder lpExeName, ref int lpdwSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(IntPtr hObject);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetProcessDPIAware();

        [DllImport("user32.dll")]
        public static extern uint GetDpiForWindow(IntPtr hwnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern uint RegisterWindowMessage(string lpString);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ChangeWindowMessageFilterEx(IntPtr hWnd, uint msg, uint action, IntPtr pChangeFilterStruct);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
        private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
        private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        public static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
        {
            return IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : new IntPtr(GetWindowLong32(hWnd, nIndex));
        }

        public static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
        {
            return IntPtr.Size == 8 ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong) : new IntPtr(SetWindowLong32(hWnd, nIndex, dwNewLong.ToInt32()));
        }

        [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
        public static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
        public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)]
        public static extern IntPtr CreateCompatibleDC(IntPtr hDC);

        [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll", ExactSpelling = true)]
        public static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);

        [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeleteObject(IntPtr hObject);

        [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pprSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        public static void EnableDpiAwareness()
        {
            try { if (SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2)) return; } catch { }
            try { SetProcessDPIAware(); } catch { }
        }

        public static void ApplyDarkTitleBar(IntPtr hwnd, Color captionColor, Color textColor)
        {
            try
            {
                int en = 1;
                if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref en, sizeof(int)) != 0)
                    DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref en, sizeof(int));
                int cap = captionColor.R | (captionColor.G << 8) | (captionColor.B << 16);
                DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref cap, sizeof(int));
                int txt = textColor.R | (textColor.G << 8) | (textColor.B << 16);
                DwmSetWindowAttribute(hwnd, DWMWA_TEXT_COLOR, ref txt, sizeof(int));
            }
            catch { }
        }

        public static void AllowWakeMessage(IntPtr hwnd)
        {
            try
            {
                if (hwnd != IntPtr.Zero && WM_SHOW_EXISTING != 0)
                    ChangeWindowMessageFilterEx(hwnd, WM_SHOW_EXISTING, MSGFLT_ALLOW, IntPtr.Zero);
            }
            catch { }
        }

        public static void SetCueBanner(IntPtr hwnd, string text)
        {
            try { if (hwnd != IntPtr.Zero) SendMessage(hwnd, EM_SETCUEBANNER, new IntPtr(1), text ?? ""); } catch { }
        }

        public static bool IsAdministrator()
        {
            if (_cachedIsAdmin.HasValue) return _cachedIsAdmin.Value;
            try
            {
                using (WindowsIdentity id = WindowsIdentity.GetCurrent())
                {
                    _cachedIsAdmin = new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
                    return _cachedIsAdmin.Value;
                }
            }
            catch
            {
                _cachedIsAdmin = false;
                return false;
            }
        }

        public static bool RestartAsAdministrator(string extraArgs)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = Application.ExecutablePath;
                if (!string.IsNullOrEmpty(extraArgs)) psi.Arguments = extraArgs;
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                Process.Start(psi);
                return true;
            }
            catch { return false; }
        }

        public static string QueryProcessImagePath(int pid)
        {
            if (pid <= 4) return "";
            IntPtr h = IntPtr.Zero;
            try
            {
                h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
                if (h == IntPtr.Zero) return "";
                StringBuilder sb = new StringBuilder(520);
                int cap = sb.Capacity;
                if (QueryFullProcessImageName(h, 0, sb, ref cap) && cap > 0)
                    return sb.ToString(0, cap);
            }
            catch { }
            finally
            {
                if (h != IntPtr.Zero) CloseHandle(h);
            }
            return "";
        }

        public static int RunHiddenProcess(string fileName, string args, int timeoutMs, out string stdout)
        {
            stdout = "";
            try
            {
                Encoding enc;
                string exe = Path.GetFileName(fileName ?? "").ToLowerInvariant();
                if (exe == "powershell.exe" || exe == "powershell") enc = Encoding.UTF8;
                else
                {
                    try { enc = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage); }
                    catch { enc = Encoding.Default; }
                }

                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = fileName;
                psi.Arguments = args;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.StandardOutputEncoding = enc;
                psi.StandardErrorEncoding = enc;

                StringBuilder sb = new StringBuilder();
                object lck = new object();
                using (Process p = new Process())
                {
                    p.StartInfo = psi;
                    p.OutputDataReceived += delegate(object s, DataReceivedEventArgs e)
                    {
                        if (!string.IsNullOrEmpty(e.Data)) lock (lck) { sb.AppendLine(e.Data); }
                    };
                    p.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e) { };
                    p.Start();
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    if (!p.WaitForExit(Math.Max(1500, timeoutMs)))
                    {
                        try { p.Kill(); } catch { }
                        return -1;
                    }
                    p.WaitForExit();
                    lock (lck) { stdout = sb.ToString(); }
                    return p.ExitCode;
                }
            }
            catch { return -1; }
        }
    }
}
