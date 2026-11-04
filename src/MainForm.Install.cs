using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace UwfTeacher
{
    public partial class MainForm
    {
        private StepRow[] _steps;
        private Card _bannerCard;
        private Label _bannerTitle;
        private Label _bannerText;
        private TextBox _p2Folder;
        private TextBox _p2Commit;
        private Label _p2FolderHint;

        private Panel BuildInstallPage()
        {
            Panel page = new Panel();
            page.Dock = DockStyle.Fill;
            page.BackColor = Ui.Bg;

            FlowLayoutPanel stack = Ui.Stack();
            page.Controls.Add(stack);

            _bannerCard = new Card("", 98, Ui.Accent);
            _bannerTitle = Ui.L("", Ui.F10B, Ui.TextColor);
            _bannerTitle.Location = new Point(0, 2);
            _bannerCard.Body.Controls.Add(_bannerTitle);
            _bannerText = Ui.Wrap("", Ui.F9, Ui.Muted);
            _bannerText.Location = new Point(0, 28);
            _bannerText.Size = new Size(900, 46);
            _bannerCard.Body.Controls.Add(_bannerText);
            stack.Controls.Add(_bannerCard);

            Card wizard = new Card("安装向导：让软件不被还原（推荐）", 380);
            _steps = new StepRow[4];
            string[] titles = { "关闭保护（暂停还原）", "重启计算机", "安装软件", "恢复保护" };
            string[] descs =
            {
                "排队“关闭 UWF 保护”，下次重启后系统暂时不再还原，改动会直接写入硬盘。",
                "重启后本页会显示『保护已关闭』，此时系统不再还原，安装的软件才会留下。",
                "运行安装包，按平时的方式安装到 C 盘即可，安装过程不会被拦截。",
                "重新开启还原并重启。刚装的软件保留，学生之后的改动仍然会被还原。"
            };
            string[] buttons = { "关闭保护", "立即重启", "运行安装程序…", "恢复保护并重启" };

            TableLayoutPanel stepHost = new TableLayoutPanel();
            stepHost.Dock = DockStyle.Fill;
            stepHost.ColumnCount = 1;
            stepHost.RowCount = 4;
            stepHost.BackColor = Ui.CardBg;
            stepHost.Margin = new Padding(0);
            for (int i = 0; i < 4; i++) stepHost.RowStyles.Add(new RowStyle(SizeType.Absolute, 86));
            wizard.Body.Controls.Add(stepHost);

            for (int i = 0; i < 4; i++)
            {
                StepRow row = new StepRow(i + 1, titles[i], descs[i], buttons[i]);
                row.Dock = DockStyle.Fill;
                row.Margin = new Padding(0, 0, 0, 8);
                stepHost.Controls.Add(row, 0, i);
                _steps[i] = row;
            }
            _steps[0].Action.Click += delegate { DoPauseProtection(); };
            _steps[1].Action.Click += delegate
            {
                if (!ConfirmDanger("立即重启", "计算机将立即重启，请先保存正在编辑的文件。")) return;
                RunAction(delegate { return Uwf.RestartNow(); }, "已发送重启命令。");
            };
            _steps[2].Action.Click += delegate { DoRunInstaller(); };
            _steps[3].Action.Click += delegate { DoResumeProtection(); };
            stack.Controls.Add(wizard);

            Card folder = new Card("方式二：把某个目录设为『永久保存区』", 196);
            Label fl = Ui.Wrap("把目录加入排除列表后，写进这个目录的文件不会被还原。适合：学生作业、教学资源、绿色软件、" +
                               "软件的配置 / 题库 / 词库目录。添加后需要重启一次生效。", Ui.F9, Ui.Muted);
            fl.Location = new Point(0, 0);
            fl.Size = new Size(1000, 40);
            folder.Body.Controls.Add(fl);

            _p2Folder = new TextBox();
            Ui.StyleInput(_p2Folder);
            _p2Folder.Location = new Point(0, 50);
            _p2Folder.Width = 560;
            _p2Folder.Text = @"D:\作业";
            folder.Body.Controls.Add(_p2Folder);

            Button browse = Ui.B("浏览…");
            browse.Location = new Point(568, 48);
            browse.Width = 76;
            browse.Height = 28;
            browse.Click += delegate
            {
                FolderBrowserDialog dlg = new FolderBrowserDialog();
                dlg.Description = "选择要永久保存的目录";
                if (dlg.ShowDialog(this) == DialogResult.OK) _p2Folder.Text = dlg.SelectedPath;
            };
            folder.Body.Controls.Add(browse);

            Button addEx = Ui.B("加入排除列表", true);
            addEx.Location = new Point(652, 47);
            addEx.Width = 124;
            addEx.Height = 30;
            addEx.Click += delegate
            {
                string path = (_p2Folder.Text ?? "").Trim();
                if (path.Length < 3 || path[1] != ':')
                {
                    MessageBox.Show(this, "请输入带盘符的完整路径，例如 D:\\作业", "路径不正确");
                    return;
                }
                if (!Confirm("加入排除列表", "路径：" + path + "\r\n\r\n加入后需要重启一次才会生效。\r\n" +
                                              "该目录内的所有写入都会永久保留，包括学生放进去的文件。")) return;
                RunAction(delegate { return Uwf.AddFileExclusion(path); }, "已加入排除列表，请重启一次生效。");
            };
            folder.Body.Controls.Add(addEx);

            _p2FolderHint = Ui.Wrap("", Ui.F85, Ui.Muted);
            _p2FolderHint.Location = new Point(0, 92);
            _p2FolderHint.Size = new Size(1000, 60);
            folder.Body.Controls.Add(_p2FolderHint);
            stack.Controls.Add(folder);

            Card commit = new Card("方式三：把本次已经产生的改动立刻写盘", 258);
            Label cl = Ui.Wrap("适合只改了一点点东西（一个配置文件、一个注册表项）的情况。注意：只提交『已经存在』的改动，" +
                               "提交之后新产生的写入仍然会被还原；安装大型软件请用上面的第 1–4 步（关闭保护 → 装 → 恢复保护）。", Ui.F9, Ui.Muted);
            cl.Location = new Point(0, 0);
            cl.Size = new Size(1000, 40);
            commit.Body.Controls.Add(cl);

            _p2Commit = new TextBox();
            Ui.StyleInput(_p2Commit);
            _p2Commit.Location = new Point(0, 50);
            _p2Commit.Width = 560;
            _p2Commit.Text = "";
            _p2Commit.ForeColor = Ui.Muted;
            commit.Body.Controls.Add(_p2Commit);

            Button pickFile = Ui.B("选择文件…");
            pickFile.Location = new Point(568, 48);
            pickFile.Width = 96;
            pickFile.Height = 28;
            pickFile.Click += delegate
            {
                OpenFileDialog dlg = new OpenFileDialog();
                dlg.Title = "选择要提交的文件";
                if (dlg.ShowDialog(this) == DialogResult.OK) _p2Commit.Text = dlg.FileName;
            };
            commit.Body.Controls.Add(pickFile);

            Button pickDir = Ui.B("选择文件夹…");
            pickDir.Location = new Point(670, 48);
            pickDir.Width = 104;
            pickDir.Height = 28;
            pickDir.Click += delegate
            {
                FolderBrowserDialog dlg = new FolderBrowserDialog();
                dlg.Description = "选择要提交的文件夹";
                if (dlg.ShowDialog(this) == DialogResult.OK) _p2Commit.Text = dlg.SelectedPath;
            };
            commit.Body.Controls.Add(pickDir);

            Button doCommit = Ui.B("提交到硬盘", true);
            doCommit.Location = new Point(780, 47);
            doCommit.Width = 112;
            doCommit.Height = 30;
            doCommit.Click += delegate
            {
                string path = (_p2Commit.Text ?? "").Trim();
                if (path.Length < 3) { MessageBox.Show(this, "请先选择或填写要提交的文件 / 文件夹路径。"); return; }
                if (path.Length <= 3 && path.EndsWith(":\\", StringComparison.Ordinal)) { if (!Confirm("提交整个磁盘", "将提交整个 " + path + " 的改动，数据量大时可能非常久。确定继续？")) return; }
                if (!Confirm("提交更改", "将把 " + path + " 目前在缓存里的改动写入硬盘。\r\n\r\n确定继续？")) return;
                RunAction(delegate { return Uwf.CommitFile(path); }, "提交请求已发送。");
            };
            commit.Body.Controls.Add(doCommit);

            Label regLabel = Ui.L("注册表项（可选，例如 HKLM\\SOFTWARE\\MyApp）：", Ui.F9, Ui.Muted);
            regLabel.Location = new Point(0, 100);
            commit.Body.Controls.Add(regLabel);

            TextBox regBox = new TextBox();
            Ui.StyleInput(regBox);
            regBox.Location = new Point(0, 126);
            regBox.Width = 560;
            regBox.Text = @"HKLM\SOFTWARE\";
            commit.Body.Controls.Add(regBox);

            Button regCommit = Ui.B("提交注册表项");
            regCommit.Location = new Point(568, 124);
            regCommit.Width = 132;
            regCommit.Height = 30;
            regCommit.Click += delegate
            {
                string key = (regBox.Text ?? "").Trim();
                if (!key.StartsWith("HKLM\\") && !key.StartsWith("HKCU\\"))
                {
                    MessageBox.Show(this, "注册表路径请以 HKLM\\ 或 HKCU\\ 开头。");
                    return;
                }
                if (!Confirm("提交注册表", "将把 " + key + " 的当前改动写入硬盘。确定继续？")) return;
                RunAction(delegate { return Uwf.CommitReg(key); }, "注册表提交请求已发送。");
            };
            commit.Body.Controls.Add(regCommit);

            Label tip = Ui.Wrap("提示：如果软件安装时大量写注册表，这种方式容易遗漏。建议直接用上面的四步向导。", Ui.F85, Ui.Muted);
            tip.Location = new Point(0, 162);
            tip.Size = new Size(1000, 30);
            commit.Body.Controls.Add(tip);
            stack.Controls.Add(commit);

            Card guide = new Card("老师常问：装完软件重启又没了，怎么办？", 188);
            Label g = Ui.Wrap(
                "① 先看顶部状态：如果显示『保护已关闭』，说明现在装软件正合适，装完执行第 4 步即可。\r\n" +
                "② 如果显示『保护已开启』，说明安装时系统一直在还原——请按第 1 步关闭保护并重启后重新安装。\r\n" +
                "③ 装完一定要执行第 4 步『恢复保护』并重启，否则这台机器会一直处于不还原状态。\r\n" +
                "④ 装软件不要用『维修模式』：那是微软留给 Windows 更新的，退出时更新没装完会黑屏。", Ui.F9, Ui.TextColor);
            g.Location = new Point(0, 0);
            g.Size = new Size(1000, 130);
            guide.Body.Controls.Add(g);
            stack.Controls.Add(guide);

            return page;
        }

        private void UpdateInstallPage()
        {
            if (_steps == null) return;

            bool installed = _state.Installed;
            bool paused = installed && !_state.FilterCurrent && !_state.FilterNext;      // 保护已关闭（暂停还原中）
            bool pausePending = installed && _state.FilterCurrent && !_state.FilterNext; // 已排队关闭保护
            bool resumePending = installed && !_state.FilterCurrent && _state.FilterNext; // 已排队恢复保护

            for (int i = 0; i < 4; i++) _steps[i].Action.Enabled = false;
            for (int i = 0; i < 4; i++) _steps[i].SetStatus(StepStatus.Pending);
            _steps[3].Action.Text = "恢复保护并重启";

            if (!installed)
            {
                _bannerTitle.Text = "这台电脑没有启用 UWF";
                _bannerText.Text = "下面的步骤用不上。请先确认系统版本（教育版 / 企业版 / IoT 版）并安装 UWF 组件。";
                _steps[0].SetStatus(StepStatus.Current);
                _p2FolderHint.Text = "UWF 未启用时，所有改动本来就会保留，不需要排除或提交。";
                return;
            }

            if (paused)
            {
                _bannerTitle.Text = "UWF 保护已关闭：现在的所有改动都会永久保存";
                _bannerText.Text = "安装 / 设置完成后，请立刻执行第 4 步『恢复保护并重启』，否则学生使用期间改动不会被还原。";
                _steps[0].SetStatus(StepStatus.Done);
                _steps[1].SetStatus(StepStatus.Done);
                _steps[2].SetStatus(StepStatus.Current);
                _steps[2].Action.Enabled = true;
                _steps[3].Action.Enabled = true;
                _p2FolderHint.Text = "保护关闭期间添加的排除项也会一并保留。";
            }
            else if (pausePending)
            {
                _bannerTitle.Text = "关闭保护已排队，等待重启";
                _bannerText.Text = "重启后本页会切换成『UWF 保护已关闭』，那时再安装软件。重启前系统仍在还原。";
                _steps[0].SetStatus(StepStatus.Done);
                _steps[1].SetStatus(StepStatus.Current);
                _steps[1].Action.Enabled = true;
                _p2FolderHint.Text = "重启之前系统仍在还原，此时安装的软件不会保留。";
            }
            else if (resumePending)
            {
                _bannerTitle.Text = "保护已排队恢复，等待重启";
                _bannerText.Text = "重启后系统恢复还原，本次安装的内容仍然保留。如果没有自动重启，请点第 2 步。";
                _steps[0].SetStatus(StepStatus.Done);
                _steps[1].SetStatus(StepStatus.Done);
                _steps[2].SetStatus(StepStatus.Done);
                _steps[3].SetStatus(StepStatus.Current);
                _steps[1].Action.Enabled = true;
                _steps[3].Action.Enabled = true;
                _p2FolderHint.Text = "重启后记得确认顶部状态变成『保护已开启』。";
            }
            else
            {
                _bannerTitle.Text = "系统正在保护中：学生的改动会在重启后还原";
                _bannerText.Text = "要装软件或改系统设置，请从第 1 步开始。装完记得执行第 4 步恢复保护。";
                _steps[0].SetStatus(StepStatus.Current);
                _steps[0].Action.Enabled = true;
                _p2FolderHint.Text = "当前未添加新排除项。常用建议：把学生作业目录加入排除列表。";
            }
        }

        /// <summary>第 1 步：关闭 UWF 保护（filter disable），也就是“暂停还原”。</summary>
        private void DoPauseProtection()
        {
            if (!_state.Installed) return;
            if (!Confirm("关闭保护（暂停还原）",
                "第 1 步：关闭 UWF 保护。\r\n\r\n此操作在重启后生效：重启后系统暂时不再还原，所有改动都会直接写入硬盘，" +
                "这时才能把软件真正装进去。\r\n\r\n安装完成后必须执行第 4 步恢复保护，否则这台电脑会一直处于不还原状态。\r\n\r\n确定继续？")) return;

            Cursor = Cursors.WaitCursor;
            UwfResult r;
            try { r = Uwf.DisableProtection(); }
            finally { Cursor = Cursors.Default; }

            if (!r.Ok)
            {
                Log.Error("关闭保护失败：" + r.All);
                MessageBox.Show(this, "关闭保护失败：\r\n\r\n" + (string.IsNullOrEmpty(r.All) ? r.Command : r.All),
                                "失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                ReloadState(false);
                return;
            }

            Log.Info("已提交关闭保护（暂停还原），等待重启。");
            if (MessageBox.Show(this,
                "已排队，重启后生效。\r\n\r\n现在立即重启吗？\r\n（重启后回到本工具，状态会显示『UWF 保护已关闭』，那时再安装软件。）",
                "关闭保护", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
            {
                UwfResult rr = Uwf.RestartNow();
                if (!rr.Ok)
                    MessageBox.Show(this, "重启命令没有成功，请手动重启。\r\n\r\n" + rr.All, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            ReloadState(false);
        }

        /// <summary>第 4 步：重新开启 UWF 保护（filter enable）。</summary>
        private void DoResumeProtection()
        {
            if (!_state.Installed) return;
            if (!Confirm("恢复保护",
                "第 4 步：重新开启 UWF 还原。\r\n\r\n重启后系统恢复还原，学生之后的改动会丢失；本次安装的软件会保留。\r\n\r\n确定继续？")) return;

            Cursor = Cursors.WaitCursor;
            UwfResult r;
            try { r = Uwf.EnableProtection(); }
            finally { Cursor = Cursors.Default; }

            if (!r.Ok)
            {
                Log.Error("恢复保护失败：" + r.All);
                MessageBox.Show(this, "恢复保护失败：\r\n\r\n" + (string.IsNullOrEmpty(r.All) ? r.Command : r.All),
                                "失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                ReloadState(false);
                return;
            }

            Log.Info("已提交恢复保护，等待重启。");
            if (MessageBox.Show(this,
                "已排队，重启后恢复还原。\r\n\r\n现在立即重启吗？",
                "恢复保护", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
            {
                UwfResult rr = Uwf.RestartNow();
                if (!rr.Ok)
                    MessageBox.Show(this, "重启命令没有成功，请手动重启。\r\n\r\n" + rr.All, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            ReloadState(false);
        }

        /// <summary>进入维修模式（uwfmgr servicing enable）：微软留给 Windows 更新的模式，进入后本机自动重启。</summary>
        private void DoEnterServicingMode()
        {
            if (!_state.Installed) return;
            if (_state.ServicingCurrent)
            {
                MessageBox.Show(this, "这台电脑现在就在维修模式里。\r\n\r\nWindows 更新全部装完后，再点「退出维修模式」。",
                                "已经在维修模式", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!WarnDialog.Show(this, "进入维修模式（仅 Windows 更新用）",
                "本机将进入维修模式并自动重启",
                "维修模式是微软专门留给 Windows 更新的模式：进去后本机会重启并自动检查更新，期间 UWF 不还原。\r\n\r\n" +
                "**维修模式必须等这台机器（以及机房里所有电脑）的 Windows 更新全部安装完成，才能退出。** " +
                "更新装到一半就退出、断电或强制重启，会停在“正在配置更新”，甚至黑屏进不了桌面。\r\n\r\n" +
                "**装软件、改设置不要用它** —— 请用上面的「关闭保护」，或远程安装向导的「暂停还原」。\r\n\r\n" +
                "**日常请保持 UWF 保护开启，一般不要进入维修模式。**",
                "我已确认现在可以开始装 Windows 更新，并且不会中途退出维修模式",
                "确认进入维修模式")) return;

            Cursor = Cursors.WaitCursor;
            UwfResult r;
            try { r = Uwf.EnterServicing(); }
            finally { Cursor = Cursors.Default; }

            if (!r.Ok)
            {
                Log.Error("进入维修模式失败：" + r.All);
                MessageBox.Show(this, "进入维修模式失败：\r\n\r\n" + (string.IsNullOrEmpty(r.All) ? r.Command : r.All),
                                "失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                ReloadState(false);
                return;
            }

            Log.Info("已进入维修模式（仅 Windows 更新用），本机将自动重启。");
            MessageBox.Show(this,
                "已进入维修模式，本机会自动重启并开始检查更新。\r\n\r\n" +
                "更新没装完之前不要退出维修模式，也不要断电；全部装完后再回来点「退出维修模式」。",
                "维修模式", MessageBoxButtons.OK, MessageBoxIcon.Information);
            ReloadState(false);
        }

        /// <summary>退出维修模式（uwfmgr servicing disable）：必须等更新全部装完。</summary>
        private void DoLeaveServicingMode()
        {
            if (!_state.Installed) return;
            if (!WarnDialog.Show(this, "退出维修模式",
                "本机将退出维修模式并重启",
                "**退出前必须确认这台机器（以及机房里所有电脑）的 Windows 更新已经全部装完。** " +
                "更新没装完就退出并重启，会停在“正在配置更新”，甚至黑屏。\r\n\r\n" +
                "退出后要重启才回到正常保护：系统会先把更新配置完，再恢复 UWF 还原。\r\n\r\n" +
                "**重启过程中不要断电、不要反复重启**，耐心等它自己回到桌面。",
                "我已确认更新已经全部装完",
                "确认退出维修模式")) return;

            Cursor = Cursors.WaitCursor;
            UwfResult r;
            try { r = Uwf.LeaveServicing(); }
            finally { Cursor = Cursors.Default; }

            if (!r.Ok)
            {
                Log.Error("退出维修模式失败：" + r.All);
                MessageBox.Show(this, "退出维修模式失败：\r\n\r\n" + (string.IsNullOrEmpty(r.All) ? r.Command : r.All),
                                "失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                ReloadState(false);
                return;
            }

            Log.Info("已退出维修模式，等待重启恢复保护。");
            MessageBox.Show(this,
                "已退出维修模式，本机会重启并恢复 UWF 保护。\r\n\r\n" +
                "重启过程中不要断电、不要反复重启；如果卡在“正在配置更新”，耐心等它自己完成。",
                "退出维修模式", MessageBoxButtons.OK, MessageBoxIcon.Information);
            ReloadState(false);
        }

        private void DoRunInstaller()
        {
            OpenFileDialog dlg = new OpenFileDialog();
            dlg.Title = "选择要安装的程序";
            dlg.Filter = "安装程序|*.exe;*.msi;*.bat;*.cmd|所有文件|*.*";
            if (dlg.ShowDialog(this) != DialogResult.OK) return;

            if (_state.ProtectedNow)
            {
                if (!Confirm("当前仍在还原状态",
                    "系统现在还在保护中，直接安装的软件重启后会消失。\r\n\r\n仍然要运行吗？（建议先完成第 1、2 步）")) return;
            }

            Log.Info("开始运行安装程序：" + dlg.FileName);
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                if (dlg.FileName.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
                {
                    psi.FileName = "msiexec.exe";
                    psi.Arguments = "/i \"" + dlg.FileName + "\"";
                }
                else
                {
                    psi.FileName = dlg.FileName;
                    psi.WorkingDirectory = Path.GetDirectoryName(dlg.FileName);
                }
                psi.UseShellExecute = true;
                using (Process.Start(psi)) { }
                MessageBox.Show(this,
                    "安装程序已启动，请按平时的方式完成安装。\r\n\r\n" +
                    "安装结束后回到本页，执行第 4 步『恢复保护并重启』。\r\n" +
                    "别忘了：这一步不做，这台电脑会一直不还原。",
                    "安装进行中", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                Log.Error("运行安装程序失败：" + ex.Message);
                MessageBox.Show(this, "无法启动安装程序：\r\n" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            ReloadState(false);
        }
    }
}
