using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace UwfTeacher
{
    /// <summary>
    /// 远程安装向导：关闭 UWF 保护（filter disable，不是微软的维修模式）→ 重启 → 等待上线
    /// → 分发安装 → 恢复还原 → 验证。
    /// </summary>
    public class RemoteInstallForm : Form
    {
        private readonly List<Machine> _candidates;
        private readonly AppSettings _settings;
        private readonly Func<Machine, RemoteTarget> _targetFactory;

        private CheckedListBox _list;
        private FlowLayoutPanel _steps;
        private TextBox _log;
        private Label _progressText;
        private int _finishedTargets;
        private TextBox _installer;
        private CheckBox _silent;         // 静默安装：参数按安装包类型自动选，不让老师填
        private TextBox _extraArgs;       // 附加参数（可选）：自动参数不对时的备用出口
        private CheckBox _wait;
        private CheckBox _interactive;    // 以登录用户身份运行（界面出现在学生机屏幕上）
        private NumericUpDown _parallel;  // 同时处理台数
        private Button _installButton;

        // 第 4 步：安装包 / 便携版双模式
        private RadioButton _modeInstall, _modePortable, _srcLocal, _srcRemote;
        private Panel _installRow, _portableRow;
        private TextBox _deployDir;      // 安装包放到学生机的目录
        private TextBox _portDir;
        private TextBox _portTarget;     // 便携版装到学生机的目录
        private TextBox _portExe;
        private TextBox _portLink;
        private CheckBox _portDesktop;

        private StepUi _s1, _s2, _s3, _s4, _s5, _s6;
        private FleetRunner _runner;
        private BusyForm _busy;
        private string _lastDone = "";   // 最近完成的步骤名，用来判断流程是不是已经走完

        private class StepUi
        {
            public Panel Panel;
            public Label Status;
            public Label Desc;
            public Button Action;
        }

        public RemoteInstallForm(List<Machine> machines, AppSettings settings, Func<Machine, RemoteTarget> targetFactory)
        {
            _candidates = new List<Machine>(machines);
            _settings = settings;
            _targetFactory = targetFactory;

            Text = "远程安装向导 · 让软件在重启后仍然存在";
            Font = Ui.F9;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(1000, 848);          // 高一点：底部日志不用滚动才能看见
            MinimumSize = new Size(880, 760);
            MinimizeBox = false;
            BackColor = Ui.Bg;

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 5;
            root.BackColor = Ui.Bg;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 458));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            Controls.Add(root);

            Label intro = Ui.Wrap("按 1 → 6 的顺序操作：灰色=未开始，蓝色=进行中，绿色=完成。本向导用的是「关闭 UWF 保护（暂停还原）」，不是维修模式，随时可恢复。" +
                                  "目标列表来自机房总览里勾选的机器：要装 50 台就先在总览勾选/全选（或用「更多操作 → 全选」）再打开本向导。", Ui.F9, Ui.TextColor);
            intro.Margin = new Padding(16, 12, 16, 4);
            intro.Dock = DockStyle.Fill;
            root.Controls.Add(intro, 0, 0);

            // 目标机器
            Panel machinePanel = new Panel();
            machinePanel.Dock = DockStyle.Fill;
            machinePanel.Margin = new Padding(16, 0, 16, 6);
            machinePanel.BackColor = Ui.CardBg;
            machinePanel.Paint += delegate(object s, PaintEventArgs e)
            {
                using (Pen p = new Pen(Ui.Border)) e.Graphics.DrawRectangle(p, 0, 0, machinePanel.Width - 1, machinePanel.Height - 1);
            };

            Label ml = Ui.L("目标计算机（勾选要安装的机器）", Ui.F9B, Ui.TextColor);
            ml.Location = new Point(20, 10);
            machinePanel.Controls.Add(ml);

            _list = new CheckedListBox();
            _list.Font = Ui.F9;
            _list.CheckOnClick = true;
            _list.BorderStyle = BorderStyle.FixedSingle;
            _list.Location = new Point(20, 34);
            _list.Size = new Size(machinePanel.Width - 140, 70);
            _list.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            foreach (Machine m in _candidates)
            {
                string text = m.Name + (m.Group.Length > 0 ? "  ·  " + m.Group : "");
                int idx = _list.Items.Add(text);
                _list.SetItemChecked(idx, true);
            }
            machinePanel.Controls.Add(_list);

            FlowLayoutPanel mb = new FlowLayoutPanel();
            mb.FlowDirection = FlowDirection.TopDown;
            mb.WrapContents = false;
            mb.Location = new Point(machinePanel.Width - 110, 34);
            mb.Size = new Size(94, 72);
            mb.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            mb.BackColor = Ui.CardBg;
            Button all = Ui.B("全选");
            all.Width = 92;
            all.Height = 28;
            all.Click += delegate { for (int i = 0; i < _list.Items.Count; i++) _list.SetItemChecked(i, true); };
            Button none = Ui.B("全不选");
            none.Width = 92;
            none.Height = 28;
            none.Click += delegate { for (int i = 0; i < _list.Items.Count; i++) _list.SetItemChecked(i, false); };
            mb.Controls.Add(all);
            mb.Controls.Add(none);
            machinePanel.Controls.Add(mb);
            root.Controls.Add(machinePanel, 0, 1);

            // 步骤
            _steps = new FlowLayoutPanel();
            _steps.Dock = DockStyle.Fill;
            _steps.FlowDirection = FlowDirection.TopDown;
            _steps.WrapContents = false;
            _steps.AutoScroll = true;
            _steps.Margin = new Padding(16, 0, 16, 6);
            _steps.BackColor = Ui.Bg;
            _steps.Resize += delegate { FitStepWidths(); };
            root.Controls.Add(_steps, 0, 2);

            _s1 = MakeStep(1, "关闭 UWF 保护（暂停还原）", "给所选机器排队“下次启动不再保护”（uwfmgr filter disable）。必须重启才生效：没重启就装，会写满缓存导致黑屏。", "关闭保护");
            _s1.Action.Click += delegate
            {
                List<Machine> t = Checked();
                if (t.Count == 0) { MessageBox.Show(this, "请先勾选计算机。"); return; }
                if (!Confirm("关闭 UWF 保护（暂停还原）",
                    "将给 " + t.Count + " 台机器排队“关闭 UWF 保护”，重启后系统暂时不再还原，这时才能把软件真正装进去。\r\n\r\n" +
                    "装完必须执行第 5 步“恢复还原”，否则这些机器会一直不还原。\r\n\r\n继续？")) return;
                RunStep("暂停还原", delegate(Machine m) { return Remote.Action(TargetFor(m), "disable", m.State); }, _s1);
            };

            _s2 = MakeStep(2, "重启所选机器", "让“关闭保护”生效，重启后离线 1–2 分钟。重启被阻止时，第 3 步会提示仍在保护。", "重启所选");
            _s2.Action.Click += delegate
            {
                List<Machine> t = Checked();
                if (t.Count == 0) { MessageBox.Show(this, "请先勾选计算机。"); return; }
                if (!Confirm("重启 " + t.Count + " 台计算机", "这些计算机会立即重启，请确认没有学生正在使用。\r\n\r\n继续？")) return;
                RunStep("重启", delegate(Machine m) { return Remote.Action(TargetFor(m), "restart", m.State); }, _s2);
            };

            _s3 = MakeStep(3, "等待机器回到在线", "重启后等机器开机联网。点“重新检测”，直到全部在线且显示“保护已关闭”。", "重新检测");
            _s3.Action.Click += delegate { CheckOnline(); };

            _s4 = MakeStep(4, "分发并部署软件", "", "分发并安装");
            _s4.Action.Visible = false;
            _s4.Status.Visible = false;
            BuildInstallControls(_s4.Panel);

            _s5 = MakeStep(5, "恢复还原（重新开启 UWF 保护）", "重新开启 UWF 保护。重启后恢复还原，刚装的软件保留。", "恢复还原");
            _s5.Action.Click += delegate
            {
                List<Machine> t = Checked();
                if (t.Count == 0) { MessageBox.Show(this, "请先勾选计算机。"); return; }
                if (!Confirm("恢复还原", "将给 " + t.Count + " 台机器重新开启 UWF 保护，重启后生效。\r\n\r\n" +
                    "请先确认学生机上的安装程序已经结束（第 4 步显示完成、界面上不再有正在安装的窗口）。\r\n" +
                    "安装还没结束就恢复还原并重启，会留下装了一半的系统，重启后可能黑屏。\r\n\r\n继续？")) return;
                RunStep("恢复还原", delegate(Machine m) { return Remote.Action(TargetFor(m), "enable", m.State); }, _s5);
            };

            _s6 = MakeStep(6, "重启生效（完成）", "让恢复保护与安装结果生效：重启后系统回到全盘保护状态。", "重启所选");
            _s6.Action.Click += delegate
            {
                List<Machine> t = Checked();
                if (t.Count == 0) { MessageBox.Show(this, "请先勾选计算机。"); return; }
                if (!Confirm("重启生效", "将重启 " + t.Count + " 台计算机，让恢复保护生效。\r\n\r\n确认没有学生正在使用？")) return;
                RunStep("重启生效", delegate(Machine m) { return Remote.Action(TargetFor(m), "restart", m.State); }, _s6);
            };

            _steps.Controls.Add(_s1.Panel);
            _steps.Controls.Add(_s2.Panel);
            _steps.Controls.Add(_s3.Panel);
            _steps.Controls.Add(_s4.Panel);
            _steps.Controls.Add(_s5.Panel);
            _steps.Controls.Add(_s6.Panel);
            FitStepWidths();   // 添加后立即撑满宽度，不依赖 Resize 时机

            // 日志
            Panel logPanel = new Panel();
            logPanel.Dock = DockStyle.Fill;
            logPanel.Margin = new Padding(16, 0, 16, 6);
            logPanel.BackColor = Ui.CardBg;
            logPanel.Paint += delegate(object s, PaintEventArgs e)
            {
                using (Pen p = new Pen(Ui.Border)) e.Graphics.DrawRectangle(p, 0, 0, logPanel.Width - 1, logPanel.Height - 1);
            };
            Panel progressRow = new Panel();
            progressRow.Dock = DockStyle.Top;
            progressRow.Height = 30;
            progressRow.Padding = new Padding(8, 4, 8, 2);
            progressRow.BackColor = Ui.CardBg;
            _progressText = Ui.L("就绪", Ui.F85, Ui.Muted);
            _progressText.Location = new Point(0, 5);
            _progressText.Size = new Size(560, 20);
            _progressText.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            progressRow.Controls.Add(_progressText);
            logPanel.Controls.Add(progressRow);

            _log = new TextBox();
            _log.Multiline = true;
            _log.ReadOnly = true;
            _log.ScrollBars = ScrollBars.Vertical;
            _log.BorderStyle = BorderStyle.None;
            _log.BackColor = Ui.CardBg;
            _log.Font = Ui.Mono;
            _log.Dock = DockStyle.Fill;
            logPanel.Padding = new Padding(10, 8, 10, 8);
            logPanel.Controls.Add(_log);
            root.Controls.Add(logPanel, 0, 3);

            // 底部
            Panel footer = new Panel();
            footer.Dock = DockStyle.Fill;
            footer.BackColor = Ui.Bg;
            Button close = Ui.B("关闭");
            close.Width = 96;
            close.Height = 34;
            close.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            close.Location = new Point(footer.Width - 112, 8);
            close.Click += delegate { Close(); };
            footer.Controls.Add(close);
            footer.Resize += delegate { close.Location = new Point(footer.Width - 112, 8); };
            Label tip = Ui.L("关闭向导不会撤销已经排队到目标机的操作。", Ui.F85, Ui.Muted);
            tip.Location = new Point(16, 16);
            footer.Controls.Add(tip);
            root.Controls.Add(footer, 0, 4);

            LogLine("向导已就绪，共 " + _candidates.Count + " 台计算机。");
            SetStatus(_s1, "未开始", Ui.Muted);
            SetStatus(_s2, "未开始", Ui.Muted);
            SetStatus(_s3, "未开始", Ui.Muted);
            SetStatus(_s4, "未开始", Ui.Muted);
            SetStatus(_s5, "未开始", Ui.Muted);
            SetStatus(_s6, "未开始", Ui.Muted);

            // 打开向导就自动查一遍机器现状：已经关过保护的不再让你从第 1 步点起
            Shown += delegate(object s, EventArgs e)
            {
                try { BeginInvoke((MethodInvoker)delegate { AutoCheckState(); }); }
                catch (InvalidOperationException) { }
            };

            FormClosing += delegate(object s, FormClosingEventArgs e)
            {
                if (_runner != null && _runner.Running)
                {
                    if (MessageBox.Show(this, "还有任务在执行，确定关闭吗？", "关闭向导", MessageBoxButtons.YesNo,
                                        MessageBoxIcon.Warning) == DialogResult.No)
                    {
                        e.Cancel = true;
                        return;
                    }
                    _runner.Cancel();
                }
            };
        }

        private RemoteTarget TargetFor(Machine m)
        {
            return _targetFactory(m);
        }

        private void BuildInstallControls(Panel host)
        {
            // 第 4 步说明行放模式相关提示，给足宽度（标题右侧是模式/来源单选）
            _s4.Desc.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            _s4.Desc.Size = new Size(880, 62);       // 三行：说明文字较长，24px 会被裁掉
            _s4.Panel.Resize += delegate { _s4.Desc.Width = Math.Max(300, _s4.Panel.Width - 60); };

            // 标题行右侧：模式 + 来源
            _modeInstall = new RadioButton();
            _modeInstall.Text = "安装包";
            _modeInstall.Font = Ui.F9;
            _modeInstall.AutoSize = true;
            _modeInstall.Location = new Point(300, 5);
            _modeInstall.Checked = true;
            host.Controls.Add(_modeInstall);

            _modePortable = new RadioButton();
            _modePortable.Text = "便携版（目录）";
            _modePortable.Font = Ui.F9;
            _modePortable.AutoSize = true;
            _modePortable.Location = new Point(386, 5);
            host.Controls.Add(_modePortable);

            Label srcL = Ui.L("来源", Ui.F9, Ui.Muted);
            srcL.Location = new Point(530, 6);
            host.Controls.Add(srcL);

            _srcLocal = new RadioButton();
            _srcLocal.Text = "从教师机分发";
            _srcLocal.Font = Ui.F9;
            _srcLocal.AutoSize = true;
            _srcLocal.Location = new Point(572, 5);
            _srcLocal.Checked = true;
            host.Controls.Add(_srcLocal);

            _srcRemote = new RadioButton();
            _srcRemote.Text = "文件已在学生机（电子教室等已传好）";
            _srcRemote.Font = Ui.F9;
            _srcRemote.AutoSize = true;
            _srcRemote.Location = new Point(688, 5);
            host.Controls.Add(_srcRemote);

            // 安装包区（两行）
            _installRow = new Panel();
            _installRow.Location = new Point(0, 88);
            _installRow.Size = new Size(940, 62);
            _installRow.BackColor = Ui.CardBg;
            Label l = Ui.L("安装包", Ui.F9, Ui.TextColor);
            l.Location = new Point(0, 4);
            _installRow.Controls.Add(l);

            _installer = new TextBox();
            Ui.StyleInput(_installer);
            _installer.Location = new Point(46, 0);
            _installer.Width = 256;
            _installRow.Controls.Add(_installer);

            Button browse = Ui.B("浏览…");
            browse.Location = new Point(308, -1);
            browse.Width = 64;
            browse.Height = 28;
            browse.Click += delegate
            {
                OpenFileDialog dlg = new OpenFileDialog();
                dlg.Title = "选择要安装到学生机的程序";
                dlg.Filter = "安装程序|*.exe;*.msi;*.bat;*.cmd|所有文件|*.*";
                if (dlg.ShowDialog(this) == DialogResult.OK) _installer.Text = dlg.FileName;
            };
            _installRow.Controls.Add(browse);

            // 参数不让老师填：勾「静默安装」按安装包类型自动选（exe→/S，MSI→/quiet /norestart，bat 不加）
            _silent = new CheckBox();
            _silent.Text = "静默安装";
            _silent.Font = Ui.F85;
            _silent.AutoSize = true;
            _silent.Checked = true;
            _silent.Location = new Point(384, 2);
            _installRow.Controls.Add(_silent);

            _wait = new CheckBox();
            _wait.Text = "等待安装完成";
            _wait.Font = Ui.F85;
            _wait.AutoSize = true;
            _wait.Checked = true;
            _wait.Location = new Point(472, 2);
            _installRow.Controls.Add(_wait);

            Label ddl = Ui.L("放到学生机", Ui.F85, Ui.Muted);
            ddl.Location = new Point(0, 38);
            _installRow.Controls.Add(ddl);
            _deployDir = new TextBox();
            Ui.StyleInput(_deployDir);
            _deployDir.Location = new Point(72, 34);
            _deployDir.Width = 256;
            _deployDir.Text = @"C:\Windows\Temp\UWFTeacher\deploy";
            _installRow.Controls.Add(_deployDir);

            Label pl = Ui.L("同时台数", Ui.F85, Ui.Muted);
            pl.Location = new Point(350, 38);
            _installRow.Controls.Add(pl);

            _parallel = new NumericUpDown();
            _parallel.Minimum = 1;
            _parallel.Maximum = 8;
            _parallel.Value = 4;
            _parallel.Width = 52;
            _parallel.Location = new Point(410, 34);
            _parallel.Font = Ui.F9;
            _installRow.Controls.Add(_parallel);

            // 文案短一点：详细说明放在上方的 _s4.Desc 里
            _interactive = new CheckBox();
            _interactive.Text = "以登录用户运行";
            _interactive.Font = Ui.F85;
            _interactive.AutoSize = true;
            _interactive.Location = new Point(484, 38);
            _installRow.Controls.Add(_interactive);

            Label eal = Ui.L("附加参数（可选）", Ui.F85, Ui.Muted);
            eal.Location = new Point(600, 38);
            _installRow.Controls.Add(eal);

            _extraArgs = new TextBox();
            Ui.StyleInput(_extraArgs);
            _extraArgs.Location = new Point(706, 34);
            _extraArgs.Width = 160;
            _extraArgs.Text = "";
            _installRow.Controls.Add(_extraArgs);
            host.Controls.Add(_installRow);

            // 便携版区（两行）
            _portableRow = new Panel();
            _portableRow.Location = new Point(0, 88);
            _portableRow.Size = new Size(940, 62);
            _portableRow.BackColor = Ui.CardBg;
            _portableRow.Visible = false;

            Label dl = Ui.L("目录", Ui.F9, Ui.TextColor);
            dl.Location = new Point(0, 4);
            _portableRow.Controls.Add(dl);

            _portDir = new TextBox();
            Ui.StyleInput(_portDir);
            _portDir.Location = new Point(46, 0);
            _portDir.Width = 256;
            _portableRow.Controls.Add(_portDir);

            Button browseDir = Ui.B("浏览…");
            browseDir.Location = new Point(308, -1);
            browseDir.Width = 64;
            browseDir.Height = 28;
            browseDir.Click += delegate
            {
                FolderBrowserDialog dlg = new FolderBrowserDialog();
                dlg.Description = "选择便携版软件的文件夹（会整个复制到学生机）";
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    _portDir.Text = dlg.SelectedPath;
                    try
                    {
                        string[] exes = Directory.GetFiles(dlg.SelectedPath, "*.exe");
                        if (exes.Length >= 1 && _portExe.Text.Trim().Length == 0) _portExe.Text = Path.GetFileName(exes[0]);
                    }
                    catch { }
                }
            };
            _portableRow.Controls.Add(browseDir);

            Label tl = Ui.L("装到学生机", Ui.F85, Ui.Muted);
            tl.Location = new Point(388, 4);
            _portableRow.Controls.Add(tl);
            _portTarget = new TextBox();
            Ui.StyleInput(_portTarget);
            _portTarget.Location = new Point(460, 0);
            _portTarget.Width = 200;
            _portTarget.Text = @"C:\Program Files";
            _portableRow.Controls.Add(_portTarget);

            Label el = Ui.L("快捷启动程序", Ui.F85, Ui.Muted);
            el.Location = new Point(0, 38);
            _portableRow.Controls.Add(el);

            _portExe = new TextBox();
            Ui.StyleInput(_portExe);
            _portExe.Location = new Point(86, 34);
            _portExe.Width = 170;
            _portableRow.Controls.Add(_portExe);

            Button browseExe = Ui.B("选程序…");
            browseExe.Location = new Point(262, 33);
            browseExe.Width = 72;
            browseExe.Height = 28;
            browseExe.Click += delegate
            {
                string baseDir = (_portDir.Text ?? "").Trim();
                if (_srcLocal.Checked && Directory.Exists(baseDir))
                {
                    OpenFileDialog dlg = new OpenFileDialog();
                    dlg.Title = "选择要创建快捷启动的程序（在刚才选的目录里）";
                    dlg.InitialDirectory = baseDir;
                    dlg.Filter = "程序|*.exe;*.bat;*.cmd|所有文件|*.*";
                    if (dlg.ShowDialog(this) == DialogResult.OK)
                    {
                        string full = dlg.FileName;
                        _portExe.Text = full.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase)
                            ? full.Substring(baseDir.Length).TrimStart('\\')
                            : Path.GetFileName(full);
                        if (_portLink.Text.Trim().Length == 0)
                        {
                            _portLink.Text = Path.GetFileNameWithoutExtension(full);
                        }
                    }
                }
                else if (_srcRemote.Checked && baseDir.Length > 0)
                {
                    OpenFileDialog dlg = new OpenFileDialog();
                    dlg.Title = "随便挑一个同名程序（只用于自动填相对文件名，路径按学生机目录算）";
                    dlg.Filter = "程序|*.exe;*.bat;*.cmd|所有文件|*.*";
                    if (dlg.ShowDialog(this) == DialogResult.OK)
                    {
                        _portExe.Text = Path.GetFileName(dlg.FileName);
                        if (_portLink.Text.Trim().Length == 0) _portLink.Text = Path.GetFileNameWithoutExtension(dlg.FileName);
                    }
                }
                else
                {
                    MessageBox.Show(this, "请先选择/填写软件目录。");
                }
            };
            _portableRow.Controls.Add(browseExe);

            Label nl = Ui.L("快捷方式名", Ui.F85, Ui.Muted);
            nl.Location = new Point(352, 38);
            _portableRow.Controls.Add(nl);

            _portLink = new TextBox();
            Ui.StyleInput(_portLink);
            _portLink.Location = new Point(424, 34);
            _portLink.Width = 120;
            _portableRow.Controls.Add(_portLink);

            _portDesktop = new CheckBox();
            _portDesktop.Text = "公共桌面快捷方式";
            _portDesktop.Font = Ui.F85;
            _portDesktop.AutoSize = true;
            _portDesktop.Checked = true;
            _portDesktop.Location = new Point(560, 36);
            _portableRow.Controls.Add(_portDesktop);
            host.Controls.Add(_portableRow);

            _installButton = Ui.B("分发并安装", true);
            _installButton.Size = new Size(150, 30);
            _installButton.Location = new Point(720, 0);
            _installButton.Click += delegate { DoInstall(); };
            _installRow.Controls.Add(_installButton);

            EventHandler sync = delegate
            {
                bool portable = _modePortable.Checked;
                bool remote = _srcRemote.Checked;
                _installRow.Visible = !portable;
                _portableRow.Visible = portable;
                browse.Enabled = !remote;
                browseDir.Enabled = !remote;
                _deployDir.Enabled = !remote;
                _portTarget.Enabled = !remote;
                _installButton.Text = portable ? "分发并创建快捷方式" : "分发并安装";
                if (portable)
                {
                    _portableRow.Controls.Add(_installButton);
                    _installButton.Location = new Point(720, 16);
                    _s4.Desc.Text = remote
                        ? "目录已传到学生机：填学生机上的目录路径，选好快捷启动的程序，本工具在公共桌面创建快捷方式。"
                        : "目录自动上传到学生机的指定位置（默认 C:\\Program Files\\），选好快捷启动的程序，公共桌面快捷方式全自动创建。";
                    _s4.Panel.Height = 154;
                }
                else
                {
                    _installRow.Controls.Add(_installButton);
                    _installButton.Location = new Point(720, 0);
                    _s4.Desc.Text = remote
                        ? "文件已由电子教室传到学生机：填学生机上的完整路径，远程拉起安装（先切到该目录再运行，输出与退出码会回传到日志）。"
                        : "安装包自动上传并远程运行。勾「静默安装」会先识别安装包类型再给参数（Inno→/VERYSILENT、NSIS→/S、MSI→/quiet），" +
                          "识别不准可在「附加参数」里改；输入法等按用户安装的软件请勾「以登录用户运行」。";
                    _s4.Panel.Height = 154;
                }
                // 安全提示：装之前必须确认保护已关闭，否则会写满 UWF 缓存导致重启黑屏
                _s4.Desc.Text += "  装前自动确认保护已关闭，未生效的机器会跳过。";
            };
            _modeInstall.CheckedChanged += sync;
            _modePortable.CheckedChanged += sync;
            _srcLocal.CheckedChanged += sync;
            _srcRemote.CheckedChanged += sync;
            sync(null, EventArgs.Empty);
        }
        private void FitStepWidths()
        {
            if (_steps == null) return;
            int w = _steps.ClientSize.Width - 2;
            if (w < 400) w = 400;
            foreach (Control c in _steps.Controls) c.Width = w;
        }

        private StepUi MakeStep(int number, string title, string desc, string buttonText)
        {
            Panel p = new Panel();
            p.Height = 56;
            p.Margin = new Padding(0, 0, 0, 4);
            p.BackColor = Ui.SoftGray;
            p.Paint += delegate(object s, PaintEventArgs e)
            {
                using (Pen pen = new Pen(Ui.Border)) e.Graphics.DrawRectangle(pen, 0, 0, p.Width - 1, p.Height - 1);
            };

            Label num = new Label();
            num.Text = number.ToString();
            num.Font = Ui.F9B;
            num.ForeColor = Color.White;
            num.BackColor = Ui.Accent;
            num.TextAlign = ContentAlignment.MiddleCenter;
            num.Size = new Size(26, 26);
            num.Location = new Point(14, 15);
            Ui.ApplyRegion(num, 13);
            p.Controls.Add(num);

            Label t = Ui.L(title, Ui.F9B, Ui.TextColor);
            t.Location = new Point(52, 6);
            p.Controls.Add(t);

            Label d = Ui.Wrap(desc, Ui.F85, Ui.Muted);
            d.Location = new Point(52, 26);
            d.Size = new Size(Math.Max(200, p.Width - 230), 22);
            p.Controls.Add(d);

            Label status = Ui.L("未开始", Ui.F85, Ui.Muted);
            status.AutoSize = false;
            status.TextAlign = ContentAlignment.MiddleCenter;
            status.Size = new Size(150, 18);
            status.Location = new Point(p.Width - 164, 36);
            p.Controls.Add(status);

            Button action = Ui.B(buttonText);
            action.Size = new Size(150, 28);
            action.Location = new Point(p.Width - 164, 8);
            p.Controls.Add(action);

            p.Resize += delegate
            {
                int right = p.ClientSize.Width - 164;
                d.Width = Math.Max(200, right - d.Left - 12);
                status.Location = new Point(right, 36);
                action.Location = new Point(right, 4);
            };

            StepUi ui = new StepUi();
            ui.Panel = p;
            ui.Status = status;
            ui.Desc = d;
            ui.Action = action;
            return ui;
        }

        private List<Machine> Checked()
        {
            List<Machine> list = new List<Machine>();
            for (int i = 0; i < _list.Items.Count && i < _candidates.Count; i++)
            {
                if (_list.GetItemChecked(i)) list.Add(_candidates[i]);
            }
            return list;
        }

        private void LogLine(string text)
        {
            if (_log == null || _log.IsDisposed) return;
            _log.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + text + "\r\n");
            Log.Info("[远程安装] " + text);
        }

        /// <summary>线程安全的日志：复制 / 安装跑在工作线程上，直接碰控件会抛跨线程异常。</summary>
        private void PostLog(string text)
        {
            if (IsDisposed || !IsHandleCreated) return;
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    if (!IsDisposed) LogLine(text);
                });
            }
            catch (InvalidOperationException) { }
        }

        /// <summary>
        /// 刷新“现在到底在干什么”。遮罩盖住了整个向导，只改底部那行（_progressText）老师根本看不到，
        /// 所以遮罩里的说明也要一起更新——否则界面上永远停在“正在处理 1 台计算机”。
        /// </summary>
        private void Beat(string text)
        {
            SetProgressActivity(text);
            if (_busy != null) _busy.SetProgress(-1, -1, text);
        }

        private static string Mb(long bytes)
        {
            if (bytes <= 0) return "0 MB";
            if (bytes >= 1024L * 1024L * 1024L) return (bytes / 1073741824.0).ToString("0.00") + " GB";
            return (bytes / 1048576.0).ToString("0.0") + " MB";
        }

        private static string FmtDur(int seconds)
        {
            if (seconds < 0) seconds = 0;
            if (seconds < 60) return seconds + " 秒";
            int m = seconds / 60, s = seconds % 60;
            if (m < 60) return m + " 分 " + s.ToString("00") + " 秒";
            return (m / 60) + " 小时 " + (m % 60) + " 分";
        }

        private class InstallerInfo
        {
            public string TypeName = "未知";
            public string SilentArgs = "";
            public bool Known;
        }

        /// <summary>
        /// 识别安装包类型，给出真正管用的静默参数。
        /// exe 统一塞 /S 只对 NSIS 系有效：Inno Setup 要 /VERYSILENT，不认识 /S 会弹一个
        /// 系统会话里没人看得见的界面——表现就是“装不上、不报错、还一直不结束”。
        /// </summary>
        private static InstallerInfo DetectInstaller(string file)
        {
            InstallerInfo info = new InstallerInfo();
            string ext = Path.GetExtension(file).ToLowerInvariant();
            if (ext == ".msi")
            {
                info.TypeName = "Windows Installer (MSI)";
                info.SilentArgs = "/quiet /norestart";
                info.Known = true;
                return info;
            }
            if (ext == ".bat" || ext == ".cmd")
            {
                info.TypeName = "批处理脚本";
                info.Known = true;
                return info;
            }

            string markers = ReadInstallerMarkers(file);
            if (markers.IndexOf("Inno Setup", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                info.TypeName = "Inno Setup";
                info.SilentArgs = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART";
                info.Known = true;
            }
            else if (markers.IndexOf("Nullsoft", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                info.TypeName = "NSIS（Nullsoft）";
                info.SilentArgs = "/S";
                info.Known = true;
            }
            else if (markers.IndexOf("InstallShield", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                info.TypeName = "InstallShield";
                info.SilentArgs = "/s /v/qn";
                info.Known = true;
            }
            else if (markers.IndexOf("Advanced Installer", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                info.TypeName = "Advanced Installer";
                info.SilentArgs = "/quiet";
                info.Known = true;
            }
            else if (markers.IndexOf("Wise Installation", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                info.TypeName = "Wise";
                info.SilentArgs = "/s";
                info.Known = true;
            }
            else if (ext == ".exe")
            {
                // 识别不出来先按最常见的 NSIS 习惯给 /S，日志里会说明，装不上再换
                info.SilentArgs = "/S";
            }
            return info;
        }

        /// <summary>取安装包头尾的 ASCII 特征串（类型标记一般在文件开头，个别在结尾）。</summary>
        private static string ReadInstallerMarkers(string file)
        {
            try
            {
                using (FileStream fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    int head = (int)Math.Min(fs.Length, 2 * 1024 * 1024);
                    byte[] buf = new byte[head];
                    int read = fs.Read(buf, 0, head);
                    string s = System.Text.Encoding.ASCII.GetString(buf, 0, read);
                    if (fs.Length > head)
                    {
                        int tail = (int)Math.Min(fs.Length - head, 512 * 1024);
                        fs.Seek(-tail, SeekOrigin.End);
                        byte[] t = new byte[tail];
                        int tr = fs.Read(t, 0, tail);
                        s += System.Text.Encoding.ASCII.GetString(t, 0, tr);
                    }
                    return s;
                }
            }
            catch { return ""; }
        }

        private void SetStatus(StepUi step, string text, Color color)
        {
            if (step == null || step.Status == null || step.Status.IsDisposed) return;
            step.Status.Text = text;
            step.Status.ForeColor = color;
        }

        private bool Confirm(string title, string text)
        {
            return MessageBox.Show(this, text, title, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning,
                                   MessageBoxDefaultButton.Button2) == DialogResult.OK;
        }

        // ---------------------------------------------------------------- steps

        /// <summary>
        /// 开始等待：弹出模态遮罩挡住向导里的所有按钮。
        /// 远程操作重复点击会在学生机/教师端堆并发，严重的会卡死，所以等待期间一律不准再点。
        /// </summary>
        private void ShowBusy(string title, string text, int total)
        {
            CloseBusy();
            _busy = new BusyForm(title, text, total, delegate { if (_runner != null) _runner.Cancel(); });
        }

        private void UpdateBusy(int done, int total, string text)
        {
            if (_busy != null) _busy.SetProgress(done, total, text);
        }

        private void CloseBusy()
        {
            if (_busy == null) return;
            BusyForm b = _busy;
            _busy = null;
            b.CloseFromOwner();
        }

        private void WaitBusy()
        {
            if (_busy == null) return;
            BusyForm b = _busy;
            try { b.ShowDialog(this); }
            finally { _busy = null; b.Dispose(); }
        }

        /// <summary>遮罩被“取消”提前关掉时：后台还在收尾，先把界面放出来并说明情况。返回 true 表示调用方应直接返回。</summary>
        private bool HandleBusyCancelled(string what, StepUi step)
        {
            if (_runner == null || !_runner.Running) return false;
            SetButtons(true);
            if (step != null) SetStatus(step, "已取消（收尾中）", Ui.Amber);
            LogLine("已请求取消「" + what + "」，正在结束已经发出的操作；界面已解锁。");
            return true;
        }

        private void RunStep(string title, Func<Machine, UwfResult> action, StepUi step)
        {
            if (_runner != null && _runner.Running) return;
            List<Machine> targets = Checked();
            if (targets.Count == 0) { MessageBox.Show(this, "请先勾选计算机。"); return; }

            SetStatus(step, "执行中…", Ui.Accent);
            SetButtons(false);
            LogLine("开始" + title + "：" + targets.Count + " 台。");
            ShowBusy(title, "正在对 " + targets.Count + " 台计算机执行「" + title + "」，请等待，不要重复点击。", targets.Count);

            int ok = 0, bad = 0;
            _runner = new FleetRunner(this);
            _runner.Parallelism = _settings.Parallel;
            _runner.Started = delegate(Machine m)
            {
                UpdateBusy(-1, -1, "正在" + title + "：" + m.Name + "…（共 " + targets.Count + " 台）");
            };
            _runner.Work = delegate(Machine m)
            {
                UwfResult r = action(m);
                m.Busy = false;
                if (r.Ok)
                {
                    m.LastAction = title;
                    m.LastError = "";
                    // 本地状态跟着“已排队”的意图走：步骤状态立刻正确，不用每步都再查一遍机器
                    if (m.State != null && m.State.Ok && m.State.UwfPresent)
                    {
                        if (title == "暂停还原") m.State.FilterNext = false;
                        else if (title == "恢复还原") m.State.FilterNext = true;
                    }
                    if (title == "重启" || title == "重启生效") m.State = null;   // 重启后旧状态作废，等重新检测
                }
                else
                {
                    m.LastError = Ui.Short(r.All, 200);
                }
            };
            _runner.Completed = delegate(Machine m)
            {
                if (m.LastError.Length == 0) { ok++; LogLine("  √ " + m.Name + "：" + title + " 已提交"); }
                else { bad++; LogLine("  × " + m.Name + "：" + m.LastError); }
            };
            _runner.Progress = delegate(int done, int total)
            {
                UpdateBusy(done, total, null);
            };
            _runner.Finished = delegate
            {
                SetButtons(true);
                SetStatus(step, "完成 " + ok + " / 失败 " + bad, bad == 0 ? Ui.Green : Ui.Amber);
                LogLine(title + "结束：成功 " + ok + " 台，失败 " + bad + " 台。");
                if (title == "暂停还原") LogLine("下一步：点第 2 步“重启所选机器”，让关闭保护生效。");
                if (title == "重启") LogLine("重启命令已提交。等 1–2 分钟后点第 3 步“重新检测”，要看到“保护已关闭”才能安装。");
                if (title == "恢复还原") LogLine("下一步：点第 6 步“重启生效”，让保护重新开启。");
                if (title == "重启生效") LogLine("全部完成。学生机已回到全盘保护状态，软件仍在。");
                _lastDone = title;
                RefreshStepState();
                CloseBusy();
            };
            _runner.Start(targets);
            WaitBusy();
            HandleBusyCancelled(title, step);
        }

        private void CheckOnline()
        {
            CheckOnline(false);
        }

        /// <summary>
        /// 检查每台机器当前的 UWF 状态。quiet=true 是“自动检查”（向导打开时跑一次、步骤完成后补一次），
        /// 不弹提示框、日志只留摘要——老师不需要每次都点“重新检测”才能知道流程走到哪一步。
        /// </summary>
        private void CheckOnline(bool quiet)
        {
            if (_runner != null && _runner.Running) return;
            List<Machine> targets = Checked();
            if (targets.Count == 0) targets = _candidates;
            if (targets.Count == 0)
            {
                if (!quiet) MessageBox.Show(this, "请先勾选计算机。");
                return;
            }

            SetStatus(_s3, "检测中…", Ui.Accent);
            SetButtons(false);
            LogLine((quiet ? "自动检查" : "检测") + "在线状态：" + targets.Count + " 台。");
            ShowBusy(quiet ? "正在自动检查机器状态" : "正在检测在线状态",
                "正在连接 " + targets.Count + " 台计算机。离线机器每台要等到超时，请等待，不要重复点击。", targets.Count);

            int online = 0, guarded = 0;
            _runner = new FleetRunner(this);
            _runner.Parallelism = _settings.Parallel;
            _runner.Started = delegate(Machine m)
            {
                UpdateBusy(-1, -1, "正在连接 " + m.Name + "，读取 UWF 状态…（共 " + targets.Count + " 台）");
            };
            _runner.Work = delegate(Machine m)
            {
                RemoteState st = Remote.Query(TargetFor(m), 20000);
                m.State = st;
                m.StatusText = st.Ok ? "正常" : "失败";
                m.LastError = st.Ok ? "" : st.Error;
                m.LastSeen = DateTime.Now;
            };
            _runner.Completed = delegate(Machine m)
            {
                if (m.State != null && m.State.Ok)
                {
                    online++;
                    if (m.State.InMaintenance)
                    { if (!quiet) LogLine("  √ " + m.Name + " 在线，UWF 保护已关闭（可以安装）"); }
                    else if (m.State.UwfPresent)
                    {
                        guarded++;
                        if (!quiet)
                        {
                            // 过滤器开着、但磁盘保护还没在当前会话生效（排队中）：
                            // 这种“半生效”状态最容易误判，单独说清楚该怎么处理。
                            bool volQueued = m.State.FilterCurrent && !m.State.HasProtectedVolume && m.State.HasProtectedVolumeNext;
                            LogLine(volQueued
                                ? "  ! " + m.Name + " 在线：过滤器开着，但 " + m.State.PendingVolumeText +
                                  " 的磁盘保护还在排队（当前会话未生效）——先再重启一次让保护真的生效，再走第 1 步"
                                : "  ! " + m.Name + " 在线，但仍在保护 —— 重启没真正生效，先别安装");
                        }
                    }
                    else
                    { if (!quiet) LogLine("  √ " + m.Name + " 在线（这台没有 UWF，安装内容会直接保存）"); }
                }
                else
                {
                    if (!quiet) LogLine("  … " + m.Name + " 还连不上");
                }
            };
            _runner.Progress = delegate(int done, int total)
            {
                UpdateBusy(done, total, null);
            };
            _runner.Finished = delegate
            {
                SetButtons(true);
                bool ready = online == targets.Count && guarded == 0;
                SetStatus(_s3, "在线 " + online + " / " + targets.Count + (guarded > 0 ? " · 未关保护 " + guarded : ""),
                          ready ? Ui.Green : Ui.Amber);
                LogLine((quiet ? "自动检查" : "检测") + "结束：在线 " + online + " 台，共 " + targets.Count + " 台" +
                        (guarded > 0 ? "，其中 " + guarded + " 台仍在保护。" : "。"));
                if (guarded > 0)
                    LogLine("注意：这些机器关闭保护没有生效（还在还原）。此时安装会写进 UWF 缓存，缓存写满后重启会黑屏、进不了桌面。");
                else if (online == targets.Count)
                    LogLine("UWF 保护已确认关闭，可以继续第 4 步安装软件了。");
                else
                    LogLine("还没上线的机器：等 1–2 分钟再点一次；仍不在线就检查它是否停在登录界面或网络没通。");
                RefreshStepState();
                CloseBusy();
            };
            _runner.Start(targets);
            WaitBusy();
            if (HandleBusyCancelled("检测在线状态", _s3)) return;

            // 遮罩关掉之后再弹提示，否则提示框会被压在模态遮罩下面
            if (!quiet && guarded > 0)
            {
                MessageBox.Show(this,
                    "有 " + guarded + " 台机器在线，但仍在 UWF 保护状态 —— 说明“关闭保护”还没真正生效（通常是第 2 步的重启没成功，比如有程序阻止了重启）。\r\n\r\n" +
                    "现在安装有风险：安装内容会全部写进 UWF 缓存，缓存写满后机器重启就会黑屏、进不了桌面。\r\n\r\n" +
                    "请先回到第 2 步重启这些机器，等它们真的重启后再点“重新检测”。\r\n\r\n" +
                    "另：如果日志里写着“磁盘保护还在排队（当前会话未生效）”，说明过滤器虽然开着、但磁盘保护还没生效——" +
                    "先点第 2 步【重启所选】让它生效（用重启，不要关机再开机），再继续。",
                    "UWF 保护还没关闭", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /// <summary>向导打开后自动跑一次状态检查，不用老师每回从第 1 步点起。</summary>
        private void AutoCheckState()
        {
            if (_runner != null && _runner.Running) return;
            CheckOnline(true);
        }

        /// <summary>
        /// 按每台机器最近一次查到的状态，自动判断流程走到哪一步：
        /// 已经关过保护的机器不会再让你点第 1、2 步；全部关好了就直接指向第 4 步安装。
        /// </summary>
        private void RefreshStepState()
        {
            List<Machine> targets = Checked();
            if (targets.Count == 0) targets = _candidates;
            int total = targets.Count;
            if (total == 0) return;

            int off = 0, queued = 0, on = 0, resumeQueued = 0, unknown = 0;
            foreach (Machine m in targets)
            {
                RemoteState st = m.State;
                if (st == null || !st.Ok) { unknown++; continue; }
                if (!st.UwfPresent) { off++; continue; }                       // 没装 UWF：本来就保留
                if (st.ServicingCurrent) { off++; continue; }                  // 维修模式：也不还原
                if (!st.FilterCurrent && st.FilterNext) { resumeQueued++; continue; }  // 已排队恢复，等重启
                if (!st.FilterCurrent) { off++; continue; }                    // 保护已关闭（暂停还原中）
                if (!st.FilterNext) { queued++; continue; }                    // 已排队关闭，等重启
                on++;                                                          // 仍在保护
            }

            // 第 1 步
            if (off == total)
                SetStatus(_s1, "已全部关闭保护（不用再点）", Ui.Green);
            else if (off + queued == total && queued > 0)
                SetStatus(_s1, "已排队关闭 " + queued + " 台，点第 2 步重启生效", Ui.Amber);
            else if (off > 0 || queued > 0)
                SetStatus(_s1, "已关闭 " + off + " / 排队 " + queued + "，共 " + total + " 台", Ui.Amber);
            else if (unknown == total)
                SetStatus(_s1, "未检测", Ui.Muted);
            else
                SetStatus(_s1, "未开始", Ui.Muted);

            // 第 2 步
            if (queued > 0)
                SetStatus(_s2, "需重启生效 " + queued + " 台", Ui.Amber);
            else if (off == total)
                SetStatus(_s2, "已生效（不用再点）", Ui.Green);
            else
                SetStatus(_s2, "待第 1 步", Ui.Muted);

            // 第 3 步
            if (unknown > 0)
                SetStatus(_s3, "未确认 " + unknown + " 台（重新检测）", Ui.Amber);
            else if (off == total)
                SetStatus(_s3, "已确认全部保护已关闭，可直接装", Ui.Green);
            else
                SetStatus(_s3, "待第 2 步重启后确认", Ui.Muted);

            // 第 5 步：只要还有机器没恢复保护，就一直提醒
            bool done = _lastDone == "重启生效";
            if (done)
            {
                SetStatus(_s5, "已恢复保护（完成）", Ui.Green);
                SetStatus(_s6, "已重启生效（完成）", Ui.Green);
            }
            else
            {
                if (resumeQueued > 0)
                    SetStatus(_s5, "已排队恢复 " + resumeQueued + " 台，等重启", Ui.Amber);
                else if (off > 0)
                    SetStatus(_s5, "当前未还原 " + off + " 台，装完必须恢复", Ui.Amber);
                else
                    SetStatus(_s5, "待安装完成", Ui.Muted);

                SetStatus(_s6, resumeQueued > 0 ? "待重启生效" : (off > 0 ? "待第 5 步" : "待安装完成"), Ui.Muted);
            }

            // 当前该做第几步，一句话说清楚
            string step;
            if (done) step = "已全部完成：重启后学生机回到全盘保护，装的软件仍在";
            else if (unknown > 0) step = "第 3 步（先点「重新检测」确认机器状态）";
            else if (queued > 0) step = "第 2 步（重启所选，让关闭保护生效）";
            else if (on > 0) step = "第 1 步（关闭 UWF 保护）";
            else if (off > 0) step = "第 4 步（分发并安装软件）";
            else step = "第 5 步（恢复还原）";

            LogLine("自动判断进度：保护已关闭 " + off + " 台、排队关闭 " + queued + " 台、仍在保护 " + on +
                    " 台、排队恢复 " + resumeQueued + " 台、未确认 " + unknown + " 台。当前该做" + step + "。");
        }

        /// <summary>
        /// 安装前的硬性闸口：学生机必须真的关闭了 UWF 保护（filter 已停用）。
        /// 在保护状态下安装，写入会全部进 UWF 缓存；缓存写满后机器重启会黑屏、进不了桌面。
        /// 返回空字符串表示可以安装，否则返回跳过原因。
        /// </summary>
        private string ProtectionBlock(Machine m)
        {
            RemoteState st = Remote.Query(TargetFor(m), 20000);
            m.State = st;
            if (st == null || !st.Ok)
                return "读不到 UWF 状态，已跳过安装" +
                       (st != null && st.Error.Length > 0 ? "（" + Ui.Short(st.Error, 80) + "）" : "。");
            if (!st.UwfPresent) return "";      // 本来就没有 UWF，装哪都直接保存
            if (st.InMaintenance) return "";    // 保护已关闭（或维修模式中）
            if (st.FilterCurrent && !st.FilterNext)
                return "关闭保护已排队但还没重启生效，已跳过安装。请先执行第 2 步重启。";
            return "仍在 UWF 保护状态（还没关闭保护），已跳过安装。请先执行第 1、2 步并等机器真的重启。";
        }

        private void DoInstall()
        {
            if (_modePortable.Checked) DoPortable();
            else DoInstaller();
        }

        private void DoInstaller()
        {
            if (_runner != null && _runner.Running) return;
            List<Machine> targets = Checked();
            if (targets.Count == 0) { MessageBox.Show(this, "请先勾选计算机。"); return; }

            bool remote = _srcRemote.Checked;
            string file = (_installer.Text ?? "").Trim();
            if (file.Length == 0)
            {
                MessageBox.Show(this, remote ? "请填写学生机上的安装包完整路径。" : "请选择一个安装包。");
                return;
            }
            if (!remote && !File.Exists(file))
            {
                MessageBox.Show(this, "请选择一个存在的安装包。");
                return;
            }
            // 静默参数按“识别出来的安装包类型”自动选，老师不用填：
            // Inno Setup→/VERYSILENT…，NSIS→/S，MSI→/quiet /norestart。
            // 以前 exe 一律塞 /S：Inno 系不认，会弹一个系统会话里没人看得见的界面，就是“装不上”。
            InstallerInfo pkg = DetectInstaller(file);
            string args = "";
            if (_silent.Checked) args = pkg.SilentArgs;
            string extra = (_extraArgs.Text ?? "").Trim();
            if (extra.Length > 0) args = args.Length > 0 ? args + " " + extra : extra;
            bool wait = _wait.Checked;
            bool interactive = _interactive.Checked;
            string deployDir = (_deployDir.Text ?? "").Trim();
            if (deployDir.Length == 0) deployDir = @"C:\Windows\Temp\UWFTeacher\deploy";

            if (!Confirm("分发并安装", (remote ? "学生机路径：" : "安装包：") + file +
                "\r\n安装包类型：" + pkg.TypeName +
                (remote ? "" : "\r\n放到学生机：" + deployDir) +
                "\r\n安装方式：" + (args.Length > 0 ? "静默安装（参数 " + args + "）" : "不加静默参数（可能弹安装界面）") +
                (interactive ? "，以登录用户身份运行" : "") +
                (wait ? "，等待安装结束" : "，不等待安装结束") +
                "\r\n目标：" + targets.Count + " 台\r\n\r\n确定开始？")) return;

            SetStatus(_s4, "安装中…", Ui.Accent);
            SetButtons(false);
            BeginProgress(targets.Count, "准备安装 " + Path.GetFileName(file));
            LogLine("开始" + (remote ? "远程安装" : "分发安装包") + "：" + Path.GetFileName(file) + " → " + targets.Count + " 台。");
            LogLine("安装包类型：" + pkg.TypeName + (args.Length > 0 ? "，静默参数：" + args : "，不加静默参数"));
            if (_silent.Checked && !pkg.Known)
                LogLine("提示：没能识别这个安装包的类型，暂按 NSIS 习惯用 /S。装不上时：去掉「静默安装」重试，" +
                        "或在「附加参数」里填它认的参数（Inno 系用 /VERYSILENT），或勾「以登录用户运行」。");
            if (!interactive)
                LogLine("提示：输入法、浏览器插件这类“按用户安装”的软件，要勾「以登录用户运行」才装得到学生账号下。");
            ShowBusy("正在分发并安装",
                "正在处理 " + targets.Count + " 台计算机（" + Path.GetFileName(file) + "）。下方会实时显示复制进度和安装耗时，请等待。",
                targets.Count);

            int ok = 0, bad = 0;
            _runner = new FleetRunner(this);
            int parallel = (int)_parallel.Value;
            _runner.Parallelism = Math.Max(1, Math.Min(parallel, targets.Count));
            _runner.Started = delegate(Machine m)
            {
                UpdateBusy(-1, -1, "开始处理 " + m.Name + "：" + Path.GetFileName(file) + "…");
            };
            long pkgSize = 0;
            if (!remote)
            {
                try { pkgSize = new FileInfo(file).Length; } catch { }
                try
                {
                    DriveInfo di = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(file)));
                    if (di.DriveType == DriveType.Network)
                        LogLine("提示：安装包放在网络盘（" + di.Name + "）上，复制时会经教师机中转，速度取决于两端网络，大文件请耐心等进度。");
                }
                catch { }
                if (pkgSize > 300L * 1024 * 1024 && parallel > 4)
                    LogLine("安装包较大（" + Mb(pkgSize) + "）且同时台数 " + parallel + "，带宽会被分摊；想更快传完可调小到 2–4 台。");
                else
                    LogLine("本次同时处理 " + _runner.Parallelism + " 台（可在第 4 步调整「同时台数」，最多 8 台）。");
            }

            _runner.Work = delegate(Machine m)
            {
                RemoteTarget t = TargetFor(m);
                string fileName = Path.GetFileName(file);

                Beat("正在检查 " + m.Name + " 的 UWF 状态…（" + fileName + "）");
                string block = ProtectionBlock(m);
                if (block.Length > 0) { m.LastError = block; return; }

                string remotePath = file;
                if (!remote)
                {
                    long size = pkgSize;
                    PostLog("  · " + m.Name + " 开始复制安装包（" + Mb(size) + "）→ " + deployDir);
                    Beat("正在复制安装包到 " + m.Name + "：" + Mb(size) + "，大文件要几分钟，请等待…");

                    DateTime copyStart = DateTime.Now;
                    string err;
                    remotePath = Remote.PushFileTo(t, file, deployDir, out err,
                        delegate(long done, long total)
                        {
                            double sec = Math.Max(0.5, (DateTime.Now - copyStart).TotalSeconds);
                            double mbps = done / 1048576.0 / sec;
                            string percent = total > 0 ? "（" + (done * 100 / total) + "%）" : "";
                            string left = "";
                            if (total > 0 && mbps > 0.05)
                                left = "，约剩 " + FmtDur((int)((total - done) / 1048576.0 / mbps));
                            Beat("复制到 " + m.Name + "：" + Mb(done) + " / " + Mb(total) + percent +
                                 "，" + mbps.ToString("0.0") + " MB/s" + left);
                        });

                    if (remotePath == null)
                    {
                        m.LastError = err;
                        PostLog("  × " + m.Name + " 复制安装包失败：" + err);
                        return;
                    }
                    PostLog("  · " + m.Name + " 安装包已复制完成（" + Mb(size) + "，用时 " +
                            FmtDur((int)(DateTime.Now - copyStart).TotalSeconds) + "）");
                }

                string cmd = BuildInstallCommand(remotePath, file, args);
                string workDir = deployDir;
                if (remote)
                {
                    try { workDir = Path.GetDirectoryName(remotePath); } catch { }
                    if (string.IsNullOrEmpty(workDir)) workDir = @"C:\";
                }

                Func<bool> cancelled = delegate { return _runner != null && _runner.Cancelled; };
                Action<int> beat = delegate(int sec)
                {
                    // 超过 2 分钟没结束就提醒常见原因，别让老师对着遮罩干等
                    string hint = sec >= 120
                        ? "（学生机上毫无动静多半是：静默参数不被支持，或是输入法这类要装到用户账号的软件——点取消，去掉「静默安装」或勾「以登录用户运行」重试）"
                        : "";
                    Beat("正在安装 " + m.Name + "：已用 " + FmtDur(sec) + "，最多等 30 分钟…" + hint);
                };

                Beat("正在启动安装程序：" + m.Name + "…");
                DateTime runStart = DateTime.Now;
                UwfResult r;

                if (interactive)
                {
                    // 以登录用户身份：计划任务在用户的会话里跑，安装界面会出现在学生机屏幕上
                    string reportId;
                    uint outerPid;
                    string perr;
                    if (!Remote.StartInstallInteractive(t, cmd, workDir, out reportId, out outerPid, out perr))
                    {
                        m.LastError = "启动安装程序失败：" + (string.IsNullOrEmpty(perr) ? "学生机没有返回结果" : perr);
                        PostLog("  × " + m.Name + " " + m.LastError);
                        return;
                    }
                    PostLog("  · " + m.Name + " 安装程序已启动（以登录用户身份，界面会出现在学生机上）：" + fileName);
                    if (!wait)
                    {
                        m.LastError = "";
                        m.LastAction = "安装 " + fileName;
                        PostLog("  · " + m.Name + " 已后台启动，不等待安装结束（安装是否成功请到机器上看）");
                        return;
                    }
                    Beat("正在安装 " + m.Name + "：安装程序已启动，等待结束…");
                    r = Remote.WaitInstall(t, reportId, outerPid, 1800000, beat, cancelled);
                }
                else
                {
                    // 直接启动安装包本身，盯它自己的 PID。
                    // 不要用 cmd 包一层再去等：那样会连安装包拉起的子进程一起等，
                    // 表现就是“安装永远不结束、取消也没反应”。
                    uint pid;
                    string perr;
                    if (!Remote.StartProcess(t, cmd, out perr, out pid))
                    {
                        m.LastError = "启动安装程序失败：" + (string.IsNullOrEmpty(perr) ? "学生机没有返回进程号" : perr);
                        PostLog("  × " + m.Name + " " + m.LastError);
                        return;
                    }
                    PostLog("  · " + m.Name + " 安装程序已启动（PID " + pid + "）：" + fileName);
                    if (!wait)
                    {
                        m.LastError = "";
                        m.LastAction = "安装 " + fileName;
                        PostLog("  · " + m.Name + " 已后台启动，不等待安装结束（安装是否成功请到机器上看）");
                        return;
                    }
                    Beat("正在安装 " + m.Name + "：安装程序已启动，等待结束…");
                    r = Remote.WaitProcess(t, pid, 1800000, beat, cancelled);
                }

                int runSeconds = (int)(DateTime.Now - runStart).TotalSeconds;
                if (r.ExitCode == -15)
                {
                    m.LastError = "已取消";
                    PostLog("  · " + m.Name + " 已取消（学生机上的安装进程已结束）");
                    return;
                }
                if (r.TimedOut)
                {
                    m.LastError = Ui.Short(r.StdErr, 200);
                    PostLog("  × " + m.Name + " 等待超时（已等 " + FmtDur(runSeconds) + "）：" + m.LastError);
                    return;
                }
                if (r.ExitCode != 0 && r.StdErr.Length > 0 && string.IsNullOrEmpty(r.StdOut))
                {
                    m.LastError = Ui.Short(r.StdErr, 200);
                    PostLog("  × " + m.Name + " 启动方式有问题（用时 " + FmtDur(runSeconds) + "）：" + m.LastError);
                    return;
                }

                // 无论成败都把安装包自己的输出打出来：这是“装了没反应”时唯一的线索
                string output = (r.StdOut ?? "").Trim();
                if (output.Length > 0)
                {
                    foreach (string line in output.Replace("\r", "").Split('\n'))
                    {
                        string s = line.TrimEnd();
                        if (s.Length > 0) PostLog("      " + Ui.Short(s, 200));
                    }
                }

                if (r.ExitCode != 0)
                {
                    m.LastError = "安装包返回非 0 退出码 " + r.ExitCode + (output.Length > 0 ? "：" + Ui.Short(output, 120) : "") +
                                  "（多数是静默参数不对或缺少运行库）";
                    PostLog("  × " + m.Name + " " + m.LastError + "，用时 " + FmtDur(runSeconds));
                    return;
                }

                m.LastError = "";
                m.LastAction = "安装 " + fileName;
                PostLog("  · " + m.Name + " 安装包正常退出（退出码 0，用时 " + FmtDur(runSeconds) + "）" +
                        (output.Length == 0 ? "（安装包没有输出任何文字）" : ""));
                if (runSeconds < 20)
                    PostLog("    提示：结束得很快，可能只是引导器退出了（真正的安装在子进程里继续）。以学生机上看到的结果为准。");
            };
            _runner.Completed = delegate(Machine m)
            {
                _finishedTargets++;
                UpdateProgress(_finishedTargets, targets.Count, "已处理 " + _finishedTargets + " / " + targets.Count + " 台");
                if (m.LastError.Length == 0) { ok++; LogLine("  √ " + m.Name + " 已" + (wait ? "安装程序退出" : "启动安装")); }
                else { bad++; LogLine("  × " + m.Name + "：" + m.LastError); }
            };
            _runner.Finished = delegate
            {
                SetButtons(true);
                FinishProgress("安装结束：成功 " + ok + " 台，失败 " + bad + " 台");
                SetStatus(_s4, "完成 " + ok + " / 失败 " + bad, bad == 0 ? Ui.Green : Ui.Amber);
                LogLine("安装结束：成功 " + ok + " 台，失败 " + bad + " 台。");
                if (bad > 0) LogLine("失败的机器多数是“UWF 保护还没关闭”被主动跳过：回到第 1、2 步重启后再装。");
                LogLine("下一步：先确认学生机上的安装程序已经结束，再点第 5 步“恢复还原”，最后第 6 步重启生效。");
                _lastDone = "安装";
                RefreshStepState();
                CloseBusy();
            };
            _runner.Start(targets);
            WaitBusy();
            HandleBusyCancelled(_modePortable.Checked ? "部署便携版" : "分发并安装", _s4);
        }

        private void DoPortable()
        {
            if (_runner != null && _runner.Running) return;
            List<Machine> targets = Checked();
            if (targets.Count == 0) { MessageBox.Show(this, "请先勾选计算机。"); return; }

            bool remote = _srcRemote.Checked;
            string dir = (_portDir.Text ?? "").Trim();
            string exeRel = (_portExe.Text ?? "").Trim().Replace('/', '\\').TrimStart('\\');
            string linkName = (_portLink.Text ?? "").Trim();
            bool desktop = _portDesktop.Checked;

            if (dir.Length == 0) { MessageBox.Show(this, remote ? "请填写学生机上的目录完整路径。" : "请选择便携版软件的文件夹。"); return; }
            if (!remote && !Directory.Exists(dir)) { MessageBox.Show(this, "文件夹不存在：" + dir); return; }
            if (exeRel.Length == 0) { MessageBox.Show(this, "请填写快捷启动程序（目录内的相对路径，如 MyApp.exe）。"); return; }
            if (desktop && linkName.Length == 0) { MessageBox.Show(this, "请填写快捷方式名称。"); return; }
            string targetParent = (_portTarget.Text ?? "").Trim();
            if (targetParent.Length == 0) targetParent = @"C:\Program Files";

            if (!Confirm("部署便携版", (remote ? "学生机目录：" : "本地目录：") + dir +
                (remote ? "" : "\r\n装到学生机：" + targetParent + "\\<目录名>") +
                "\r\n快捷启动程序：" + exeRel +
                (desktop ? "\r\n公共桌面快捷方式：" + linkName : "\r\n（不创建快捷方式）") +
                "\r\n目标：" + targets.Count + " 台\r\n\r\n确定开始？")) return;

            SetStatus(_s4, "部署中…", Ui.Accent);
            SetButtons(false);
            BeginProgress(targets.Count, "准备部署便携版");
            LogLine("开始部署便携版 → " + targets.Count + " 台。");
            ShowBusy("正在部署便携版", "正在把目录复制到 " + targets.Count + " 台计算机并创建快捷方式，大目录会比较久，请等待。",
                     targets.Count);

            int ok = 0, bad = 0;
            _runner = new FleetRunner(this);
            _runner.Parallelism = Math.Min(_settings.Parallel, 8);
            _runner.Started = delegate(Machine m)
            {
                UpdateBusy(-1, -1, "开始部署到 " + m.Name + "…（共 " + targets.Count + " 台）");
            };
            _runner.Work = delegate(Machine m)
            {
                RemoteTarget t = TargetFor(m);
                Beat("正在检查 " + m.Name + " 的 UWF 状态…");
                string block = ProtectionBlock(m);
                if (block.Length > 0) { m.LastError = block; return; }
                string baseDir = dir;
                if (!remote)
                {
                    Beat("正在复制目录到 " + m.Name + "：大目录要几分钟，请等待…");
                    PostLog("  · " + m.Name + " 开始复制目录 " + dir + " → " + targetParent);
                    DateTime copyStart = DateTime.Now;
                    string err;
                    baseDir = Remote.PushDirectory(t, dir, targetParent, out err, delegate(int files, long bytes)
                    {
                        Beat("复制目录到 " + m.Name + "：" + files + " 个文件、" + Mb(bytes) + "…");
                    });
                    if (baseDir == null)
                    {
                        m.LastError = err;
                        PostLog("  × " + m.Name + " 复制目录失败：" + err);
                        return;
                    }
                    PostLog("  · " + m.Name + " 目录已复制完成（用时 " +
                            FmtDur((int)(DateTime.Now - copyStart).TotalSeconds) + "）");
                }
                string exe = baseDir.TrimEnd('\\') + "\\" + exeRel;
                if (desktop)
                {
                    Remote.RemoteShortcut sc = new Remote.RemoteShortcut();
                    sc.LinkPath = @"C:\Users\Public\Desktop\" + linkName + ".lnk";
                    sc.TargetPath = exe;
                    sc.WorkDir = baseDir;
                    sc.Description = linkName;
                    UwfResult r = Remote.CreateShortcut(t, sc);
                    if (!r.Ok)
                    {
                        m.LastError = Ui.Short(r.All, 200);
                        return;
                    }
                }
                m.LastError = "";
                m.LastAction = "部署 " + linkName;
            };
            _runner.Completed = delegate(Machine m)
            {
                _finishedTargets++;
                UpdateProgress(_finishedTargets, targets.Count, "已处理 " + _finishedTargets + " / " + targets.Count + " 台");
                if (m.LastError.Length == 0) { ok++; LogLine("  √ " + m.Name + " 已部署" + (desktop ? "，快捷方式在公共桌面" : "")); }
                else { bad++; LogLine("  × " + m.Name + "：" + m.LastError); }
            };
            _runner.Finished = delegate
            {
                SetButtons(true);
                FinishProgress("部署结束：成功 " + ok + " 台，失败 " + bad + " 台");
                SetStatus(_s4, "完成 " + ok + " / 失败 " + bad, bad == 0 ? Ui.Green : Ui.Amber);
                LogLine("便携版部署结束：成功 " + ok + " 台，失败 " + bad + " 台。");
                if (bad > 0) LogLine("失败的机器多数是“UWF 保护还没关闭”被主动跳过：回到第 1、2 步重启后再部署。");
                LogLine("下一步：点第 5 步“恢复还原”，然后第 6 步重启生效。");
                _lastDone = "安装";
                RefreshStepState();
                CloseBusy();
            };
            _runner.Start(targets);
            WaitBusy();
            HandleBusyCancelled(_modePortable.Checked ? "部署便携版" : "分发并安装", _s4);
        }

        private string BuildInstallCommand(string remotePath, string localFile, string args)
        {
            if (localFile.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
                return "msiexec.exe /i \"" + remotePath + "\"" + (args.Length > 0 ? " " + args : "");
            if (localFile.EndsWith(".bat", StringComparison.OrdinalIgnoreCase) || localFile.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase))
                return "cmd.exe /c \"" + remotePath + "\"" + (args.Length > 0 ? " " + args : "");
            return "\"" + remotePath + "\"" + (args.Length > 0 ? " " + args : "");
        }

        private void BeginProgress(int total, string text)
        {
            _finishedTargets = 0;
            _progressText.Text = text;
        }

        private void SetProgressActivity(string text)
        {
            if (IsDisposed || !IsHandleCreated) return;
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    if (IsDisposed) return;
                    _progressText.Text = text;
                });
            }
            catch (InvalidOperationException) { }
        }

        private void UpdateProgress(int value, int total, string text)
        {
            _progressText.Text = text;
            UpdateBusy(value, total, null);
        }

        private void FinishProgress(string text)
        {
            _progressText.Text = text;
        }

        private void SetButtons(bool enabled)
        {
            StepUi[] all = new StepUi[] { _s1, _s2, _s3, _s4, _s5, _s6 };
            foreach (StepUi s in all)
            {
                if (s == null) continue;
                if (s.Action != null) s.Action.Enabled = enabled;
            }
            if (_installButton != null) _installButton.Enabled = enabled;
            if (_list != null) _list.Enabled = enabled;
        }
    }
}
