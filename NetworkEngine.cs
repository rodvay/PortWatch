using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace PortWatch
{
    internal sealed class PortEntry
    {
        public string Protocol;      // "TCP", "TCP6", "UDP", "UDP6"
        public string LocalAddress;
        public int LocalPort;
        public string RemoteAddress;
        public int RemotePort;
        public string State;         // "LISTENING", "ESTABLISHED", "TIME_WAIT", "UDP", etc.
        public int Pid;
        public string ProcessName;
        public string ExePath;
        public string ServiceTag;
        public bool IsCriticalSystem;
        public bool IsFirewallBlocked;

        public string LocalEndpoint { get { return FormatEndpoint(LocalAddress, LocalPort); } }
        public string RemoteEndpoint { get { return RemotePort > 0 ? FormatEndpoint(RemoteAddress, RemotePort) : "—"; } }

        private static string FormatEndpoint(string addr, int port)
        {
            if (string.IsNullOrEmpty(addr)) return ":" + port;
            if (addr.IndexOf(':') >= 0) return "[" + addr + "]:" + port;
            return addr + ":" + port;
        }
    }

    internal sealed class NetSpeedSnapshot
    {
        public double DownBytesPerSec;
        public double UpBytesPerSec;
        public long SessionDownBytes;
        public long SessionUpBytes;
        public string ActiveAdapter = "";
    }

    internal sealed class PortSnapshot
    {
        public List<PortEntry> Entries = new List<PortEntry>();
        public int ListeningCount;
        public int EstablishedCount;
        public int UdpCount;
        public int BlockedRuleCount;
        public NetSpeedSnapshot Speed = new NetSpeedSnapshot();
    }

    internal static class FirewallEngine
    {
        public const string RulePrefix = "PortWatch Block - ";
        private const string FirewallRulesKey = @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\FirewallRules";

        public static HashSet<string> ReadBlockedExePaths(out int ruleCount)
        {
            HashSet<string> set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            ruleCount = 0;
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(FirewallRulesKey, false))
                {
                    if (k == null) return set;
                    foreach (string name in k.GetValueNames())
                    {
                        string val = k.GetValue(name) as string;
                        if (string.IsNullOrEmpty(val)) continue;
                        if (val.IndexOf("Name=" + RulePrefix, StringComparison.OrdinalIgnoreCase) < 0) continue;
                        if (val.IndexOf("Active=TRUE", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        if (val.IndexOf("Action=Block", StringComparison.OrdinalIgnoreCase) < 0) continue;

                        string appPath = ExtractFirewallField(val, "App=");
                        if (!string.IsNullOrEmpty(appPath))
                        {
                            try { appPath = Environment.ExpandEnvironmentVariables(appPath); } catch { }
                            set.Add(appPath);
                        }
                    }
                }
                ruleCount = set.Count;
            }
            catch { }
            return set;
        }

        internal static string ExtractFirewallField(string ruleString, string fieldPrefix)
        {
            if (string.IsNullOrEmpty(ruleString) || string.IsNullOrEmpty(fieldPrefix)) return "";
            int idx = ruleString.IndexOf(fieldPrefix, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return "";
            int start = idx + fieldPrefix.Length;
            int end = ruleString.IndexOf('|', start);
            return (end > start ? ruleString.Substring(start, end - start) : ruleString.Substring(start)).Trim();
        }

        public static string BuildRuleBaseName(string exePath)
        {
            string file = Path.GetFileName(exePath ?? "unknown.exe");
            // Stable short hash suffix so two apps with the same filename in different folders don't collide
            uint hash = 2166136261u;
            string norm = (exePath ?? "").ToLowerInvariant();
            for (int i = 0; i < norm.Length; i++)
            {
                hash ^= norm[i];
                hash *= 16777619u;
            }
            return RulePrefix + file + " [" + hash.ToString("x8") + "]";
        }

        public static bool BlockExecutable(string exePath, out string detail)
        {
            detail = "";
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
            {
                detail = "Executable path not found.";
                return false;
            }
            string safePath = exePath.Replace("\"", "");
            string baseName = BuildRuleBaseName(exePath);
            string outTxt1, outTxt2;
            // Remove any stale duplicates first
            WinNative.RunHiddenProcess("netsh.exe", "advfirewall firewall delete rule name=\"" + baseName + " (Out)\"", 6000, out outTxt1);
            WinNative.RunHiddenProcess("netsh.exe", "advfirewall firewall delete rule name=\"" + baseName + " (In)\"", 6000, out outTxt2);

            int rc1 = WinNative.RunHiddenProcess("netsh.exe",
                "advfirewall firewall add rule name=\"" + baseName + " (Out)\" dir=out action=block program=\"" + safePath + "\" enable=yes profile=any",
                8000, out outTxt1);
            int rc2 = WinNative.RunHiddenProcess("netsh.exe",
                "advfirewall firewall add rule name=\"" + baseName + " (In)\" dir=in action=block program=\"" + safePath + "\" enable=yes profile=any",
                8000, out outTxt2);
            detail = (outTxt1 + " " + outTxt2).Trim();
            return rc1 == 0 && rc2 == 0;
        }

        public static bool UnblockExecutable(string exePath, out string detail)
        {
            detail = "";
            if (string.IsNullOrEmpty(exePath)) return false;
            string baseName = BuildRuleBaseName(exePath);
            string outTxt1, outTxt2;
            int rc1 = WinNative.RunHiddenProcess("netsh.exe", "advfirewall firewall delete rule name=\"" + baseName + " (Out)\"", 8000, out outTxt1);
            int rc2 = WinNative.RunHiddenProcess("netsh.exe", "advfirewall firewall delete rule name=\"" + baseName + " (In)\"", 8000, out outTxt2);
            detail = (outTxt1 + " " + outTxt2).Trim();
            return rc1 == 0 || rc2 == 0;
        }
    }

    internal sealed class NetworkEngine
    {
        private static readonly HashSet<string> CriticalProcessNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "System", "Idle", "Registry", "Memory Compression",
            "smss", "csrss", "wininit", "winlogon", "services", "lsass",
            "svchost", "dwm", "fontdrvhost", "ntoskrnl", "MsMpEng",
            "SecurityHealthService", "NisSrv", "spoolsv", "explorer"
        };

        private static readonly Dictionary<int, string> KnownPorts = BuildKnownPorts();

        private readonly Dictionary<int, string> _pidNameCache = new Dictionary<int, string>();
        private readonly Dictionary<int, string> _pidPathCache = new Dictionary<int, string>();
        private readonly int _selfPid = Process.GetCurrentProcess().Id;

        private readonly Stopwatch _speedWatch = new Stopwatch();
        private long _prevRx = -1;
        private long _prevTx = -1;
        private long _sessionRx = 0;
        private long _sessionTx = 0;
        private double _lastDownBps = 0;
        private double _lastUpBps = 0;

        private static Dictionary<int, string> BuildKnownPorts()
        {
            Dictionary<int, string> d = new Dictionary<int, string>();
            d[21] = "FTP"; d[22] = "SSH"; d[25] = "SMTP"; d[53] = "DNS";
            d[80] = "HTTP"; d[135] = "RPC"; d[139] = "NetBIOS"; d[443] = "HTTPS";
            d[445] = "SMB"; d[1433] = "MSSQL"; d[1521] = "Oracle";
            d[3000] = "Dev/Node"; d[3001] = "Dev"; d[3306] = "MySQL"; d[3389] = "RDP";
            d[4000] = "Dev"; d[5000] = "API/Flask"; d[5173] = "Vite"; d[5174] = "Vite";
            d[5353] = "mDNS"; d[5432] = "Postgres"; d[6379] = "Redis";
            d[7890] = "Proxy"; d[7891] = "Proxy"; d[7897] = "Proxy";
            d[8000] = "Dev/Py"; d[8080] = "HTTP-Alt"; d[8443] = "HTTPS-Alt";
            d[8888] = "Jupyter"; d[9000] = "Dev"; d[9090] = "Proxy-API";
            d[10808] = "SOCKS5"; d[10809] = "HTTP-Proxy"; d[11434] = "Ollama";
            d[25565] = "Minecraft"; d[27017] = "MongoDB";
            return d;
        }

        public static bool IsCriticalProcess(int pid, string processName)
        {
            if (pid <= 4) return true;
            if (string.IsNullOrEmpty(processName)) return false;
            return CriticalProcessNames.Contains(processName);
        }

        public static string LookupServiceTag(int port)
        {
            string tag;
            return KnownPorts.TryGetValue(port, out tag) ? tag : "";
        }

        private static int ConvertPort(uint netPort)
        {
            return ((int)(netPort & 0xFF) << 8) | (int)((netPort >> 8) & 0xFF);
        }

        private static string TcpStateToString(uint state)
        {
            switch (state)
            {
                case 1: return "CLOSED";
                case 2: return "LISTENING";
                case 3: return "SYN_SENT";
                case 4: return "SYN_RCVD";
                case 5: return "ESTABLISHED";
                case 6: return "FIN_WAIT1";
                case 7: return "FIN_WAIT2";
                case 8: return "CLOSE_WAIT";
                case 9: return "CLOSING";
                case 10: return "LAST_ACK";
                case 11: return "TIME_WAIT";
                case 12: return "DELETE_TCB";
                default: return "UNKNOWN";
            }
        }

        private void RefreshProcessCache()
        {
            _pidNameCache.Clear();
            _pidNameCache[0] = "Idle";
            _pidNameCache[4] = "System";

            HashSet<int> alive = new HashSet<int>();
            Process[] procs = null;
            try
            {
                procs = Process.GetProcesses();
                for (int i = 0; i < procs.Length; i++)
                {
                    try
                    {
                        int id = procs[i].Id;
                        alive.Add(id);
                        _pidNameCache[id] = procs[i].ProcessName;
                    }
                    catch { }
                    finally { procs[i].Dispose(); }
                }
            }
            catch { }

            // Prune exited PIDs from path cache
            List<int> dead = new List<int>();
            foreach (int pid in _pidPathCache.Keys)
                if (!alive.Contains(pid)) dead.Add(pid);
            foreach (int pid in dead) _pidPathCache.Remove(pid);
        }

        private void ResolveProcessMeta(int pid, out string name, out string exePath)
        {
            if (!_pidNameCache.TryGetValue(pid, out name))
                name = pid == 0 ? "Idle" : (pid == 4 ? "System" : "PID " + pid);

            if (pid <= 4) { exePath = ""; return; }
            if (!_pidPathCache.TryGetValue(pid, out exePath))
            {
                exePath = WinNative.QueryProcessImagePath(pid);
                _pidPathCache[pid] = exePath;
            }
        }

        public NetSpeedSnapshot SampleNetworkSpeed()
        {
            NetSpeedSnapshot ns = new NetSpeedSnapshot();
            long curRx = 0, curTx = 0;
            string primaryName = "";

            try
            {
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                        ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                        continue;
                    string desc = (ni.Description ?? "").ToLowerInvariant();
                    if (desc.Contains("loopback") || desc.Contains("virtualbox") || desc.Contains("vmware") ||
                        desc.Contains("hyper-v") || desc.Contains("bluetooth"))
                        continue;

                    try
                    {
                        IPInterfaceStatistics st = ni.GetIPStatistics();
                        curRx += st.BytesReceived;
                        curTx += st.BytesSent;
                        if (string.IsNullOrEmpty(primaryName)) primaryName = ni.Name;
                    }
                    catch { }
                }
            }
            catch { }

            ns.ActiveAdapter = primaryName;
            if (!_speedWatch.IsRunning)
            {
                _prevRx = curRx;
                _prevTx = curTx;
                _speedWatch.Start();
            }
            else
            {
                double secs = _speedWatch.Elapsed.TotalSeconds;
                if (secs >= 0.25)
                {
                    long dRx = Math.Max(0, curRx - _prevRx);
                    long dTx = Math.Max(0, curTx - _prevTx);
                    _lastDownBps = dRx / secs;
                    _lastUpBps = dTx / secs;
                    _sessionRx += dRx;
                    _sessionTx += dTx;
                    _prevRx = curRx;
                    _prevTx = curTx;
                    _speedWatch.Restart();
                }
                ns.DownBytesPerSec = _lastDownBps;
                ns.UpBytesPerSec = _lastUpBps;
            }
            ns.SessionDownBytes = _sessionRx;
            ns.SessionUpBytes = _sessionTx;
            return ns;
        }

        public PortSnapshot CaptureLiveSnapshot()
        {
            RefreshProcessCache();
            int blockedCount;
            HashSet<string> blockedExes = FirewallEngine.ReadBlockedExePaths(out blockedCount);

            PortSnapshot snap = new PortSnapshot();
            snap.BlockedRuleCount = blockedCount;
            snap.Speed = SampleNetworkSpeed();

            ReadTcpV4(snap.Entries, blockedExes);
            ReadTcpV6(snap.Entries, blockedExes);
            ReadUdpV4(snap.Entries, blockedExes);
            ReadUdpV6(snap.Entries, blockedExes);

            foreach (PortEntry e in snap.Entries)
            {
                if (e.State == "LISTENING") snap.ListeningCount++;
                else if (e.State == "ESTABLISHED") snap.EstablishedCount++;
                if (e.Protocol == "UDP" || e.Protocol == "UDP6") snap.UdpCount++;
            }

            // Sort: LISTENING first, then ESTABLISHED, then by local port ascending
            snap.Entries.Sort(delegate(PortEntry a, PortEntry b)
            {
                int ra = StateRank(a.State);
                int rb = StateRank(b.State);
                if (ra != rb) return ra.CompareTo(rb);
                int pc = a.LocalPort.CompareTo(b.LocalPort);
                if (pc != 0) return pc;
                return string.Compare(a.ProcessName, b.ProcessName, StringComparison.OrdinalIgnoreCase);
            });

            return snap;
        }

        private static int StateRank(string st)
        {
            if (st == "LISTENING") return 0;
            if (st == "ESTABLISHED") return 1;
            if (st == "UDP") return 2;
            return 3;
        }

        private void ReadTcpV4(List<PortEntry> target, HashSet<string> blockedExes)
        {
            int size = 0;
            WinNative.GetExtendedTcpTable(IntPtr.Zero, ref size, true, WinNative.AF_INET, WinNative.TCP_TABLE_OWNER_PID_ALL, 0);
            if (size <= 0) return;

            IntPtr buf = Marshal.AllocHGlobal(size);
            try
            {
                if (WinNative.GetExtendedTcpTable(buf, ref size, true, WinNative.AF_INET, WinNative.TCP_TABLE_OWNER_PID_ALL, 0) != 0)
                    return;
                int count = Marshal.ReadInt32(buf);
                IntPtr rowPtr = new IntPtr(buf.ToInt64() + 4);
                int rowSize = Marshal.SizeOf(typeof(WinNative.MIB_TCPROW_OWNER_PID));
                for (int i = 0; i < count; i++)
                {
                    WinNative.MIB_TCPROW_OWNER_PID row = (WinNative.MIB_TCPROW_OWNER_PID)Marshal.PtrToStructure(rowPtr, typeof(WinNative.MIB_TCPROW_OWNER_PID));
                    rowPtr = new IntPtr(rowPtr.ToInt64() + rowSize);

                    string pName, pPath;
                    int pid = (int)row.owningPid;
                    ResolveProcessMeta(pid, out pName, out pPath);
                    int lPort = ConvertPort(row.localPort);
                    int rPort = row.state == 2 ? 0 : ConvertPort(row.remotePort);

                    PortEntry e = new PortEntry();
                    e.Protocol = "TCP";
                    e.LocalAddress = new IPAddress(row.localAddr).ToString();
                    e.LocalPort = lPort;
                    e.RemoteAddress = row.state == 2 ? "" : new IPAddress(row.remoteAddr).ToString();
                    e.RemotePort = rPort;
                    e.State = TcpStateToString(row.state);
                    e.Pid = pid;
                    e.ProcessName = pName;
                    e.ExePath = pPath;
                    e.ServiceTag = LookupServiceTag(lPort);
                    if (string.IsNullOrEmpty(e.ServiceTag) && rPort > 0) e.ServiceTag = LookupServiceTag(rPort);
                    e.IsCriticalSystem = IsCriticalProcess(pid, pName) || pid == _selfPid;
                    e.IsFirewallBlocked = !string.IsNullOrEmpty(pPath) && blockedExes.Contains(pPath);
                    target.Add(e);
                }
            }
            catch { }
            finally { Marshal.FreeHGlobal(buf); }
        }

        private void ReadTcpV6(List<PortEntry> target, HashSet<string> blockedExes)
        {
            int size = 0;
            WinNative.GetExtendedTcpTable(IntPtr.Zero, ref size, true, WinNative.AF_INET6, WinNative.TCP_TABLE_OWNER_PID_ALL, 0);
            if (size <= 0) return;

            IntPtr buf = Marshal.AllocHGlobal(size);
            try
            {
                if (WinNative.GetExtendedTcpTable(buf, ref size, true, WinNative.AF_INET6, WinNative.TCP_TABLE_OWNER_PID_ALL, 0) != 0)
                    return;
                int count = Marshal.ReadInt32(buf);
                IntPtr rowPtr = new IntPtr(buf.ToInt64() + 4);
                int rowSize = Marshal.SizeOf(typeof(WinNative.MIB_TCP6ROW_OWNER_PID));
                for (int i = 0; i < count; i++)
                {
                    WinNative.MIB_TCP6ROW_OWNER_PID row = (WinNative.MIB_TCP6ROW_OWNER_PID)Marshal.PtrToStructure(rowPtr, typeof(WinNative.MIB_TCP6ROW_OWNER_PID));
                    rowPtr = new IntPtr(rowPtr.ToInt64() + rowSize);

                    string pName, pPath;
                    int pid = (int)row.owningPid;
                    ResolveProcessMeta(pid, out pName, out pPath);
                    int lPort = ConvertPort(row.localPort);
                    int rPort = row.state == 2 ? 0 : ConvertPort(row.remotePort);

                    PortEntry e = new PortEntry();
                    e.Protocol = "TCP6";
                    e.LocalAddress = new IPAddress(row.localAddr).ToString();
                    e.LocalPort = lPort;
                    e.RemoteAddress = row.state == 2 ? "" : new IPAddress(row.remoteAddr).ToString();
                    e.RemotePort = rPort;
                    e.State = TcpStateToString(row.state);
                    e.Pid = pid;
                    e.ProcessName = pName;
                    e.ExePath = pPath;
                    e.ServiceTag = LookupServiceTag(lPort);
                    if (string.IsNullOrEmpty(e.ServiceTag) && rPort > 0) e.ServiceTag = LookupServiceTag(rPort);
                    e.IsCriticalSystem = IsCriticalProcess(pid, pName) || pid == _selfPid;
                    e.IsFirewallBlocked = !string.IsNullOrEmpty(pPath) && blockedExes.Contains(pPath);
                    target.Add(e);
                }
            }
            catch { }
            finally { Marshal.FreeHGlobal(buf); }
        }

        private void ReadUdpV4(List<PortEntry> target, HashSet<string> blockedExes)
        {
            int size = 0;
            WinNative.GetExtendedUdpTable(IntPtr.Zero, ref size, true, WinNative.AF_INET, WinNative.UDP_TABLE_OWNER_PID, 0);
            if (size <= 0) return;

            IntPtr buf = Marshal.AllocHGlobal(size);
            try
            {
                if (WinNative.GetExtendedUdpTable(buf, ref size, true, WinNative.AF_INET, WinNative.UDP_TABLE_OWNER_PID, 0) != 0)
                    return;
                int count = Marshal.ReadInt32(buf);
                IntPtr rowPtr = new IntPtr(buf.ToInt64() + 4);
                int rowSize = Marshal.SizeOf(typeof(WinNative.MIB_UDPROW_OWNER_PID));
                for (int i = 0; i < count; i++)
                {
                    WinNative.MIB_UDPROW_OWNER_PID row = (WinNative.MIB_UDPROW_OWNER_PID)Marshal.PtrToStructure(rowPtr, typeof(WinNative.MIB_UDPROW_OWNER_PID));
                    rowPtr = new IntPtr(rowPtr.ToInt64() + rowSize);

                    string pName, pPath;
                    int pid = (int)row.owningPid;
                    ResolveProcessMeta(pid, out pName, out pPath);
                    int lPort = ConvertPort(row.localPort);

                    PortEntry e = new PortEntry();
                    e.Protocol = "UDP";
                    e.LocalAddress = new IPAddress(row.localAddr).ToString();
                    e.LocalPort = lPort;
                    e.RemoteAddress = "";
                    e.RemotePort = 0;
                    e.State = "UDP";
                    e.Pid = pid;
                    e.ProcessName = pName;
                    e.ExePath = pPath;
                    e.ServiceTag = LookupServiceTag(lPort);
                    e.IsCriticalSystem = IsCriticalProcess(pid, pName) || pid == _selfPid;
                    e.IsFirewallBlocked = !string.IsNullOrEmpty(pPath) && blockedExes.Contains(pPath);
                    target.Add(e);
                }
            }
            catch { }
            finally { Marshal.FreeHGlobal(buf); }
        }

        private void ReadUdpV6(List<PortEntry> target, HashSet<string> blockedExes)
        {
            int size = 0;
            WinNative.GetExtendedUdpTable(IntPtr.Zero, ref size, true, WinNative.AF_INET6, WinNative.UDP_TABLE_OWNER_PID, 0);
            if (size <= 0) return;

            IntPtr buf = Marshal.AllocHGlobal(size);
            try
            {
                if (WinNative.GetExtendedUdpTable(buf, ref size, true, WinNative.AF_INET6, WinNative.UDP_TABLE_OWNER_PID, 0) != 0)
                    return;
                int count = Marshal.ReadInt32(buf);
                IntPtr rowPtr = new IntPtr(buf.ToInt64() + 4);
                int rowSize = Marshal.SizeOf(typeof(WinNative.MIB_UDP6ROW_OWNER_PID));
                for (int i = 0; i < count; i++)
                {
                    WinNative.MIB_UDP6ROW_OWNER_PID row = (WinNative.MIB_UDP6ROW_OWNER_PID)Marshal.PtrToStructure(rowPtr, typeof(WinNative.MIB_UDP6ROW_OWNER_PID));
                    rowPtr = new IntPtr(rowPtr.ToInt64() + rowSize);

                    string pName, pPath;
                    int pid = (int)row.owningPid;
                    ResolveProcessMeta(pid, out pName, out pPath);
                    int lPort = ConvertPort(row.localPort);

                    PortEntry e = new PortEntry();
                    e.Protocol = "UDP6";
                    e.LocalAddress = new IPAddress(row.localAddr).ToString();
                    e.LocalPort = lPort;
                    e.RemoteAddress = "";
                    e.RemotePort = 0;
                    e.State = "UDP";
                    e.Pid = pid;
                    e.ProcessName = pName;
                    e.ExePath = pPath;
                    e.ServiceTag = LookupServiceTag(lPort);
                    e.IsCriticalSystem = IsCriticalProcess(pid, pName) || pid == _selfPid;
                    e.IsFirewallBlocked = !string.IsNullOrEmpty(pPath) && blockedExes.Contains(pPath);
                    target.Add(e);
                }
            }
            catch { }
            finally { Marshal.FreeHGlobal(buf); }
        }

        public static bool KillProcessSafely(int pid, string processName, out string errorMsg)
        {
            errorMsg = "";
            if (IsCriticalProcess(pid, processName) || pid == Process.GetCurrentProcess().Id)
            {
                errorMsg = "Protected OS or self process cannot be killed.";
                return false;
            }
            try
            {
                using (Process p = Process.GetProcessById(pid))
                {
                    if (!string.IsNullOrEmpty(processName) &&
                        !string.Equals(p.ProcessName, processName, StringComparison.OrdinalIgnoreCase))
                    {
                        errorMsg = "Process changed (PID reused); kill aborted for safety.";
                        return false;
                    }
                    p.Kill();
                    p.WaitForExit(2500);
                    return true;
                }
            }
            catch (Exception ex)
            {
                errorMsg = ex.Message;
                return false;
            }
        }

        public static string FormatSpeed(double bytesPerSec)
        {
            if (double.IsNaN(bytesPerSec) || double.IsInfinity(bytesPerSec) || bytesPerSec < 0) bytesPerSec = 0;
            if (bytesPerSec < 1024.0) return Loc.F("{0:0} B/s", bytesPerSec);
            double kb = bytesPerSec / 1024.0;
            if (kb < 1024.0) return Loc.F("{0:0.0} KB/s", kb);
            double mb = kb / 1024.0;
            if (mb < 1024.0) return Loc.F("{0:0.00} MB/s", mb);
            double gb = mb / 1024.0;
            return Loc.F("{0:0.00} GB/s", gb);
        }

        public static PortSnapshot BuildDemoSnapshot()
        {
            PortSnapshot s = new PortSnapshot();
            s.Speed.DownBytesPerSec = 14.6 * 1024 * 1024;
            s.Speed.UpBytesPerSec = 2.3 * 1024 * 1024;
            s.Speed.ActiveAdapter = "Ethernet";
            s.BlockedRuleCount = 1;

            Action<string, string, int, string, int, string, int, string, string, bool, bool> add =
                delegate(string pr, string la, int lp, string ra, int rp, string st, int pid, string pn, string path, bool crit, bool blk)
                {
                    PortEntry e = new PortEntry();
                    e.Protocol = pr; e.LocalAddress = la; e.LocalPort = lp;
                    e.RemoteAddress = ra; e.RemotePort = rp; e.State = st;
                    e.Pid = pid; e.ProcessName = pn; e.ExePath = path;
                    e.ServiceTag = LookupServiceTag(lp);
                    if (string.IsNullOrEmpty(e.ServiceTag) && rp > 0) e.ServiceTag = LookupServiceTag(rp);
                    e.IsCriticalSystem = crit; e.IsFirewallBlocked = blk;
                    s.Entries.Add(e);
                };

            add("TCP", "127.0.0.1", 3000, "", 0, "LISTENING", 14820, "node", @"C:\Program Files\nodejs\node.exe", false, false);
            add("TCP", "127.0.0.1", 5173, "", 0, "LISTENING", 18442, "node", @"C:\Program Files\nodejs\node.exe", false, false);
            add("TCP", "127.0.0.1", 5432, "", 0, "LISTENING", 6120, "postgres", @"C:\Program Files\PostgreSQL\16\bin\postgres.exe", false, false);
            add("TCP", "127.0.0.1", 7890, "", 0, "LISTENING", 9312, "mihomo", @"C:\Tools\Mihomo\mihomo.exe", false, false);
            add("TCP", "127.0.0.1", 11434, "", 0, "LISTENING", 11024, "ollama", @"C:\Users\Dev\AppData\Local\Programs\Ollama\ollama.exe", false, false);
            add("TCP", "192.168.1.42", 52418, "140.82.121.4", 443, "ESTABLISHED", 15904, "Code", @"C:\Users\Dev\AppData\Local\Programs\Microsoft VS Code\Code.exe", false, false);
            add("TCP", "192.168.1.42", 53011, "198.51.100.77", 443, "ESTABLISHED", 19220, "TelemetryAgent", @"C:\Program Files\VendorApp\TelemetryAgent.exe", false, true);
            add("TCP", "0.0.0.0", 445, "", 0, "LISTENING", 4, "System", "", true, false);
            add("UDP", "0.0.0.0", 5353, "", 0, "UDP", 8840, "chrome", @"C:\Program Files\Google\Chrome\Application\chrome.exe", false, false);
            add("TCP", "127.0.0.1", 6379, "", 0, "LISTENING", 7892, "redis-server", @"C:\Tools\Redis\redis-server.exe", false, false);
            add("TCP", "0.0.0.0", 8080, "", 0, "LISTENING", 21504, "python", @"C:\Python313\python.exe", false, false);
            add("TCP", "192.168.1.42", 52890, "104.26.10.233", 443, "ESTABLISHED", 8840, "chrome", @"C:\Program Files\Google\Chrome\Application\chrome.exe", false, false);

            foreach (PortEntry e in s.Entries)
            {
                if (e.State == "LISTENING") s.ListeningCount++;
                else if (e.State == "ESTABLISHED") s.EstablishedCount++;
                if (e.Protocol == "UDP" || e.Protocol == "UDP6") s.UdpCount++;
            }
            return s;
        }
    }
}
