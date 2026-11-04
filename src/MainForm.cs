using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace UwfTeacher
{
    public partial class MainForm : CCWin.CCSkinMain
    {
        private UwfState _state = new UwfState();
        private readonly string[] _pageNames =
        {
            "机房总览", "远程安装", "机器清单", "母盘放行检查", "本机状态", "本机安装", "排除管理", "磁盘与缓存", "操作日志"
        };
        private readonly string[] _pageHints =
        {
            "一屏看完所有学生机，批量暂停 / 恢复还原、重启；双击某一行直接打开它的目录",
            "批量把软件装进学生机，重启后仍然存在",
            "添加、导入、扫描学生机，设置统一的连接账号",
            "做母盘前确认 WMI / SMB 已放行，一键检测与修复",
            "本机（母盘）的 UWF 状态与常用操作",
            "在本机安装软件 / 修改设置并保留",
            "本机的文件、目录、注册表排除",
            "本机受保护卷与缓存设置",
            "操作记录与诊断"
        };

        private Panel _pageHost;
        private Label _pageTitle;
        private Label _pageSub;
        private Label _machineLabel;
        private Button _btnRefresh;
        private ThemeToggleButton _btnTheme;
        private Panel _topBar;
        private NavButton[] _nav;
        private Panel[] _pages;
        private Label _logPathLabel;

        // page 1
        private StatCard _scProtect;
        private StatCard _scNext;
        private StatCard _scMode;
        private StatCard _scOverlay;
        private TextBox _raw;
        private Card _cardMissing;
        private Label _missingText;
        private Label _missingDesc;
        private Card _cardNoVolume;

        // page 6
        private ListView _logList;
        private ComboBox _keepCombo;
        private static readonly int[] LogKeepDays = { 7, 30, 90, 180, 0 };

        public static string ShortVersion
        {
            get
            {
                Version v = typeof(MainForm).Assembly.GetName().Version;
                return v.Build > 0 ? (v.Major + "." + v.Minor + "." + v.Build) : (v.Major + "." + v.Minor);
            }
        }
        public int PageCount { get { return _pages.Length; } }
        public bool SuppressStartupRefresh;

        public MainForm()
        {
            Text = "UWFtool · UWF 远程管理";
            Font = Ui.F9;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1280, 820);
            MinimumSize = new Size(1120, 700);
            KeyPreview = true;
            Ui.StyleShell(this);   // CSkin 外壳：圆角窗体 + 窗口阴影 + 标题栏配色

            BuildShell();
            LoadFleet();
            SyncLogKeepCombo();   // 日志页在 BuildShell 里先用默认设置构建，这里按真实设置校正
            if (_settings.ThemeDark) Ui.ApplyTheme(this, true);
            RefreshInventory();

            Log.Added += OnLogAdded;
            Log.RetentionDays = _settings.LogDays;
            Log.Cleanup();
            Log.Info("程序启动  v" + ShortVersion + "  ·  " + Environment.MachineName + "  ·  " + Environment.UserName +
                     "  ·  管理员=" + Uwf.IsAdmin() + "  ·  开发者 小可");
            Log.Info("日志目录：" + Log.Dir);
            if (_settings.PasswordLost)
            {
                Log.Warn("连接账号密码无法解密（可能换了 Windows 账号或计算机），请到「机器清单 → 连接账号」重新输入密码并保存。");
            }
            Shown += delegate
            {
                if (_settings.PasswordLost && !SuppressStartupRefresh)
                {
                    MessageBox.Show(this,
                        "保存的连接账号密码无法解密（通常是因为换了 Windows 账号或计算机）。\r\n请到「机器清单 → 连接账号」重新输入密码并点“保存账号设置”。",
                        "连接账号");
                }
                if (SuppressStartupRefresh) return;
                if (_fleet.Count > 0) RefreshFleet(false);
                else ReloadState(false);
            };
            FormClosing += delegate(object s, FormClosingEventArgs e)
            {
                // 批量任务进行中点 X：确认后立刻退出，后台收尾自己继续（不再像以前一样“关不掉、退出慢”）
                if (_runner != null && _runner.Running)
                {
                    if (MessageBox.Show(this,
                        "还有批量任务在执行，确定退出吗？\r\n已经发到学生机的操作会在后台继续，不会因此中断。",
                        "退出", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                        MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                    {
                        e.Cancel = true;
                        return;
                    }
                    _runner.Cancel();
                }
                // 关窗收尾提速：分层阴影窗销毁慢，先关阴影；日志也不再往列表里刷
                try { Shadow = false; } catch { }
                Log.Added -= OnLogAdded;
            };
            FormClosed += delegate
            {
                if (_runner != null) _runner.Cancel();
                Log.Info("程序退出  v" + ShortVersion);
                Log.Added -= OnLogAdded;
            };
            KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.F5)
                {
                    if (_pages[3] != null && _pages[3].Visible) { RunMasterCheck(); return; }
                    if (_pages[0].Visible || _pages[1].Visible || _pages[2].Visible) RefreshFleet(false);
                    else ReloadState(true);
                }
            };
        }

        // ------------------------------------------------------------------ shell

        private void BuildShell()
        {
            Panel side = new Panel();
            side.Dock = DockStyle.Left;
            side.Width = 232;
            side.Height = ClientSize.Height;
            side.BackColor = Ui.Sidebar;
            side.Paint += delegate(object s, PaintEventArgs e)
            {
                using (Pen p = new Pen(Ui.Border)) e.Graphics.DrawLine(p, side.Width - 1, 0, side.Width - 1, side.Height);
            };
            Controls.Add(side);

            LogoView logo = new LogoView();
            logo.Size = new Size(32, 32);
            logo.Location = new Point(20, 22);
            side.Controls.Add(logo);

            Label brand = Ui.L("UWFtool", Ui.F14B, Ui.TextColor);
            brand.Location = new Point(60, 18);
            side.Controls.Add(brand);

            Label brandSub = Ui.L("UWF 教师控制台", Ui.F85, Ui.MutedLight);
            brandSub.Location = new Point(22, 58);
            side.Controls.Add(brandSub);

            _nav = new NavButton[_pageNames.Length];
            int navY = 88;
            navY = AddNavGroup(side, "机房", navY);
            navY = AddNav(side, 0, navY);
            navY = AddNav(side, 1, navY);
            navY = AddNav(side, 2, navY);
            navY += 12;
            navY = AddNavGroup(side, "母盘", navY);
            navY = AddNav(side, 3, navY);
            navY += 12;
            navY = AddNavGroup(side, "本机", navY);
            navY = AddNav(side, 4, navY);
            navY = AddNav(side, 5, navY);
            navY = AddNav(side, 6, navY);
            navY = AddNav(side, 7, navY);
            navY += 12;
            navY = AddNavGroup(side, "其他", navY);
            navY = AddNav(side, 8, navY);

            GrassStrip grass = new GrassStrip();
            grass.SetBounds(0, side.Height - 100, 232, 56);
            grass.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            side.Controls.Add(grass);

            Label footer = Ui.L("v" + ShortVersion + "  ·  F5 刷新", Ui.F85, Ui.MutedLight);
            footer.Location = new Point(22, side.Height - 62);
            footer.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            side.Controls.Add(footer);

            Label footer2 = Ui.L("开发者 小可 · ikee002.top", Ui.F85, Ui.MutedLight);
            footer2.Location = new Point(22, side.Height - 38);
            footer2.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            side.Controls.Add(footer2);

            Panel content = new Panel();
            content.Dock = DockStyle.Fill;
            content.BackColor = Ui.Bg;
            Controls.Add(content);

            _topBar = new Panel();
            _topBar.Dock = DockStyle.Top;
            _topBar.Height = 76;
            _topBar.BackColor = Ui.CardBg;
            _topBar.Paint += delegate(object s, PaintEventArgs e)
            {
                using (Pen p = new Pen(Ui.Border)) e.Graphics.DrawLine(p, 0, _topBar.Height - 1, _topBar.Width, _topBar.Height - 1);
            };
            content.Controls.Add(_topBar);

            _pageTitle = Ui.L("机房总览", Ui.F14B, Ui.TextColor);
            _pageTitle.Location = new Point(28, 14);
            _topBar.Controls.Add(_pageTitle);

            _pageSub = Ui.L("", Ui.F85, Ui.Muted);
            _pageSub.Location = new Point(28, 42);
            _topBar.Controls.Add(_pageSub);

            _btnRefresh = Ui.B("刷新状态  (F5)");
            _btnRefresh.Width = 124;
            _btnRefresh.Height = 32;
            _btnRefresh.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _btnRefresh.Click += delegate
            {
                if (_pages != null && _pages[3] != null && _pages[3].Visible) { RunMasterCheck(); return; }
                if (_pages != null && _pages[0] != null && (_pages[0].Visible || _pages[1].Visible || _pages[2].Visible)) RefreshFleet(false);
                else ReloadState(true);
            };
            _topBar.Controls.Add(_btnRefresh);

            _btnTheme = new ThemeToggleButton();
            _btnTheme.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _btnTheme.Click += delegate
            {
                bool toDark = !Ui.Dark;
                ThemeReveal.Toggle(this, _btnTheme, toDark);
                _settings.ThemeDark = toDark;
                SettingsStore.Save(_settings);
            };
            _topBar.Controls.Add(_btnTheme);

            _machineLabel = Ui.L("", Ui.F85, Ui.Muted);
            _machineLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _machineLabel.TextAlign = ContentAlignment.MiddleRight;
            _machineLabel.AutoSize = false;
            _machineLabel.AutoEllipsis = true;
            _machineLabel.Height = 20;
            _topBar.Controls.Add(_machineLabel);

            _topBar.Resize += delegate { LayoutTopBar(); };

            _pageHost = new Panel();
            _pageHost.Dock = DockStyle.Fill;
            _pageHost.BackColor = Ui.Bg;
            content.Controls.Add(_pageHost);

            _pages = new Panel[_pageNames.Length];
            _pages[0] = BuildFleetPage();
            _pages[1] = BuildRemoteInstallPage();
            _pages[2] = BuildInventoryPage();
            _pages[3] = BuildMasterCheckPage();
            _pages[4] = BuildStatusPage();
            _pages[5] = BuildInstallPage();
            _pages[6] = BuildExclusionPage();
            _pages[7] = BuildVolumePage();
            _pages[8] = BuildLogPage();
            for (int i = 0; i < _pages.Length; i++)
            {
                _pages[i].Visible = i == 0;
                _pageHost.Controls.Add(_pages[i]);
            }

            SelectPage(0);
            LayoutTopBar();

            // WinForms 按 z-order 反序处理停靠：填充面板必须排在最后（索引最小），
            // 否则它会先占满空间，把左右/上下停靠的面板挤成 0 尺寸。
            content.Controls.SetChildIndex(_pageHost, 0);
            content.Controls.SetChildIndex(_topBar, 1);
            Controls.SetChildIndex(content, 0);
            Controls.SetChildIndex(side, 1);
        }

        private void LayoutTopBar()
        {
            if (_topBar == null) return;
            int right = _topBar.Width - 24;
            _btnRefresh.Location = new Point(right - _btnRefresh.Width, 20);
            if (_btnTheme != null) _btnTheme.Location = new Point(_btnRefresh.Left - 44, 19);
            int x = (_btnTheme != null ? _btnTheme.Left : _btnRefresh.Left) - 18;
            int labelWidth = Math.Min(280, Math.Max(0, x - 360));
            _machineLabel.Visible = labelWidth >= 120;
            _machineLabel.Location = new Point(Math.Max(300, x - labelWidth), 26);
            _machineLabel.Width = labelWidth;
        }

        private int AddNavGroup(Panel side, string text, int y)
        {
            Label g = Ui.L(text, Ui.F85, Ui.MutedLight);
            g.Location = new Point(22, y);
            side.Controls.Add(g);
            return y + 24;
        }

        private int AddNav(Panel side, int index, int y)
        {
            NavButton b = new NavButton(_pageNames[index]);
            b.Location = new Point(0, y);
            b.Width = side.Width;
            b.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            int i = index;
            b.Click += delegate { SelectPage(i); };
            side.Controls.Add(b);
            _nav[index] = b;
            return y + 38;
        }

        public void SelectPage(int index)
        {
            if (index < 0 || index >= _pages.Length) return;
            for (int i = 0; i < _pages.Length; i++)
            {
                _pages[i].Visible = i == index;
                _nav[i].SetSelected(i == index);
            }
            _pageTitle.Text = _pageNames[index];
            _pageSub.Text = _pageHints[index];
            bool fleetPage = index == 0 || index == 1 || index == 2;
            _btnRefresh.Text = fleetPage ? "刷新全部  (F5)" : (index == 3 ? "重新检测  (F5)" : "刷新本机  (F5)");
            LayoutTopBar();

            if (index >= 4 && index <= 7 && !_localLoaded && !SuppressStartupRefresh)
            {
                _localLoaded = true;
                ReloadState(false);
            }
            if (index == 2) RefreshInventory();
            if (index == 3)
            {
                if (MasterAudit.HasFresh) ShowCachedMasterCheck();
                else RunMasterCheck();
            }
            if (index == 8 && _logList != null)
            {
                _logList.BeginUpdate();
                _logList.Items.Clear();
                foreach (string line in Log.All) AppendLog(line);
                _logList.EndUpdate();
            }
        }

        // ------------------------------------------------------------------ state

        public void ReloadState(bool verbose)
        {
            ReloadState(verbose, true);
        }

        /// <summary>
        /// 重读本机 UWF 状态（uwfmgr get-config ×2 + 多条 WMI，1–3 秒）。
        /// showBusy=true 时给遮罩，别让界面假死；false 用在已经在遮罩下的场景（避免遮罩叠遮罩）。
        /// </summary>
        public void ReloadState(bool verbose, bool showBusy)
        {
            UwfState[] slot = new UwfState[1];
            if (showBusy)
            {
                BusyForm busy = new BusyForm("正在读取状态", "正在读取本机 UWF 状态（uwfmgr / WMI）…", 0, null);
                System.Threading.SynchronizationContext ui = System.Threading.SynchronizationContext.Current;
                System.Threading.Tasks.Task.Factory.StartNew(delegate
                {
                    try { slot[0] = Uwf.Read(); }
                    catch (Exception ex) { Log.Error("读取状态异常：" + ex.Message); }
                }).ContinueWith(delegate
                {
                    Action done = delegate { busy.CloseFromOwner(); };
                    if (ui != null) ui.Post(delegate { try { done(); } catch { } }, null);
                    else try { done(); } catch { }
                });
                busy.ShowDialog(this);
                try { busy.Dispose(); } catch { }
            }
            else
            {
                try { slot[0] = Uwf.Read(); }
                catch (Exception ex) { Log.Error("读取状态异常：" + ex.Message); }
            }
            _state = slot[0] ?? new UwfState();

            UpdateStatusPage();
            UpdateInstallPage();
            UpdateExclusionPage();
            UpdateVolumePage();

            if (!_state.Installed)
            {
                Log.Warn("未检测到 UWF 组件（uwfmgr.exe 不存在，或 WMI 里查不到 UWF_Filter）。");
            }
            else
            {
                StringBuilder volText = new StringBuilder();
                foreach (VolumeState vv in _state.Volumes)
                {
                    volText.Append(" " + (vv.Letter.Length > 0 ? vv.Letter : "(无盘符)") +
                                   "[当前=" + vv.ProtectedCurrent + ",下次=" + vv.ProtectedNext + "]");
                }
                Log.Info("读取本机状态：保护=" + (_state.ProtectedNow ? "开" : "关") +
                         "，卷明细=" + (volText.Length == 0 ? " 无" : volText.ToString()) +
                         "，受保护卷=" + _state.ProtectedVolumeText +
                         "，维修模式=" + (_state.InServicing ? "是" : "否") +
                         "，待重启=" + (_state.PendingReboot ? "是" : "否") +
                         "，排除=" + _state.FileExclusions.Count + " 目录 / " + _state.RegExclusions.Count + " 注册表项。");
            }
        }

        // ------------------------------------------------------------------ demo (开发用)

#if DEMO
        public void LoadDemoState(string mode)
        {
            UwfState s = new UwfState();
            s.UwfmgrPresent = true;
            s.WmiPresent = true;
            s.HasFilter = true;
            s.HasServicing = true;
            s.FilterCurrent = !string.Equals(mode, "paused", StringComparison.OrdinalIgnoreCase);
            s.FilterNext = s.FilterCurrent;
            s.OverlayUsedMb = 1832;
            s.OverlayFreeMb = 2264;
            s.OverlayMaxMb = 4096;
            s.OverlayWarnMb = 3072;
            s.OverlayCritMb = 3840;
            s.OverlayType = 1;

            VolumeState c = new VolumeState();
            c.Letter = "C:";
            c.Label = "系统";
            c.CapacityGb = 476;
            bool noVolume = string.Equals(mode, "novolume", StringComparison.OrdinalIgnoreCase);
            c.ProtectedCurrent = !noVolume;
            c.ProtectedNext = !noVolume;
            VolumeState d = new VolumeState();
            d.Letter = "D:";
            d.Label = "数据";
            d.CapacityGb = 931;
            d.ProtectedCurrent = false;
            d.ProtectedNext = false;
            s.Volumes.Add(c);
            s.Volumes.Add(d);

            s.FileExclusions.Add(@"D:\作业");
            s.FileExclusions.Add(@"C:\ProgramData\K12\题库");
            s.RegExclusions.Add(@"HKLM\SOFTWARE\K12");

            s.RawConfig = "Current Session Settings\r\n-----------------\r\nFilter state          : Enabled\r\nProtected volumes     : C:\r\nOverlay type          : Disk\r\n\r\nNext Session Settings\r\n-----------------\r\nFilter state          : Enabled\r\nProtected volumes     : C:\r\nOverlay type          : Disk";
            s.RawOverlay = "Current Session Settings\r\n-----------------\r\nOverlay type          : Disk\r\nMaximum size          : 4096 MB\r\nWarning threshold     : 3072 MB\r\nCritical threshold    : 3840 MB";

            _state = s;
            UpdateStatusPage();
            UpdateInstallPage();
            UpdateExclusionPage();
            UpdateVolumePage();

            LoadDemoFleet();
        }

        private void LoadDemoFleet()
        {
            _fleet.Clear();
            _byName.Clear();
            string[] names = { "PC-01", "PC-02", "PC-03", "PC-04", "PC-05", "PC-06",
                               "PC-07", "PC-08", "PC-09", "PC-10", "PC-11", "PC-12" };
            for (int i = 0; i < names.Length; i++)
            {
                Machine m = new Machine();
                m.Name = names[i];
                m.Group = i < 6 ? "机房1" : "机房2";
                m.LastSeen = DateTime.Now.AddMinutes(-3);

                RemoteState st = new RemoteState();
                st.UwfPresent = true;
                st.Transport = RemoteTransport.Wmi;
                st.Ok = true;
                st.FilterCurrent = true;
                st.FilterNext = true;
                st.Used = 780 + i * 37;
                st.Max = 4096;
                st.Free = 4096 - st.Used;
                st.Warn = 3072;
                st.Crit = 3840;
                st.Type = 1;
                VolumeState v = new VolumeState();
                v.Letter = "C:";
                v.ProtectedCurrent = true;
                v.ProtectedNext = true;
                st.Volumes.Add(v);

                if (i == 8)
                {
                    st.FilterCurrent = false;
                    st.FilterNext = false;
                    m.LastAction = "暂停还原";
                    m.StatusText = "正常";
                }
                else if (i == 9)
                {
                    st.FilterNext = false;
                    m.LastAction = "暂停还原";
                    m.StatusText = "正常";
                }
                else if (i == 10)
                {
                    st = new RemoteState();
                    st.Ok = false;
                    st.Transport = RemoteTransport.None;
                    st.Error = "WMI：无法连接目标机的 WMI（RPC 服务器不可用）。";
                    m.StatusText = "失败";
                    m.LastError = st.Error;
                }
                else if (i == 11)
                {
                    st.Transport = RemoteTransport.Smb;
                    st.Used = 2100;
                    m.LastAction = "恢复还原";
                    m.StatusText = "正常";
                }
                else
                {
                    m.StatusText = "正常";
                    if (i % 3 == 0) m.LastAction = "刷新状态";
                }

                m.State = st;
                _fleet.Add(m);
                _byName[m.Name] = m;
            }
            RebuildGrid();
            RefreshInventory();
            UpdateFleetStats();
            ShowDetailForSelected();
        }

        public void DemoCaptureDialogs(string dir, System.Text.StringBuilder log)
        {
            using (DiscoveryForm d = new DiscoveryForm(TargetForHost))
            {
                d.StartPosition = FormStartPosition.Manual;
                d.Location = new Point(-2400, 40);
                d.ShowInTaskbar = false;
                d.Show();
                d.LoadDemoHosts();
                Application.DoEvents();
                using (Bitmap bmp = new Bitmap(d.Width, d.Height))
                {
                    d.DrawToBitmap(bmp, new Rectangle(0, 0, d.Width, d.Height));
                    string path = Path.Combine(dir, "discovery.png");
                    bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                    log.AppendLine("saved " + path);
                }
                d.Hide();
            }

            using (ProtectDialog p = new ProtectDialog(_state, _settings))
            {
                p.StartPosition = FormStartPosition.Manual;
                p.Location = new Point(-2400, 40);
                p.ShowInTaskbar = false;
                p.Show();
                Application.DoEvents();
                using (Bitmap bmp = new Bitmap(p.Width, p.Height))
                {
                    p.DrawToBitmap(bmp, new Rectangle(0, 0, p.Width, p.Height));
                    string path = Path.Combine(dir, "protect.png");
                    bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                    log.AppendLine("saved " + path);
                }
                p.Hide();
            }

            // 维修模式风险提示框：正文有加粗，必须亲眼确认没被裁掉
            using (WarnDialog w = WarnDialog.CreatePreview("进入维修模式（仅 Windows 更新用）",
                "即将让 6 台计算机进入维修模式",
                "维修模式是微软专门留给 Windows 更新的模式：进去后机器会重启并自动检查更新，期间 UWF 不还原。\r\n\r\n" +
                "**维修模式必须等机房所有电脑的 Windows 更新全部安装完成，才能退出。** 更新装到一半就退出、断电或强制重启，" +
                "机器会停在“正在配置更新”，甚至黑屏进不了桌面。\r\n\r\n" +
                "**装软件、改设置不要用维修模式** —— 请用「UWF ▼ → 关闭 UWF」或远程安装向导的「暂停还原」，那只是临时关闭保护，随时可以恢复。\r\n\r\n" +
                "**日常请保持 UWF 保护开启，一般不要进入维修模式。**\r\n\r\n" +
                "本次目标：PC-01、PC-02、PC-03、PC-04、PC-05、PC-06"))
            {
                w.StartPosition = FormStartPosition.Manual;
                w.Location = new Point(80, 80);
                w.ShowInTaskbar = false;
                w.TopMost = true;
                w.Show();
                w.Activate();
                Application.DoEvents();
                System.Threading.Thread.Sleep(220);
                Application.DoEvents();
                // RichTextBox 用 DrawToBitmap 画不出来，这里直接抓屏
                using (Bitmap bmp = new Bitmap(w.Width, w.Height))
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.CopyFromScreen(w.Left, w.Top, 0, 0, new Size(w.Width, w.Height));
                    }
                    string path = Path.Combine(dir, "warn-servicing.png");
                    bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                    log.AppendLine("saved " + path);
                }
                log.AppendLine("warn dialog: " + w.PreviewDiagnostics());
                w.Hide();
            }

            // 批量设置保护层对话框：确认预设行、三项数值与说明文字都不被裁
            using (OverlayBatchDialog o = new OverlayBatchDialog(12, "192.168.140.157", 4096, 3072, 3840, 1))
            {
                o.StartPosition = FormStartPosition.Manual;
                o.Location = new Point(80, 80);
                o.ShowInTaskbar = false;
                o.TopMost = true;
                o.Show();
                o.Activate();
                Application.DoEvents();
                System.Threading.Thread.Sleep(220);
                Application.DoEvents();
                using (Bitmap bmp = new Bitmap(o.Width, o.Height))
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.CopyFromScreen(o.Left, o.Top, 0, 0, new Size(o.Width, o.Height));
                    }
                    string path = Path.Combine(dir, "overlay-batch.png");
                    bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                    log.AppendLine("saved " + path);
                }
                o.Hide();
            }

            // 分发/安装遮罩：三行说明要能放下“312.4 MB / 698.1 MB（45%），12.4 MB/s，约剩 31 秒”
            using (BusyForm b = new BusyForm("正在分发并安装",
                "正在复制到 PC-07：312.4 MB / 698.1 MB（45%），12.4 MB/s，约剩 31 秒", 6, null))
            {
                b.StartPosition = FormStartPosition.Manual;
                b.Location = new Point(-2400, 40);
                b.ShowInTaskbar = false;
                b.Show();
                Application.DoEvents();
                b.SetProgress(-1, -1, "正在安装 PC-07：已用 3 分 12 秒（安装程序退出后自动继续，最多等 30 分钟）");
                Application.DoEvents();
                using (Bitmap bmp = new Bitmap(b.Width, b.Height))
                {
                    b.DrawToBitmap(bmp, new Rectangle(0, 0, b.Width, b.Height));
                    string path = Path.Combine(dir, "busy.png");
                    bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                    log.AppendLine("saved " + path);
                }
                b.Hide();
            }
        }
        public void DemoCaptureInstallWizard(string dir, System.Text.StringBuilder log)
        {
            using (RemoteInstallForm dlg = new RemoteInstallForm(new List<Machine>(_fleet), _settings, TargetFor))
            {
                dlg.StartPosition = FormStartPosition.Manual;
                dlg.Location = new Point(-2400, 40);
                dlg.ShowInTaskbar = false;
                dlg.Show();
                Application.DoEvents();
                using (Bitmap bmp = new Bitmap(dlg.Width, dlg.Height))
                {
                    dlg.DrawToBitmap(bmp, new Rectangle(0, 0, dlg.Width, dlg.Height));
                    string path = Path.Combine(dir, "wizard.png");
                    bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                    log.AppendLine("saved " + path);
                }
                dlg.Hide();
            }
        }
#endif
        private void UpdateHeader()
        {
            FleetSummary s = FleetSummary.Of(_fleet);
            _machineLabel.Text = "教师机 " + Environment.MachineName + "  ·  在线 " + s.Online + " / " + s.Total + " 台";
            LayoutTopBar();
        }

        // ------------------------------------------------------------------ page 1

        private Panel BuildStatusPage()
        {
            Panel page = new Panel();
            page.Dock = DockStyle.Fill;
            page.BackColor = Ui.Bg;

            FlowLayoutPanel stack = Ui.Stack();
            page.Controls.Add(stack);

            _cardMissing = new Card("尚未安装 UWF 组件", 150, Ui.Red);
            _missingDesc = Ui.Wrap("本机找不到 uwfmgr.exe，也没有 UWF 的 WMI 接口。教育版 / 企业版 / IoT 版才自带该组件，专业版与家庭版没有。", Ui.F9, Ui.TextColor);
            _missingDesc.Location = new Point(0, 2);
            _missingDesc.Size = new Size(760, 40);
            _cardMissing.Body.Controls.Add(_missingDesc);
            Button bEnable = Ui.B("尝试启用组件（DISM）");
            bEnable.Location = new Point(0, 52);
            bEnable.Width = 190;
            bEnable.Click += delegate
            {
                if (!Confirm("启用 UWF 组件",
                    "将通过 DISM 尝试启用系统自带的统一写入筛选器组件，完成后需要重启。\r\n\r\n" +
                    "如果系统版本是专业版 / 家庭版，可能找不到该组件。")) return;
                RunAction(delegate { return Uwf.EnableFeature(); }, "组件启用命令已执行，请重启后重新打开本工具。");
            };
            _cardMissing.Body.Controls.Add(bEnable);
            _missingText = Ui.Wrap("", Ui.F85, Ui.Muted);
            _missingText.Location = new Point(200, 58);
            _missingText.Size = new Size(560, 34);
            _cardMissing.Body.Controls.Add(_missingText);
            _cardMissing.Visible = false;
            stack.Controls.Add(_cardMissing);
            Card noVol = new Card("磁盘没有加入保护列表（改动不会还原）", 150, Ui.Red);
            Label nvText = Ui.Wrap("UWF 过滤器已开启，但没有任何磁盘在保护列表里。此时所有写入都直接落盘，重启当然不会还原。" +
                                   "点下面的按钮保护 C 盘，然后重启一次。", Ui.F9, Ui.TextColor);
            nvText.Location = new Point(0, 2);
            nvText.Size = new Size(760, 40);
            noVol.Body.Controls.Add(nvText);

            Button fixVol = Ui.B("保护 C 盘（重启后生效）", true);
            fixVol.Location = new Point(0, 54);
            fixVol.Width = 210;
            fixVol.Height = 34;
            fixVol.Click += delegate { ProtectSystemVolume(); };
            noVol.Body.Controls.Add(fixVol);

            Button openVol = Ui.B("打开磁盘与缓存");
            openVol.Location = new Point(222, 54);
            openVol.Width = 150;
            openVol.Height = 34;
            openVol.Click += delegate { SelectPage(7); };
            noVol.Body.Controls.Add(openVol);

            noVol.Visible = false;
            _cardNoVolume = noVol;
            stack.Controls.Add(noVol);

            TableLayoutPanel statRow = new TableLayoutPanel();
            statRow.ColumnCount = 4;
            statRow.RowCount = 1;
            statRow.Height = 112;
            statRow.Margin = new Padding(0, 0, 0, 14);
            statRow.BackColor = Color.Transparent;
            statRow.Tag = "wide";
            for (int i = 0; i < 4; i++) statRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
            statRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            _scProtect = new StatCard();
            _scProtect.Caption = "当前保护状态";
            _scProtect.Tint = Ui.TintGreen;
            _scProtect.Dock = DockStyle.Fill;
            _scProtect.Margin = new Padding(0, 0, 12, 0);

            _scNext = new StatCard();
            _scNext.Caption = "重启后的保护状态";
            _scNext.Tint = Ui.TintBlue;
            _scNext.Dock = DockStyle.Fill;
            _scNext.Margin = new Padding(0, 0, 12, 0);

            _scMode = new StatCard();
            _scMode.Caption = "运行模式";
            _scMode.Tint = Ui.SoftGray;
            _scMode.Dock = DockStyle.Fill;
            _scMode.Margin = new Padding(0, 0, 12, 0);

            _scOverlay = new StatCard();
            _scOverlay.Caption = "缓存占用";
            _scOverlay.Tint = Ui.TintIndigo;
            _scOverlay.Dock = DockStyle.Fill;
            _scOverlay.Margin = new Padding(0, 0, 0, 0);

            statRow.Controls.Add(_scProtect, 0, 0);
            statRow.Controls.Add(_scNext, 1, 0);
            statRow.Controls.Add(_scMode, 2, 0);
            statRow.Controls.Add(_scOverlay, 3, 0);
            stack.Controls.Add(statRow);

            Card actions = new Card("常用操作", 214);
            TableLayoutPanel tiles = new TableLayoutPanel();
            tiles.Dock = DockStyle.Fill;
            tiles.ColumnCount = 3;
            tiles.RowCount = 2;
            tiles.BackColor = Ui.CardBg;
            tiles.Margin = new Padding(0);
            for (int i = 0; i < 3; i++) tiles.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
            for (int i = 0; i < 2; i++) tiles.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));
            actions.Body.Controls.Add(tiles);

            Panel[] tiles6 = new Panel[6];
            tiles6[0] = ActionTile("启用保护", "启用还原，同时设置保护层（缓存）大小。", delegate { DoEnableProtection(); });
            tiles6[1] = ActionTile("关闭保护", "装软件 / 改设置时用（暂停还原），重启后生效。", delegate
            {
                if (!Confirm("关闭保护（暂停还原）", "关闭后重启，系统暂时不再还原，改动会直接写入硬盘——装软件、改设置就用这个。\r\n\r\n" +
                                          "装完请立刻点「启用保护」并重启，否则这台机器会一直不还原。\r\n\r\n确定继续？")) return;
                RunAction(delegate { return Uwf.DisableProtection(); }, "关闭保护已排队，重启后生效。");
            });
            tiles6[2] = ActionTile("进入维修模式", "仅 Windows 更新用，一般不要使用。", delegate { DoEnterServicingMode(); });
            tiles6[3] = ActionTile("退出维修模式", "先确认更新全部装完，再退出。", delegate { DoLeaveServicingMode(); });
            tiles6[4] = ActionTile("立即重启", "立即重启计算机，使配置生效。", delegate
            {
                if (!ConfirmDanger("立即重启", "计算机将立即重启，请先保存正在编辑的文件。")) return;
                RunAction(delegate { return Uwf.RestartNow(); }, "已发送重启命令。");
            });
            tiles6[5] = ActionTile("立即关机", "立即关闭计算机。", delegate
            {
                if (!ConfirmDanger("立即关机", "计算机将立即关机，请先保存正在编辑的文件。")) return;
                RunAction(delegate { return Uwf.ShutdownNow(); }, "已发送关机命令。");
            });
            for (int i = 0; i < 6; i++)
            {
                Panel tile = tiles6[i];
                tile.Dock = DockStyle.Fill;
                tile.Margin = new Padding(0, 0, 12, 8);
                tiles.Controls.Add(tile, i % 3, i / 3);
            }
            stack.Controls.Add(actions);

            Card rawCard = new Card("配置详情（uwfmgr 原始输出）", 272);
            TableLayoutPanel rawLayout = new TableLayoutPanel();
            rawLayout.Dock = DockStyle.Fill;
            rawLayout.Margin = new Padding(0);
            rawLayout.ColumnCount = 1;
            rawLayout.RowCount = 2;
            rawLayout.BackColor = Ui.CardBg;
            rawLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            rawLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            rawCard.Body.Controls.Add(rawLayout);

            _raw = new TextBox();
            _raw.Multiline = true;
            _raw.ReadOnly = true;
            _raw.ScrollBars = ScrollBars.Both;
            _raw.WordWrap = false;
            _raw.Dock = DockStyle.Fill;
            _raw.Margin = new Padding(0);
            _raw.Font = Ui.Mono;
            _raw.BackColor = Ui.RawBg;
            _raw.ForeColor = Ui.TextColor;
            _raw.BorderStyle = BorderStyle.FixedSingle;
            rawLayout.Controls.Add(_raw, 0, 0);

            FlowLayoutPanel rawButtons = new FlowLayoutPanel();
            rawButtons.Dock = DockStyle.Fill;
            rawButtons.FlowDirection = FlowDirection.LeftToRight;
            rawButtons.Margin = new Padding(0, 8, 0, 0);
            rawButtons.BackColor = Ui.CardBg;
            Button copy = Ui.B("复制");
            copy.Width = 84;
            copy.Click += delegate
            {
                try { Clipboard.SetText(_raw.Text); Log.Info("已复制配置文本到剪贴板。"); }
                catch (Exception ex) { MessageBox.Show("复制失败：" + ex.Message); }
            };
            Button save = Ui.B("保存为文件");
            save.Width = 110;
            save.Click += delegate
            {
                SaveFileDialog dlg = new SaveFileDialog();
                dlg.FileName = "uwf-config-" + Environment.MachineName + ".txt";
                dlg.Filter = "文本文件|*.txt";
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    File.WriteAllText(dlg.FileName, _raw.Text, Encoding.UTF8);
                    Log.Info("配置输出已保存：" + dlg.FileName);
                }
            };
            Button dump = Ui.B("导出诊断信息");
            dump.Width = 128;
            dump.Click += delegate { ExportDiagnostics(); };
            rawButtons.Controls.Add(copy);
            rawButtons.Controls.Add(save);
            rawButtons.Controls.Add(dump);
            rawLayout.Controls.Add(rawButtons, 0, 1);
            stack.Controls.Add(rawCard);

            return page;
        }

        private Panel ActionTile(string title, string desc, EventHandler onClick)
        {
            Panel tile = new Panel();
            tile.Width = 300;
            tile.Height = 84;
            tile.Margin = new Padding(0, 0, 12, 8);
            tile.BackColor = Ui.CardBg;

            Button b = Ui.B(title);
            b.Width = 178;
            b.Height = 32;
            b.Location = new Point(0, 0);
            b.Click += onClick;
            tile.Controls.Add(b);

            Label d = Ui.Wrap(desc, Ui.F85, Ui.Muted);
            d.Location = new Point(1, 40);
            d.Size = new Size(270, 40);
            d.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            tile.Controls.Add(d);
            tile.Resize += delegate { d.Width = Math.Max(120, tile.Width - 14); };
            return tile;
        }

        private void UpdateStatusPage()
        {
            if (_scProtect == null) return;
            bool installed = _state.Installed;
            _cardMissing.Visible = !installed;
            if (!installed)
            {
                _cardNoVolume.Visible = false;   // 组件都没了，别再挂着“磁盘未加入保护”的红卡
                bool denied = IsAccessDenied(_state.Error);
                _cardMissing.Title = denied ? "无法读取 UWF 状态（请以管理员身份运行）" : "尚未安装 UWF 组件";
                _cardMissing.Invalidate();
                _missingDesc.Text = denied
                    ? "可以连接到 UWF 的接口，但读取被拒绝。请关闭本窗口，右键『以管理员身份运行』重试。"
                    : "本机找不到 uwfmgr.exe，也查不到 UWF_Filter。教育版 / 企业版 / IoT 版才自带该组件，专业版与家庭版没有。";
            }
            _missingText.Text = installed ? "" : Ui.Short(_state.Error, 140);

            if (!installed)
            {
                _scProtect.Value = "未安装";
                _scProtect.ValueColor = Ui.Red;
                _scProtect.Sub = "未检测到 UWF 组件";
                _scNext.Value = "--";
                _scNext.ValueColor = Ui.Muted;
                _scNext.Sub = "";
                _scMode.Value = "--";
                _scMode.ValueColor = Ui.Muted;
                _scMode.Sub = "";
                _scOverlay.Value = "--";
                _scOverlay.Sub = "";
                _scOverlay.Progress = -1;
                _raw.Text = "uwfmgr.exe 路径：" + Uwf.ExePath + "\r\n存在：" + File.Exists(Uwf.ExePath) + "\r\n错误：" + _state.Error;
                return;
            }

            bool curProt = _state.HasProtectedVolume;
            bool nextProt = _state.HasProtectedVolumeNext;
            bool noVolume = _state.FilterCurrent && !curProt && !nextProt;
            _cardNoVolume.Visible = noVolume;

            if (noVolume)
            {
                _scProtect.Value = "未保护磁盘";
                _scProtect.ValueColor = Ui.Red;
                _scProtect.Sub = "过滤器开着，但没有磁盘在保护列表";
            }
            else if (_state.FilterCurrent && !curProt && nextProt)
            {
                // 过滤器已开、卷保护已排队：必须重启一次才真正生效，文案把卷名写出来
                _scProtect.Value = "已排队";
                _scProtect.ValueColor = Ui.Amber;
                _scProtect.Sub = "重启后开始保护 " + _state.PendingVolumeText + "（需重启一次）";
            }
            else if (!_state.FilterCurrent && _state.FilterNext)
            {
                _scProtect.Value = "已排队开启";
                _scProtect.ValueColor = Ui.Amber;
                _scProtect.Sub = "重启后开始保护" + (nextProt ? " " + _state.PendingVolumeText : "") + "（需重启一次）";
            }
            else if (!_state.FilterCurrent)
            {
                _scProtect.Value = "已暂停";
                _scProtect.ValueColor = Ui.Amber;
                _scProtect.Sub = "保护已关闭（暂停还原）：改动会永久保存";
            }
            else if (_state.ProtectedNow)
            {
                _scProtect.Value = "已开启";
                _scProtect.ValueColor = Ui.Green;
                _scProtect.Sub = "保护卷 " + _state.ProtectedVolumeText;
            }
            else
            {
                _scProtect.Value = "已关闭";
                _scProtect.ValueColor = Ui.Red;
                _scProtect.Sub = "改动会写入硬盘";
            }

            _scNext.Value = _state.FilterNext ? "开启" : "关闭";
            _scNext.ValueColor = _state.FilterNext ? Ui.Green : Ui.Red;
            _scNext.Sub = _state.PendingReboot ? "待重启后切换" : "与当前一致";

            _scMode.Value = _state.ServicingCurrent ? "维修模式" : (_state.FilterCurrent ? "正常" : "已关闭保护");
            _scMode.ValueColor = _state.ProtectedNow ? Ui.TextColor : Ui.Amber;
            _scMode.Sub = _state.ServicingCurrent ? "Windows 更新中，更新装完再退出"
                          : (_state.FilterCurrent ? "受保护运行中" : "装完请及时启用保护");

            if (_state.OverlayUsedMb >= 0)
            {
                _scOverlay.Value = _state.OverlayUsedMb + " MB";
                _scOverlay.ValueColor = Ui.TextColor;
                string maxText = _state.OverlayMaxMb > 0 ? _state.OverlayMaxMb + " MB" : "未知";
                _scOverlay.Sub = "上限 " + (_state.OverlayMaxMb > 0 ? _state.OverlayMaxMb + " MB" : "未知") + " · 剩 " + (_state.OverlayFreeMb >= 0 ? _state.OverlayFreeMb + " MB" : "未知");
                if (_state.OverlayMaxMb > 0)
                {
                    int pct = (int)(_state.OverlayUsedMb * 100.0 / _state.OverlayMaxMb);
                    _scOverlay.Progress = pct;
                    _scOverlay.ProgressColor = pct >= 90 ? Ui.Red : (pct >= 70 ? Ui.Amber : Ui.Accent);
                }
                else
                {
                    _scOverlay.Progress = -1;
                }
            }
            else
            {
                _scOverlay.Value = "--";
                _scOverlay.ValueColor = Ui.Muted;
                _scOverlay.Sub = "无法读取缓存信息";
                _scOverlay.Progress = -1;
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("计算机：" + Environment.MachineName + "    用户：" + Environment.UserName);
            sb.AppendLine("UWF 组件：" + (installed ? "已安装" : "未安装") + "    WMI：" + (_state.WmiPresent ? "可用" : "不可用"));
            sb.AppendLine("保护（当前 / 下次重启）：" + (_state.FilterCurrent ? "开启" : "关闭") + " / " + (_state.FilterNext ? "开启" : "关闭"));
            sb.AppendLine("维修模式 Servicing（仅 Windows 更新用，当前 / 下次重启）：" + (_state.ServicingCurrent ? "开启" : "关闭") + " / " + (_state.ServicingNext ? "开启" : "关闭"));
            sb.AppendLine("缓存类型：" + (_state.OverlayType == 0 ? "内存 RAM" : (_state.OverlayType == 1 ? "磁盘 DISK" : "未知")) +
                          "    上限：" + _state.OverlayMaxMb + " MB    已用：" + _state.OverlayUsedMb + " MB");
            sb.AppendLine();
            sb.AppendLine(_state.RawConfig);
            sb.AppendLine();
            sb.AppendLine("---------- overlay get-config ----------");
            sb.AppendLine(_state.RawOverlay);
            _raw.Text = sb.ToString();
        }

        // ------------------------------------------------------------------ page 6

        private Panel BuildLogPage()
        {
            Panel page = new Panel();
            page.Dock = DockStyle.Fill;
            page.BackColor = Ui.Bg;

            FlowLayoutPanel stack = Ui.Stack();
            page.Controls.Add(stack);

            Card card = new Card("操作日志", 312);
            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.Margin = new Padding(0);
            layout.ColumnCount = 1;
            layout.RowCount = 2;
            layout.BackColor = Ui.CardBg;
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            card.Body.Controls.Add(layout);

            _logList = new ListView();
            Ui.StyleList(_logList);
            _logList.Dock = DockStyle.Fill;
            _logList.Margin = new Padding(0);
            _logList.View = View.Details;
            _logList.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            _logList.Columns.Add("时间", 90);
            _logList.Columns.Add("级别", 60);
            _logList.Columns.Add("内容", 800);
            layout.Controls.Add(_logList, 0, 0);
            _logList.Resize += delegate
            {
                if (_logList.Columns.Count < 3) return;
                int rest = _logList.ClientSize.Width - 4 - _logList.Columns[0].Width - _logList.Columns[1].Width;
                if (rest > 200) _logList.Columns[2].Width = rest;
            };

            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.FlowDirection = FlowDirection.LeftToRight;
            buttons.Margin = new Padding(0, 8, 0, 0);
            buttons.BackColor = Ui.CardBg;

            Button open = Ui.B("打开日志文件夹");
            open.Width = 130;
            open.Click += delegate
            {
                try { using (Process.Start("explorer.exe", "\"" + Log.Dir + "\"")) { } }
                catch (Exception ex) { MessageBox.Show("打开失败：" + ex.Message); }
            };
            Button saveLog = Ui.B("导出日志");
            saveLog.Width = 96;
            saveLog.Click += delegate
            {
                SaveFileDialog dlg = new SaveFileDialog();
                dlg.FileName = "uwf-log-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".txt";
                dlg.Filter = "文本文件|*.txt";
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    File.WriteAllLines(dlg.FileName, _logList.Items.Cast<ListViewItem>().Select(delegate(ListViewItem it)
                    {
                        return it.SubItems[0].Text + "  [" + it.SubItems[1].Text + "]  " + it.SubItems[2].Text;
                    }).ToArray(), Encoding.UTF8);
                }
            };
            Button clear = Ui.B("清空显示");
            clear.Width = 96;
            clear.Click += delegate { _logList.Items.Clear(); };
            buttons.Controls.Add(open);
            buttons.Controls.Add(saveLog);
            buttons.Controls.Add(clear);
            layout.Controls.Add(buttons, 0, 1);

            stack.Controls.Add(card);

            Card logCfg = new Card("日志文件", 142);
            Label pathLabel = new Label();
            pathLabel.Text = "目录：" + Log.Dir + (Log.IsPortable ? "（程序目录）" : "（ProgramData）");
            pathLabel.Font = Ui.F85;
            pathLabel.ForeColor = Ui.Muted;
            pathLabel.BackColor = Color.Transparent;
            pathLabel.AutoSize = false;
            pathLabel.AutoEllipsis = true;
            pathLabel.Tag = "fillw";
            pathLabel.Location = new Point(0, 0);
            pathLabel.Size = new Size(700, 20);
            logCfg.Body.Controls.Add(pathLabel);
            _logPathLabel = pathLabel;

            Label keepLabel = Ui.L("保留天数", Ui.F9, Ui.TextColor);
            keepLabel.Location = new Point(0, 28);
            logCfg.Body.Controls.Add(keepLabel);

            ComboBox keep = new ComboBox();
            keep.DropDownStyle = ComboBoxStyle.DropDownList;
            keep.Font = Ui.F9;
            keep.Width = 120;
            keep.Location = new Point(72, 24);
            keep.Items.AddRange(new object[] { "7 天", "30 天", "90 天", "180 天", "永久保留" });
            keep.SelectedIndex = 1;
            logCfg.Body.Controls.Add(keep);
            _keepCombo = keep;
            SyncLogKeepCombo();

            Button saveKeep = Ui.B("保存设置");
            saveKeep.Location = new Point(206, 23);
            saveKeep.Width = 96;
            saveKeep.Height = 28;
            saveKeep.Click += delegate
            {
                _settings.LogDays = LogKeepDays[Math.Max(0, Math.Min(LogKeepDays.Length - 1, keep.SelectedIndex))];
                if (!SettingsStore.Save(_settings))
                {
                    MessageBox.Show(this, "设置没能保存到磁盘，详见操作日志。", "保存失败",
                                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                Log.RetentionDays = _settings.LogDays;
                int removed = Log.Cleanup();
                MessageBox.Show(this, "已保存。" + (removed > 0 ? "\r\n清理了 " + removed + " 个旧日志。" : ""), "日志设置");
            };
            logCfg.Body.Controls.Add(saveKeep);

            Button cleanNow = Ui.B("立即清理");
            cleanNow.Location = new Point(310, 23);
            cleanNow.Width = 96;
            cleanNow.Height = 28;
            cleanNow.Click += delegate
            {
                int removed = Log.Cleanup();
                MessageBox.Show(this, removed == 0 ? "没有需要清理的旧日志。" : "已清理 " + removed + " 个旧日志。", "日志清理");
            };
            logCfg.Body.Controls.Add(cleanNow);

            Label allLogs = Ui.Wrap("每次启动、每次读取状态、每次操作都会记到 uwf-日期.log，保留天数到期后自动删除。", Ui.F85, Ui.Muted);
            allLogs.Location = new Point(0, 62);
            allLogs.Size = new Size(760, 22);
            logCfg.Body.Controls.Add(allLogs);
            stack.Controls.Add(logCfg);

            foreach (string line in Log.All) AppendLog(line);

            Card about = new Card("关于", 152);
            LogoView aboutLogo = new LogoView();
            aboutLogo.Size = new Size(46, 46);
            aboutLogo.Location = new Point(0, 2);
            about.Body.Controls.Add(aboutLogo);

            Label an = Ui.L("UWFtool", Ui.F12B, Ui.TextColor);
            an.Location = new Point(60, 4);
            about.Body.Controls.Add(an);

            Label av = Ui.L("版本 " + ShortVersion + "   ·   开发者：小可", Ui.F9, Ui.Muted);
            av.Location = new Point(60, 30);
            about.Body.Controls.Add(av);

            LinkLabel site = new LinkLabel();
            site.Text = "www.ikee002.top";
            site.Font = Ui.F9;
            site.AutoSize = true;
            site.BackColor = Color.Transparent;
            site.LinkColor = Ui.Accent;
            site.ActiveLinkColor = Ui.AccentDark;
            site.LinkBehavior = LinkBehavior.HoverUnderline;
            site.Location = new Point(60, 54);
            site.Click += delegate
            {
                try { using (Process.Start("http://www.ikee002.top")) { } }
                catch (Exception ex) { MessageBox.Show(this, "无法打开浏览器：" + ex.Message); }
            };
            about.Body.Controls.Add(site);

            Label ad = Ui.Wrap("只调用 Windows 自带的 UWF 接口（uwfmgr.exe 与 WMI），不收集、不上传任何数据。", Ui.F85, Ui.MutedLight);
            ad.Location = new Point(0, 70);
            ad.Size = new Size(700, 20);
            about.Body.Controls.Add(ad);
            stack.Controls.Add(about);
            return page;
        }

        // 日志批量刷：批量操作时几十行日志会瞬间涌进来，逐行 BeginInvoke 会把 UI 消息队列冲爆，
        // 表现就是弹窗和表格都“卡卡的”。攒一批、一次刷完。
        private readonly Queue<string> _pendingLog = new Queue<string>();
        private bool _logFlushQueued;

        private void OnLogAdded(string line)
        {
            if (!IsHandleCreated) return;
            lock (_pendingLog)
            {
                _pendingLog.Enqueue(line);
                if (_logFlushQueued) return;
                _logFlushQueued = true;
            }
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    List<string> batch = new List<string>();
                    lock (_pendingLog)
                    {
                        _logFlushQueued = false;
                        while (_pendingLog.Count > 0 && batch.Count < 400) batch.Add(_pendingLog.Dequeue());
                    }
                    bool updating = _logList != null && !_logList.IsDisposed;
                    if (updating) _logList.BeginUpdate();
                    try
                    {
                        foreach (string s in batch) AppendLog(s);
                    }
                    finally
                    {
                        if (updating) _logList.EndUpdate();
                    }
                    // 写盘失败不能只吞掉：日志页直接标红，否则出问题后拿到的日志是缺行的
                    if (_logPathLabel != null && !_logPathLabel.IsDisposed && Log.FileWriteFailed)
                    {
                        _logPathLabel.Text = "目录：" + Log.Dir + "  —— 写入失败（磁盘只读或文件被占用），当前只有内存里的日志";
                        _logPathLabel.ForeColor = Ui.Red;
                    }
                });
            }
            catch
            {
                lock (_pendingLog) { _logFlushQueued = false; }
            }
        }

        private void SyncLogKeepCombo()
        {
            if (_keepCombo == null) return;
            int idx = 1;
            for (int i = 0; i < LogKeepDays.Length; i++) if (LogKeepDays[i] == _settings.LogDays) idx = i;
            _keepCombo.SelectedIndex = idx;
        }

        private void AppendLog(string line)
        {
            if (_logList == null || _logList.IsDisposed) return;
            string time = "";
            string level = "";
            string text = line;
            int p1 = line.IndexOf("  [", StringComparison.Ordinal);
            if (p1 > 0)
            {
                time = line.Substring(0, p1);
                int p2 = line.IndexOf("]  ", p1, StringComparison.Ordinal);
                if (p2 > p1)
                {
                    level = line.Substring(p1 + 3, p2 - p1 - 3);
                    text = line.Substring(p2 + 3);
                }
            }
            ListViewItem item = new ListViewItem(new string[] { time, level, text });
            if (level == "警告") item.ForeColor = Ui.Amber;
            else if (level == "错误") item.ForeColor = Ui.Red;
            _logList.Items.Add(item);
            if (_logList.Items.Count > 2000)
            {
                _logList.Items.RemoveAt(0);
            }
            _logList.EnsureVisible(_logList.Items.Count - 1);
        }

        private static bool IsAccessDenied(string error)
        {
            if (string.IsNullOrEmpty(error)) return false;
            return error.IndexOf("拒绝访问", StringComparison.Ordinal) >= 0 ||
                   error.IndexOf("Access is denied", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   error.IndexOf("0x80070005", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // ------------------------------------------------------------------ helpers

        private bool Confirm(string title, string text)
        {
            return MessageBox.Show(this, text, title, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning,
                                   MessageBoxDefaultButton.Button2) == DialogResult.OK;
        }

        private bool ConfirmDanger(string title, string text)
        {
            return MessageBox.Show(this, text, title, MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                                   MessageBoxDefaultButton.Button2) == DialogResult.No ? false : true;
        }

        private void RunAction(Func<UwfResult> action, string successText)
        {
            if (action == null) return;

            // uwfmgr / DISM 命令要跑几秒到几十秒：放后台线程，UI 上给遮罩，跑完再弹结果。
            // 原来同步跑在 UI 线程——点一下按钮界面假死好几秒，结果框“姗姗来迟”，就是“弹窗卡卡的”。
            BusyForm busy = new BusyForm("正在执行", "正在提交命令到系统，请等待…", 0, null);
            System.Threading.SynchronizationContext ui = System.Threading.SynchronizationContext.Current;
            UwfResult[] slot = new UwfResult[1];

            System.Threading.Tasks.Task.Factory.StartNew(delegate
            {
                UwfResult r;
                try
                {
                    r = action();
                }
                catch (Exception ex)
                {
                    r = new UwfResult();
                    r.ExitCode = -1;
                    r.StdErr = ex.Message;
                    r.Command = "(异常)";
                }
                slot[0] = r;
            }).ContinueWith(delegate
            {
                Action finish = delegate
                {
                    // 先刷状态再关遮罩：Uwf.Read() 也要 1–3 秒，藏在遮罩下比放出来再冻一次体验好
                    try { ReloadState(false, false); }
                    catch { }
                    busy.CloseFromOwner();

                    if (IsDisposed || Disposing) return;
                    UwfResult r = slot[0];
                    if (r != null && r.Ok)
                    {
                        Log.Info("成功：" + r.Command + (string.IsNullOrEmpty(r.All) ? "" : " → " + Ui.Short(r.All, 180)));
                        if (!string.IsNullOrEmpty(successText))
                            MessageBox.Show(this, successText, "操作已提交", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    else
                    {
                        string all = r == null ? "" : r.All;
                        string cmd = r == null ? "(未知)" : r.Command;
                        Log.Error("失败：" + cmd + " → " + Ui.Short(all, 300));
                        MessageBox.Show(this, "操作没有成功：\r\n\r\n" + cmd + "\r\n\r\n" + (string.IsNullOrEmpty(all) ? "（没有任何输出）" : all),
                                        "操作失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                };

                if (ui != null) ui.Post(delegate { try { finish(); } catch { } }, null);
                else try { finish(); } catch { }
            });

            busy.ShowDialog(this);
            try { busy.Dispose(); } catch { }
        }

        private static string SystemVolumeLetter()
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

        private void ProtectSystemVolume()
        {
            if (!_state.Installed) return;
            if (!Confirm("保护 C 盘",
                "将把 C 盘加入 UWF 保护列表，重启后生效。\r\n\r\n重启后，对 C 盘的改动（桌面、文档、安装的软件）都会被还原。\r\n\r\n确定继续？")) return;

            RunMasked(delegate { RunActionSilent(Uwf.ProtectVolume("C:")); });
            ReloadState(false);
            if (MessageBox.Show(this, "已提交。需要重启一次才会真正开始保护。\r\n\r\n现在重启吗？", "保护 C 盘",
                                MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                UwfResult r = Uwf.RestartNow();
                if (!r.Ok)
                    MessageBox.Show(this, "重启命令没有成功，请手动重启。\r\n\r\n" + r.All, "提示",
                                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        private void DoEnableProtection()
        {
            if (!_state.Installed)
            {
                MessageBox.Show(this, "本机没有检测到 UWF 组件，无法启用保护。", "无法启用");
                return;
            }

            using (ProtectDialog dlg = new ProtectDialog(_state, _settings))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                Cursor = Cursors.WaitCursor;
                StringBuilder fails = new StringBuilder();
                int activeType = dlg.ActiveType;                       // 0=RAM 1=DISK
                int aSize = activeType == 0 ? dlg.RamSizeMb : dlg.DiskSizeMb;
                int aWarn = activeType == 0 ? dlg.RamWarnMb : dlg.DiskWarnMb;
                int aCrit = activeType == 0 ? dlg.RamCritMb : dlg.DiskCritMb;
                try
                {
                    if (dlg.WantApplyOverlay)
                    {
                        // 切换生效类型必须关闭保护：UWF 要求当前会话关闭才能把覆盖层改成磁盘
                        bool typeChanged = _state.OverlayType < 0 || activeType != _state.OverlayType;
                        bool sizeChanged = !(_state.OverlayMaxMb > 0) || aSize != _state.OverlayMaxMb;
                        bool warnChanged = !(_state.OverlayWarnMb > 0) || aWarn != _state.OverlayWarnMb;
                        bool critChanged = !(_state.OverlayCritMb > 0) || aCrit != _state.OverlayCritMb;
                        if (typeChanged && _state.FilterCurrent)
                        {
                            MessageBox.Show(this,
                                "切换覆盖层生效类型（内存 ↔ 磁盘）前，必须先『关闭保护』并重启一次。\r\n\r\n" +
                                "请先到『本机状态 → 关闭保护』，重启后再设置缓存，最后重新启用保护。",
                                "需要先关闭保护", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            return;
                        }
                        if (typeChanged) CheckResult(Uwf.SetOverlayType(activeType == 1), fails);
                        if (sizeChanged) CheckResult(Uwf.SetOverlaySize(aSize), fails);
                        if (warnChanged) CheckResult(Uwf.SetOverlayWarning(aWarn), fails);
                        if (critChanged) CheckResult(Uwf.SetOverlayCritical(aCrit), fails);
                        if (!typeChanged && !sizeChanged && !warnChanged && !critChanged)
                            Log.Info("缓存设置与当前一致，未重复提交。");
                        // 两套参数分别记住：以后切到另一种时用各自配置的值，
                        // 不会出现“只配过磁盘、内存还是默认小值被写满”的情况
                        if (_settings.RamSizeMb != dlg.RamSizeMb || _settings.RamWarnMb != dlg.RamWarnMb || _settings.RamCritMb != dlg.RamCritMb ||
                            _settings.DiskSizeMb != dlg.DiskSizeMb || _settings.DiskWarnMb != dlg.DiskWarnMb || _settings.DiskCritMb != dlg.DiskCritMb)
                        {
                            _settings.RamSizeMb = dlg.RamSizeMb; _settings.RamWarnMb = dlg.RamWarnMb; _settings.RamCritMb = dlg.RamCritMb;
                            _settings.DiskSizeMb = dlg.DiskSizeMb; _settings.DiskWarnMb = dlg.DiskWarnMb; _settings.DiskCritMb = dlg.DiskCritMb;
                            SettingsStore.Save(_settings);
                        }
                    }
                    if (dlg.WantEnableProtection)
                        CheckResult(Uwf.EnableProtection(), fails);
                    // 只开过滤器不加保护列表 = 什么都不会还原：同一轮把系统盘排进保护，一次重启全部生效
                    if (dlg.WantEnableProtection || dlg.WantProtectVolume)
                    {
                        if (!_state.HasProtectedVolume && !_state.HasProtectedVolumeNext)
                        {
                            string sys = SystemVolumeLetter();
                            CheckResult(Uwf.ProtectVolume(sys), fails);
                            Log.Info("系统盘 " + sys + " 不在保护列表，已一并排队保护（重启后生效）。");
                        }
                    }
                }
                finally
                {
                    Cursor = Cursors.Default;
                }

                if (fails.Length > 0)
                {
                    Log.Error("启用保护 / 缓存设置失败：" + fails);
                    MessageBox.Show(this, "有设置没有成功：\r\n\r\n" + fails +
                        "\r\n提示：从内存缓存切换到磁盘缓存时，系统要求当前会话 UWF 已关闭——可以先“关闭保护”并重启，再回来设置。" +
                        "\r\n\r\n未成功的设置不会生效。", "部分失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    ReloadState(false);
                    return;
                }

                Log.Info("已应用缓存设置：生效类型 " + (activeType == 1 ? "磁盘 DISK" : "内存 RAM") +
                         "（上限 " + aSize + " MB · 警告 " + aWarn + " · 严重 " + aCrit + "）；" +
                         "内存配置 " + dlg.RamSizeMb + "/" + dlg.RamWarnMb + "/" + dlg.RamCritMb +
                         " · 磁盘配置 " + dlg.DiskSizeMb + "/" + dlg.DiskWarnMb + "/" + dlg.DiskCritMb + " MB" +
                         (dlg.WantEnableProtection ? "，并排队启用保护" : "") +
                         ((dlg.WantEnableProtection || dlg.WantProtectVolume) ? "，系统盘已排入保护列表" : "") + "。");
                ReloadState(false);
                string done = (dlg.WantEnableProtection || dlg.WantProtectVolume)
                    ? "设置已提交。\r\n\r\n重启一次后：过滤器开启，系统盘开始受保护，状态会变成「已开启」。\r\n\r\n现在重启吗？"
                    : "设置已提交，需要重启一次才生效。\r\n\r\n现在重启吗？";
                if (MessageBox.Show(this, done, "完成",
                                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    UwfResult r = Uwf.RestartNow();
                    if (!r.Ok)
                        MessageBox.Show(this, "重启命令没有成功，请手动重启。\r\n\r\n" + r.All, "提示",
                                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private void CheckResult(UwfResult r, StringBuilder fails)
        {
            if (r.Ok) return;
            fails.AppendLine("• " + r.Command + " → " + (string.IsNullOrEmpty(r.All) ? "失败" : r.All) +
                             "（退出码 " + r.ExitCode + " / 0x" + r.ExitCode.ToString("X8") + "）");
        }
        private void ExportDiagnostics()
        {
            SaveFileDialog dlg = new SaveFileDialog();
            dlg.FileName = "uwf-diagnose-" + Environment.MachineName + "-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".txt";
            dlg.Filter = "文本文件|*.txt";
            if (dlg.ShowDialog(this) != DialogResult.OK) return;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("UWFtool 诊断信息");
            sb.AppendLine("时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("计算机：" + Environment.MachineName);
            sb.AppendLine("用户：" + Environment.UserDomainName + "\\" + Environment.UserName);
            sb.AppendLine("系统：" + Environment.OSVersion.VersionString + " build " + Environment.OSVersion.Version.Build);
            sb.AppendLine("管理员：" + Uwf.IsAdmin());
            sb.AppendLine("uwfmgr：" + Uwf.ExePath + " 存在=" + File.Exists(Uwf.ExePath));
            sb.AppendLine();
            sb.AppendLine("== 状态 ==");
            sb.AppendLine("已安装：" + _state.Installed + "  WMI：" + _state.WmiPresent);
            sb.AppendLine("保护：" + _state.FilterCurrent + "/" + _state.FilterNext + "  维护：" + _state.ServicingCurrent + "/" + _state.ServicingNext);
            sb.AppendLine("待重启：" + _state.PendingReboot);
            if (_state.StateFix != null && _state.StateFix.Length > 0) sb.AppendLine("状态修正：" + _state.StateFix);
            sb.AppendLine("缓存：已用 " + _state.OverlayUsedMb + " MB / 上限 " + _state.OverlayMaxMb + " MB / 剩余 " + _state.OverlayFreeMb + " MB / 类型 " + _state.OverlayType);
            sb.AppendLine("错误：" + _state.Error);
            foreach (VolumeState v in _state.Volumes)
                sb.AppendLine("卷 " + v.Letter + "  标签=" + v.Label + "  当前=" + v.ProtectedCurrent + "  下次=" + v.ProtectedNext);
            sb.AppendLine();
            sb.AppendLine("== 文件排除 ==");
            foreach (string s in _state.FileExclusions) sb.AppendLine(s);
            sb.AppendLine();
            sb.AppendLine("== 注册表排除 ==");
            foreach (string s in _state.RegExclusions) sb.AppendLine(s);
            sb.AppendLine();
            sb.AppendLine("== get-config ==");
            sb.AppendLine(_state.RawConfig);
            sb.AppendLine();
            sb.AppendLine("== overlay ==");
            sb.AppendLine(_state.RawOverlay);
            sb.AppendLine();
            sb.AppendLine("== 日志 ==");
            foreach (string s in Log.All) sb.AppendLine(s);

            File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
            Log.Info("诊断信息已导出：" + dlg.FileName);
            MessageBox.Show(this, "已导出到：\r\n" + dlg.FileName, "导出完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
