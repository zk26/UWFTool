using System;
using System.Collections.Generic;
using System.IO;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace UwfTeacher
{
    public enum RemoteTransport
    {
        None = 0,
        Wmi = 1,
        Smb = 2
    }

    public class RemoteTarget
    {
        public string Host = "";
        public string User = "";
        public string Password = "";
    }

    public class RemoteState
    {
        public bool Ok;
        public bool UwfPresent;
        public string Error = "";
        public string Raw = "";
        public RemoteTransport Transport = RemoteTransport.None;
        public string MachineName = "";
        public DateTime At = DateTime.Now;

        public bool FilterCurrent, FilterNext;
        public bool ServicingCurrent, ServicingNext;

        /// <summary>多个 WMI 实例给出的值互相矛盾（部分 UWF 版本会返回陈旧实例）：需要文本复核。</summary>
        public bool SourceConflict;

        /// <summary>状态被按运行时事实（缓存占用）修正过时的说明文字。</summary>
        public string StateFix = "";

        public int Used = -1, Free = -1, Max = -1, Warn = -1, Crit = -1, Type = -1;

        public List<VolumeState> Volumes = new List<VolumeState>();

        /// <summary>是否真的读到了卷列表：没读到就不能断言"没有卷受保护"。</summary>
        public bool VolumeInfoKnown;

        public bool HasProtectedVolume
        {
            get
            {
                foreach (VolumeState v in Volumes) if (v.ProtectedCurrent) return true;
                return false;
            }
        }

        public bool HasProtectedVolumeNext
        {
            get
            {
                foreach (VolumeState v in Volumes) if (v.ProtectedNext) return true;
                return false;
            }
        }

        public bool ProtectedNow { get { return FilterCurrent && !ServicingCurrent; } }

        /// <summary>不还原状态：UWF 保护已关闭（filter disable），或正处于维修模式 Servicing（两种情况下改动都会永久保存）。</summary>
        public bool InMaintenance { get { return UwfPresent && (ServicingCurrent || !FilterCurrent); } }

        /// <summary>有卷的保护状态排队，需要重启一次才生效。</summary>
        public bool HasQueuedVolume
        {
            get
            {
                foreach (VolumeState v in Volumes)
                {
                    if (v.ProtectedCurrent != v.ProtectedNext) return true;
                }
                return false;
            }
        }

        /// <summary>已排队、重启后才开始保护（过滤器或卷）。</summary>
        public bool QueuedProtect
        {
            get
            {
                if (!FilterCurrent && FilterNext) return true;
                if (!HasProtectedVolume && HasProtectedVolumeNext) return true;
                foreach (VolumeState v in Volumes)
                {
                    if (!v.ProtectedCurrent && v.ProtectedNext) return true;
                }
                return false;
            }
        }

        /// <summary>给中文界面用的失败提示（把常见 WMI/共享错误翻译成可操作的建议）。</summary>
        public string ErrorHint
        {
            get
            {
                string e = Error == null ? "" : Error;
                if (e.Length == 0) return "";
                if (Contains(e, "0x80070005", "拒绝访问", "Access is denied", "Access denied"))
                    return "账号不是目标机管理员，或密码不正确。";
                if (Contains(e, "0x800706BA", "RPC 服务器不可用", "RPC server is unavailable"))
                    return "目标机未开机、防火墙拦截 WMI，或 RPC 端口不可达。";
                if (Contains(e, "0x800706BE", "RPC 服务器已停止", "RPC server is stopped"))
                    return "目标机的 WMI 服务已停止，请在目标机运行一次放行脚本。";
                if (Contains(e, "超时", "timeout", "Timeout", "timed out"))
                    return "目标机响应超时：检查它是否已开机并连上网络。";
                if (Contains(e, "无效命名空间", "Invalid namespace", "无效的命名空间"))
                    return "目标机没有 UWF 组件（需要企业版 / 教育版 / IoT 版）。";
                if (Contains(e, "找不到网络路径", "network path was not found"))
                    return "找不到目标机：检查计算机名 / IP、DNS 与文件共享是否开启。";
                if (Contains(e, "不能访问", "无法访问", "not accessible", "拒绝"))
                    return "无法访问目标机的共享：确认共享已开启、账号是管理员。";
                return "";
            }
        }

        private static bool Contains(string text, params string[] words)
        {
            foreach (string w in words)
            {
                if (text.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        public bool PendingReboot
        {
            get
            {
                if (FilterCurrent != FilterNext) return true;
                if (ServicingCurrent != ServicingNext) return true;
                foreach (VolumeState v in Volumes)
                {
                    if (v.ProtectedCurrent != v.ProtectedNext) return true;
                }
                return false;
            }
        }

        public string VolumeText
        {
            get
            {
                List<string> list = new List<string>();
                foreach (VolumeState v in Volumes)
                {
                    if (!v.ProtectedCurrent) continue;
                    if (v.Letter.Length > 0) list.Add(v.Letter);
                    else if (v.VolumeName.Length > 0) list.Add(v.VolumeName);
                }
                return list.Count == 0 ? "—" : string.Join(" ", list.ToArray());
            }
        }

        public string PendingVolumeText
        {
            get
            {
                List<string> list = new List<string>();
                foreach (VolumeState v in Volumes)
                {
                    if (!v.ProtectedNext || v.ProtectedCurrent) continue;
                    if (v.Letter.Length > 0) list.Add(v.Letter);
                    else if (v.VolumeName.Length > 0) list.Add(v.VolumeName);
                }
                return list.Count == 0 ? "—" : string.Join(" ", list.ToArray());
            }
        }

        public string TransportText
        {
            get
            {
                if (Transport == RemoteTransport.Wmi) return "WMI";
                if (Transport == RemoteTransport.Smb) return "SMB";
                return "—";
            }
        }
    }

    public static class Remote
    {
        public const string RemoteDir = @"C:\Windows\Temp\UWFTeacher";
        private const string UwfNs = @"root\standardcimv2\embedded";

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private class NetResource
        {
            public int Scope;
            public int Type;
            public int DisplayType;
            public int Usage;
            public string LocalName;
            public string RemoteName;
            public string Comment;
            public string Provider;
        }

        [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
        private static extern int WNetAddConnection2(NetResource netResource, string password, string username, int flags);

        [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
        private static extern int WNetCancelConnection2(string name, int flags, bool force);

        // ---------------------------------------------------------------- basics

        public static bool Ping(string host, int timeoutMs)
        {
            try
            {
                using (Ping p = new Ping())
                {
                    PingReply r = p.Send(host, timeoutMs);
                    return r != null && r.Status == IPStatus.Success;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 在线判断：ping 只是其中一种。机房防火墙常把 ICMP 关掉，
        /// 所以再探 SMB / RPC / 远程桌面端口，最后用 ARP 兜底（同网段即使全禁也能发现）。
        /// </summary>
        public static bool IsAlive(string host)
        {
            if (Ping(host, 400)) return true;
            int[] ports = { 445, 135, 139, 3389, 5985 };
            foreach (int port in ports)
            {
                if (TcpAlive(host, port, 260)) return true;
            }
            return ArpAlive(host);
        }

        private static bool TcpAlive(string host, int port, int timeoutMs)
        {
            TcpClient client = null;
            IAsyncResult ar = null;
            try
            {
                client = new TcpClient();
                ar = client.BeginConnect(host, port, null, null);
                if (!ar.AsyncWaitHandle.WaitOne(timeoutMs)) return false;
                client.EndConnect(ar);
                return client.Connected;
            }
            catch
            {
                return false;
            }
            finally
            {
                // BeginConnect 每次都会分配一个 OS 等待句柄，必须显式释放，
                // 否则几十台机器反复探活会把句柄数堆上去。
                if (ar != null) { try { ((IDisposable)ar.AsyncWaitHandle).Dispose(); } catch { } }
                if (client != null)
                {
                    try { client.Close(); } catch { }
                }
            }
        }

        [DllImport("iphlpapi.dll", ExactSpelling = true)]
        private static extern int SendARP(int destIp, int srcIp, byte[] macAddr, ref int phyAddrLen);

        /// <summary>同网段用 ARP 确认主机存在：不依赖目标机是否响应 ping / 端口是否开放。</summary>
        public static bool ArpAlive(string host)
        {
            try
            {
                IPAddress ip;
                if (!IPAddress.TryParse(host, out ip)) return false;
                byte[] addr = ip.GetAddressBytes();
                if (addr.Length != 4) return false;
                int dest = addr[0] | (addr[1] << 8) | (addr[2] << 16) | (addr[3] << 24);
                byte[] mac = new byte[6];
                int len = mac.Length;
                int rc = SendARP(dest, 0, mac, ref len);
                if (rc != 0) return false;
                for (int i = 0; i < len && i < mac.Length; i++) if (mac[i] != 0) return true;
                return false;
            }
            catch
            {
                return false;
            }
        }

        public static string ConnectShare(RemoteTarget t)
        {
            if (string.IsNullOrEmpty(t.User)) return "";
            try
            {
                NetResource nr = new NetResource();
                nr.Type = 0x00000001;
                nr.RemoteName = @"\\" + t.Host + @"\IPC$";
                int rc = WNetAddConnection2(nr, t.Password, t.User, 0);
                if (rc == 1219)
                {
                    // 凭据冲突：同一台机器上已有其它账号的连接。
                    // 只断开本工具自己建的 IPC$，不去 force 断别人正在用的 C$（会把资源管理器的会话一起切掉）。
                    WNetCancelConnection2(nr.RemoteName, 0, false);
                    rc = WNetAddConnection2(nr, t.Password, t.User, 0);
                }
                if (rc == 1219)
                {
                    return "无法连接 \\\\" + t.Host + "：这台机器上已有其它账号的连接（Windows 错误 1219）。" +
                           "请先在教师机断开它的共享会话，或改用与该会话相同的账号。";
                }
                if (rc != 0)
                {
                    return "无法用指定账号连接 \\\\" + t.Host + "（Windows 错误 " + rc + "）。请检查账号、密码，以及目标机是否开启了文件共享。";
                }
                return "";
            }
            catch (Exception ex)
            {
                return "连接共享失败：" + ex.Message;
            }
        }

        /// <summary>用完后释放 ConnectShare 建立的 IPC$ 连接，避免长期运行累积连接。</summary>
        public static void DisconnectShare(RemoteTarget t)
        {
            if (string.IsNullOrEmpty(t.User)) return;
            try { WNetCancelConnection2(@"\\" + t.Host + @"\IPC$", 0, true); }
            catch { }
        }

        public static string ShareRoot(RemoteTarget t)
        {
            return @"\\" + t.Host + @"\C$\Windows\Temp\UWFTeacher";
        }

        private static ManagementScope BuildScope(RemoteTarget t, string ns, int timeoutSeconds)
        {
            ConnectionOptions opt = new ConnectionOptions();
            opt.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
            if (!string.IsNullOrEmpty(t.User))
            {
                opt.Username = t.User;
                opt.Password = t.Password;
                opt.Impersonation = ImpersonationLevel.Impersonate;
                opt.EnablePrivileges = true;
                opt.Authentication = AuthenticationLevel.PacketPrivacy;
            }
            ManagementScope scope = new ManagementScope(@"\\" + t.Host + @"\" + ns, opt);
            scope.Connect();
            return scope;
        }

        // ---------------------------------------------------------------- query

        public static RemoteState Query(RemoteTarget t, int timeoutMs)
        {
            // 先快速探活（ping + 135/445/139 + ARP）：机房几十台里总有几台没开机，
            // 直接进 WMI/SMB 会每台白等几十秒，几十台叠起来就像卡死了。
            if (!IsAlive(t.Host))
            {
                RemoteState dead = new RemoteState();
                dead.Transport = RemoteTransport.None;
                dead.Error = "机器不在线：ping 和 135 / 445 / 139 端口都不通。";
                return dead;
            }

            string wmiError = "";
            RemoteState wmi = null;
            try
            {
                wmi = QueryWmi(t);
                if (wmi.Ok)
                {
                    // 状态“可疑”（显示成待重启 / 未保护磁盘）或 WMI 实例互相矛盾时，用 uwfmgr 文本复核：
                    // 文本就是管理员在机器上敲 uwfmgr get-config 看到的真相，和 WMI 不一致以文本为准。
                    // 正常（保护中）的机器不走这一步，刷新 50 台不会变慢。
                    if (wmi.UwfPresent &&
                        (wmi.SourceConflict ||
                         (!wmi.FilterCurrent && wmi.FilterNext) ||
                         (wmi.FilterCurrent && !wmi.HasProtectedVolume)))
                    {
                        VerifyFromText(t, wmi, timeoutMs);
                    }
                    // 最后一道纠错：缓存占用 > 0 = 写入正被拦截（有卷在保护），
                    // 卷状态却显示“当前没有任何卷受保护”时按运行时事实修正（部分 UWF 版本 WMI 会读到陈旧值）
                    if (Uwf.RepairVolumesFromOverlay(wmi.Volumes, wmi.FilterCurrent, wmi.Used))
                        wmi.StateFix = "卷状态已按缓存占用修正：缓存已有 " + wmi.Used + " MB 重定向写入，此刻确实在还原。";
                    return wmi;
                }
                wmiError = wmi.Error;
            }
            catch (Exception ex)
            {
                wmiError = ex.Message;
            }

            RemoteState smb = QuerySmb(t, timeoutMs);
            if (smb.Ok)
            {
                if (smb.UwfPresent &&
                    (smb.SourceConflict ||
                     (!smb.FilterCurrent && smb.FilterNext) ||
                     (smb.FilterCurrent && !smb.HasProtectedVolume)))
                {
                    VerifyFromText(t, smb, timeoutMs);
                }
                if (Uwf.RepairVolumesFromOverlay(smb.Volumes, smb.FilterCurrent, smb.Used))
                    smb.StateFix = "卷状态已按缓存占用修正：缓存已有 " + smb.Used + " MB 重定向写入，此刻确实在还原。";
                return smb;
            }

            smb.Error = "WMI：" + Ui.Short(wmiError, 100) + "\r\nSMB：" + smb.Error;
            // 两条通道都没通：不要显示成"走了 SMB"，那会误导排障
            smb.Transport = RemoteTransport.None;
            smb.UwfPresent = wmi != null && wmi.UwfPresent;
            return smb;
        }

        private static bool IsInvalidNamespace(Exception ex)
        {
            for (Exception e = ex; e != null; e = e.InnerException)
            {
                string m = e.Message ?? "";
                if (m.IndexOf("Invalid namespace", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    m.IndexOf("无效命名空间", StringComparison.Ordinal) >= 0 ||
                    m.IndexOf("无效的命名空间", StringComparison.Ordinal) >= 0) return true;
            }
            return false;
        }

        /// <summary>
        /// 目标机没有 UWF 的典型错误：命名空间不存在、类不存在、查询不到对象。
        /// 这些情况要显示为"无 UWF"，而不是"连接失败"。
        /// </summary>
        private static bool IsNoUwfError(Exception ex)
        {
            if (IsInvalidNamespace(ex)) return true;
            for (Exception e = ex; e != null; e = e.InnerException)
            {
                string m = e.Message ?? "";
                if (m.IndexOf("Invalid class", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    m.IndexOf("无效的类", StringComparison.Ordinal) >= 0 ||
                    m.IndexOf("无效类", StringComparison.Ordinal) >= 0) return true;
            }
            return false;
        }

        /// <summary>UWF 命名空间能连上不代表组件已启用：必须真的能查到 UWF_Filter。</summary>
        public static bool UwfFilterPresent(ManagementScope scope, out string error)
        {
            error = "";
            bool found = false;
            try
            {
                EachMo(scope, "SELECT * FROM UWF_Filter", delegate(ManagementObject mo)
                {
                    if (mo != null) found = true;
                });
            }
            catch (Exception ex)
            {
                error = IsNoUwfError(ex) ? "没有 UWF 组件" : ex.Message;
                return false;
            }
            if (!found) error = "没有 UWF 组件";
            return found;
        }

        /// <summary>枚举 WMI 查询结果并逐个释放（ManagementObject 持有 COM 句柄，必须 Dispose）。</summary>
        internal static void EachMo(ManagementScope scope, string query, Action<ManagementObject> handler)
        {
            using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(scope, new ObjectQuery(query)))
            using (ManagementObjectCollection results = searcher.Get())
            {
                foreach (ManagementObject mo in results)
                {
                    using (mo)
                    {
                        handler(mo);
                    }
                }
            }
        }

        private static RemoteState QueryWmi(RemoteTarget t)
        {
            RemoteState st = new RemoteState();
            st.Transport = RemoteTransport.Wmi;

            // 先连 root\cimv2：这一步成功才说明"机器在线 + 账号可用"，失败原因最准确
            ManagementScope baseScope;
            try
            {
                baseScope = BuildScope(t, @"root\cimv2", 8);
            }
            catch (Exception ex)
            {
                st.Transport = RemoteTransport.None;
                st.Error = ex.Message;
                return st;
            }

            try
            {
                EachMo(baseScope, "SELECT Name FROM Win32_ComputerSystem", delegate(ManagementObject mo)
                {
                    st.MachineName = Str(mo, "Name");
                });
            }
            catch
            {
            }

            ManagementScope scope;
            try
            {
                scope = BuildScope(t, UwfNs, 8);
            }
            catch (Exception ex)
            {
                if (IsInvalidNamespace(ex))
                {
                    // 机器在线、账号可用，只是没有 UWF：这是"无 UWF"，不是"连接失败"
                    st.UwfPresent = false;
                    st.Ok = true;
                    st.Error = "";
                    return st;
                }
                st.Error = "已连上 WMI，但打不开 UWF 接口（" + Ui.Short(ex.Message, 80) + "）。";
                return st;
            }

            bool filterFound = false;
            try
            {
                EachMo(scope, "SELECT * FROM UWF_Filter", delegate(ManagementObject mo)
                {
                    // 部分 UWF 版本返回多个实例、其中一个的值是陈旧的：
                    // 首个实例定基准，后续按“任一为真”合并，并记下矛盾（触发文本复核）。
                    bool c = Bool(mo, "CurrentEnabled");
                    bool n = Bool(mo, "NextEnabled");
                    if (!filterFound)
                    {
                        st.FilterCurrent = c;
                        st.FilterNext = n;
                    }
                    else
                    {
                        if (c != st.FilterCurrent || n != st.FilterNext) st.SourceConflict = true;
                        if (c) st.FilterCurrent = true;
                        if (n) st.FilterNext = true;
                    }
                    filterFound = true;
                });
            }
            catch (Exception ex)
            {
                if (IsNoUwfError(ex))
                {
                    st.UwfPresent = false;
                    st.Ok = true;
                    st.Error = "";
                    return st;
                }
                st.Error = "UWF 接口已连上，但读取过滤器状态失败（" + Ui.Short(ex.Message, 70) + "）。";
                return st;
            }
            if (!filterFound)
            {
                // 命名空间在、类查询成功但没有对象：系统没有启用 UWF
                st.UwfPresent = false;
                st.Ok = true;
                st.Error = "";
                return st;
            }
            st.UwfPresent = true;

            try
            {
                EachMo(scope, "SELECT * FROM UWF_Servicing", delegate(ManagementObject mo)
                {
                    if (Bool(mo, "CurrentEnabled")) st.ServicingCurrent = true;
                    if (Bool(mo, "NextEnabled")) st.ServicingNext = true;
                });
            }
            catch
            {
            }

            List<VolumeState> volumes = new List<VolumeState>();
            // 同一个卷的“当前/下次”两个实例可能一个带盘符、一个只有卷 GUID：
            // 按盘符和卷名双索引合并，避免被拆成两个条目、C: 只剩“下次”的假状态。
            Dictionary<string, VolumeState> byLetter = new Dictionary<string, VolumeState>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, VolumeState> byName = new Dictionary<string, VolumeState>(StringComparer.OrdinalIgnoreCase);
            Dictionary<VolumeState, bool[]> seen = new Dictionary<VolumeState, bool[]>();
            try
            {
                EachMo(scope, "SELECT * FROM UWF_Volume", delegate(ManagementObject mo)
                {
                    string letter = Str(mo, "DriveLetter");
                    string vn = Str(mo, "VolumeName");
                    if (letter.Length == 0 && vn.Length == 0) return;
                    VolumeState v = null;
                    if (vn.Length > 0) byName.TryGetValue(vn, out v);
                    if (v == null && letter.Length > 0) byLetter.TryGetValue(letter, out v);
                    if (v == null)
                    {
                        v = new VolumeState();
                        v.Letter = letter;
                        v.VolumeName = vn;
                        seen[v] = new bool[2];
                        volumes.Add(v);
                    }
                    if (v.Letter.Length == 0 && letter.Length > 0) v.Letter = letter;
                    if (v.VolumeName.Length == 0 && vn.Length > 0) v.VolumeName = vn;
                    if (letter.Length > 0) byLetter[letter] = v;
                    if (vn.Length > 0) byName[vn] = v;

                    bool cur = Bool(mo, "CurrentSession");
                    bool prot = Bool(mo, "Protected");
                    if (cur)
                    {
                        v.ProtectedCurrent = prot;
                        v.SawCurrentSession = true;
                        seen[v][0] = true;
                    }
                    else
                    {
                        v.ProtectedNext = prot;
                        v.SawNextSession = true;
                        seen[v][1] = true;
                    }
                });
            }
            catch
            {
            }

            // 个别 UWF 版本只返回当前会话（或只返回下次会话）的实例：
            // 缺失的一侧按已知的一侧补齐，避免"已保护却显示未保护 / 已排队"。
            foreach (VolumeState v in volumes)
            {
                bool[] s;
                if (!seen.TryGetValue(v, out s)) continue;
                if (s[0] && !s[1]) v.ProtectedNext = v.ProtectedCurrent;
                else if (!s[0] && s[1]) v.ProtectedCurrent = v.ProtectedNext;
            }
            st.Volumes = volumes;
            st.VolumeInfoKnown = volumes.Count > 0;

            try
            {
                EachMo(scope, "SELECT * FROM UWF_Overlay", delegate(ManagementObject mo)
                {
                    st.Used = (int)Num(mo, "OverlayConsumption");
                    st.Free = (int)Num(mo, "AvailableSpace");
                    st.Warn = (int)Num(mo, "WarningOverlayThreshold");
                    st.Crit = (int)Num(mo, "CriticalOverlayThreshold");
                });
            }
            catch
            {
            }
            try
            {
                EachMo(scope, "SELECT * FROM UWF_OverlayConfig", delegate(ManagementObject mo)
                {
                    st.Max = (int)Num(mo, "MaximumSize");
                    object ty = Val(mo, "Type");
                    if (ty != null) st.Type = Convert.ToInt32(ty);
                });
            }
            catch
            {
            }

            if (st.MachineName.Length == 0)
            {
                // WMI 通道读不到主机名（SMB 脚本才有 NAME=），用 DNS 反查兜底，供清单备注显示
                try
                {
                    string host = System.Net.Dns.GetHostEntry(t.Host).HostName;
                    int dot = host.IndexOf('.');
                    st.MachineName = dot > 0 ? host.Substring(0, dot) : host;
                }
                catch
                {
                }
            }

            st.Ok = true;
            st.Error = "";
            return st;
        }

        private static RemoteState QuerySmb(RemoteTarget t, int timeoutMs)
        {
            RemoteState st = new RemoteState();
            st.Transport = RemoteTransport.Smb;

            string err = ConnectShare(t);
            if (err.Length > 0)
            {
                st.Error = err;
                return st;
            }

            UwfResult r = Exec(t, EncodedPowerShell(BuildStateScript()), timeoutMs);
            st.Raw = r.All;
            if (!r.Ok)
            {
                st.Error = r.TimedOut ? "查询超时（" + (timeoutMs / 1000) + " 秒）。" :
                           "查询失败（退出码 " + r.ExitCode + "）：" + Ui.Short(r.All, 140);
                return st;
            }

            bool gotOk = false;
            foreach (string rawLine in r.All.Split('\n'))
            {
                string line = rawLine.Trim();
                if (line.StartsWith("FILTER="))
                {
                    string[] p = line.Substring(7).Split('|');
                    if (p.Length >= 2) { st.FilterCurrent = p[0] == "1"; st.FilterNext = p[1] == "1"; }
                }
                else if (line.StartsWith("SERV="))
                {
                    string[] p = line.Substring(5).Split('|');
                    if (p.Length >= 2) { st.ServicingCurrent = p[0] == "1"; st.ServicingNext = p[1] == "1"; }
                }
                else if (line.StartsWith("OVL="))
                {
                    string[] p = line.Substring(4).Split('|');
                    int a;
                    if (p.Length >= 1 && int.TryParse(p[0], out a)) st.Used = a;
                    if (p.Length >= 2 && int.TryParse(p[1], out a)) st.Free = a;
                    if (p.Length >= 3 && int.TryParse(p[2], out a)) st.Max = a;
                    if (p.Length >= 4 && int.TryParse(p[3], out a)) st.Type = a;
                }
                else if (line.StartsWith("VOL="))
                {
                    st.UwfPresent = true;
                    st.VolumeInfoKnown = true;
                    string[] p = line.Substring(4).Split('|');
                    // 新格式：盘符|卷名|当前会话|保护    旧格式：盘符|当前会话|保护
                    string letter = "", vn = "", cur = "", prot = "";
                    if (p.Length >= 4) { letter = p[0].Trim(); vn = p[1].Trim(); cur = p[2]; prot = p[3]; }
                    else if (p.Length >= 3) { letter = p[0].Trim(); cur = p[1]; prot = p[2]; }
                    if (cur.Length > 0)
                    {
                        // 盘符 + 卷名双索引合并：两个实例可能一个带盘符、一个只有卷 GUID
                        VolumeState v = null;
                        foreach (VolumeState x in st.Volumes)
                        {
                            if (letter.Length > 0 && string.Equals(x.Letter, letter, StringComparison.OrdinalIgnoreCase)) { v = x; break; }
                            if (vn.Length > 0 && string.Equals(x.VolumeName, vn, StringComparison.OrdinalIgnoreCase)) { v = x; break; }
                        }
                        if (v == null)
                        {
                            v = new VolumeState();
                            v.Letter = letter;
                            v.VolumeName = vn;
                            st.Volumes.Add(v);
                        }
                        if (v.Letter.Length == 0) v.Letter = letter;
                        if (v.VolumeName.Length == 0) v.VolumeName = vn;
                        bool isProt = prot == "1";
                        if (cur == "1") { v.ProtectedCurrent = isProt; v.SawCurrentSession = true; }
                        else { v.ProtectedNext = isProt; v.SawNextSession = true; }
                    }
                }
                else if (line == "OK=1")
                {
                    gotOk = true;
                    st.UwfPresent = true;
                }
                else if (line.StartsWith("NAME="))
                {
                    st.MachineName = line.Substring(5).Trim();
                }
                else if (line.StartsWith("ERR="))
                {
                    st.Error = "目标机读取 UWF 失败（" + Ui.Short(line.Substring(4), 60) + "）。";
                }
            }

            // 个别 UWF 版本只返回一侧实例：缺的那侧按已知侧补齐，避免"已保护却显示未保护 / 已排队"
            foreach (VolumeState v in st.Volumes)
            {
                if (v.SawCurrentSession && !v.SawNextSession) v.ProtectedNext = v.ProtectedCurrent;
                else if (!v.SawCurrentSession && v.SawNextSession) v.ProtectedCurrent = v.ProtectedNext;
            }

            if (!gotOk)
            {
                if (st.Error.Length == 0) st.Error = "目标机返回内容无法识别。";
                return st;
            }
            st.Ok = true;
            st.Error = "";
            return st;
        }

        /// <summary>
        /// 只取 uwfmgr 文本状态的脚本（用于复核）：
        /// WMI 读出来的“排队 / 未保护”和真实状态经常对不上，而老师核对的是 uwfmgr get-config。
        /// </summary>
        public static string BuildTextStateScript()
        {
            StringBuilder ps = new StringBuilder();
            ps.AppendLine("$ErrorActionPreference='Continue'");
            ps.AppendLine("$cfg = (& uwfmgr.exe get-config 2>&1) -join ' ~ '");
            ps.AppendLine("'CFG=' + $cfg");
            ps.AppendLine("foreach ($di in [System.IO.DriveInfo]::GetDrives()) {");
            ps.AppendLine("  if ($di.DriveType -ne 'Fixed') { continue }");
            ps.AppendLine("  $lt = $di.Name.Substring(0,2)");
            ps.AppendLine("  $t = (& uwfmgr.exe volume get-config $lt 2>&1) -join ' ~ '");
            ps.AppendLine("  if ($t) { 'VCFG=' + $lt + '|' + $t }");
            ps.AppendLine("}");
            return ps.ToString();
        }

        /// <summary>找卷条目：按盘符，找不到再按卷名。</summary>
        private static VolumeState FindVolume(RemoteState st, string letter, string vn)
        {
            foreach (VolumeState v in st.Volumes)
            {
                if (letter.Length > 0 && string.Equals(v.Letter, letter, StringComparison.OrdinalIgnoreCase)) return v;
                if (vn.Length > 0 && string.Equals(v.VolumeName, vn, StringComparison.OrdinalIgnoreCase)) return v;
            }
            return null;
        }

        /// <summary>用 uwfmgr 文本复核过滤器与卷状态；文本解析出来的值覆盖 WMI（文本是管理员看到的真相）。</summary>
        private static void VerifyFromText(RemoteTarget t, RemoteState st, int timeoutMs)
        {
            UwfResult r = Exec(t, EncodedPowerShell(BuildTextStateScript()), Math.Min(timeoutMs, 40000));
            if (!r.Ok || r.All.Length == 0) return;

            foreach (string raw in r.All.Split('\n'))
            {
                string line = raw.Trim();
                if (line.StartsWith("CFG="))
                {
                    bool? cur, next;
                    Uwf.ParseFilterStateText(line.Substring(4).Replace(" ~ ", "\n"), out cur, out next);
                    if (cur.HasValue) st.FilterCurrent = cur.Value;
                    if (next.HasValue) st.FilterNext = next.Value;
                }
                else if (line.StartsWith("VCFG="))
                {
                    string body = line.Substring(5);
                    int bar = body.IndexOf('|');
                    if (bar <= 0) continue;
                    string letter = body.Substring(0, bar).Trim();
                    string text = body.Substring(bar + 1).Replace(" ~ ", "\n");
                    bool? cur, next;
                    Uwf.ParseVolumeText(text, out cur, out next);
                    VolumeState v = FindVolume(st, letter, "");
                    if (v == null)
                    {
                        v = new VolumeState();
                        v.Letter = letter;
                        st.Volumes.Add(v);
                        st.VolumeInfoKnown = true;
                    }
                    if (cur.HasValue) { v.ProtectedCurrent = cur.Value; v.SawCurrentSession = true; }
                    if (next.HasValue) { v.ProtectedNext = next.Value; v.SawNextSession = true; }
                }
            }
        }

        public static string BuildStateScript()
        {
            StringBuilder ps = new StringBuilder();
            ps.AppendLine("$ErrorActionPreference='Stop'");
            ps.AppendLine("try {");
            ps.AppendLine("'NAME=' + $env:COMPUTERNAME");
            ps.AppendLine("$ns='root/standardcimv2/embedded'");
            ps.AppendLine("$f=Get-CimInstance -Namespace $ns -Class UWF_Filter");
            ps.AppendLine("'FILTER=' + [int]$f.CurrentEnabled + '|' + [int]$f.NextEnabled");
            ps.AppendLine("$s=Get-CimInstance -Namespace $ns -Class UWF_Servicing");
            ps.AppendLine("'SERV=' + [int]$s.CurrentEnabled + '|' + [int]$s.NextEnabled");
            ps.AppendLine("$o=Get-CimInstance -Namespace $ns -Class UWF_Overlay");
            ps.AppendLine("$oc=Get-CimInstance -Namespace $ns -Class UWF_OverlayConfig");
            ps.AppendLine("'OVL=' + $o.OverlayConsumption + '|' + $o.AvailableSpace + '|' + $oc.MaximumSize + '|' + $oc.Type");
            ps.AppendLine("Get-CimInstance -Namespace $ns -Class UWF_Volume | ForEach-Object { 'VOL=' + $_.DriveLetter + '|' + $_.VolumeName + '|' + [int]$_.CurrentSession + '|' + [int]$_.Protected }");
            ps.AppendLine("'OK=1'");
            ps.AppendLine("} catch { 'ERR=' + $_.Exception.GetType().Name }");
            return ps.ToString();
        }

        public static string EncodedPowerShell(string script)
        {
            string b64 = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            return "powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + b64;
        }

        // ---------------------------------------------------------------- smb exec

        public static UwfResult Exec(RemoteTarget t, string command, int timeoutMs)
        {
            UwfResult r = new UwfResult();
            r.Command = command;

            string auth = ConnectShare(t);
            if (auth.Length > 0)
            {
                r.StdErr = auth;
                r.ExitCode = -14;
                return r;
            }

            try
            {
                string root = ShareRoot(t);
                try
                {
                    Directory.CreateDirectory(root);
                }
                catch (Exception ex)
                {
                    r.StdErr = "无法访问 \\\\" + t.Host + "\\C$（" + ex.Message + "）。目标机需开启“文件和打印机共享”，且当前账号需是它的管理员。";
                    r.ExitCode = -10;
                    return r;
                }

                string id = Guid.NewGuid().ToString("N").Substring(0, 8);
                string runName = "run_" + id + ".cmd";
                string outName = "out_" + id + ".txt";
                string rcName = "rc_" + id + ".txt";
                string doneName = "done_" + id + ".txt";

                string script =
                    "@echo off\r\n" +
                    "chcp 936 >nul 2>&1\r\n" +
                    command + " > \"C:\\Windows\\Temp\\UWFTeacher\\" + outName + "\" 2>&1\r\n" +
                    // 注意：重定向必须写成 "> 文件 echo …" 或 "echo … > 文件"（> 前有空格），
                    // 否则 cmd 会把紧邻的个位数字当作文件句柄（echo 0> f），退出码永远读不回来。
                    "> \"C:\\Windows\\Temp\\UWFTeacher\\" + rcName + "\" echo %ERRORLEVEL%\r\n" +
                    "> \"C:\\Windows\\Temp\\UWFTeacher\\" + doneName + "\" echo 1\r\n";

                try
                {
                    File.WriteAllText(Path.Combine(root, runName), script, ScriptEncoding());
                }
                catch (Exception ex)
                {
                    r.StdErr = "无法写入远程脚本：" + ex.Message;
                    r.ExitCode = -11;
                    return r;
                }

                string err;
                uint pid = 0;
                StartRemoteProcess(t, "cmd.exe /c \"C:\\Windows\\Temp\\UWFTeacher\\" + runName + "\"", out err, out pid);
                if (err.Length > 0)
                {
                    Cleanup(root, id);   // 启动失败也要清掉目标机上的脚本，别把命令明文留在 Temp 里
                    r.StdErr = err;
                    r.ExitCode = -12;
                    return r;
                }

                DateTime deadline = DateTime.Now.AddMilliseconds(timeoutMs);
                string done = Path.Combine(root, doneName);
                while (DateTime.Now < deadline)
                {
                    try
                    {
                        if (File.Exists(done))
                        {
                            string rcText;
                            try { rcText = File.ReadAllText(Path.Combine(root, rcName)).Trim(); }
                            catch { rcText = ""; }
                            int rc;
                            if (!int.TryParse(rcText, out rc)) rc = -1;
                            r.ExitCode = rc;
                            try { r.StdOut = File.ReadAllText(Path.Combine(root, outName), Uwf.OemEncoding()); }
                            catch { r.StdOut = ""; }
                            Cleanup(root, id);
                            return r;
                        }
                    }
                    catch
                    {
                    }
                    System.Threading.Thread.Sleep(300);
                }

                Cleanup(root, id);
                if (pid != 0)
                {
                    // 超时后把目标机上的 cmd 进程树杀掉：否则 uwfmgr 还在跑，
                    // 老师重试时会撞到“另一个操作正在进行”。
                    try
                    {
                        string killErr;
                        uint ignored;
                        StartRemoteProcess(t, "taskkill.exe /PID " + pid + " /T /F", out killErr, out ignored);
                    }
                    catch
                    {
                    }
                }
                r.TimedOut = true;
                r.StdErr = "等待目标机返回超时（" + (timeoutMs / 1000) + " 秒）。";
                r.ExitCode = -13;
                return r;
            }
            finally
            {
                DisconnectShare(t);
            }
        }

        /// <summary>远程 .cmd 固定按 GBK 写入：脚本里是 chcp 936，用教师机的 ANSI（英文系统 1252）写会把中文路径变问号。</summary>
        private static Encoding ScriptEncoding()
        {
            try { return Encoding.GetEncoding(936); }
            catch { return Encoding.Default; }
        }

        /// <summary>路径等字面量进 .cmd 时把 % 转义成 %%，否则会被 cmd 当变量展开。</summary>
        private static string CmdText(string text)
        {
            return text == null ? "" : text.Replace("%", "%%");
        }

        private static void Cleanup(string root, string id)
        {
            try { File.Delete(Path.Combine(root, "run_" + id + ".cmd")); } catch { }
            try { File.Delete(Path.Combine(root, "out_" + id + ".txt")); } catch { }
            try { File.Delete(Path.Combine(root, "rc_" + id + ".txt")); } catch { }
            try { File.Delete(Path.Combine(root, "done_" + id + ".txt")); } catch { }
        }

        /// <summary>直接启动一个程序（不等待、不重定向输出），用于交互式安装包。</summary>
        public static UwfResult LaunchAndWait(RemoteTarget t, string commandLine, int timeoutMs)
        {
            UwfResult r = new UwfResult();
            r.Command = commandLine;
            string err;
            uint pid = 0;
            StartRemoteProcess(t, commandLine, out err, out pid);
            if (err.Length > 0)
            {
                r.StdErr = err;
                r.ExitCode = -12;
                return r;
            }
            string waitScript = "$ErrorActionPreference='Stop'; $p=" + pid + "; $sw=[Diagnostics.Stopwatch]::StartNew(); " +
                                "while($sw.ElapsedMilliseconds -lt " + timeoutMs + "){ " +
                                "$proc=Get-CimInstance Win32_Process -Filter ('ProcessId=' + $p) -ErrorAction SilentlyContinue; " +
                                "if(-not $proc){ 'DONE=1'; exit 0 }; Start-Sleep -Seconds 2 }; 'TIMEOUT=1'; exit 124";
            UwfResult wait = Exec(t, EncodedPowerShell(waitScript), timeoutMs + 30000);
            r.StdOut = wait.All;
            r.ExitCode = wait.Ok ? 0 : (wait.TimedOut ? -13 : wait.ExitCode);
            r.TimedOut = wait.TimedOut || wait.ExitCode == 124;
            if (r.TimedOut) r.StdErr = "等待学生机安装程序退出超时（" + (timeoutMs / 60000) + " 分钟）。";
            return r;
        }

        /// <summary>启动远程进程并带回 PID：后面靠它做“还在装吗”的心跳，而不是干等 30 分钟。</summary>
        public static bool StartProcess(RemoteTarget t, string commandLine, out string error, out uint pid)
        {
            StartRemoteProcess(t, commandLine, out error, out pid);
            return error.Length == 0 && pid != 0;
        }

        /// <summary>进程是否还在：1 = 在，0 = 已退出，-1 = 查不到（网络/WMI 抖动，不等于退出）。</summary>
        public static int ProcessAlive(RemoteTarget t, uint pid)
        {
            if (pid == 0) return 0;
            try
            {
                ManagementScope scope = BuildScope(t, @"root\cimv2", 10);
                using (ManagementObjectSearcher s = new ManagementObjectSearcher(scope,
                    new ObjectQuery("SELECT ProcessId FROM Win32_Process WHERE ProcessId=" + pid)))
                using (ManagementObjectCollection list = s.Get())
                {
                    foreach (ManagementObject mo in list)
                    {
                        mo.Dispose();
                        return 1;
                    }
                    return 0;
                }
            }
            catch
            {
                return -1;
            }
        }

        /// <summary>
        /// 等远程进程退出，边等边回报“已用多少秒”，并且随时可以取消（取消时顺手杀掉学生机上的进程树）。
        /// 用“直接启动进程 + 轮询 PID”的方式，不用 cmd 包一层：
        /// cmd 包装会等整个控制台进程组，安装包一旦拉起子进程就会连着子进程一起挂住，
        /// 表现就是“安装永远不结束”。直接盯安装包自己的 PID，它退出就是退出。
        /// </summary>
        public static UwfResult WaitProcess(RemoteTarget t, uint pid, int timeoutMs, Action<int> heartbeat,
                                            Func<bool> cancelled)
        {
            UwfResult r = new UwfResult();
            r.Command = "wait pid " + pid;
            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
            int errors = 0;
            int lastBeat = -1;

            while (true)
            {
                if (cancelled != null && cancelled())
                {
                    // 取消时把安装包连子进程一起杀掉，别在学生机上留个看不见的安装窗口
                    try
                    {
                        string killErr;
                        uint ignored;
                        StartRemoteProcess(t, "taskkill.exe /PID " + pid + " /T /F", out killErr, out ignored);
                    }
                    catch { }
                    r.ExitCode = -15;
                    r.StdErr = "已取消（学生机上的安装进程已结束）。";
                    return r;
                }

                int alive = ProcessAlive(t, pid);
                if (alive == 0)
                {
                    r.ExitCode = 0;
                    r.StdOut = "安装程序已退出（用时 " + (int)sw.Elapsed.TotalSeconds + " 秒）";
                    return r;
                }
                if (alive < 0)
                {
                    errors++;
                    if (errors >= 5)
                    {
                        r.ExitCode = -1;
                        r.StdErr = "连续多次查不到安装进程（网络或 WMI 中断），无法确认是否安装完成。请到学生机上看一眼。";
                        return r;
                    }
                }
                else errors = 0;

                if (sw.ElapsedMilliseconds >= timeoutMs)
                {
                    r.ExitCode = -13;
                    r.TimedOut = true;
                    r.StdErr = "等待安装程序退出超时（" + (timeoutMs / 60000) + " 分钟）。安装可能还在学生机上进行，请到机器上看，或先别恢复还原。";
                    return r;
                }

                int sec = (int)sw.Elapsed.TotalSeconds;
                if (heartbeat != null && sec / 5 != lastBeat)
                {
                    lastBeat = sec / 5;
                    try { heartbeat(sec); } catch { }
                }
                System.Threading.Thread.Sleep(500);
            }
        }

        /// <summary>兼容旧签名：不取消。</summary>
        public static UwfResult WaitProcess(RemoteTarget t, uint pid, int timeoutMs, Action<int> heartbeat)
        {
            return WaitProcess(t, pid, timeoutMs, heartbeat, null);
        }

        // ---------------------------------------------------------------- installer run（带回传输出）

        /// <summary>
        /// 以“登录用户”的会话启动安装程序（安装界面出现在学生机屏幕上），并把输出与退出码写进文件。
        /// 只用于「以登录用户运行」；默认安装走 StartProcess + WaitProcess（直接盯安装包自己的 PID）。
        /// 注意：这里用批处理跑安装包——批处理只等它启动的那个进程，不会被安装包拉起的子进程连累；
        /// 等待也只认批处理写出来的 done 标记，不去等计划任务的“运行中”状态（那个会被子进程拖住）。
        /// </summary>
        public static bool StartInstallInteractive(RemoteTarget t, string commandLine, string workDir,
                                                   out string reportId, out uint outerPid, out string error)
        {
            reportId = "";
            outerPid = 0;
            error = "";
            if (string.IsNullOrEmpty(workDir)) workDir = @"C:\Windows\Temp\UWFTeacher\deploy";

            string auth = ConnectShare(t);
            if (auth.Length > 0) { error = auth; return false; }
            try
            {
                string root = ShareRoot(t);
                try { Directory.CreateDirectory(root); }
                catch (Exception ex) { error = "无法访问 \\\\" + t.Host + "\\C$（" + ex.Message + "）。"; return false; }

                string id = Guid.NewGuid().ToString("N").Substring(0, 8);
                string outName = "inst_" + id + ".out";     // 安装包自己的输出
                string rcName = "inst_" + id + ".rc";       // 安装包的退出码
                string doneName = "inst_" + id + ".done";   // 安装包跑完的标记（批处理在安装包退出后写）
                string runName = "inst_" + id + ".cmd";     // 外层脚本（我们启动它）
                string innerName = "inst_" + id + "_inner.cmd";
                string psName = "inst_" + id + ".ps1";
                string psLogName = "inst_" + id + ".pslog";

                // 内层（在计划任务里、以登录用户身份执行）：切到安装包目录 → 运行 → 写输出/退出码/done
                string inner =
                    "@echo off\r\n" +
                    "chcp 936 >nul 2>&1\r\n" +
                    "cd /d \"" + CmdText(workDir) + "\" 2>nul\r\n" +
                    commandLine + " > \"C:\\Windows\\Temp\\UWFTeacher\\" + outName + "\" 2>&1\r\n" +
                    "> \"C:\\Windows\\Temp\\UWFTeacher\\" + rcName + "\" echo %ERRORLEVEL%\r\n" +
                    "> \"C:\\Windows\\Temp\\UWFTeacher\\" + doneName + "\" echo 1\r\n";

                // 外层：把内层排进计划任务（Interactive 令牌，不需要密码，只在用户已登录时运行），
                // 然后只等 done 标记——不等任务状态，避免被安装包的子进程拖住
                string ps =
                    "$ErrorActionPreference='Continue'\r\n" +
                    "$u=(Get-CimInstance Win32_ComputerSystem -ErrorAction SilentlyContinue).UserName\r\n" +
                    "if(-not $u){ 'NO-LOGON-USER'; exit 3 }\r\n" +
                    "$n='UWFt-" + id + "'\r\n" +
                    "$a=New-ScheduledTaskAction -Execute 'cmd.exe' -Argument ('/c \"C:\\Windows\\Temp\\UWFTeacher\\" + innerName + "\"')\r\n" +
                    "$p=New-ScheduledTaskPrincipal -UserId $u -LogonType Interactive -RunLevel Highest\r\n" +
                    "Register-ScheduledTask -TaskName $n -Action $a -Principal $p -Force | Out-Null\r\n" +
                    "Start-ScheduledTask -TaskName $n\r\n" +
                    "$deadline=(Get-Date).AddMinutes(30)\r\n" +
                    "while((Get-Date) -lt $deadline){ " +
                    "if(Test-Path 'C:\\Windows\\Temp\\UWFTeacher\\" + doneName + "'){ break }; Start-Sleep -Seconds 2 }\r\n" +
                    "Unregister-ScheduledTask -TaskName $n -Confirm:$false -ErrorAction SilentlyContinue\r\n" +
                    "'TASK-END'\r\n";
                File.WriteAllText(Path.Combine(root, psName), ps, new UTF8Encoding(true));

                string outer =
                    "@echo off\r\n" +
                    "chcp 936 >nul 2>&1\r\n" +
                    "powershell.exe -NoProfile -ExecutionPolicy Bypass -File \"C:\\Windows\\Temp\\UWFTeacher\\" + psName + "\" > \"C:\\Windows\\Temp\\UWFTeacher\\" + psLogName + "\" 2>&1\r\n";

                try
                {
                    File.WriteAllText(Path.Combine(root, innerName), inner, ScriptEncoding());
                    File.WriteAllText(Path.Combine(root, runName), outer, ScriptEncoding());
                }
                catch (Exception ex)
                {
                    error = "无法写入远程脚本：" + ex.Message;
                    return false;
                }

                string startErr;
                StartRemoteProcess(t, "cmd.exe /c \"C:\\Windows\\Temp\\UWFTeacher\\" + runName + "\"", out startErr, out outerPid);
                if (startErr.Length > 0)
                {
                    error = startErr;
                    try { CleanupInstall(root, id); } catch { }
                    return false;
                }
                reportId = id;
                return true;
            }
            finally
            {
                DisconnectShare(t);
            }
        }

        /// <summary>
        /// 等安装跑完（done 标记出现），随后读回安装包的输出与退出码，写进返回值。
        /// 边等边回报已用秒数；随时可取消（取消时结束计划任务并杀掉外层脚本进程树）。
        /// </summary>
        public static UwfResult WaitInstall(RemoteTarget t, string reportId, uint outerPid, int timeoutMs,
                                            Action<int> heartbeat, Func<bool> cancelled)
        {
            UwfResult r = new UwfResult();
            r.Command = "install " + reportId;

            string auth = ConnectShare(t);
            if (auth.Length > 0) { r.StdErr = auth; r.ExitCode = -14; return r; }
            try
            {
                string root = ShareRoot(t);
                string doneFile = Path.Combine(root, "inst_" + reportId + ".done");
                string outFile = Path.Combine(root, "inst_" + reportId + ".out");
                string rcFile = Path.Combine(root, "inst_" + reportId + ".rc");
                string psLog = Path.Combine(root, "inst_" + reportId + ".pslog");

                System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
                int lastBeat = -1;
                while (true)
                {
                    if (cancelled != null && cancelled())
                    {
                        try
                        {
                            string e1, e2;
                            uint u;
                            StartRemoteProcess(t, "schtasks.exe /end /tn UWFt-" + reportId, out e1, out u);
                            if (outerPid != 0) StartRemoteProcess(t, "taskkill.exe /PID " + outerPid + " /T /F", out e2, out u);
                        }
                        catch { }
                        r.ExitCode = -15;
                        r.StdErr = "已取消（学生机上的安装进程已结束）。";
                        return r;
                    }

                    bool done = false;
                    try { done = File.Exists(doneFile); } catch { }
                    if (done) break;

                    if (sw.ElapsedMilliseconds >= timeoutMs)
                    {
                        r.TimedOut = true;
                        r.ExitCode = -13;
                        r.StdErr = "等待安装脚本结束超时（" + (timeoutMs / 60000) + " 分钟）。安装可能仍在学生机上进行，请到机器上确认，先别恢复还原。";
                        return r;
                    }

                    int sec = (int)sw.Elapsed.TotalSeconds;
                    if (heartbeat != null && sec / 5 != lastBeat)
                    {
                        lastBeat = sec / 5;
                        try { heartbeat(sec); } catch { }
                    }
                    System.Threading.Thread.Sleep(500);
                }

                string output = "";
                string rcText = "";
                string psOut = "";
                try { output = File.ReadAllText(outFile, Uwf.OemEncoding()); } catch { }
                try { rcText = File.ReadAllText(rcFile).Trim(); } catch { }
                try { psOut = File.ReadAllText(psLog, Uwf.OemEncoding()); } catch { }

                int rc;
                if (int.TryParse(rcText, out rc)) r.ExitCode = rc;
                else
                {
                    r.ExitCode = -1;
                    // 退出码都没写出来：要么安装包把 cmd 打崩了，要么交互模式里根本没有登录用户
                    r.StdErr = psOut.IndexOf("NO-LOGON-USER", StringComparison.OrdinalIgnoreCase) >= 0
                        ? "学生机上当前没有登录的用户，无法以登录用户身份启动。让一台学生机登录后再试，或去掉“以登录用户身份运行”。"
                        : (psOut.Length > 0 ? UiShort(psOut, 400) : "拿不到安装程序的退出码。");
                }
                r.StdOut = output;
                if (output.Length == 0 && psOut.Length > 0) r.StdOut = UiShort(psOut, 400);

                try { CleanupInstall(root, reportId); } catch { }
                return r;
            }
            finally
            {
                DisconnectShare(t);
            }
        }

        private static string UiShort(string s, int max)
        {
            if (s == null) return "";
            s = s.Trim();
            return s.Length <= max ? s : s.Substring(0, max) + "…";
        }

        private static void CleanupInstall(string root, string id)
        {
            string[] names = { "inst_" + id + ".cmd", "inst_" + id + "_inner.cmd", "inst_" + id + ".ps1",
                               "inst_" + id + ".out", "inst_" + id + ".rc", "inst_" + id + ".done", "inst_" + id + ".pslog" };
            foreach (string n in names) { try { File.Delete(Path.Combine(root, n)); } catch { } }
        }

        public static UwfResult Launch(RemoteTarget t, string commandLine)
        {
            UwfResult r = new UwfResult();
            r.Command = commandLine;
            string err;
            StartRemoteProcess(t, commandLine, out err);
            if (err.Length > 0)
            {
                r.StdErr = err;
                r.ExitCode = -12;
            }
            else
            {
                r.StdOut = "已启动";
            }
            return r;
        }

        private static void StartRemoteProcess(RemoteTarget t, string commandLine, out string error)
        {
            uint ignoredPid;
            StartRemoteProcess(t, commandLine, out error, out ignoredPid);
        }

        private static void StartRemoteProcess(RemoteTarget t, string commandLine, out string error, out uint processId)
        {
            error = "";
            processId = 0;
            try
            {
                ManagementScope scope = BuildScope(t, @"root\cimv2", 10);
                using (ManagementClass mc = new ManagementClass(scope, new ManagementPath("Win32_Process"), null))
                {
                    using (ManagementBaseObject inParams = mc.GetMethodParameters("Create"))
                    {
                        inParams["CommandLine"] = commandLine;
                        using (ManagementBaseObject outParams = mc.InvokeMethod("Create", inParams, null))
                        {
                            uint ret = 0;
                            try { ret = Convert.ToUInt32(outParams["ReturnValue"]); } catch { }
                            try { processId = Convert.ToUInt32(outParams["ProcessId"]); } catch { }
                            if (ret != 0)
                            {
                                error = "目标机拒绝执行命令（WMI 返回码 " + ret + "）。请确认账号是目标机管理员，且目标机开启了 WMI。";
                                return;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                error = "无法连接目标机的 WMI（" + ex.Message + "）。请确认目标机已开机、防火墙允许 WMI/RPC、账号有管理员权限。";
                return;
            }
            return;
        }

        // ---------------------------------------------------------------- actions

        /// <summary>verb: enable / disable / restart / shutdown</summary>
        public static UwfResult Action(RemoteTarget t, string verb, RemoteState st)
        {
            string method;
            string className = "UWF_Filter";
            switch (verb)
            {
                case "enable": method = "Enable"; break;
                case "disable": method = "Disable"; break;
                case "restart": method = "RestartSystem"; break;
                case "shutdown": method = "ShutdownSystem"; break;
                case "servicing-enable": method = "Enable"; className = "UWF_Servicing"; break;
                case "servicing-disable": method = "Disable"; className = "UWF_Servicing"; break;
                default: throw new ArgumentException("unknown verb " + verb);
            }

            if (st != null && st.Ok && st.Transport == RemoteTransport.Wmi)
            {
                try
                {
                    UwfResult wr = WmiCall(t, "SELECT * FROM " + className, method);
                    if (wr.Ok) return wr;
                }
                catch
                {
                }
            }

            bool powerOff = verb == "restart" || verb == "shutdown";
            string command = verb == "servicing-enable" ? "uwfmgr.exe servicing enable" :
                             verb == "servicing-disable" ? "uwfmgr.exe servicing disable" :
                             "uwfmgr.exe filter " + verb;
            UwfResult r = Exec(t, command, powerOff ? 30000 : 90000);
            if (powerOff && r.TimedOut)
            {
                // 目标机往往在把结果写回之前就已重启/关机：命令已启动却等不到 done 文件，
                // 这种超时视为已提交，否则批量重启会被大面积误标为失败。
                r.TimedOut = false;
                r.ExitCode = 0;
                r.StdOut = "已提交（目标机未及返回结果，视为" + (verb == "restart" ? "重启" : "关机") + "已开始）";
            }
            return r;
        }

        private static UwfResult WmiCall(RemoteTarget t, string query, string method)
        {
            UwfResult r = new UwfResult();
            r.Command = "WMI " + method;
            ManagementScope scope = BuildScope(t, UwfNs, 10);
            using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(scope, new ObjectQuery(query)))
            using (ManagementObjectCollection results = searcher.Get())
            {
                foreach (ManagementObject mo in results)
                {
                    using (mo)
                    {
                        object ret = mo.InvokeMethod(method, null);
                        uint code = 0;
                        try { code = Convert.ToUInt32(ret); } catch { }
                        r.ExitCode = (int)code;
                        r.StdOut = code == 0 ? "已提交" : "返回码 " + code;
                        return r;
                    }
                }
            }
            r.ExitCode = -1;
            r.StdErr = "目标机上没有找到对应的 UWF 对象。";
            return r;
        }

        public static UwfResult AddFileExclusion(RemoteTarget t, string path)
        {
            return Exec(t, "uwfmgr.exe file add-exclusion \"" + CmdText(path) + "\"", 90000);
        }

        public static UwfResult RemoveFileExclusion(RemoteTarget t, string path)
        {
            return Exec(t, "uwfmgr.exe file remove-exclusion \"" + CmdText(path) + "\"", 90000);
        }

        public static UwfResult AddRegExclusion(RemoteTarget t, string key)
        {
            return Exec(t, "uwfmgr.exe registry add-exclusion \"" + CmdText(key) + "\"", 90000);
        }

        public static UwfResult RemoveRegExclusion(RemoteTarget t, string key)
        {
            return Exec(t, "uwfmgr.exe registry remove-exclusion \"" + CmdText(key) + "\"", 90000);
        }

        // ---------------------------------------------------------------- files / details

        /// <summary>把本地安装包推到目标机默认目录（C:\Windows\Temp\UWFTeacher\deploy），返回目标机上的完整路径。</summary>
        public static string PushFile(RemoteTarget t, string localFile, out string error)
        {
            return PushFileTo(t, localFile, @"C:\Windows\Temp\UWFTeacher\deploy", out error);
        }

        /// <summary>把本地文件复制到目标机 remoteDir（C:\... 形式），返回目标机上的完整路径。</summary>
        public static string PushFileTo(RemoteTarget t, string localFile, string remoteDir, out string error)
        {
            return PushFileTo(t, localFile, remoteDir, out error, null);
        }

        /// <summary>
        /// 同上，但边复制边回报字节进度。
        /// 几百 MB 的安装包走 SMB 要几分钟，没有进度看起来就像卡死（老师端只剩一个 0 / 1 台）。
        /// </summary>
        public static string PushFileTo(RemoteTarget t, string localFile, string remoteDir, out string error,
                                        Action<long, long> progress)
        {
            error = "";
            string auth = ConnectShare(t);
            if (auth.Length > 0) { error = auth; return null; }
            try
            {
                string name = Path.GetFileName(localFile);
                try
                {
                    string destDir = UncOf(t, remoteDir.TrimEnd('\\'));
                    Directory.CreateDirectory(destDir);
                    string destFile = Path.Combine(destDir, name);
                    CopyFileWithProgress(localFile, destFile, progress);

                    // 复制完核对大小：SMB 中断有时不报错，装到一半的安装包反而更难排查
                    try
                    {
                        long srcLen = new FileInfo(localFile).Length;
                        long dstLen = new FileInfo(destFile).Length;
                        if (srcLen != dstLen)
                        {
                            error = "复制不完整：" + dstLen + " / " + srcLen + " 字节（网络中断？请重试）";
                            return null;
                        }
                    }
                    catch (Exception ex)
                    {
                        error = "复制后无法核对文件大小：" + ex.Message;
                        return null;
                    }
                    return remoteDir.TrimEnd('\\') + "\\" + name;
                }
                catch (Exception ex)
                {
                    error = "分发失败：" + ex.Message;
                    return null;
                }
            }
            finally
            {
                DisconnectShare(t);
            }
        }

        /// <summary>分块复制并回调进度（块 1 MB，最多每 0.8 秒回调一次，避免刷爆 UI 线程）。</summary>
        private static void CopyFileWithProgress(string src, string dst, Action<long, long> progress)
        {
            const int bufSize = 1024 * 1024;
            long total = 0;
            try { total = new FileInfo(src).Length; } catch { }

            using (FileStream input = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufSize, FileOptions.SequentialScan))
            using (FileStream output = new FileStream(dst, FileMode.Create, FileAccess.Write, FileShare.None, bufSize))
            {
                byte[] buffer = new byte[bufSize];
                long done = 0;
                DateTime last = DateTime.MinValue;
                DateTime lastMove = DateTime.Now;
                int lastPct = -1;
                while (true)
                {
                    // 目标机中途掉线时 SMB 写会一直挂着：3 分钟没有任何进展就当作失败，别让界面永远卡在 0%
                    if ((DateTime.Now - lastMove).TotalSeconds > 180)
                        throw new IOException("复制超过 3 分钟没有任何进展（学生机可能掉线、磁盘已满或被关机）");

                    int read = input.Read(buffer, 0, buffer.Length);
                    if (read <= 0) break;
                    output.Write(buffer, 0, read);
                    done += read;
                    lastMove = DateTime.Now;

                    int pct = total > 0 ? (int)(done * 100 / total) : -1;
                    if (progress != null && (pct != lastPct || (DateTime.Now - last).TotalMilliseconds >= 800))
                    {
                        last = DateTime.Now;
                        lastPct = pct;
                        try { progress(done, total); } catch { }
                    }
                }
                output.Flush();
                if (progress != null) { try { progress(done, total > 0 ? total : done); } catch { } }
            }
        }

        /// <summary>把本地目录（含全部子目录与文件）递归复制到目标机 remoteParent 下的同名目录，返回目标机上的完整路径。</summary>
        public static string PushDirectory(RemoteTarget t, string localDir, string remoteParent, out string error)
        {
            return PushDirectory(t, localDir, remoteParent, out error, null);
        }

        /// <summary>同上，但边复制边回报“已复制多少个文件 / 多少字节”——大目录复制几分钟没动静就像卡死。</summary>
        public static string PushDirectory(RemoteTarget t, string localDir, string remoteParent, out string error,
                                           Action<int, long> progress)
        {
            error = "";
            string auth = ConnectShare(t);
            if (auth.Length > 0) { error = auth; return null; }
            try
            {
                string name = new DirectoryInfo(localDir).Name;
                string remoteDir = remoteParent.TrimEnd('\\') + "\\" + name;
                try
                {
                    int[] fileCount = new int[1];
                    long[] byteCount = new long[1];
                    DateTime[] lastBeat = new DateTime[] { DateTime.MinValue };
                    CopyTree(localDir, UncOf(t, remoteDir), delegate
                    {
                        fileCount[0]++;
                        if (progress != null && (DateTime.Now - lastBeat[0]).TotalMilliseconds >= 800)
                        {
                            lastBeat[0] = DateTime.Now;
                            try { progress(fileCount[0], byteCount[0]); } catch { }
                        }
                    }, byteCount);
                    if (progress != null) { try { progress(fileCount[0], byteCount[0]); } catch { } }
                    return remoteDir;
                }
                catch (Exception ex)
                {
                    error = "分发目录失败：" + ex.Message;
                    return null;
                }
            }
            finally
            {
                DisconnectShare(t);
            }
        }

        private static void CopyTree(string src, string dst, Action onFile, long[] byteCount)
        {
            Directory.CreateDirectory(dst);
            foreach (string f in Directory.GetFiles(src))
            {
                File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
                try { byteCount[0] += new FileInfo(f).Length; } catch { }
                if (onFile != null) onFile();
            }
            foreach (string d in Directory.GetDirectories(src))
            {
                CopyTree(d, Path.Combine(dst, Path.GetFileName(d)), onFile, byteCount);
            }
        }

        /// <summary>目标机本地路径 → 本机可访问的 UNC：C:\Windows\x → \\主机\C$\Windows\x。</summary>
        public static string UncOf(RemoteTarget t, string remotePath)
        {
            if (remotePath != null && remotePath.Length >= 2 && remotePath[1] == ':')
            {
                return @"\\" + t.Host + @"\" + remotePath[0] + "$" + remotePath.Substring(2);
            }
            return remotePath;
        }

        public class RemoteShortcut
        {
            public string LinkPath = "";    // 目标机上的 .lnk 完整路径，如 C:\Users\Public\Desktop\Foo.lnk
            public string TargetPath = "";
            public string WorkDir = "";
            public string IconPath = "";
            public string Description = "";
        }

        /// <summary>
        /// 在目标机创建快捷方式：在教师机本地用 WScript.Shell 生成 .lnk，
        /// 再经管理共享复制到目标机——不往学生机写脚本、不需要学生端常驻程序。
        /// </summary>
        public static UwfResult CreateShortcut(RemoteTarget t, RemoteShortcut s)
        {
            UwfResult r = new UwfResult();
            r.Command = "创建快捷方式 " + s.LinkPath;
            string tmp = Path.Combine(Path.GetTempPath(), "uwflnk_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".lnk");
            object ws = null;
            object sc = null;
            try
            {
                Type wsType = Type.GetTypeFromProgID("WScript.Shell");
                if (wsType == null) throw new InvalidOperationException("本机缺少 WScript.Shell 组件");
                ws = Activator.CreateInstance(wsType);
                sc = wsType.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, ws, new object[] { tmp });
                Type scType = sc.GetType();
                scType.InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, sc, new object[] { s.TargetPath });
                if (s.WorkDir.Length > 0) scType.InvokeMember("WorkingDirectory", System.Reflection.BindingFlags.SetProperty, null, sc, new object[] { s.WorkDir });
                if (s.IconPath.Length > 0) scType.InvokeMember("IconLocation", System.Reflection.BindingFlags.SetProperty, null, sc, new object[] { s.IconPath });
                if (s.Description.Length > 0) scType.InvokeMember("Description", System.Reflection.BindingFlags.SetProperty, null, sc, new object[] { s.Description });
                scType.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, sc, null);
            }
            catch (Exception ex)
            {
                r.StdErr = "生成快捷方式失败：" + ex.Message;
                r.ExitCode = -15;
                TryDelete(tmp);
                return r;
            }
            finally
            {
                try { if (sc != null && System.Runtime.InteropServices.Marshal.IsComObject(sc)) System.Runtime.InteropServices.Marshal.ReleaseComObject(sc); } catch { }
                try { if (ws != null && System.Runtime.InteropServices.Marshal.IsComObject(ws)) System.Runtime.InteropServices.Marshal.ReleaseComObject(ws); } catch { }
            }

            string auth = ConnectShare(t);
            if (auth.Length > 0)
            {
                r.StdErr = auth;
                r.ExitCode = -14;
                TryDelete(tmp);
                return r;
            }
            try
            {
                string dest = UncOf(t, s.LinkPath);
                string destDir = Path.GetDirectoryName(dest);
                if (destDir != null && destDir.Length > 0) Directory.CreateDirectory(destDir);
                File.Copy(tmp, dest, true);
                r.StdOut = "快捷方式已创建：" + s.LinkPath;
            }
            catch (Exception ex)
            {
                r.StdErr = "复制快捷方式到目标机失败：" + ex.Message;
                r.ExitCode = -16;
            }
            finally
            {
                DisconnectShare(t);
                TryDelete(tmp);
            }
            return r;
        }

        private static void TryDelete(string path)
        {
            try { File.Delete(path); } catch { }
        }

        public static List<string> FetchFileExclusions(RemoteTarget t, RemoteState st)
        {
            List<string> list = new List<string>();
            if (st != null && st.Ok && st.Transport == RemoteTransport.Wmi)
            {
                try
                {
                    ManagementScope scope = BuildScope(t, UwfNs, 10);
                    EachMo(scope, "SELECT * FROM UWF_Volume WHERE CurrentSession=FALSE", delegate(ManagementObject mo)
                    {
                        using (ManagementBaseObject outp = mo.InvokeMethod("GetExclusions", null, null))
                        {
                            list.AddRange(ReadStringArray(outp, "ExcludedFiles", "FileName"));
                        }
                    });
                    if (list.Count > 0) return list;
                }
                catch
                {
                }
            }

            UwfResult r = Exec(t, "uwfmgr.exe file get-exclusions all", 90000);
            foreach (string raw in r.All.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length < 3) continue;
                if (Regex.IsMatch(line, @"^[A-Za-z]:\\") || (line.StartsWith(@"\") && line.Length > 2))
                {
                    if (!list.Contains(line)) list.Add(line);
                }
            }
            return list;
        }

        public static List<string> FetchRegExclusions(RemoteTarget t)
        {
            List<string> list = new List<string>();
            UwfResult r = Exec(t, "uwfmgr.exe registry get-exclusions", 90000);
            foreach (string raw in r.All.Split('\n'))
            {
                string line = raw.Trim();
                if (Regex.IsMatch(line, @"^(HKLM|HKCU|HKEY_LOCAL_MACHINE|HKEY_CURRENT_USER)\\", RegexOptions.IgnoreCase))
                {
                    if (!list.Contains(line)) list.Add(line);
                }
            }
            return list;
        }

        public static List<string> ReadStringArray(ManagementBaseObject result, string arrayProp, string itemProp)
        {
            List<string> list = new List<string>();
            if (result == null) return list;
            object value = null;
            try { value = result[arrayProp]; } catch { }
            Array arr = value as Array;
            if (arr == null) return list;
            foreach (object item in arr)
            {
                ManagementBaseObject mbo = item as ManagementBaseObject;
                if (mbo != null)
                {
                    object v = null;
                    try { v = mbo[itemProp]; } catch { }
                    if (v == null)
                    {
                        foreach (PropertyData pd in mbo.Properties)
                        {
                            if (pd.Value is string) { v = pd.Value; break; }
                        }
                    }
                    if (v != null) list.Add(Convert.ToString(v));
                }
                else if (item != null)
                {
                    list.Add(Convert.ToString(item));
                }
            }
            return list;
        }

        // ---------------------------------------------------------------- discovery probe

        public class ProbeResult
        {
            public string Host = "";
            public string Name = "";
            public bool WmiOk;
            public bool UwfOk;
            public bool NoUwf;
            public bool ReadFailed;
            public bool FilterCurrent;
            public bool Servicing;
            public bool Protected;
            public string Error = "";
        }

        /// <summary>轻量探测：只走 WMI，不做 SMB 回退，用于扫描网段时快速判断是不是 UWF 机器。</summary>
        public static ProbeResult Probe(RemoteTarget t, int timeoutSeconds)
        {
            ProbeResult p = new ProbeResult();
            p.Host = t.Host;

            ManagementScope baseScope;
            try
            {
                baseScope = BuildScope(t, @"root\cimv2", timeoutSeconds);
                p.WmiOk = true;
            }
            catch (Exception ex)
            {
                p.Error = Ui.Short(ex.Message, 100);
                return p;
            }

            try
            {
                EachMo(baseScope, "SELECT Name FROM Win32_ComputerSystem", delegate(ManagementObject mo)
                {
                    p.Name = Str(mo, "Name");
                });
            }
            catch
            {
            }

            try
            {
                ManagementScope uwf = BuildScope(t, UwfNs, timeoutSeconds);
                bool found = false;
                try
                {
                    EachMo(uwf, "SELECT * FROM UWF_Filter", delegate(ManagementObject mo)
                    {
                        p.FilterCurrent = Bool(mo, "CurrentEnabled");
                        found = true;
                    });
                }
                catch (Exception ex)
                {
                    if (IsNoUwfError(ex))
                    {
                        p.NoUwf = true;
                        p.Error = "未安装 UWF 组件";
                        return p;
                    }
                    throw;
                }
                if (!found)
                {
                    p.NoUwf = true;
                    p.Error = "未安装 UWF 组件";
                    return p;
                }
                p.UwfOk = true;
                try
                {
                    EachMo(uwf, "SELECT * FROM UWF_Servicing", delegate(ManagementObject mo)
                    {
                        p.Servicing = Bool(mo, "CurrentEnabled");
                    });
                }
                catch
                {
                }
                p.Protected = p.FilterCurrent && !p.Servicing;
            }
            catch (Exception ex)
            {
                if (IsNoUwfError(ex))
                {
                    p.NoUwf = true;
                    p.Error = "未安装 UWF 组件";
                }
                else
                {
                    p.ReadFailed = true;
                    p.Error = "已连上 WMI，但读不到 UWF（" + Ui.Short(ex.Message, 70) + "）";
                }
            }
            return p;
        }
        // ---------------------------------------------------------------- diagnosis

        public static string Diagnose(RemoteTarget t)
        {
            StringBuilder sb = new StringBuilder();
            bool ping = Ping(t.Host, 1200);
            sb.AppendLine((ping ? "√" : "×") + " 网络可达（ping）：" + (ping ? "通" : "不通（有些机器禁 ping，下面再看端口/共享）"));

            string share = ConnectShare(t);
            bool smb = false;
            try
            {
                smb = Directory.Exists(@"\\" + t.Host + @"\C$\Windows");
            }
            catch
            {
            }
            finally
            {
                DisconnectShare(t);
            }
            sb.AppendLine((smb ? "√" : "×") + " 管理共享 \\\\" + t.Host + "\\C$：" +
                          (smb ? "可访问" : "不可访问" + (share.Length > 0 ? "（" + share + "）" : "（共享未开启 / 防火墙拦截 / 账号不是管理员）")));

            bool wmi = false;
            string wmiError = "";
            try
            {
                BuildScope(t, @"root\cimv2", 8);
                wmi = true;
            }
            catch (Exception ex)
            {
                wmiError = ex.Message;
            }
            sb.AppendLine((wmi ? "√" : "×") + " WMI 连接：" + (wmi ? "正常" : "失败（" + Ui.Short(wmiError, 70) + "）"));

            bool uwf = false;
            bool noUwf = false;
            string uwfError = "";
            if (wmi)
            {
                try
                {
                    ManagementScope uwfScope = BuildScope(t, UwfNs, 8);
                    string fe;
                    if (UwfFilterPresent(uwfScope, out fe)) uwf = true;
                    else if (fe == "没有 UWF 组件") noUwf = true;
                    else uwfError = fe;
                }
                catch (Exception ex)
                {
                    if (IsNoUwfError(ex)) noUwf = true;
                    else uwfError = ex.Message;
                }
            }
            if (uwf) sb.AppendLine("√ 远程 UWF 接口：可用（走 WMI 快通道）");
            else if (noUwf) sb.AppendLine("— 远程 UWF 接口：目标机没有 UWF 组件（不是企业版 / 教育版，或未启用）");
            else sb.AppendLine("— 远程 UWF 接口：不可用（将改用 SMB 通道）" + (uwfError.Length > 0 ? "（" + Ui.Short(uwfError, 70) + "）" : ""));

            RemoteState st = Query(t, 40000);
            sb.AppendLine((st.Ok ? "√" : "×") + " 状态读取：" +
                          (st.Ok ? "成功，通道 " + st.TransportText + (st.UwfPresent ? "" : "，目标机没有 UWF") : "失败（" + Ui.Short(st.Error, 90) + "）"));

            string hint = st.Ok ? "" : st.ErrorHint;
            if (hint.Length > 0)
            {
                sb.AppendLine();
                sb.AppendLine("建议：" + hint);
            }
            if (!smb && !wmi)
            {
                sb.AppendLine();
                sb.AppendLine("建议：在目标机上以管理员身份执行一次 tools\\启用远程管理.cmd，然后重启目标机。");
            }
            return sb.ToString();
        }

        // ---------------------------------------------------------------- helpers

        private static object Val(ManagementObject mo, string name)
        {
            try { return mo[name]; }
            catch { return null; }
        }

        private static string Str(ManagementObject mo, string name)
        {
            object v = Val(mo, name);
            return v == null ? "" : Convert.ToString(v).Trim();
        }

        private static bool Bool(ManagementObject mo, string name)
        {
            object v = Val(mo, name);
            if (v == null) return false;
            try { return Convert.ToBoolean(v); } catch { return false; }
        }

        private static long Num(ManagementObject mo, string name)
        {
            object v = Val(mo, name);
            if (v == null) return 0;
            try { return Convert.ToInt64(v); } catch { return 0; }
        }
    }
}
