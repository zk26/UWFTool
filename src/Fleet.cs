using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;

namespace UwfTeacher
{
    public class Machine
    {
        public string Name = "";
        public string Group = "";
        public string Note = "";
        public string User = "";
        public string Password = "";

        // 运行期状态（不保存）
        public RemoteState State;
        public string StatusText = "未查询";
        public string LastAction = "";
        public string LastError = "";
        public DateTime LastSeen = DateTime.MinValue;
        public bool Busy;

        public string DisplayName
        {
            get { return Name.Length > 0 ? Name : "(未命名)"; }
        }
    }

    /// <summary>一台机器的统一状态分类：界面上的所有数字、颜色、文案都以此为准，避免各处判断不一致。</summary>
    public enum FleetHealth
    {
        Unknown = 0,
        Offline = 1,
        NoUwf = 2,
        Unprotected = 3,
        Queued = 4,
        Maintenance = 5,
        Protected = 6
    }

    public static class FleetView
    {
        public static FleetHealth Health(RemoteState st)
        {
            if (st == null) return FleetHealth.Unknown;
            if (!st.Ok) return FleetHealth.Offline;
            if (!st.UwfPresent) return FleetHealth.NoUwf;
            if (st.ServicingCurrent) return FleetHealth.Maintenance;
            if (!st.FilterCurrent && !st.FilterNext) return FleetHealth.Maintenance;
            if (!st.FilterCurrent && st.FilterNext) return FleetHealth.Queued;
            if (!st.HasProtectedVolume && st.VolumeInfoKnown)
                return st.HasProtectedVolumeNext ? FleetHealth.Queued : FleetHealth.Unprotected;
            return FleetHealth.Protected;
        }

        public static string StatusText(Machine m)
        {
            if (m == null) return "—";
            if (m.Busy) return "处理中";
            RemoteState st = m.State;
            if (st == null) return m.StatusText.Length > 0 ? m.StatusText : "未查询";
            switch (Health(st))
            {
                case FleetHealth.Protected: return "正常";
                case FleetHealth.Queued: return "待重启";
                case FleetHealth.Maintenance: return st.ServicingCurrent ? "维修模式" : "暂停还原";
                case FleetHealth.Unprotected: return "未保护磁盘";
                case FleetHealth.NoUwf: return "无 UWF";
                case FleetHealth.Offline: return "无法连接";
                default: return m.StatusText;
            }
        }

        public static string ProtectText(RemoteState st)
        {
            if (st == null) return "—";
            if (!st.Ok) return "—";
            if (!st.UwfPresent) return "不支持";
            switch (Health(st))
            {
                case FleetHealth.Protected: return "已开启";
                case FleetHealth.Queued: return st.FilterCurrent ? "已排队" : "已排队开启";
                case FleetHealth.Maintenance: return "已暂停";
                case FleetHealth.Unprotected: return "未保护磁盘";
                default: return "已关闭";
            }
        }

        public static string MaintText(RemoteState st)
        {
            if (st == null || !st.Ok || !st.UwfPresent) return "—";
            return st.InMaintenance ? "是" : "否";
        }

        public static string PendingText(RemoteState st)
        {
            if (st == null || !st.Ok || !st.UwfPresent) return "—";
            return (st.PendingReboot || st.QueuedProtect) ? "是" : "否";
        }

        public static string NoteText(Machine m)
        {
            if (m == null) return "";
            RemoteState st = m.State;
            if (st == null) return m.LastError;
            if (!st.Ok)
            {
                string hint = st.ErrorHint;
                return hint.Length > 0 ? hint : st.Error;
            }
            if (!st.UwfPresent) return "目标机没有 UWF 组件（需企业版 / 教育版 / IoT）";
            string body;
            switch (Health(st))
            {
                case FleetHealth.Unprotected:
                    body = "过滤器开着，但没有磁盘在保护列表（" + RawSummary(st) + "）";
                    break;
                case FleetHealth.Maintenance:
                    body = st.ServicingCurrent
                        ? "维修模式（Windows 更新占用了这台机器，改动不会还原）"
                        : "已关闭 UWF 保护（暂停还原）：改动会永久保存";
                    break;
                case FleetHealth.Queued:
                    // 原始值放前面：列宽不够时也能看到判定依据（完整内容看悬浮提示）
                    body = RawSummary(st) + " — " + (st.FilterCurrent
                        ? "过滤器开着，但磁盘保护排队中：重启一次才开始还原（用「重启」，别用关机再开机）"
                        : "有设置排队，重启一次后生效");
                    break;
                default:
                    body = m.LastError;
                    break;
            }
            if (st.StateFix != null && st.StateFix.Length > 0)
                body = (body.Length > 0 ? body + "  " : "") + st.StateFix;
            return body;
        }

        /// <summary>把过滤器与各卷的原始“当前/下次”值摊开显示，方便一眼核对判定依据。</summary>
        private static string RawSummary(RemoteState st)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append("过滤器 ").Append(st.FilterCurrent ? "当前开" : "当前关")
              .Append('/').Append(st.FilterNext ? "下次开" : "下次关");
            foreach (VolumeState v in st.Volumes)
            {
                sb.Append("；").Append(v.Letter.Length > 0 ? v.Letter : "(卷)")
                  .Append(' ').Append(v.ProtectedCurrent ? "已保护" : "未保护")
                  .Append('/').Append(v.ProtectedNext ? "下次保护" : "下次不保护");
            }
            return sb.ToString();
        }

        public static Color ColorOf(FleetHealth h)
        {
            switch (h)
            {
                case FleetHealth.Protected: return Ui.Green;
                case FleetHealth.Queued: return Ui.Accent;
                case FleetHealth.Maintenance: return Ui.Amber;
                case FleetHealth.Unprotected: return Ui.Red;
                case FleetHealth.NoUwf: return Ui.Muted;
                case FleetHealth.Offline: return Ui.Red;
                default: return Ui.MutedLight;
            }
        }

        public static Color ColorFor(Machine m)
        {
            if (m == null) return Ui.MutedLight;
            if (m.Busy) return Ui.Accent;
            return ColorOf(Health(m.State));
        }
    }

    /// <summary>机房汇总（顶栏胶囊、统计卡、状态条共用一套数字）。</summary>
    public class FleetSummary
    {
        public int Total;
        public int Online;
        public int Protected;
        public int Maintenance;
        public int Pending;
        public int Queued;
        public int Unprotected;
        public int NoUwf;
        public int Offline;
        public int Unknown;
        public int Error;

        public static FleetSummary Of(System.Collections.Generic.IList<Machine> fleet)
        {
            FleetSummary s = new FleetSummary();
            if (fleet == null) return s;
            s.Total = fleet.Count;
            foreach (Machine m in fleet)
            {
                RemoteState st = m.State;
                if (st == null) { s.Unknown++; continue; }
                if (!st.Ok) { s.Offline++; s.Error++; continue; }
                s.Online++;
                if (!st.UwfPresent) { s.NoUwf++; s.Error++; continue; }
                // 与表格列（FillRow）用同一套判断，保证汇总和列表数据一致
                if (st.InMaintenance) s.Maintenance++;
                else if (FleetView.Health(st) == FleetHealth.Protected) s.Protected++;
                else if (FleetView.Health(st) == FleetHealth.Queued) s.Queued++;
                else if (FleetView.Health(st) == FleetHealth.Unprotected) { s.Unprotected++; s.Error++; }
                if (st.PendingReboot || st.QueuedProtect) s.Pending++;
            }
            return s;
        }
    }

    public class AppSettings
    {
        public string User = "";
        public string Password = "";
        public int Parallel = 12;
        public int LogDays = 30;
        /// <summary>配置里存过密码但解密失败（换了 Windows 账号/计算机），提示用户重新输入。</summary>
        public bool PasswordLost;
        /// <summary>母盘放行检查确认位：1=已运行cmd 2=放行WMI 4=放行SMB 8=账号可用，全 15 才算确认完成。</summary>
        public int MasterChecks;
        /// <summary>暗色模式。</summary>
        public bool ThemeDark;
        /// <summary>覆盖层配置：内存（RAM）/ 磁盘（DISK）两套完整参数（上限、警告阈值、严重阈值）分别记住。
        /// UWF 同时只有一套生效（作用于当前生效类型），切到哪种就自动套哪套——
        /// 两种都配好，才不会出现“没配过的那种用默认小值、被写满”（内存写满会直接拖垮系统）。</summary>
        public int RamSizeMb = 4096;
        public int RamWarnMb = 3072;
        public int RamCritMb = 3840;
        public int DiskSizeMb = 8192;
        public int DiskWarnMb = 6144;
        public int DiskCritMb = 7680;
    }

    public static class Paths
    {
        public static string DataDir
        {
            get
            {
                string d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "UWF教师控制台");
                try { Directory.CreateDirectory(d); } catch { }
                return d;
            }
        }

        public static string MachinesFile { get { return Path.Combine(DataDir, "machines.xml"); } }
        public static string SettingsFile { get { return Path.Combine(DataDir, "settings.xml"); } }
    }

    public static class FleetStore
    {
        public static List<Machine> Load()
        {
            List<Machine> list = new List<Machine>();
            try
            {
                if (!File.Exists(Paths.MachinesFile)) return list;
                XmlDocument doc = new XmlDocument();
                doc.Load(Paths.MachinesFile);
                XmlNodeList nodes = doc.SelectNodes("/fleet/machine");
                if (nodes == null) return list;
                foreach (XmlNode n in nodes)
                {
                    Machine m = new Machine();
                    m.Name = Attr(n, "name");
                    m.Group = Attr(n, "group");
                    m.Note = Attr(n, "note");
                    m.User = Attr(n, "user");
                    string enc = Attr(n, "password");
                    if (enc.Length > 0)
                    {
                        try { m.Password = Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(enc), null, DataProtectionScope.CurrentUser)); }
                        catch { m.Password = ""; }
                    }
                    if (m.Name.Length > 0) list.Add(m);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("读取机器清单失败：" + ex.Message);
            }
            return list;
        }

        private static string Attr(XmlNode n, string name)
        {
            if (n.Attributes == null) return "";
            XmlAttribute a = n.Attributes[name];
            return a == null ? "" : a.Value;
        }

        public static bool Save(List<Machine> machines)
        {
            try
            {
                XmlWriterSettings settings = new XmlWriterSettings();
                settings.Indent = true;
                settings.Encoding = new UTF8Encoding(false);
                using (XmlWriter w = XmlWriter.Create(Paths.MachinesFile, settings))
                {
                    w.WriteStartDocument();
                    w.WriteStartElement("fleet");
                    foreach (Machine m in machines)
                    {
                        w.WriteStartElement("machine");
                        w.WriteAttributeString("name", m.Name);
                        w.WriteAttributeString("group", m.Group ?? "");
                        w.WriteAttributeString("note", m.Note ?? "");
                        w.WriteAttributeString("user", m.User ?? "");
                        string enc = "";
                        if (!string.IsNullOrEmpty(m.Password))
                        {
                            // 加密失败必须让整次保存失败：否则会把空密码写回文件，界面上却显示“已保存”
                            enc = Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(m.Password), null, DataProtectionScope.CurrentUser));
                        }
                        w.WriteAttributeString("password", enc);
                        w.WriteEndElement();
                    }
                    w.WriteEndElement();
                    w.WriteEndDocument();
                }
            }
            catch (Exception ex)
            {
                Log.Error("保存机器清单失败（清单文件保持原样）：" + ex.Message);
                return false;
            }
            return true;
        }
    }

    public static class SettingsStore
    {
        public static AppSettings Load()
        {
            AppSettings s = new AppSettings();
            try
            {
                if (!File.Exists(Paths.SettingsFile)) return s;
                XmlDocument doc = new XmlDocument();
                doc.Load(Paths.SettingsFile);
                XmlNode root = doc.SelectSingleNode("/settings");
                if (root == null) return s;
                s.User = Node(root, "user");
                s.Parallel = ParseInt(Node(root, "parallel"), 12);
                s.LogDays = ParseInt(Node(root, "logdays"), 30);
                s.MasterChecks = ParseInt(Node(root, "masterchecks"), 0);
                s.ThemeDark = Node(root, "dark") == "1";
                s.RamSizeMb = ClampInt(ParseInt(Node(root, "ramsize"), 4096), 256, 65536);
                s.RamWarnMb = ClampInt(ParseInt(Node(root, "ramwarn"), 3072), 0, 65536);
                s.RamCritMb = ClampInt(ParseInt(Node(root, "ramcrit"), 3840), 0, 65536);
                s.DiskSizeMb = ClampInt(ParseInt(Node(root, "disksize"), 8192), 1024, 65536);
                s.DiskWarnMb = ClampInt(ParseInt(Node(root, "diskwarn"), 6144), 0, 65536);
                s.DiskCritMb = ClampInt(ParseInt(Node(root, "diskcrit"), 7680), 0, 65536);
                string enc = Node(root, "password");
                if (enc.Length > 0)
                {
                    try
                    {
                        byte[] raw = ProtectedData.Unprotect(Convert.FromBase64String(enc), null, DataProtectionScope.CurrentUser);
                        s.Password = Encoding.UTF8.GetString(raw);
                    }
                    catch
                    {
                        s.PasswordLost = true;
                    }
                }
            }
            catch
            {
            }
            if (s.Parallel < 1) s.Parallel = 1;
            if (s.Parallel > 32) s.Parallel = 32;
            if (s.LogDays < 0) s.LogDays = 0;
            return s;
        }

        public static bool Save(AppSettings s)
        {
            try
            {
                XmlWriterSettings settings = new XmlWriterSettings();
                settings.Indent = true;
                settings.Encoding = new UTF8Encoding(false);
                string pwd = "";
                if (!string.IsNullOrEmpty(s.Password))
                {
                    // 加密失败就抛出去：绝不能静默写空密码，否则下次连接全部“拒绝访问”却查不出原因
                    byte[] raw = ProtectedData.Protect(Encoding.UTF8.GetBytes(s.Password), null, DataProtectionScope.CurrentUser);
                    pwd = Convert.ToBase64String(raw);
                }
                using (XmlWriter w = XmlWriter.Create(Paths.SettingsFile, settings))
                {
                    w.WriteStartDocument();
                    w.WriteStartElement("settings");
                    w.WriteElementString("user", s.User ?? "");
                    w.WriteElementString("password", pwd);
                    w.WriteElementString("parallel", s.Parallel.ToString(CultureInfo.InvariantCulture));
                    w.WriteElementString("logdays", s.LogDays.ToString(CultureInfo.InvariantCulture));
                    w.WriteElementString("masterchecks", s.MasterChecks.ToString(CultureInfo.InvariantCulture));
                    w.WriteElementString("dark", s.ThemeDark ? "1" : "0");
                    w.WriteElementString("ramsize", ClampInt(s.RamSizeMb, 256, 65536).ToString(CultureInfo.InvariantCulture));
                    w.WriteElementString("ramwarn", ClampInt(s.RamWarnMb, 0, 65536).ToString(CultureInfo.InvariantCulture));
                    w.WriteElementString("ramcrit", ClampInt(s.RamCritMb, 0, 65536).ToString(CultureInfo.InvariantCulture));
                    w.WriteElementString("disksize", ClampInt(s.DiskSizeMb, 1024, 65536).ToString(CultureInfo.InvariantCulture));
                    w.WriteElementString("diskwarn", ClampInt(s.DiskWarnMb, 0, 65536).ToString(CultureInfo.InvariantCulture));
                    w.WriteElementString("diskcrit", ClampInt(s.DiskCritMb, 0, 65536).ToString(CultureInfo.InvariantCulture));
                    w.WriteEndElement();
                    w.WriteEndDocument();
                }
            }
            catch (Exception ex)
            {
                Log.Error("保存设置失败（原文件保持不动）：" + ex.Message);
                return false;
            }
            return true;
        }

        private static string Node(XmlNode root, string name)
        {
            XmlNode n = root.SelectSingleNode(name);
            return n == null ? "" : n.InnerText.Trim();
        }

        private static int ClampInt(int v, int min, int max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }

        private static int ParseInt(string text, int def)
        {
            int v;
            return int.TryParse(text, out v) ? v : def;
        }
    }

    public static class SubnetScanner
    {
        /// <summary>扫描 prefix 网段的 start..end，返回能 ping / TCP / ARP 通的主机（尽量解析计算机名）。
        /// progress 回报（已完成、总数、当前地址）——要让老师看见正在扫哪个 IP，不然两百多个地址像卡死。</summary>
        public static List<string> Scan(string prefix, int start, int end, bool resolveNames, Action<int, int, string> progress, Func<bool> cancelled)
        {
            List<string> found = new List<string>();
            object sync = new object();
            int total = Math.Max(1, end - start + 1);
            int done = 0;

            // 并发探活：不只看 ICMP，禁 ping 的机器也会被 TCP / ARP 找到
            Parallel.For(start, end + 1, new ParallelOptions { MaxDegreeOfParallelism = 48 }, delegate(int i)
            {
                if (cancelled != null && cancelled()) return;
                string ip = prefix + i;
                if (Remote.IsAlive(ip))
                {
                    string name = ip;
                    if (resolveNames)
                    {
                        try
                        {
                            IPHostEntry entry = Dns.GetHostEntry(ip);
                            if (entry != null && !string.IsNullOrEmpty(entry.HostName)) name = entry.HostName;
                        }
                        catch
                        {
                        }
                    }
                    lock (sync) found.Add(name + "|" + ip);
                }
                int d = Interlocked.Increment(ref done);
                if (progress != null && (d % 4 == 0 || d == total)) progress(d, total, ip);
            });

            found.Sort(StringComparer.OrdinalIgnoreCase);
            return found;
        }

        // ---------------------------------------------------------------- 地址解析

        /// <summary>
        /// 把用户输入的扫描范围展开成 IP 列表。支持：
        /// 192.168.1. / 192.168.1 / 192.168.1.1 / 192.168.1.10-60 /
        /// 192.168.1.10-192.168.1.60 / 192.168.1.0/24，多个用换行、逗号或空格分隔。
        /// </summary>
        public static List<string> ExpandTargets(string text, out string error)
        {
            error = "";
            List<string> list = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string[] tokens = (text ?? "").Replace("，", ",").Replace("；", ";")
                .Split(new char[] { '\r', '\n', ',', ';', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string raw in tokens)
            {
                string token = raw.Trim();
                if (token.Length == 0) continue;
                List<uint> range = ParseToken(token, ref error);
                if (range == null) continue;
                if (range.Count == 0) continue;
                for (uint v = range[0]; v <= range[1]; v++)
                {
                    string ip = UIntToIp(v);
                    if (seen.Add(ip)) list.Add(ip);
                    if (list.Count >= 4096)
                    {
                        error = "地址太多，只扫描前 4096 个；请缩小范围。";
                        break;
                    }
                    if (v == uint.MaxValue) break;
                }
                if (list.Count >= 4096) break;
            }

            list.Sort(delegate(string a, string b) { return CompareIp(a, b); });
            return list;
        }

        /// <summary>返回 [起始, 结束]；无法识别时返回 null 并写 error。</summary>
        private static List<uint> ParseToken(string token, ref string error)
        {
            try
            {
                if (token.IndexOf('/') > 0)
                {
                    string[] p = token.Split('/');
                    uint ip;
                    int bits;
                    if (p.Length == 2 && TryIp(p[0], out ip) && int.TryParse(p[1], out bits) && bits >= 8 && bits <= 32)
                    {
                        uint mask = bits == 32 ? 0xFFFFFFFFu : (0xFFFFFFFFu << (32 - bits));
                        uint net = ip & mask;
                        uint broadcast = net | ~mask;
                        if (bits >= 31) return new List<uint> { net, broadcast };
                        return new List<uint> { net + 1, broadcast - 1 };
                    }
                    error = "无法识别网段：" + token;
                    return null;
                }

                if (token.IndexOf('-') > 0)
                {
                    string[] p = token.Split('-');
                    if (p.Length != 2) { error = "无法识别范围：" + token; return null; }
                    string left = p[0].Trim();
                    string right = p[1].Trim();
                    string prefix = null;
                    int startOctet;
                    uint start;
                    if (TryIp(left, out start)) { }
                    else if (TryPrefix(left, out prefix, out startOctet)) start = IpToUInt(prefix + startOctet);
                    else { error = "无法识别范围：" + token; return null; }

                    uint end;
                    int endOctet;
                    if (TryIp(right, out end)) { }
                    else if (int.TryParse(right, out endOctet))
                    {
                        if (prefix == null) prefix = PrefixOfIp(left);
                        end = IpToUInt(prefix + endOctet);
                    }
                    else if (TryPrefix(right, out prefix, out endOctet)) end = IpToUInt(prefix + endOctet);
                    else { error = "无法识别范围：" + token; return null; }

                    if (end < start) { uint t = start; start = end; end = t; }
                    return new List<uint> { start, end };
                }

                uint single;
                if (TryIp(token, out single)) return new List<uint> { single, single };

                string pfx;
                int first;
                if (TryPrefix(token, out pfx, out first))
                {
                    return new List<uint> { IpToUInt(pfx + 1), IpToUInt(pfx + 254) };
                }

                error = "无法识别：" + token + "（示例：192.168.1. 或 192.168.1.10-60 或 192.168.1.0/24）";
                return null;
            }
            catch
            {
                error = "无法识别：" + token;
                return null;
            }
        }

        private static bool TryIp(string text, out uint value)
        {
            value = 0;
            if (string.IsNullOrEmpty(text)) return false;
            string[] p = text.Trim().Split('.');
            if (p.Length != 4) return false;
            uint v = 0;
            foreach (string s in p)
            {
                int n;
                if (!int.TryParse(s, out n) || n < 0 || n > 255) return false;
                v = (v << 8) | (uint)n;
            }
            value = v;
            return true;
        }

        /// <summary>接受 "192.168.1." / "192.168.1" / "192.168.1.x"，返回前缀与 1。</summary>
        private static bool TryPrefix(string text, out string prefix, out int first)
        {
            prefix = null;
            first = 1;
            if (string.IsNullOrEmpty(text)) return false;
            string t = text.Trim().TrimEnd('.');
            int x = t.IndexOfAny(new char[] { 'x', 'X' });
            if (x >= 0) t = t.Substring(0, x).TrimEnd('.');
            string[] p = t.Split('.');
            if (p.Length != 3) return false;
            foreach (string s in p)
            {
                int n;
                if (!int.TryParse(s, out n) || n < 0 || n > 255) return false;
            }
            prefix = p[0] + "." + p[1] + "." + p[2] + ".";
            return true;
        }

        private static int CompareIp(string a, string b)
        {
            uint x, y;
            if (TryIp(a, out x) && TryIp(b, out y))
            {
                if (x == y) return 0;
                return x < y ? -1 : 1;
            }
            return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
        }

        private static string PrefixOfIp(string ip)
        {
            int idx = ip.LastIndexOf('.');
            return idx > 0 ? ip.Substring(0, idx + 1) : ip + ".";
        }

        private static uint IpToUInt(string ip)
        {
            uint v;
            return TryIp(ip, out v) ? v : 0;
        }

        private static string UIntToIp(uint v)
        {
            return ((v >> 24) & 0xFF) + "." + ((v >> 16) & 0xFF) + "." + ((v >> 8) & 0xFF) + "." + (v & 0xFF);
        }
    }

    /// <summary>并发执行器：把一组机器交给多个线程处理，结果回到 UI 线程。</summary>
    public class FleetRunner
    {
        public int Parallelism = 12;
        public Action<Machine> Work;
        public Action<Machine> Started;      // 每台开始时（UI 线程）：让遮罩显示“正在处理哪一台”，不再只有 0 / N
        public Action<Machine> Completed;
        public Action<int, int> Progress;
        public Action Finished;

        private CancellationTokenSource _cts;
        private int _done;
        private readonly Control _host;

        public bool Running { get; private set; }

        public FleetRunner()
        {
        }

        /// <summary>host：回调要更新的界面宿主，窗口关闭后自动丢弃迟到的回调。</summary>
        public FleetRunner(Control host)
        {
            _host = host;
        }

        private bool Alive()
        {
            return _host == null || (!_host.IsDisposed && !_host.Disposing);
        }

        public void Start(IList<Machine> machines)
        {
            if (Running || machines == null || machines.Count == 0) return;
            _cts = new CancellationTokenSource();
            _done = 0;
            Running = true;

            List<Machine> list = new List<Machine>(machines);
            CancellationToken token = _cts.Token;
            SynchronizationContext ui = SynchronizationContext.Current;

            Task.Factory.StartNew(delegate
            {
                try
                {
                    Parallel.ForEach(list, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Parallelism), CancellationToken = token },
                        delegate(Machine m)
                        {
                            if (ui != null && Started != null)
                            {
                                ui.Post(delegate
                                {
                                    if (!Alive()) return;
                                    try { Started(m); } catch { }
                                }, null);
                            }
                            try
                            {
                                if (Work != null) Work(m);
                            }
                            catch (Exception ex)
                            {
                                m.LastError = ex.Message;
                                m.StatusText = "失败";
                            }
                            int d = Interlocked.Increment(ref _done);
                            if (ui != null)
                            {
                                ui.Post(delegate
                                {
                                    if (!Alive()) return;
                                    try
                                    {
                                        if (Completed != null) Completed(m);
                                        if (Progress != null) Progress(d, list.Count);
                                    }
                                    catch (Exception ex)
                                    {
                                        Log.Error("批量任务回调异常：" + ex.Message);
                                    }
                                }, null);
                            }
                        });
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    Log.Error("批量任务异常：" + ex.Message);
                }
                finally
                {
                    if (ui != null)
                    {
                        ui.Post(delegate
                        {
                            // 先复位再判存活：原来 Alive() 为 false 时直接 return，
                            // Running 会永远停在 true，之后所有“刷新/批量”按钮都静默失灵。
                            Running = false;
                            if (!Alive()) return;
                            try
                            {
                                if (Finished != null) Finished();
                            }
                            catch (Exception ex)
                            {
                                Log.Error("批量任务完成回调异常：" + ex.Message);
                            }
                        }, null);
                    }
                    else
                    {
                        Running = false;
                    }
                }
            });
        }

        public void Cancel()
        {
            if (_cts != null)
            {
                try { _cts.Cancel(); } catch { }
            }
        }

        /// <summary>是否已请求取消：长时间等待（装安装包）时工作线程要靠它及时退出。</summary>
        public bool Cancelled
        {
            get
            {
                try { return _cts != null && _cts.IsCancellationRequested; }
                catch { return false; }
            }
        }
    }
}
