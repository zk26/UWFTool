using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Net.NetworkInformation;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace UwfTeacher
{
    /// <summary>一条放行检查项。</summary>
    public class AuditItem
    {
        public string Name = "";
        public string Desc = "";
        public string Category = MasterAudit.CatRemote;
        public bool Required = true;
        public Func<string> Test;
    }

    /// <summary>一条放行检查结果。Ok=false 时 Message 是原因。</summary>
    public class AuditResult
    {
        public string Name = "";
        public string Desc = "";
        public string Category = "";
        public bool Required = true;
        public bool Ok;
        public string Message = "";
    }

    /// <summary>整体结论：远程管理是否放行、母盘是否就绪。</summary>
    public class AuditVerdict
    {
        public bool RemoteReady;
        public bool MasterReady;
        public string Headline = "";
        public string Detail = "";
        public List<string> Warnings = new List<string>();
    }

    /// <summary>
    /// 母盘放行检查：所有判断都基于"实际能力"而不是表面配置——
    /// 共享按 Win32_Share 实际共享名判断，防火墙按端口放行判断（不依赖中英文规则名），
    /// 并且区分"需要放行"和"建议项"，避免把正常机器误判成没放行。
    /// </summary>
    public static class MasterAudit
    {
        public const string CatRemote = "远程管理";
        public const string CatMaster = "母盘保护";

        private static readonly object _sync = new object();
        private static List<AuditResult> _last = new List<AuditResult>();
        private static DateTime _lastAt = DateTime.MinValue;

        public static List<AuditResult> Last
        {
            get { lock (_sync) return new List<AuditResult>(_last); }
        }

        public static DateTime LastAt { get { return _lastAt; } }

        public static bool HasFresh
        {
            get { return _lastAt != DateTime.MinValue && (DateTime.Now - _lastAt).TotalMinutes < 10; }
        }

        // ---------------------------------------------------------------- 检查项

        public static List<AuditItem> BuildItems()
        {
            UwfState held = null;
            Func<UwfState> state = delegate
            {
                if (held == null) held = Uwf.Read();
                return held;
            };

            List<AuditItem> list = new List<AuditItem>();

            Add(list, "管理员权限", "本工具需要管理员权限才能读取服务、共享和防火墙设置", true,
                delegate { return Uwf.IsAdmin() ? null : "当前不是以管理员身份运行"; });

            Add(list, "SMB 文件共享服务", "「Server」服务运行中，别的机器才能访问共享、分发文件", true,
                delegate { return TestService("LanmanServer", "SMB 文件共享"); });

            Add(list, "C$ 管理共享", "实际检查 \\\\本机\\C$ 是否已共享，远程分发文件靠它", true, TestAdminShare);

            Add(list, "WMI 管理服务", "「Windows Management Instrumentation」服务运行中，远程读状态靠它", true,
                delegate { return TestService("Winmgmt", "WMI"); });

            Add(list, "本机 WMI 连接", "本机 root\\cimv2 可连接，说明 WMI 提供程序没有损坏", true,
                delegate { return WithTimeout(delegate { return TestWmiLocal(@"root\cimv2"); }, 4000); });

            Add(list, "防火墙入站放行", "按端口检查：445（文件共享）、135（WMI / RPC）是否允许入站", true, TestFirewall);

            Add(list, "允许 Ping（建议）", "只影响「搜索学生机」的第一层判断；不做也不影响远程管理", false, TestIcmp);

            Add(list, "远程管理员令牌", "工作组环境需要 LocalAccountTokenFilterPolicy=1，否则远程访问 C$ 会被 UAC 拦掉", true,
                TestTokenPolicy);

            Add(list, "UWF 组件已安装", "母盘要能还原，必须装有 uwfmgr.exe 与 UWF 服务", false,
                delegate
                {
                    UwfState s = state();
                    if (s.Installed) return null;
                    return "没装 UWF（需企业版 / 教育版 / IoT）";
                }, CatMaster);

            Add(list, "UWF 远程接口", "能查到 UWF_Filter 才说明这台机器真的启用了 UWF，远程才能开关还原", false,
                delegate { return WithTimeout(TestUwfRemoteInterface, 4000); }, CatMaster);

            Add(list, "UWF 保护已开启", "母盘当前必须处于保护状态，克隆出去的机器才会还原", false,
                delegate
                {
                    UwfState s = state();
                    if (!s.Installed) return "本机没有 UWF 组件";
                    if (s.FilterCurrent && !s.ServicingCurrent) return null;
                    if (s.FilterCurrent && s.ServicingCurrent)
                        return "处于维修模式（Windows 更新专用，不还原），请先退出维修模式并重启再做母盘";
                    if (!s.FilterCurrent && s.FilterNext) return "保护已排队，重启后开启";
                    return "UWF 保护未开启（被关闭保护了，需重新启用并重启）";
                }, CatMaster);

            Add(list, "系统盘在保护列表", "只开过滤器、不加保护卷，写入会直接落盘，重启不会还原", false,
                delegate
                {
                    UwfState s = state();
                    if (!s.Installed) return "本机没有 UWF 组件";
                    string sys = SystemVolume();
                    foreach (VolumeState v in s.Volumes)
                    {
                        if (string.Equals(Uwf.NormalizeVolume(v.Letter), sys, StringComparison.OrdinalIgnoreCase))
                        {
                            if (v.ProtectedCurrent) return null;
                            if (v.ProtectedNext)
                                return "系统盘 " + sys + " 的保护还在排队（重启一次才生效）——现在对拷，克隆出去的机器也全是未保护状态，先重启再做母盘";
                            return "系统盘 " + sys + " 没有被保护";
                        }
                    }
                    return "保护列表里没有系统盘 " + sys;
                }, CatMaster);

            return list;
        }

        private static string SystemVolume()
        {
            try
            {
                string root = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
                if (!string.IsNullOrEmpty(root) && root.Length >= 2) return root.Substring(0, 2);
            }
            catch
            {
            }
            return "C:";
        }

        private static void Add(List<AuditItem> list, string name, string desc, bool required, Func<string> test)
        {
            Add(list, name, desc, required, test, CatRemote);
        }

        private static void Add(List<AuditItem> list, string name, string desc, bool required, Func<string> test, string category)
        {
            AuditItem item = new AuditItem();
            item.Name = name;
            item.Desc = desc;
            item.Required = required;
            item.Test = test;
            item.Category = category == null ? CatRemote : category;
            list.Add(item);
        }

        // ---------------------------------------------------------------- 执行

        /// <summary>逐项执行检查（可指定每项完成后的回调），结果同时写入缓存。</summary>
        public static List<AuditResult> RunAll(Action<AuditResult> onEach)
        {
            List<AuditItem> items = BuildItems();
            List<AuditResult> results = new List<AuditResult>();
            foreach (AuditItem item in items)
            {
                AuditResult r = RunOne(item);
                results.Add(r);
                if (onEach != null)
                {
                    try { onEach(r); }
                    catch { }
                }
            }
            lock (_sync)
            {
                _last = new List<AuditResult>(results);
                _lastAt = DateTime.Now;
            }
            Log.Info("母盘放行检查：" + Verdict(results).Headline);
            return results;
        }

        public static List<AuditResult> RunAll()
        {
            return RunAll(null);
        }

        private static AuditResult RunOne(AuditItem item)
        {
            AuditResult r = new AuditResult();
            r.Name = item.Name;
            r.Desc = item.Desc;
            r.Category = item.Category;
            r.Required = item.Required;
            string err = null;
            try
            {
                err = item.Test == null ? null : item.Test();
            }
            catch (Exception ex)
            {
                err = ex.Message;
            }
            r.Ok = err == null;
            r.Message = err == null ? "已放行" : err;
            return r;
        }

        public static AuditVerdict Verdict(List<AuditResult> results)
        {
            AuditVerdict v = new AuditVerdict();
            int needPass = 0, needTotal = 0, masterPass = 0, masterTotal = 0;
            List<string> requiredFails = new List<string>();
            List<string> masterFails = new List<string>();
            List<string> optionalWarns = new List<string>();
            foreach (AuditResult r in results)
            {
                if (string.Equals(r.Category, CatMaster, StringComparison.Ordinal))
                {
                    masterTotal++;
                    if (r.Ok) masterPass++;
                    else masterFails.Add(r.Name);
                }
                else
                {
                    if (r.Required) needTotal++;
                    if (r.Ok)
                    {
                        if (r.Required) needPass++;
                    }
                    else if (r.Required)
                    {
                        requiredFails.Add(r.Name);
                    }
                    else
                    {
                        optionalWarns.Add(r.Name);
                    }
                }
                if (!r.Ok) v.Warnings.Add(r.Name + "：" + r.Message);
            }

            v.RemoteReady = needPass == needTotal;
            v.MasterReady = v.RemoteReady && masterPass == masterTotal;

            if (v.MasterReady)
            {
                v.Headline = "全部就绪，可以做母盘";
            }
            else if (v.RemoteReady)
            {
                v.Headline = "远程管理已放行，但母盘保护还有 " + masterFails.Count + " 项未就绪";
            }
            else
            {
                v.Headline = "还有 " + requiredFails.Count + " 项远程管理未放行（" + needPass + "/" + needTotal + "）";
            }

            StringBuilder detail = new StringBuilder();
            if (requiredFails.Count > 0)
            {
                detail.Append("需要处理：" + JoinNames(requiredFails));
            }
            else if (masterFails.Count > 0)
            {
                detail.Append("母盘建议：" + JoinNames(masterFails));
            }
            else if (optionalWarns.Count > 0)
            {
                detail.Append("可选建议：" + JoinNames(optionalWarns) + "（不影响远程管理）");
            }
            else
            {
                detail.Append("本机的共享 / WMI / 防火墙 / 账号权限都已到位。");
            }
            v.Detail = detail.ToString();
            return v;
        }

        private static string JoinNames(List<string> names)
        {
            int n = Math.Min(names.Count, 4);
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < n; i++)
            {
                if (i > 0) sb.Append("、");
                sb.Append(names[i]);
            }
            if (names.Count > n) sb.Append(" 等 " + names.Count + " 项");
            return sb.ToString();
        }

        // ---------------------------------------------------------------- 单项检查

        private static string TestService(string serviceName, string display)
        {
            try
            {
                using (ServiceController sc = new ServiceController(serviceName))
                {
                    ServiceControllerStatus st = sc.Status;
                    if (st == ServiceControllerStatus.Running) return null;
                    if (st == ServiceControllerStatus.StartPending) return null;
                    return display + "服务未运行（" + ServiceStatusText(st) + "）";
                }
            }
            catch (Exception ex)
            {
                return "读不到服务状态：" + ex.Message;
            }
        }

        private static string ServiceStatusText(ServiceControllerStatus st)
        {
            switch (st)
            {
                case ServiceControllerStatus.Stopped: return "已停止";
                case ServiceControllerStatus.StartPending: return "正在启动";
                case ServiceControllerStatus.StopPending: return "正在停止";
                case ServiceControllerStatus.Paused: return "已暂停";
                case ServiceControllerStatus.PausePending: return "正在暂停";
                case ServiceControllerStatus.ContinuePending: return "正在继续";
                default: return st.ToString();
            }
        }

        /// <summary>用 Win32_Share 实际确认 C$ 存在（比读注册表更接近真实情况）。</summary>
        private static string TestAdminShare()
        {
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                    new ObjectQuery("SELECT Name FROM Win32_Share WHERE Name='C$'")))
                using (ManagementObjectCollection list = searcher.Get())
                {
                    foreach (ManagementObject mo in list)
                    {
                        using (mo)
                        {
                            return null;
                        }
                    }
                }
                return "C$ 管理共享不存在";
            }
            catch (Exception ex)
            {
                // Win32_Share 读不到时退回注册表，避免把权限问题误判成"共享不存在"
                try
                {
                    using (RegistryKey k = Registry.LocalMachine.OpenSubKey(
                        @"SYSTEM\CurrentControlSet\Services\LanmanServer\Shares"))
                    {
                        if (k == null) return "读不到共享信息：" + Short(ex.Message, 40);
                        foreach (string n in k.GetValueNames())
                        {
                            if (string.Equals(n, "C$", StringComparison.OrdinalIgnoreCase)) return null;
                        }
                    }
                    return "C$ 管理共享不存在";
                }
                catch
                {
                    return "读不到共享信息：" + Short(ex.Message, 40);
                }
            }
        }

        private static string TestWmiLocal(string ns)
        {
            try
            {
                ManagementScope scope = new ManagementScope(@"\\.\" + ns);
                scope.Connect();
                return scope.IsConnected ? null : "WMI 连接失败";
            }
            catch (Exception ex)
            {
                string msg = Short(ex.Message, 60);
                if (msg.IndexOf("Invalid namespace", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    msg.IndexOf("无效命名空间", StringComparison.Ordinal) >= 0 ||
                    msg.IndexOf("无效的命名空间", StringComparison.Ordinal) >= 0)
                {
                    return ns.IndexOf("embedded", StringComparison.OrdinalIgnoreCase) >= 0
                        ? "UWF 命名空间不存在（组件未启用）"
                        : "WMI 命名空间不存在";
                }
                return msg;
            }
        }

        /// <summary>UWF 命名空间可能存在但组件没启用，必须真的能查到 UWF_Filter 才算放行。</summary>
        private static string TestUwfRemoteInterface()
        {
            try
            {
                ManagementScope scope = new ManagementScope(@"\\.\root\standardcimv2\embedded");
                scope.Connect();
                string err;
                if (Remote.UwfFilterPresent(scope, out err)) return null;
                if (err == "没有 UWF 组件") return "查不到 UWF_Filter：本机没有启用 UWF";
                return err;
            }
            catch (Exception ex)
            {
                string msg = Short(ex.Message, 60);
                if (msg.IndexOf("Invalid namespace", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    msg.IndexOf("无效命名空间", StringComparison.Ordinal) >= 0 ||
                    msg.IndexOf("无效的命名空间", StringComparison.Ordinal) >= 0)
                    return "UWF 命名空间不存在（组件未启用）";
                return msg;
            }
        }

        // ---------------------------------------------------------------- 防火墙

        private class RuleInfo
        {
            public bool Inbound;
            public bool Allow;
            public bool Active;
            public int Protocol = 6;
            public string Ports = "";
            public int Profiles = 0x7FFFFFFF;
            public string Name = "";
        }

        /// <summary>
        /// 按端口判断入站是否放行，不依赖规则的中英文名称。
        /// 先看当前网络配置文件的防火墙状态，再要求该配置文件下确实有放行 445 / 135 的规则。
        /// </summary>
        private static string TestFirewall()
        {
            List<int> active = ActiveFirewallProfiles();
            List<RuleInfo> rules = ReadFirewallRules();
            if (rules == null)
            {
                if (active.Count == 0) return null;
                string msg = "读不到本机防火墙规则（可能由域策略统一托管）";
                if (IsDomainJoined()) return msg + "；域环境请用「抽一台实测放行」验证";
                return msg;
            }
            if (active.Count == 0) return null;

            List<string> missing = new List<string>();
            foreach (int profile in active)
            {
                if (!HasRule(rules, profile, 445) && !HasRule(rules, profile, 139))
                    missing.Add(ProfileName(profile) + "缺 445");
                if (!HasRule(rules, profile, 135) && !HasRpcRule(rules, profile))
                    missing.Add(ProfileName(profile) + "缺 135/RPC");
            }
            if (missing.Count == 0) return null;
            return string.Join("；", missing.ToArray());
        }

        private static string TestIcmp()
        {
            List<int> active = ActiveFirewallProfiles();
            if (active.Count == 0) return null;
            List<RuleInfo> rules = ReadFirewallRules();
            if (rules == null) return null;
            foreach (int profile in active)
            {
                if (HasIcmpRule(rules, profile)) return null;
            }
            return "未检测到 ICMPv4 入站放行：搜索学生机时禁 ping 的机器会改用 TCP / ARP 兜底";
        }

        private static string ProfileName(int profile)
        {
            if (profile == 1) return "域网络";
            if (profile == 2) return "专用网络";
            if (profile == 4) return "公用网络";
            return "网络";
        }

        private static List<int> ActiveFirewallProfiles()
        {
            List<int> list = new List<int>();
            if (ReadProfileEnabled(@"DomainProfile") != 0) list.Add(1);
            if (ReadProfileEnabled(@"StandardProfile") != 0) list.Add(2);
            if (ReadProfileEnabled(@"PublicProfile") != 0) list.Add(4);
            return list;
        }

        /// <summary>1 = 开启，0 = 关闭，-1 = 读不到（按开启处理）。</summary>
        private static int ReadProfileEnabled(string key)
        {
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\" + key))
                {
                    if (k == null) return 1;
                    object v = k.GetValue("EnableFirewall");
                    if (v == null) return 0;
                    return Convert.ToInt32(v) != 0 ? 1 : 0;
                }
            }
            catch
            {
                return 1;
            }
        }

        private static List<RuleInfo> ReadFirewallRules()
        {
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\FirewallRules"))
                {
                    if (k == null) return null;
                    List<RuleInfo> list = new List<RuleInfo>();
                    foreach (string name in k.GetValueNames())
                    {
                        string v = k.GetValue(name) as string;
                        if (string.IsNullOrEmpty(v)) continue;
                        RuleInfo r = ParseRule(name, v);
                        if (r != null) list.Add(r);
                    }
                    return list;
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>规则值格式：v2.30|Action=Allow|Active=TRUE|Dir=In|Protocol=6|LPort=445|Profile=Domain|Private|Public。</summary>
        private static RuleInfo ParseRule(string name, string raw)
        {
            RuleInfo r = new RuleInfo();
            r.Name = name;
            bool profileSet = false;
            int profileMask = 0;
            foreach (string part in raw.Split('|'))
            {
                int idx = part.IndexOf('=');
                if (idx <= 0)
                {
                    // Profile 值可能被 | 分隔（Domain|Private|Public），继续累积
                    string bare = part.Trim();
                    if (bare.Length > 0)
                    {
                        int bit = ParseProfileName(bare);
                        if (bit != 0)
                        {
                            profileSet = true;
                            profileMask |= bit;
                        }
                    }
                    continue;
                }
                string key = part.Substring(0, idx).Trim();
                string val = part.Substring(idx + 1).Trim();
                if (string.Equals(key, "Dir", StringComparison.OrdinalIgnoreCase))
                    r.Inbound = string.Equals(val, "In", StringComparison.OrdinalIgnoreCase) || val == "1";
                else if (string.Equals(key, "Action", StringComparison.OrdinalIgnoreCase))
                    r.Allow = string.Equals(val, "Allow", StringComparison.OrdinalIgnoreCase) || val == "1";
                else if (string.Equals(key, "Active", StringComparison.OrdinalIgnoreCase))
                    r.Active = string.Equals(val, "TRUE", StringComparison.OrdinalIgnoreCase) || val == "1";
                else if (string.Equals(key, "Protocol", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.Equals(val, "TCP", StringComparison.OrdinalIgnoreCase)) r.Protocol = 6;
                    else if (string.Equals(val, "UDP", StringComparison.OrdinalIgnoreCase)) r.Protocol = 17;
                    else if (string.Equals(val, "ICMPv4", StringComparison.OrdinalIgnoreCase)) r.Protocol = 1;
                    else if (string.Equals(val, "ICMPv6", StringComparison.OrdinalIgnoreCase)) r.Protocol = 58;
                    else
                    {
                        int p;
                        if (int.TryParse(val, out p)) r.Protocol = p;
                    }
                }
                else if (string.Equals(key, "LPort", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(key, "LPorts", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(key, "LocalPort", StringComparison.OrdinalIgnoreCase))
                    r.Ports = val;
                else if (string.Equals(key, "Profile", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(key, "Profiles", StringComparison.OrdinalIgnoreCase))
                {
                    profileSet = true;
                    profileMask |= ParseProfiles(val);
                }
            }
            r.Profiles = profileSet && profileMask != 0 ? profileMask : 0x7FFFFFFF;
            return r;
        }

        private static int ParseProfiles(string token)
        {
            if (string.IsNullOrEmpty(token)) return 0;
            int mask = 0;
            foreach (string piece in token.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                mask |= ParseProfileName(piece.Trim());
            }
            return mask;
        }

        private static int ParseProfileName(string p)
        {
            if (string.IsNullOrEmpty(p)) return 0;
            if (p.IndexOf("Domain", StringComparison.OrdinalIgnoreCase) >= 0) return 1;
            if (p.IndexOf("Private", StringComparison.OrdinalIgnoreCase) >= 0 ||
                p.IndexOf("Standard", StringComparison.OrdinalIgnoreCase) >= 0) return 2;
            if (p.IndexOf("Public", StringComparison.OrdinalIgnoreCase) >= 0) return 4;
            if (p.IndexOf("All", StringComparison.OrdinalIgnoreCase) >= 0) return 0x7FFFFFFF;
            int n;
            if (int.TryParse(p, out n)) return n == 0 ? 0x7FFFFFFF : n;
            return 0;
        }

        private static bool RuleApplies(RuleInfo r, int profile)
        {
            if (!r.Active || !r.Inbound || !r.Allow) return false;
            if ((r.Profiles & profile) == 0) return false;
            return true;
        }

        private static bool HasRule(List<RuleInfo> rules, int profile, int port)
        {
            foreach (RuleInfo r in rules)
            {
                if (!RuleApplies(r, profile)) continue;
                if (r.Protocol != 6 && r.Protocol != 256) continue;
                if (PortCovers(r.Ports, port)) return true;
            }
            return false;
        }

        private static bool HasRpcRule(List<RuleInfo> rules, int profile)
        {
            foreach (RuleInfo r in rules)
            {
                if (!RuleApplies(r, profile)) continue;
                if (r.Protocol != 6 && r.Protocol != 256) continue;
                if (r.Ports.IndexOf("RPC", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                if (PortCovers(r.Ports, 135)) return true;
            }
            return false;
        }

        private static bool HasIcmpRule(List<RuleInfo> rules, int profile)
        {
            foreach (RuleInfo r in rules)
            {
                if (!RuleApplies(r, profile)) continue;
                if (r.Protocol == 1 || r.Protocol == 256) return true;
            }
            return false;
        }

        private static bool PortCovers(string spec, int port)
        {
            if (string.IsNullOrEmpty(spec)) return false;
            foreach (string piece in spec.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string s = piece.Trim();
                if (s.Length == 0) continue;
                if (s == "*") return true;
                if (s.IndexOf("RPC", StringComparison.OrdinalIgnoreCase) >= 0) return port == 135;
                int dash = s.IndexOf('-');
                if (dash > 0)
                {
                    int a, b;
                    if (int.TryParse(s.Substring(0, dash).Trim(), out a) &&
                        int.TryParse(s.Substring(dash + 1).Trim(), out b) && port >= a && port <= b) return true;
                    continue;
                }
                int n;
                if (int.TryParse(s, out n) && n == port) return true;
            }
            return false;
        }

        private static string TestTokenPolicy()
        {
            if (IsDomainJoined()) return null;
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System"))
                {
                    if (k == null) return "读不到系统策略";
                    object v = k.GetValue("LocalAccountTokenFilterPolicy");
                    if (v != null && Convert.ToInt32(v) == 1) return null;
                    return "工作组需要 LocalAccountTokenFilterPolicy=1";
                }
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        private static bool IsDomainJoined()
        {
            try
            {
                string domain = IPGlobalProperties.GetIPGlobalProperties().DomainName;
                if (!string.IsNullOrEmpty(domain)) return true;
            }
            catch
            {
            }
            return !string.Equals(Environment.UserDomainName, Environment.MachineName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>给检测加时间预算，防止某一项卡住整个检测。</summary>
        private static string WithTimeout(Func<string> test, int ms)
        {
            string result = null;
            Exception error = null;
            ManualResetEvent done = new ManualResetEvent(false);
            ThreadPool.QueueUserWorkItem(delegate
            {
                try { result = test(); }
                catch (Exception ex) { error = ex; }
                finally { done.Set(); }
            });
            bool finished = done.WaitOne(ms);
            // 超时时绝不能 Close：被丢下的检测线程稍后还会 Set()，
            // 在 ThreadPool 线程上抛 ObjectDisposedException 会直接把整个进程干掉。
            if (finished) done.Close();
            if (!finished) return "检测超时（" + Math.Max(1, ms / 1000) + " 秒）";
            return error == null ? result : error.Message;
        }

        private static string Short(string text, int max)
        {
            return Ui.Short(text, max);
        }

        // ---------------------------------------------------------------- 一键修复

        /// <summary>一键放行：开服务、放行端口、补 C$ 共享、补工作组令牌策略。</summary>
        public static string FixAll()
        {
            StringBuilder log = new StringBuilder();
            if (!Uwf.IsAdmin())
            {
                return "需要管理员权限：请右键本工具 →「以管理员身份运行」，再来点一键放行。";
            }
            log.AppendLine("已执行以下放行操作：");

            Run(log, "sc.exe", "config LanmanServer start= auto");
            Run(log, "sc.exe", "start LanmanServer");
            Run(log, "sc.exe", "config Winmgmt start= auto");
            Run(log, "sc.exe", "start Winmgmt");

            Run(log, "netsh.exe", "advfirewall firewall set rule group=\"文件和打印机共享\" new enable=Yes");
            Run(log, "netsh.exe", "advfirewall firewall set rule group=\"File and Printer Sharing\" new enable=Yes");
            Run(log, "netsh.exe", "advfirewall firewall set rule group=\"Windows Management Instrumentation (WMI)\" new enable=Yes");
            Run(log, "netsh.exe", "advfirewall firewall set rule group=\"Windows Management Instrumentation\" new enable=Yes");

            EnsureInboundRule(log, "UWFtool - SMB 445", "protocol=TCP localport=445");
            EnsureInboundRule(log, "UWFtool - WMI RPC 135", "protocol=TCP localport=135");
            EnsureInboundRule(log, "UWFtool - ICMPv4", "protocol=icmpv4:8,any");

            if (!IsDomainJoined())
            {
                Run(log, "reg.exe",
                    "add \"HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System\" /v LocalAccountTokenFilterPolicy /t REG_DWORD /d 1 /f");
            }
            else
            {
                log.AppendLine("· 域环境：跳过 LocalAccountTokenFilterPolicy（由域策略管理）");
            }

            if (!string.IsNullOrEmpty(TestAdminShare())) Run(log, "net.exe", "share C$=C:\\");

            log.AppendLine();
            log.AppendLine("请重新检测；仍然不通过的项请按提示处理。");
            return log.ToString();
        }

        private static void EnsureInboundRule(StringBuilder log, string name, string spec)
        {
            Run(log, "netsh.exe",
                "advfirewall firewall set rule name=\"" + name + "\" new enable=Yes");
            Run(log, "netsh.exe",
                "advfirewall firewall add rule name=\"" + name + "\" dir=in action=allow " + spec);
        }

        private static void Run(StringBuilder log, string file, string args)
        {
            string output = "";
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(file, args);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.StandardOutputEncoding = Uwf.OemEncoding();
                psi.StandardErrorEncoding = Uwf.OemEncoding();
                using (Process p = Process.Start(psi))
                {
                    if (p == null) return;
                    string o = p.StandardOutput.ReadToEnd();
                    string e = p.StandardError.ReadToEnd();
                    p.WaitForExit(15000);
                    output = (o + " " + e).Trim();
                }
            }
            catch (Exception ex)
            {
                output = ex.Message;
            }
            log.AppendLine("· " + file + " " + args);
            if (!string.IsNullOrEmpty(output))
            {
                string one = Ui.Short(output, 120);
                if (one.Length > 0) log.AppendLine("    " + one);
            }
        }
    }
}
