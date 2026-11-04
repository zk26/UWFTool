using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Management;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;

namespace UwfTeacher
{
    public class UwfResult
    {
        public string Command = "";
        public int ExitCode;
        public string StdOut = "";
        public string StdErr = "";
        public bool TimedOut;

        public bool Ok { get { return !TimedOut && ExitCode == 0; } }

        public string All
        {
            get
            {
                string s = StdOut == null ? "" : StdOut;
                if (!string.IsNullOrEmpty(StdErr))
                {
                    if (s.Length > 0 && !s.EndsWith("\n")) s += "\r\n";
                    s += StdErr;
                }
                return s.Trim();
            }
        }
    }

    public class VolumeState
    {
        public string Letter = "";
        public string VolumeName = "";
        public string Label = "";
        public double CapacityGb;
        public bool ProtectedCurrent;
        public bool ProtectedNext;
        public bool SawCurrentSession;
        public bool SawNextSession;
    }

    public class UwfState
    {
        public DateTime At = DateTime.Now;
        public bool UwfmgrPresent;
        public bool WmiPresent;
        public bool HasFilter;
        public bool HasServicing;
        public string Error = "";

        public bool FilterCurrent;
        public bool FilterNext;
        public bool ServicingCurrent;

        /// <summary>状态被按运行时事实（缓存占用）修正过时的说明文字，显示在界面/诊断里。</summary>
        public string StateFix = "";
        public bool ServicingNext;

        public List<VolumeState> Volumes = new List<VolumeState>();
        public int OverlayUsedMb = -1;
        public int OverlayFreeMb = -1;
        public int OverlayWarnMb = -1;
        public int OverlayCritMb = -1;
        public int OverlayMaxMb = -1;
        public int OverlayType = -1; // 0 = RAM, 1 = DISK, -1 = unknown

        public List<string> FileExclusions = new List<string>();
        public List<string> RegExclusions = new List<string>();

        public string RawConfig = "";
        public string RawOverlay = "";

        public bool Installed { get { return UwfmgrPresent || HasFilter || HasServicing; } }

        public bool ProtectedNow { get { return FilterCurrent && !ServicingCurrent; } }

        public bool InServicing { get { return ServicingCurrent; } }

        public bool PendingReboot
        {
            get
            {
                if (!Installed) return false;
                if (FilterCurrent != FilterNext) return true;
                if (ServicingCurrent != ServicingNext) return true;
                foreach (VolumeState v in Volumes)
                {
                    if (v.ProtectedCurrent != v.ProtectedNext) return true;
                }
                return false;
            }
        }

        public bool HasProtectedVolume
        {
            get
            {
                foreach (VolumeState v in Volumes)
                {
                    if (v.ProtectedCurrent) return true;
                }
                return false;
            }
        }

        public bool HasProtectedVolumeNext
        {
            get
            {
                foreach (VolumeState v in Volumes)
                {
                    if (v.ProtectedNext) return true;
                }
                return false;
            }
        }

        public string ProtectedVolumeText
        {
            get
            {
                List<string> names = new List<string>();
                foreach (VolumeState v in Volumes)
                {
                    if (!v.ProtectedCurrent) continue;
                    if (v.Letter.Length > 0) names.Add(v.Letter);
                    else names.Add("卷 " + (names.Count + 1));
                }
                if (names.Count == 0) return "无";
                return string.Join("  ", names.ToArray());
            }
        }

        /// <summary>已排队、重启后才开始保护的卷（ProtectedNext 且当前未保护）。</summary>
        public string PendingVolumeText
        {
            get
            {
                List<string> names = new List<string>();
                foreach (VolumeState v in Volumes)
                {
                    if (!v.ProtectedNext || v.ProtectedCurrent) continue;
                    if (v.Letter.Length > 0) names.Add(v.Letter);
                    else names.Add("卷 " + (names.Count + 1));
                }
                if (names.Count == 0) return "无";
                return string.Join("  ", names.ToArray());
            }
        }
    }

    public static class Uwf
    {
        public static string ExePath
        {
            get
            {
                string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                return Path.Combine(win, "System32", "uwfmgr.exe");
            }
        }

        public static bool IsAdmin()
        {
            try
            {
                WindowsIdentity id = WindowsIdentity.GetCurrent();
                WindowsPrincipal p = new WindowsPrincipal(id);
                return p.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        public static Encoding OemEncoding()
        {
            try
            {
                int cp = CultureInfo.CurrentCulture.TextInfo.OEMCodePage;
                if (cp <= 0) cp = 936;
                return Encoding.GetEncoding(cp);
            }
            catch
            {
                try { return Encoding.GetEncoding(936); }
                catch { return Encoding.Default; }
            }
        }

        public static UwfResult Run(string arguments)
        {
            return Run(arguments, 180000);
        }

        public static UwfResult Run(string arguments, int timeoutMs)
        {
            UwfResult r = new UwfResult();
            r.Command = "uwfmgr " + arguments;
            if (!File.Exists(ExePath))
            {
                r.StdErr = "未找到 uwfmgr.exe（本机可能未安装统一写入筛选器组件）。";
                r.ExitCode = -2;
                return r;
            }

            // 通过临时脚本 + 文件重定向执行：uwfmgr 的部分中文错误直接写控制台，
            // 用管道读会只剩一两个字节（表现为“→ 遗”这种残缺信息）。
            string dir = Path.GetTempPath();
            string id = Guid.NewGuid().ToString("N").Substring(0, 8);
            string cmdFile = Path.Combine(dir, "uwfrun_" + id + ".cmd");
            string outFile = Path.Combine(dir, "uwfrun_" + id + ".txt");
            try
            {
                StringBuilder script = new StringBuilder();
                script.AppendLine("@echo off");
                script.AppendLine("chcp 936 >nul 2>&1");
                script.AppendLine("\"" + ExePath + "\" " + arguments + " > \"" + outFile + "\" 2>&1");
                script.AppendLine("exit /b %ERRORLEVEL%");
                File.WriteAllText(cmdFile, script.ToString(), Encoding.Default);

                ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", "/c \"" + cmdFile + "\"");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

                using (Process p = new Process())
                {
                    p.StartInfo = psi;
                    p.Start();
                    if (!p.WaitForExit(timeoutMs))
                    {
                        r.TimedOut = true;
                        try { p.Kill(); } catch { }
                    }
                    try { r.ExitCode = p.ExitCode; } catch { r.ExitCode = -3; }
                }

                if (File.Exists(outFile))
                {
                    r.StdOut = File.ReadAllText(outFile, OemEncoding());
                }
            }
            catch (Exception ex)
            {
                r.StdErr = ex.Message;
                r.ExitCode = -1;
            }
            finally
            {
                try { File.Delete(cmdFile); } catch { }
                try { File.Delete(outFile); } catch { }
            }
            return r;
        }
        // ---------- state ----------

        public static UwfState Read()
        {
            UwfState s = new UwfState();
            s.UwfmgrPresent = File.Exists(ExePath);

            if (s.UwfmgrPresent)
            {
                UwfResult c = Run("get-config", 60000);
                s.RawConfig = c.All;
                UwfResult o = Run("overlay get-config", 60000);
                s.RawOverlay = o.All;
            }

            try
            {
                ManagementScope scope = new ManagementScope(@"\\.\root\standardcimv2\embedded");
                scope.Connect();
                s.WmiPresent = true;
                s.HasFilter = ReadFilter(scope, s);
                s.HasServicing = ReadServicing(scope, s);
                s.Volumes = ReadVolumes(scope);
                ReadOverlay(scope, s);
            }
            catch (Exception ex)
            {
                if (s.Error.Length == 0) s.Error = ex.Message;
            }

            // 与 uwfmgr 文本交叉核对过滤器状态：文本是管理员看到的真相，
            // WMI 读到的和它不一致时以文本为准，避免明明开着保护却显示“未开启 / 待重启”。
            ReconcileFilterFromText(s);

            if (!s.Installed)
            {
                return s;
            }

            if (s.OverlayUsedMb < 0 || s.OverlayMaxMb < 0)
            {
                FillOverlayFromText(s);
            }

            if (s.UwfmgrPresent)
            {
                if (s.Volumes.Count == 0)
                {
                    VolumeState c = new VolumeState();
                    c.Letter = "C:";
                    s.Volumes.Add(c);
                }
                foreach (VolumeState v in s.Volumes)
                {
                    // 两种情况用 uwfmgr 文本核对（文本就是管理员看到的真相，和 WMI 不一致以文本为准）：
                    //   1) WMI 实例不全（部分系统只返回 CurrentSession=True 或只有 =False 的行）；
                    //   2) 状态可疑（当前未保护 / 排队中）——明明开着保护却显示“未保护、待重启”就是这类误报。
                    bool needCurrent = !v.SawCurrentSession;
                    bool needNext = !v.SawNextSession;
                    bool suspicious = !v.ProtectedCurrent;
                    if (!needCurrent && !needNext && !suspicious) continue;
                    if (v.Letter.Length > 0) FillVolumeFromText(v, needCurrent || suspicious, needNext || suspicious);
                    if (!v.SawCurrentSession && !v.SawNextSession) continue;   // 两侧都没有真实数据：别瞎推断
                    if (!v.SawCurrentSession) v.ProtectedCurrent = v.ProtectedNext;
                    if (!v.SawNextSession) v.ProtectedNext = v.ProtectedCurrent;
                }
            }

            // 最后一道纠错：缓存占用 > 0 就证明写入正被拦截（= 有卷在保护），
            // WMI 却显示“当前没有任何卷受保护”时按运行时事实修正。
            if (RepairVolumesFromOverlay(s.Volumes, s.FilterCurrent, s.OverlayUsedMb))
                s.StateFix = "卷状态已按缓存占用修正：缓存已有 " + s.OverlayUsedMb + " MB 重定向写入，说明此刻确实在还原；WMI 卷状态显示陈旧，已按事实改判。";

            if (s.UwfmgrPresent)
            {
                s.FileExclusions = ReadFileExclusions(s);
                UwfResult g = Run("registry get-exclusions", 60000);
                s.RegExclusions = ExtractKeys(g.All);
            }

            if (s.Volumes.Count == 0 && s.WmiPresent)
            {
                s.Volumes = ReadVolumesFallback(scopeSafe());
            }

            return s;
        }

        /// <summary>
        /// 解析一行里的 开/关 或 保护/未保护 值。只看冒号后面的值部分，避免把标签词（如 “Protected :”）当值。
        /// 支持：ON/OFF、TRUE/FALSE、是/否、Enabled/Disabled、已启用/已禁用、Protected/Unprotected、受保护/未受保护/已保护/未保护 等。
        /// </summary>
        public static bool? ParseStateValue(string line)
        {
            if (string.IsNullOrEmpty(line)) return null;
            string value = line;
            int colon = line.LastIndexOfAny(new char[] { ':', '：' });
            if (colon >= 0 && colon < line.Length - 1) value = line.Substring(colon + 1).Trim();

            // 负面词先判（Unprotected/未受保护 等包含正面词的子串）
            if (ContainsWord(value, "未受保护") || ContainsWord(value, "不受保护") ||
                ContainsWord(value, "未保护") || ContainsWord(value, "未启用") ||
                ContainsWord(value, "已禁用") || ContainsWord(value, "已关闭") ||
                ContainsWord(value, "Unprotected") || ContainsWord(value, "Disabled") ||
                RegexWord(value, "OFF") || RegexWord(value, "FALSE") || value.Trim() == "否")
                return false;

            if (ContainsWord(value, "受保护") || ContainsWord(value, "已保护") ||
                ContainsWord(value, "已启用") || ContainsWord(value, "已开启") ||
                ContainsWord(value, "Protected") || ContainsWord(value, "Enabled") ||
                RegexWord(value, "ON") || RegexWord(value, "TRUE") || value.Trim() == "是")
                return true;

            return null;
        }

        private static bool ContainsWord(string text, string word)
        {
            return text.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool RegexWord(string text, string word)
        {
            return System.Text.RegularExpressions.Regex.IsMatch(text, "\\b" + word + "\\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        /// <summary>
        /// 一致性纠错：缓存（overlay）只从“受保护的卷”的重定向写入里增长。
        /// 过滤器开着、缓存里已经有写入占用，就说明此刻确实有卷在被还原；
        /// 如果卷状态却显示“当前没有任何卷受保护”（部分 UWF 版本的 WMI 会返回陈旧值），
        /// 按“下次会话受保护”的卷把当前状态修正为已保护——运行时事实比陈旧元数据可靠。
        /// 返回 true 表示修正过（调用方应记录说明）。
        /// </summary>
        public static bool RepairVolumesFromOverlay(IList<VolumeState> volumes, bool filterCurrent, long usedMb)
        {
            if (volumes == null || volumes.Count == 0) return false;
            if (!filterCurrent || usedMb <= 0) return false;
            foreach (VolumeState v in volumes)
            {
                if (v.ProtectedCurrent) return false;   // 已经有卷显示在保护：无需修正
            }
            bool fixedAny = false;
            foreach (VolumeState v in volumes)
            {
                if (!v.ProtectedCurrent && v.ProtectedNext)
                {
                    v.ProtectedCurrent = true;
                    v.SawCurrentSession = true;
                    fixedAny = true;
                }
            }
            return fixedAny;
        }

        /// <summary>
        /// 从 uwfmgr get-config 文本里解析过滤器“当前/下次”状态（public，远程复核也用它）。
        /// 兼容各种输出格式：标题与值同一行、中英文、TRUE/FALSE/ON/OFF/已启用 等值写法。
        /// 解析不出的侧返回 null，调用方保持原值。
        /// </summary>
        public static void ParseFilterStateText(string text, out bool? cur, out bool? next)
        {
            cur = null;
            next = null;
            if (string.IsNullOrEmpty(text)) return;
            bool inCurrent = false, inNext = false;
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;

                // 标题行也可能是“Current session: xxx”一行带值：先更新会话，再继续解析本行的值
                if (ContainsWord(line, "当前会话") || ContainsWord(line, "Current Session"))
                {
                    inCurrent = true;
                    inNext = false;
                }
                else if (ContainsWord(line, "下次会话") || ContainsWord(line, "Next Session"))
                {
                    inNext = true;
                    inCurrent = false;
                }

                bool isFilter = ContainsWord(line, "Filter state") ||
                                ContainsWord(line, "过滤器状态") ||
                                ContainsWord(line, "筛选器状态") ||
                                (ContainsWord(line, "Filter") && line.IndexOfAny(new char[] { ':', '：' }) >= 0);
                if (!isFilter) continue;
                bool? val = ParseStateValue(line);
                if (val == null) continue;
                if (inCurrent && !cur.HasValue) cur = val;
                else if (inNext && !next.HasValue) next = val;
            }
        }

        /// <summary>
        /// 从 uwfmgr volume get-config 文本里解析卷“当前/下次”保护状态（public，远程复核也用它）。
        /// 兼容各种输出格式；解析不出的侧返回 null。
        /// </summary>
        public static void ParseVolumeText(string text, out bool? cur, out bool? next)
        {
            cur = null;
            next = null;
            if (string.IsNullOrEmpty(text)) return;
            bool inCurrent = false, inNext = false;
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;

                if (ContainsWord(line, "当前会话") || ContainsWord(line, "Current Session"))
                {
                    inCurrent = true;
                    inNext = false;
                }
                else if (ContainsWord(line, "下次会话") || ContainsWord(line, "Next Session"))
                {
                    inNext = true;
                    inCurrent = false;
                }

                // 状态行的标签在不同语言/版本里不一样；没有标签但有“保护”值的行也要认
                bool isState = ContainsWord(line, "卷状态") ||
                               ContainsWord(line, "保护状态") ||
                               ContainsWord(line, "Volume state") ||
                               ContainsWord(line, "Protection") ||
                               ContainsWord(line, "Protected") ||
                               (ContainsWord(line, "保护") && line.IndexOfAny(new char[] { ':', '：' }) >= 0);
                if (!isState) continue;
                bool? val = ParseStateValue(line);
                if (val == null) continue;
                if (inCurrent && !cur.HasValue) cur = val;
                else if (inNext && !next.HasValue) next = val;
            }
        }

        /// <summary>
        /// 与 uwfmgr 文本交叉核对过滤器状态：文本是管理员看到的真相，
        /// WMI 读到的和它不一致时以文本为准，避免明明开着保护却显示“未开启 / 待重启”。
        /// </summary>
        private static void ReconcileFilterFromText(UwfState s)
        {
            if (s.RawConfig == null || s.RawConfig.Length == 0) return;
            bool? cur, next;
            ParseFilterStateText(s.RawConfig, out cur, out next);
            if (cur.HasValue && cur.Value != s.FilterCurrent) s.FilterCurrent = cur.Value;
            if (next.HasValue && next.Value != s.FilterNext) s.FilterNext = next.Value;
        }

        private static ManagementScope scopeSafe()
        {
            return new ManagementScope(@"\\.\root\standardcimv2\embedded");
        }

        private static List<VolumeState> ReadVolumesFallback(ManagementScope scope)
        {
            List<VolumeState> list = new List<VolumeState>();
            try
            {
                Remote.EachMo(scope, "SELECT DeviceID, DriveLetter, Label, Capacity FROM Win32_Volume", delegate(ManagementObject mo)
                {
                    VolumeState v = new VolumeState();
                    v.Letter = Str(mo, "DriveLetter");
                    v.VolumeName = Str(mo, "DeviceID");
                    v.Label = Str(mo, "Label");
                    v.CapacityGb = Num(mo, "Capacity") / 1073741824.0;
                    if (v.Letter.Length > 0) list.Add(v);
                });
            }
            catch
            {
            }
            return list;
        }

        private static bool ReadFilter(ManagementScope scope, UwfState s)
        {
            try
            {
                Remote.EachMo(scope, "SELECT * FROM UWF_Filter", delegate(ManagementObject mo)
                {
                    // 部分 UWF 版本会返回多个实例、其中一个的 CurrentEnabled/NextEnabled 是陈旧值：
                    // 按“任一实例为真”合并（OR），而不是让最后一个实例覆盖——
                    // 否则明明开着保护会被陈旧实例刷成“待重启”。
                    if (Bool(mo, "CurrentEnabled")) s.FilterCurrent = true;
                    if (Bool(mo, "NextEnabled")) s.FilterNext = true;
                    s.HasFilter = true;
                });
            }
            catch (Exception ex)
            {
                if (string.IsNullOrEmpty(s.Error)) s.Error = ex.Message;
            }
            return s.HasFilter;
        }

        private static bool ReadServicing(ManagementScope scope, UwfState s)
        {
            try
            {
                Remote.EachMo(scope, "SELECT * FROM UWF_Servicing", delegate(ManagementObject mo)
                {
                    if (Bool(mo, "CurrentEnabled")) s.ServicingCurrent = true;
                    if (Bool(mo, "NextEnabled")) s.ServicingNext = true;
                    s.HasServicing = true;
                });
            }
            catch (Exception ex)
            {
                if (string.IsNullOrEmpty(s.Error)) s.Error = ex.Message;
            }
            return s.HasServicing;
        }

        private static List<VolumeState> ReadVolumes(ManagementScope scope)
        {
            List<VolumeState> list = new List<VolumeState>();
            // 同一个卷的“当前会话/下次会话”两个实例可能一个带盘符、一个只有卷 GUID：
            // 按盘符和卷名双索引合并，否则会被拆成两个卷条目，C: 那行只剩“下次”的数据，
            // 界面上就成了“C: 当前：未保护 ⚠ 重启后开始保护”这种假状态。
            Dictionary<string, VolumeState> byLetter = new Dictionary<string, VolumeState>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, VolumeState> byName = new Dictionary<string, VolumeState>(StringComparer.OrdinalIgnoreCase);

            try
            {
                Remote.EachMo(scope, "SELECT * FROM UWF_Volume", delegate(ManagementObject mo)
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
                        list.Add(v);
                    }
                    if (v.Letter.Length == 0 && letter.Length > 0) v.Letter = letter;
                    if (v.VolumeName.Length == 0 && vn.Length > 0) v.VolumeName = vn;
                    if (letter.Length > 0) byLetter[letter] = v;
                    if (vn.Length > 0) byName[vn] = v;

                    bool cur = Bool(mo, "CurrentSession");
                    bool prot = Bool(mo, "Protected");
                    if (cur) { v.ProtectedCurrent = prot; v.SawCurrentSession = true; }
                    else { v.ProtectedNext = prot; v.SawNextSession = true; }
                });
            }
            catch
            {
            }

            try
            {
                Remote.EachMo(scope, "SELECT DeviceID, DriveLetter, Label, Capacity, DriveType FROM Win32_Volume", delegate(ManagementObject mo)
                {
                    string did = Str(mo, "DeviceID");
                    string dl = Str(mo, "DriveLetter");
                    string label = Str(mo, "Label");
                    double cap = Num(mo, "Capacity") / 1073741824.0;
                    long type = Num(mo, "DriveType");
                    bool matched = false;
                    foreach (VolumeState v in list)
                    {
                        if (v.VolumeName.Length > 0 && string.Equals(v.VolumeName, did, StringComparison.OrdinalIgnoreCase))
                        {
                            if (v.Letter.Length == 0) v.Letter = dl;
                            v.Label = label;
                            v.CapacityGb = cap;
                            matched = true;
                        }
                    }
                    if (matched || type != 3 || dl.Length == 0) return;
                    // 未被 UWF 登记的固定磁盘：列出来，方便教师在界面上勾选保护
                    VolumeState extra = new VolumeState();
                    extra.Letter = dl;
                    extra.VolumeName = did;
                    extra.Label = label;
                    extra.CapacityGb = cap;
                    extra.ProtectedCurrent = false;
                    extra.ProtectedNext = false;
                    list.Add(extra);
                });
            }
            catch
            {
            }

            list.Sort(delegate(VolumeState a, VolumeState b) { return string.Compare(a.Letter, b.Letter, StringComparison.OrdinalIgnoreCase); });
            return list;
        }

        private static void ReadOverlay(ManagementScope scope, UwfState s)
        {
            try
            {
                Remote.EachMo(scope, "SELECT * FROM UWF_Overlay", delegate(ManagementObject mo)
                {
                    s.OverlayUsedMb = (int)Num(mo, "OverlayConsumption");
                    s.OverlayFreeMb = (int)Num(mo, "AvailableSpace");
                    s.OverlayWarnMb = (int)Num(mo, "WarningOverlayThreshold");
                    s.OverlayCritMb = (int)Num(mo, "CriticalOverlayThreshold");
                });
            }
            catch
            {
            }

            bool gotCurrentSession = false;
            try
            {
                Remote.EachMo(scope, "SELECT * FROM UWF_OverlayConfig", delegate(ManagementObject mo)
                {
                    // UWF_OverlayConfig 有两个实例：当前会话 / 下次会话（重启后生效）。
                    // 以当前会话为准——“生效类型”必须反映现在真正在用的缓存，
                    // 而不是重启后才生效的排队值，否则切换类型的判断会建立在陈旧信息上。
                    bool cur = Bool(mo, "CurrentSession");
                    if (gotCurrentSession && !cur) return;
                    if (cur) gotCurrentSession = true;
                    s.OverlayMaxMb = (int)Num(mo, "MaximumSize");
                    object t = Val(mo, "Type");
                    if (t != null) s.OverlayType = Convert.ToInt32(t);
                });
            }
            catch
            {
            }
        }

        private static void FillOverlayFromText(UwfState s)
        {
            if (s.OverlayMaxMb < 0)
            {
                s.OverlayMaxMb = FindMb(s.RawOverlay, "Maximum Size", "最大");
            }
            if (s.OverlayWarnMb < 0)
            {
                s.OverlayWarnMb = FindMb(s.RawOverlay, "Warning Threshold", "警告");
            }
            if (s.OverlayCritMb < 0)
            {
                s.OverlayCritMb = FindMb(s.RawOverlay, "Critical Threshold", "严重");
            }
            if (s.OverlayUsedMb < 0)
            {
                UwfResult r = Run("overlay get-consumption", 30000);
                s.OverlayUsedMb = FirstInt(r.All);
            }
            if (s.OverlayFreeMb < 0)
            {
                UwfResult r = Run("overlay get-available-space", 30000);
                s.OverlayFreeMb = FirstInt(r.All);
            }
            if (s.OverlayType < 0)
            {
                string t = s.RawOverlay.ToUpperInvariant();
                if (t.Contains("RAM")) s.OverlayType = 0;
                else if (t.Contains("DISK") || s.RawOverlay.Contains("磁盘")) s.OverlayType = 1;
            }
        }

        private static int FindMb(string text, string en, string cn)
        {
            if (string.IsNullOrEmpty(text)) return -1;
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim();
                bool hit = line.IndexOf(en, StringComparison.OrdinalIgnoreCase) >= 0 || (cn.Length > 0 && line.Contains(cn));
                if (!hit) continue;
                Match m = Regex.Match(line, @"(\d+)\s*(MB|mb|Mb|兆|M)\b");
                int v;
                if (m.Success && int.TryParse(m.Groups[1].Value, out v)) return v;
                Match m2 = Regex.Match(line, @":\s*(\d+)\s*$");
                if (m2.Success && int.TryParse(m2.Groups[1].Value, out v)) return v;
            }
            return -1;
        }

        private static int FirstInt(string text)
        {
            if (string.IsNullOrEmpty(text)) return -1;
            Match m = Regex.Match(text, @"(\d+)");
            int v;
            if (m.Success && int.TryParse(m.Groups[1].Value, out v)) return v;
            return -1;
        }

        /// <summary>逐个卷读取文件排除项，并把卷内相对路径补成完整路径，方便显示与删除。</summary>
        /// <summary>
        /// 从 uwfmgr volume get-config 的文本里补齐“当前/下次”保护状态（WMI 实例不全时兜底）。
        /// 只补 needCurrent/needNext 指定的那一侧；WMI 已经读到的侧不覆盖。
        /// </summary>
        private static void FillVolumeFromText(VolumeState v, bool needCurrent, bool needNext)
        {
            if (!needCurrent && !needNext) return;
            // 参数两种写法都试：有的版本要 C:，有的要 C:\，只试一种会拿不到输出、补不上状态
            UwfResult r = Run("volume get-config " + v.Letter, 60000);
            if (r.All.Trim().Length == 0 && v.Letter.EndsWith(":", StringComparison.Ordinal))
                r = Run("volume get-config " + v.Letter + "\\", 60000);

            bool? cur, next;
            ParseVolumeText(r.All, out cur, out next);
            if (needCurrent && cur.HasValue) { v.ProtectedCurrent = cur.Value; v.SawCurrentSession = true; }
            if (needNext && next.HasValue) { v.ProtectedNext = next.Value; v.SawNextSession = true; }
        }

        private static List<string> ReadFileExclusions(UwfState s)
        {
            List<string> volumes = new List<string>();
            foreach (VolumeState v in s.Volumes)
            {
                if (v.Letter.Length == 2) volumes.Add(v.Letter);
            }
            if (volumes.Count == 0) volumes.Add("C:");

            List<string> list = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string vol in volumes)
            {
                UwfResult r = Run("file get-exclusions " + vol, 60000);
                foreach (string raw in r.All.Split('\n'))
                {
                    string line = raw.Trim();
                    if (line.Length < 2 || line.StartsWith(@"\?")) continue;
                    string full = null;
                    if (line.StartsWith(@"\")) full = vol + line;
                    else if (Regex.IsMatch(line, @"^[A-Za-z]:\\")) full = line;
                    if (full == null) continue;
                    if (seen.Add(full)) list.Add(full);
                }
            }
            list.Sort(StringComparer.OrdinalIgnoreCase);
            return list;
        }

        private static List<string> ExtractPaths(string text)
        {
            List<string> list = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(text)) return list;
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length < 3) continue;
                bool looksPath = Regex.IsMatch(line, @"^[A-Za-z]:\\") || line.StartsWith(@"\\") ||
                                 (line.StartsWith(@"\") && Regex.IsMatch(line, @"^\\[A-Za-z0-9_\.\-\u4e00-\u9fff]"));
                if (!looksPath) continue;
                if (line.IndexOf(':') == 1 && line.Length <= 3) continue;
                if (seen.Add(line)) list.Add(line);
            }
            list.Sort(StringComparer.OrdinalIgnoreCase);
            return list;
        }

        private static List<string> ExtractKeys(string text)
        {
            List<string> list = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(text)) return list;
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim();
                if (Regex.IsMatch(line, @"^(HKLM|HKCU|HKCR|HKU|HKCC|HKEY_LOCAL_MACHINE|HKEY_CURRENT_USER|HKEY_CLASSES_ROOT|HKEY_USERS)\\",
                                  RegexOptions.IgnoreCase))
                {
                    if (seen.Add(line)) list.Add(line);
                }
            }
            list.Sort(StringComparer.OrdinalIgnoreCase);
            return list;
        }

        private static object Val(ManagementObject mo, string name)
        {
            try { return mo[name]; }
            catch { return null; }
        }

        private static string Str(ManagementObject mo, string name)
        {
            object v = Val(mo, name);
            if (v == null) return "";
            return Convert.ToString(v).Trim();
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

        // ---------- actions ----------

        public static UwfResult EnableProtection() { return Run("filter enable"); }
        public static UwfResult DisableProtection() { return Run("filter disable"); }
        public static UwfResult EnterServicing() { return Run("servicing enable"); }
        public static UwfResult LeaveServicing() { return Run("servicing disable"); }
        public static UwfResult RestartNow() { return Run("filter restart", 30000); }
        public static UwfResult ShutdownNow() { return Run("filter shutdown", 30000); }

        public static UwfResult ProtectVolume(string letter) { return Run("volume protect " + NormalizeVolume(letter)); }
        public static UwfResult UnprotectVolume(string letter) { return Run("volume unprotect " + NormalizeVolume(letter)); }

        public static string NormalizeVolume(string letter)
        {
            string s = (letter ?? "").Trim();
            if (s.Length == 0) return s;
            if (s.Length == 1) return s + ":";
            return s;
        }

        public static UwfResult AddFileExclusion(string path) { return Run("file add-exclusion \"" + path.Trim() + "\""); }
        public static UwfResult RemoveFileExclusion(string path) { return Run("file remove-exclusion \"" + path.Trim() + "\""); }
        public static UwfResult CommitFile(string path) { return Run("file commit \"" + path.Trim() + "\"", 600000); }
        public static UwfResult AddRegExclusion(string key) { return Run("registry add-exclusion \"" + NormalizeRegKey(key) + "\""); }
        public static UwfResult RemoveRegExclusion(string key) { return Run("registry remove-exclusion \"" + NormalizeRegKey(key) + "\""); }
        public static UwfResult CommitReg(string key) { return Run("registry commit \"" + NormalizeRegKey(key) + "\"", 600000); }
        public static string NormalizeRegKey(string key)
        {
            string k = (key ?? "").Trim();
            if (k.StartsWith("HKEY_LOCAL_MACHINE\\", StringComparison.OrdinalIgnoreCase)) k = "HKLM" + k.Substring("HKEY_LOCAL_MACHINE".Length);
            else if (k.StartsWith("HKEY_CURRENT_USER\\", StringComparison.OrdinalIgnoreCase)) k = "HKCU" + k.Substring("HKEY_CURRENT_USER".Length);
            return k;
        }

        public static UwfResult SetOverlayType(bool disk) { return Run("overlay set-type " + (disk ? "DISK" : "RAM")); }
        public static UwfResult SetOverlaySize(int mb) { return Run("overlay set-size " + mb.ToString(CultureInfo.InvariantCulture)); }
        public static UwfResult SetOverlayWarning(int mb) { return Run("overlay set-warningthreshold " + mb.ToString(CultureInfo.InvariantCulture)); }
        public static UwfResult SetOverlayCritical(int mb) { return Run("overlay set-criticalthreshold " + mb.ToString(CultureInfo.InvariantCulture)); }
        public static UwfResult SetPersistent(bool on) { return Run("overlay set-persistent " + (on ? "on" : "off")); }
        public static UwfResult ResetPersistent() { return Run("overlay reset-persistent-state on"); }

        public static UwfResult EnableFeature()
        {
            StringBuilder log = new StringBuilder();
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("dism.exe", "/online /get-features /format:table");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.StandardOutputEncoding = OemEncoding();
                using (Process p = Process.Start(psi))
                {
                    string text = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(60000);
                    foreach (string raw in text.Split('\n'))
                    {
                        string line = raw.Trim();
                        int idx = line.IndexOf('|');
                        if (idx <= 0) continue;
                        string name = line.Substring(0, idx).Trim();
                        if (name.IndexOf("WriteFilter", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        log.AppendLine("找到组件：" + name);
                        ProcessStartInfo ep = new ProcessStartInfo("dism.exe", "/online /enable-feature /featurename:" + name + " /all /norestart");
                        ep.UseShellExecute = false;
                        ep.CreateNoWindow = true;
                        ep.RedirectStandardOutput = true;
                        ep.StandardErrorEncoding = OemEncoding();
                        ep.StandardOutputEncoding = OemEncoding();
                        using (Process epx = Process.Start(ep))
                        {
                            log.AppendLine(epx.StandardOutput.ReadToEnd());
                            epx.WaitForExit(600000);
                            log.AppendLine("退出码：" + epx.ExitCode);
                        }
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                log.AppendLine("启用失败：" + ex.Message);
            }
            UwfResult res = new UwfResult();
            res.Command = "dism enable UWF";
            res.StdOut = log.ToString();
            return res;
        }
    }
}
