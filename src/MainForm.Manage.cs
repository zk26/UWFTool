using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace UwfTeacher
{
    public partial class MainForm
    {
        private ListBox _fileList;
        private ListBox _regList;
        private TextBox _regInput;
        private CheckedListBox _volList;
        private NumericUpDown _ovlRamSize;
        private NumericUpDown _ovlRamWarn;
        private NumericUpDown _ovlRamCrit;
        private NumericUpDown _ovlDiskSize;
        private NumericUpDown _ovlDiskWarn;
        private NumericUpDown _ovlDiskCrit;
        private Label _ovlUsage;
        private Label _volHint;

        // ------------------------------------------------------------------ page 3

        private Panel BuildExclusionPage()
        {
            Panel page = new Panel();
            page.Dock = DockStyle.Fill;
            page.BackColor = Ui.Bg;

            FlowLayoutPanel stack = Ui.Stack();
            page.Controls.Add(stack);

            Card fileCard = new Card("文件 / 文件夹排除（加入后重启一次生效）", 330);
            TableLayoutPanel fl = new TableLayoutPanel();
            fl.Dock = DockStyle.Fill;
            fl.Margin = new Padding(0);
            fl.ColumnCount = 1;
            fl.RowCount = 2;
            fl.BackColor = Ui.CardBg;
            fl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            fl.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
            fileCard.Body.Controls.Add(fl);

            _fileList = new ListBox();
            _fileList.Dock = DockStyle.Fill;
            _fileList.Margin = new Padding(0);
            _fileList.Font = Ui.F9;
            _fileList.IntegralHeight = false;
            _fileList.HorizontalScrollbar = true;
            _fileList.BorderStyle = BorderStyle.FixedSingle;
            fl.Controls.Add(_fileList, 0, 0);

            FlowLayoutPanel fb = new FlowLayoutPanel();
            fb.Dock = DockStyle.Fill;
            fb.FlowDirection = FlowDirection.LeftToRight;
            fb.WrapContents = true;
            fb.Margin = new Padding(0, 8, 0, 0);
            fb.BackColor = Ui.CardBg;

            Button addDir = Ui.B("添加文件夹…", true);
            addDir.Width = 118;
            addDir.Click += delegate
            {
                FolderBrowserDialog dlg = new FolderBrowserDialog();
                dlg.Description = "选择要永久保存的文件夹";
                if (dlg.ShowDialog(this) == DialogResult.OK) AddFileExclusionInteractive(dlg.SelectedPath);
            };
            Button addFile = Ui.B("添加文件…");
            addFile.Width = 100;
            addFile.Click += delegate
            {
                OpenFileDialog dlg = new OpenFileDialog();
                dlg.Title = "选择要永久保存的文件";
                if (dlg.ShowDialog(this) == DialogResult.OK) AddFileExclusionInteractive(dlg.FileName);
            };
            Button manual = Ui.B("手动输入");
            manual.Width = 96;
            manual.Click += delegate
            {
                string s = Ui.Prompt("添加文件排除", "完整路径（可以用通配符 * ，例如 C:\\AA\\*.DAT）：", "C:\\");
                if (!string.IsNullOrEmpty(s)) AddFileExclusionInteractive(s);
            };
            Button remove = Ui.Danger("删除所选文件");
            remove.Width = 112;
            remove.Click += delegate
            {
                if (_fileList.SelectedItem == null) return;
                string path = Convert.ToString(_fileList.SelectedItem);
                if (!Confirm("删除排除", "从排除列表移除：\r\n" + path + "\r\n\r\n移除后该位置会重新受还原保护（重启后生效）。")) return;
                RunAction(delegate { return Uwf.RemoveFileExclusion(path); }, "已移除，重启后生效。");
            };
            Button refreshEx = Ui.B("刷新");
            refreshEx.Width = 76;
            refreshEx.Click += delegate { ReloadState(true); };

            fb.Controls.Add(addDir);
            fb.Controls.Add(addFile);
            fb.Controls.Add(manual);
            fb.Controls.Add(remove);
            fb.Controls.Add(refreshEx);
            fl.Controls.Add(fb, 0, 1);
            stack.Controls.Add(fileCard);

            Card regCard = new Card("注册表排除（加入后重启一次生效）", 250);
            TableLayoutPanel rl = new TableLayoutPanel();
            rl.Dock = DockStyle.Fill;
            rl.Margin = new Padding(0);
            rl.ColumnCount = 1;
            rl.RowCount = 3;
            rl.BackColor = Ui.CardBg;
            rl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            rl.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            rl.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            regCard.Body.Controls.Add(rl);

            _regList = new ListBox();
            _regList.Dock = DockStyle.Fill;
            _regList.Margin = new Padding(0);
            _regList.Font = Ui.F9;
            _regList.IntegralHeight = false;
            _regList.HorizontalScrollbar = true;
            _regList.BorderStyle = BorderStyle.FixedSingle;
            rl.Controls.Add(_regList, 0, 0);

            _regInput = new TextBox();
            Ui.StyleInput(_regInput);
            _regInput.Text = @"HKLM\SOFTWARE\";
            _regInput.Dock = DockStyle.Fill;
            _regInput.Margin = new Padding(0, 8, 0, 0);
            rl.Controls.Add(_regInput, 0, 1);

            FlowLayoutPanel rb = new FlowLayoutPanel();
            rb.Dock = DockStyle.Fill;
            rb.FlowDirection = FlowDirection.LeftToRight;
            rb.Margin = new Padding(0, 4, 0, 0);
            rb.BackColor = Ui.CardBg;

            Button regAdd = Ui.B("添加注册表排除", true);
            regAdd.Width = 140;
            regAdd.Click += delegate
            {
                string key = (_regInput.Text ?? "").Trim();
                if (!key.StartsWith("HKLM\\") && !key.StartsWith("HKCU\\"))
                {
                    MessageBox.Show(this, "注册表路径请以 HKLM\\ 或 HKCU\\ 开头，例如 HKLM\\SOFTWARE\\MyApp");
                    return;
                }
                if (!Confirm("添加注册表排除", "键：" + key + "\r\n\r\n该键下的写入将不再被还原（重启后生效）。")) return;
                RunAction(delegate { return Uwf.AddRegExclusion(key); }, "已添加，重启后生效。");
            };
            Button regDel = Ui.Danger("删除所选表项");
            regDel.Width = 112;
            regDel.Click += delegate
            {
                if (_regList.SelectedItem == null) return;
                string key = Convert.ToString(_regList.SelectedItem);
                if (!Confirm("删除注册表排除", "移除：" + key)) return;
                RunAction(delegate { return Uwf.RemoveRegExclusion(key); }, "已移除，重启后生效。");
            };
            rb.Controls.Add(regAdd);
            rb.Controls.Add(regDel);
            rl.Controls.Add(rb, 0, 2);
            stack.Controls.Add(regCard);

            Card advice = new Card("该排除什么、不该排除什么", 172);
            Label ad = Ui.Wrap(
                "√ 适合排除：学生作业目录、教学资源目录、绿色软件目录、题库 / 词库 / 缓存目录、需要长期保存的配置文件。\r\n" +
                "× 不要排除：C:\\Windows、C:\\Program Files 的整个目录、用户整个配置文件目录——这等于给小部分恶意软件留了永久落脚点。\r\n" +
                "• 想保护作业又不希望被还原，最干净的做法是给数据单独分一个区（如 D 盘），把 D 盘排除在保护之外。\r\n" +
                "• 排除项只是『不还原』，不做访问控制，学生仍然可以删改其中内容。", Ui.F9, Ui.TextColor);
            ad.Location = new Point(0, 0);
            ad.Size = new Size(1000, 112);
            advice.Body.Controls.Add(ad);
            stack.Controls.Add(advice);

            return page;
        }

        private void AddFileExclusionInteractive(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            if (!Confirm("添加文件排除",
                "路径：" + path + "\r\n\r\n加入后需要重启一次生效。该位置内的写入会永久保留。")) return;
            RunAction(delegate { return Uwf.AddFileExclusion(path); }, "已添加，请重启一次生效。");
        }

        private void UpdateExclusionPage()
        {
            if (_fileList == null) return;
            _fileList.BeginUpdate();
            _fileList.Items.Clear();
            foreach (string s in _state.FileExclusions) _fileList.Items.Add(s);
            _fileList.EndUpdate();

            _regList.BeginUpdate();
            _regList.Items.Clear();
            foreach (string s in _state.RegExclusions) _regList.Items.Add(s);
            _regList.EndUpdate();
        }

        // ------------------------------------------------------------------ page 4

        private Panel BuildVolumePage()
        {
            Panel page = new Panel();
            page.Dock = DockStyle.Fill;
            page.BackColor = Ui.Bg;

            FlowLayoutPanel stack = Ui.Stack();
            page.Controls.Add(stack);

            Card volCard = new Card("受保护的磁盘卷", 248);
            TableLayoutPanel vl = new TableLayoutPanel();
            vl.Dock = DockStyle.Fill;
            vl.Margin = new Padding(0);
            vl.ColumnCount = 1;
            vl.RowCount = 3;
            vl.BackColor = Ui.CardBg;
            vl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            vl.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            vl.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            volCard.Body.Controls.Add(vl);

            _volList = new CheckedListBox();
            _volList.Dock = DockStyle.Fill;
            _volList.Margin = new Padding(0);
            _volList.Font = Ui.F9;
            _volList.CheckOnClick = true;
            _volList.ItemCheck += delegate
            {
                if (!IsHandleCreated || IsDisposed) return;
                try { BeginInvoke((MethodInvoker)delegate { UpdateVolumeHint(); }); }
                catch (InvalidOperationException) { }
            };
            _volList.IntegralHeight = false;
            _volList.BorderStyle = BorderStyle.FixedSingle;
            vl.Controls.Add(_volList, 0, 0);

            FlowLayoutPanel vb = new FlowLayoutPanel();
            vb.Dock = DockStyle.Fill;
            vb.FlowDirection = FlowDirection.LeftToRight;
            vb.Margin = new Padding(0, 6, 0, 0);
            vb.BackColor = Ui.CardBg;

            Button apply = Ui.B("应用磁盘保护设置", true);
            apply.Width = 150;
            apply.Click += delegate { ApplyVolumeChanges(); };
            Button rescan = Ui.B("重新扫描");
            rescan.Width = 96;
            rescan.Click += delegate { ReloadState(true); };
            vb.Controls.Add(apply);
            vb.Controls.Add(rescan);
            vl.Controls.Add(vb, 0, 1);

            _volHint = Ui.Wrap("勾选 = 重启后受保护。系统盘（C:）通常必须保护；数据盘可以不保护，用来保存学生作业。", Ui.F85, Ui.Muted);
            _volHint.Dock = DockStyle.Fill;
            vl.Controls.Add(_volHint, 0, 2);
            stack.Controls.Add(volCard);

            Card ovlCard = new Card("缓存（Overlay）设置", 328);
            _ovlUsage = Ui.L("", Ui.F9, Ui.Muted);
            _ovlUsage.Location = new Point(0, 0);
            _ovlUsage.Size = new Size(900, 18);
            ovlCard.Body.Controls.Add(_ovlUsage);

            TableLayoutPanel og = new TableLayoutPanel();
            og.Location = new Point(0, 28);
            og.Size = new Size(940, 170);
            og.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            og.ColumnCount = 4;
            og.RowCount = 4;
            og.BackColor = Ui.CardBg;
            og.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
            og.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 270));
            og.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 270));
            og.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 270));
            for (int i = 0; i < 4; i++) og.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

            // 表头：两种类型共用一套列名，各自一整行，谁都不用下拉选择
            og.Controls.Add(Ui.L("", Ui.F9, Ui.TextColor), 0, 0);
            og.Controls.Add(Ui.L("上限（MB）", Ui.F9B, Ui.Muted), 1, 0);
            og.Controls.Add(Ui.L("警告阈值（MB）", Ui.F9B, Ui.Muted), 2, 0);
            og.Controls.Add(Ui.L("严重阈值（MB）", Ui.F9B, Ui.Muted), 3, 0);

            og.Controls.Add(Ui.L("内存（RAM）", Ui.F9, Ui.TextColor), 0, 1);
            _ovlRamSize = MakeNum(256, 65536, 512);
            _ovlRamSize.Value = 4096;
            og.Controls.Add(_ovlRamSize, 1, 1);
            _ovlRamWarn = MakeNum(0, 65536, 256);
            _ovlRamWarn.Value = 3072;
            og.Controls.Add(_ovlRamWarn, 2, 1);
            _ovlRamCrit = MakeNum(0, 65536, 256);
            _ovlRamCrit.Value = 3840;
            og.Controls.Add(_ovlRamCrit, 3, 1);

            og.Controls.Add(Ui.L("磁盘（DISK）", Ui.F9, Ui.TextColor), 0, 2);
            _ovlDiskSize = MakeNum(1024, 65536, 512);
            _ovlDiskSize.Value = 8192;
            og.Controls.Add(_ovlDiskSize, 1, 2);
            _ovlDiskWarn = MakeNum(0, 65536, 256);
            _ovlDiskWarn.Value = 6144;
            og.Controls.Add(_ovlDiskWarn, 2, 2);
            _ovlDiskCrit = MakeNum(0, 65536, 256);
            _ovlDiskCrit.Value = 7680;
            og.Controls.Add(_ovlDiskCrit, 3, 2);

            // 应用提交“当前生效类型”的那一套；另一种类型的参数一起记住，切换时自动套用
            Button ovlApply = Ui.B("应用缓存设置", true);
            ovlApply.Width = 140;
            ovlApply.Height = 30;
            ovlApply.Click += delegate { ApplyOverlay(); };
            og.Controls.Add(ovlApply, 0, 3);

            Button ovlSwitch = Ui.B("切换生效类型…");
            ovlSwitch.Width = 140;
            ovlSwitch.Height = 30;
            ovlSwitch.Click += delegate { SwitchOverlayType(); };
            og.Controls.Add(ovlSwitch, 1, 3);

            // 单行提示：表格行高只有 40px，放多行文字会被裁掉（自检会报“文字超出高度”）
            Label sizeHint = Ui.L("常用：4 GB = 4096、8 GB = 8192 MB（两套都配全）", Ui.F85, Ui.Muted);
            sizeHint.AutoSize = false;
            sizeHint.Size = new Size(540, 30);
            sizeHint.TextAlign = ContentAlignment.MiddleLeft;
            og.Controls.Add(sizeHint, 2, 3);
            og.SetColumnSpan(sizeHint, 2);
            ovlCard.Body.Controls.Add(og);
            ovlCard.Body.Resize += delegate { og.Width = Math.Max(600, ovlCard.Body.ClientSize.Width); };

            Label on = Ui.Wrap("说明：缓存用于存放『还没写进硬盘的改动』。内存（RAM）速度最快但占内存、断电会丢；磁盘（DISK）稳定性更好，推荐机房使用。" +
                               "两套参数分别设置、分别记住，系统当前只会用生效类型那一套——两种都配好，才不怕切错类型时另一种还是默认小值被写满。" +
                               "修改后需要重启生效；切换生效类型点旁边的『切换生效类型…』。", Ui.F85, Ui.Muted);
            on.Location = new Point(0, 206);
            on.Size = new Size(1000, 56);
            ovlCard.Body.Controls.Add(on);
            stack.Controls.Add(ovlCard);

            Card adv = new Card("高级：持久缓存（让学生作业跨重启保留）", 236);
            Label at = Ui.Wrap(
                "开启持久缓存后，学生写的内容会留在缓存里，重启后仍然存在，直到手动清空或注销。\r\n" +
                "适合需要『连续两节课接着做』的场景；不适合公共机房（上一位学生的文件会留给下一位）。\r\n" +
                "开启后请配合『清空持久缓存』在换班时复位。", Ui.F9, Ui.TextColor);
            at.Location = new Point(0, 0);
            at.Size = new Size(1000, 62);
            adv.Body.Controls.Add(at);

            Button pOn = Ui.B("开启持久缓存");
            pOn.Location = new Point(0, 68);
            pOn.Width = 130;
            pOn.Click += delegate
            {
                if (!Confirm("开启持久缓存", "开启后学生写入的内容在重启后仍会保留，直到手动清空。\r\n\r\n确定继续？")) return;
                RunAction(delegate { return Uwf.SetPersistent(true); }, "已开启持久缓存（重启后生效）。");
            };
            Button pOff = Ui.B("关闭持久缓存");
            pOff.Location = new Point(140, 68);
            pOff.Width = 130;
            pOff.Click += delegate
            {
                if (!Confirm("关闭持久缓存", "关闭后，之后的改动会在重启时丢弃（已有缓存内容可能保留到下次清空）。\r\n\r\n确定继续？")) return;
                RunAction(delegate { return Uwf.SetPersistent(false); }, "已关闭持久缓存。");
            };
            Button pClear = Ui.Danger("清空持久缓存（换班复位）");
            pClear.Location = new Point(280, 68);
            pClear.Width = 200;
            pClear.Click += delegate
            {
                if (!Confirm("清空持久缓存", "将清空缓存中保留的学生数据，重启后系统恢复到母盘状态。\r\n\r\n确定继续？")) return;
                RunAction(delegate { return Uwf.ResetPersistent(); }, "已安排清空，重启后生效。");
            };
            adv.Body.Controls.Add(pOn);
            adv.Body.Controls.Add(pOff);
            adv.Body.Controls.Add(pClear);
            stack.Controls.Add(adv);

            return page;
        }

        private NumericUpDown MakeNum(int min, int max, int step)
        {
            NumericUpDown n = new NumericUpDown();
            n.Minimum = min;
            n.Maximum = max;
            n.Increment = step;
            n.Font = Ui.F9;
            n.Width = 140;
            n.ThousandsSeparator = false;
            return n;
        }

        private void UpdateVolumePage()
        {
            if (_volList == null) return;
            _volList.BeginUpdate();
            _volList.Items.Clear();
            foreach (VolumeState v in _state.Volumes)
            {
                string pending = v.ProtectedCurrent == v.ProtectedNext
                    ? ""
                    : (v.ProtectedNext ? "   ⚠ 已排队：重启一次后开始保护" : "   ⚠ 已排队：重启一次后取消保护");
                string text = (v.Letter.Length > 0 ? v.Letter : "(无盘符)") +
                              (v.Label.Length > 0 ? "  " + v.Label : "") +
                              (v.CapacityGb > 0 ? "   " + Math.Round(v.CapacityGb) + " GB" : "") +
                              (v.ProtectedCurrent ? "   当前：已保护" : "   当前：未保护") + pending;
                _volList.Items.Add(text, v.ProtectedNext);
            }
            _volList.EndUpdate();
            _volHint.Text = _state.Volumes.Count == 0
                ? "没有读取到磁盘卷信息。可能是权限不足或 UWF 未启用。"
                : "勾选 = 重启后受保护。系统盘（C:）通常必须保护；数据盘可以不保护，用来保存学生作业。";

            UpdateVolumeHint();
            _ovlUsage.Text = "当前生效：" + (_state.OverlayType == 0 ? "内存 RAM" : (_state.OverlayType == 1 ? "磁盘 DISK" : "未知（请刷新状态）")) +
                             "    ·    当前占用 " + (_state.OverlayUsedMb >= 0 ? _state.OverlayUsedMb + " MB" : "未知") +
                             "   ·   剩余 " + (_state.OverlayFreeMb >= 0 ? _state.OverlayFreeMb + " MB" : "未知") +
                             "   ·   上限 " + (_state.OverlayMaxMb >= 0 ? _state.OverlayMaxMb + " MB" : "未知");

            // 生效类型的三个框显示机器当前值；另一种类型显示本地记住的配置（两套分别设置、分别记住）
            int active = _state.OverlayType;
            _ovlRamSize.Value = ClampNum(active == 0 && _state.OverlayMaxMb > 0 ? _state.OverlayMaxMb : _settings.RamSizeMb, _ovlRamSize);
            _ovlRamWarn.Value = ClampNum(active == 0 && _state.OverlayWarnMb > 0 ? _state.OverlayWarnMb : _settings.RamWarnMb, _ovlRamWarn);
            _ovlRamCrit.Value = ClampNum(active == 0 && _state.OverlayCritMb > 0 ? _state.OverlayCritMb : _settings.RamCritMb, _ovlRamCrit);
            _ovlDiskSize.Value = ClampNum(active == 1 && _state.OverlayMaxMb > 0 ? _state.OverlayMaxMb : _settings.DiskSizeMb, _ovlDiskSize);
            _ovlDiskWarn.Value = ClampNum(active == 1 && _state.OverlayWarnMb > 0 ? _state.OverlayWarnMb : _settings.DiskWarnMb, _ovlDiskWarn);
            _ovlDiskCrit.Value = ClampNum(active == 1 && _state.OverlayCritMb > 0 ? _state.OverlayCritMb : _settings.DiskCritMb, _ovlDiskCrit);
        }

        private bool HasPendingVolumeChanges()
        {
            if (_volList == null) return false;
            for (int i = 0; i < _volList.Items.Count && i < _state.Volumes.Count; i++)
            {
                if (_volList.GetItemChecked(i) != _state.Volumes[i].ProtectedNext) return true;
            }
            return false;
        }

        private void UpdateVolumeHint()
        {
            if (_volHint == null || _state == null) return;
            if (HasPendingVolumeChanges())
            {
                _volHint.Text = "有未应用的更改：请点上方的应用磁盘保护设置提交，然后重启才会生效。";
                _volHint.ForeColor = Ui.Amber;
            }
            else
            {
                _volHint.Text = "勾选 = 重启后受保护。系统盘（C:）必须保护；数据盘可以不保护，用来保存学生作业。改动提交后需要重启一次生效。";
                _volHint.ForeColor = Ui.Muted;
            }
        }
        private void ApplyVolumeChanges()
        {
            List<string> protect = new List<string>();
            List<string> unprotect = new List<string>();
            for (int i = 0; i < _volList.Items.Count && i < _state.Volumes.Count; i++)
            {
                bool want = _volList.GetItemChecked(i);
                VolumeState v = _state.Volumes[i];
                if (want == v.ProtectedNext) continue;
                if (want) protect.Add(v.Letter); else unprotect.Add(v.Letter);
            }

            if (protect.Count == 0 && unprotect.Count == 0)
            {
                MessageBox.Show(this, "磁盘保护设置没有变化。", "无需应用");
                return;
            }
            string sysLetter = SystemVolumeLetter();
            if (!string.IsNullOrEmpty(sysLetter) && unprotect.Contains(sysLetter))
            {
                if (!Confirm("取消 " + sysLetter + " 盘保护",
                    "取消系统盘（" + sysLetter + "）保护后，学生对系统的所有改动都会永久生效，且无法通过重启恢复。\r\n\r\n真的要继续吗？")) return;
            }
            if (!Confirm("应用磁盘保护设置",
                (protect.Count > 0 ? "将保护：" + string.Join(", ", protect.ToArray()) + "\r\n" : "") +
                (unprotect.Count > 0 ? "将取消保护：" + string.Join(", ", unprotect.ToArray()) + "\r\n" : "") +
                "\r\n需要重启一次生效。确定继续？")) return;

            StringBuilder fails = new StringBuilder();
            RunMasked(delegate
            {
                foreach (string s in protect) CheckVolumeResult("保护 " + s, Uwf.ProtectVolume(s), fails);
                foreach (string s in unprotect) CheckVolumeResult("取消保护 " + s, Uwf.UnprotectVolume(s), fails);
            });
            Log.Info("已应用磁盘保护设置：" + (protect.Count > 0 ? "保护 " + string.Join(",", protect.ToArray()) + "；" : "") +
                     (unprotect.Count > 0 ? "取消 " + string.Join(",", unprotect.ToArray()) : ""));
            ReloadState(false);
            UpdateVolumeHint();

            if (fails.Length > 0)
            {
                MessageBox.Show(this, "磁盘保护设置没有完全成功：\r\n\r\n" + fails +
                    "\r\n可以打开命令提示符（管理员）手工执行，看看系统返回什么：\r\n" +
                    "uwfmgr.exe volume protect C:\r\n\r\n再把报错发给我。", "提交失败",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (protect.Count > 0 || unprotect.Count > 0)
            {
                if (MessageBox.Show(this, "已提交，重启后生效。\r\n\r\n请用『重启』，不要用关机再开机——Windows 快速启动会跳过配置应用。\r\n\r\n现在重启吗？", "完成",
                                    MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                {
                    UwfResult r = Uwf.RestartNow();
                    if (!r.Ok)
                        MessageBox.Show(this, "重启命令没有成功，请手动重启。\r\n\r\n" + r.All, "提示",
                                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private void CheckVolumeResult(string title, UwfResult r, StringBuilder fails)
        {
            if (r.Ok) return;
            fails.AppendLine("• " + title + " → " + (string.IsNullOrEmpty(r.All) ? "失败（无输出）" : r.All) +
                             "（退出码 " + r.ExitCode + " / 0x" + r.ExitCode.ToString("X8") + "）");
        }
        private void RunActionSilent(UwfResult r)
        {
            if (!r.Ok) Log.Error("失败：" + r.Command + " → " + Ui.Short(r.All, 240));
        }

        /// <summary>
        /// 把一段“要跑 uwfmgr 命令”的代码放到后台线程，UI 上给遮罩。
        /// 每条命令 1–3 秒，几条连着跑界面会冻住：弹窗排在后面出不来、按钮没反应，就是“弹窗卡卡的”。
        /// 传进去的代码不要碰控件（读控件请在调用前、刷控件请在调用后）。
        /// </summary>
        private void RunMasked(Action body)
        {
            if (body == null) return;
            BusyForm busy = new BusyForm("正在执行", "正在提交命令到系统，请等待…", 0, null);
            Exception[] err = new Exception[1];
            System.Threading.SynchronizationContext ui = System.Threading.SynchronizationContext.Current;
            System.Threading.Tasks.Task.Factory.StartNew(delegate
            {
                try { body(); }
                catch (Exception ex) { err[0] = ex; }
            }).ContinueWith(delegate
            {
                Action done = delegate { busy.CloseFromOwner(); };
                if (ui != null) ui.Post(delegate { try { done(); } catch { } }, null);
                else try { done(); } catch { }
            });
            busy.ShowDialog(this);
            try { busy.Dispose(); } catch { }
            if (err[0] != null)
            {
                Log.Error("执行命令时异常：" + err[0].Message);
                MessageBox.Show(this, "执行命令时出错：\r\n\r\n" + err[0].Message, "出错",
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>校验一整套覆盖层参数：上限不能低于最小值，阈值不能超过上限，严重阈值不能小于警告阈值。</summary>
        private static string ValidateOverlaySet(int size, int warn, int crit, int minSize, string name)
        {
            if (size < minSize)
                return name + "缓存上限至少 " + minSize + " MB" + (minSize >= 1024 ? "——切到磁盘缓存时，UWF 会按这个大小在系统盘上创建缓存文件。" : "。");
            if (warn > size) return name + "的警告阈值不能大于它的缓存上限（" + size + " MB）。";
            if (crit > size) return name + "的严重阈值不能大于它的缓存上限（" + size + " MB）。";
            if (warn > 0 && crit > 0 && crit < warn) return name + "的严重阈值不能小于警告阈值。";
            return null;
        }

        private void ReadOverlayInputs(out int ramSize, out int ramWarn, out int ramCrit,
                                       out int diskSize, out int diskWarn, out int diskCrit)
        {
            ramSize = (int)_ovlRamSize.Value; ramWarn = (int)_ovlRamWarn.Value; ramCrit = (int)_ovlRamCrit.Value;
            diskSize = (int)_ovlDiskSize.Value; diskWarn = (int)_ovlDiskWarn.Value; diskCrit = (int)_ovlDiskCrit.Value;
        }

        /// <summary>把页面上两套参数都写进设置：切到哪种类型，就自动用哪套。</summary>
        private void RememberOverlaySettings(int ramSize, int ramWarn, int ramCrit,
                                             int diskSize, int diskWarn, int diskCrit)
        {
            if (_settings.RamSizeMb == ramSize && _settings.RamWarnMb == ramWarn && _settings.RamCritMb == ramCrit &&
                _settings.DiskSizeMb == diskSize && _settings.DiskWarnMb == diskWarn && _settings.DiskCritMb == diskCrit)
                return;
            _settings.RamSizeMb = ramSize; _settings.RamWarnMb = ramWarn; _settings.RamCritMb = ramCrit;
            _settings.DiskSizeMb = diskSize; _settings.DiskWarnMb = diskWarn; _settings.DiskCritMb = diskCrit;
            SettingsStore.Save(_settings);
        }

        private static decimal ClampNum(int value, NumericUpDown n)
        {
            return Math.Min(n.Maximum, Math.Max(n.Minimum, (decimal)value));
        }

        /// <summary>应用：提交“当前生效类型”的那一套参数，另一套一起记住。</summary>
        private void ApplyOverlay()
        {
            int activeType = _state.OverlayType;
            if (activeType != 0 && activeType != 1)
            {
                MessageBox.Show(this, "读不到当前生效的缓存类型（状态刷新可能失败了）。\r\n请先点上方「刷新状态」，再修改缓存设置。",
                                "当前状态未知");
                return;
            }
            int ramSize, ramWarn, ramCrit, diskSize, diskWarn, diskCrit;
            ReadOverlayInputs(out ramSize, out ramWarn, out ramCrit, out diskSize, out diskWarn, out diskCrit);

            // 两套都校验：就算另一种类型现在没生效，也要保证切过去时是合法值
            string err = ValidateOverlaySet(ramSize, ramWarn, ramCrit, 256, "内存（RAM）");
            if (err != null) { MessageBox.Show(this, err, "数值不正确"); return; }
            err = ValidateOverlaySet(diskSize, diskWarn, diskCrit, 1024, "磁盘（DISK）");
            if (err != null) { MessageBox.Show(this, err, "数值不正确"); return; }

            int aSize = activeType == 0 ? ramSize : diskSize;
            int aWarn = activeType == 0 ? ramWarn : diskWarn;
            int aCrit = activeType == 0 ? ramCrit : diskCrit;
            bool sizeChanged = !(_state.OverlayMaxMb > 0) || aSize != _state.OverlayMaxMb;
            bool warnChanged = !(_state.OverlayWarnMb > 0) || aWarn != _state.OverlayWarnMb;
            bool critChanged = !(_state.OverlayCritMb > 0) || aCrit != _state.OverlayCritMb;

            if (!sizeChanged && !warnChanged && !critChanged)
            {
                MessageBox.Show(this, "当前生效类型的缓存设置没有变化，不需要提交。", "无需应用", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (!Confirm("应用缓存设置",
                "当前生效：" + (activeType == 1 ? "磁盘（DISK）" : "内存（RAM）") + "\r\n" +
                "上限：" + aSize + " MB\r\n警告阈值：" + aWarn + " MB\r\n严重阈值：" + aCrit + " MB\r\n\r\n" +
                "另一种类型的参数会一起记住，切换生效类型时自动套用。\r\n需要重启一次生效。确定继续？")) return;

            StringBuilder fails = new StringBuilder();
            RunMasked(delegate
            {
                if (sizeChanged) CheckResult(Uwf.SetOverlaySize(aSize), fails);
                if (warnChanged) CheckResult(Uwf.SetOverlayWarning(aWarn), fails);
                if (critChanged) CheckResult(Uwf.SetOverlayCritical(aCrit), fails);
            });
            RememberOverlaySettings(ramSize, ramWarn, ramCrit, diskSize, diskWarn, diskCrit);
            ReloadState(false);

            if (fails.Length > 0)
            {
                Log.Error("缓存设置失败：" + fails);
                MessageBox.Show(this, "有设置没有成功：\r\n\r\n" + fails +
                    "\r\n可以把这条命令复制到管理员命令提示符里执行，看完整报错：\r\n" +
                    "uwfmgr.exe overlay set-size " + aSize, "部分失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            MessageBox.Show(this, "已提交，重启一次后生效。\r\n\r\n生效类型：" + (activeType == 1 ? "磁盘（DISK）" : "内存（RAM）") +
                "，上限 " + aSize + " MB。", "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        /// <summary>切换生效类型（内存 RAM ↔ 磁盘 DISK）：切过去的同时套用该类型配置好的一整套参数。</summary>
        private void SwitchOverlayType()
        {
            int activeType = _state.OverlayType;
            if (activeType != 0 && activeType != 1)
            {
                MessageBox.Show(this, "读不到当前生效的缓存类型（状态刷新可能失败了）。\r\n请先点上方「刷新状态」再切换。",
                                "当前状态未知");
                return;
            }
            int ramSize, ramWarn, ramCrit, diskSize, diskWarn, diskCrit;
            ReadOverlayInputs(out ramSize, out ramWarn, out ramCrit, out diskSize, out diskWarn, out diskCrit);

            using (OverlaySwitchDialog dlg = new OverlaySwitchDialog(activeType, ramSize, ramWarn, ramCrit, diskSize, diskWarn, diskCrit))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                int target = dlg.TargetType;
                if (target != 0 && target != 1) return;
                if (target == activeType)
                {
                    MessageBox.Show(this, "这就是当前生效的类型，不用切换。", "无需切换", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                string tName = target == 0 ? "内存（RAM）" : "磁盘（DISK）";
                int tSize = target == 0 ? ramSize : diskSize;
                int tWarn = target == 0 ? ramWarn : diskWarn;
                int tCrit = target == 0 ? ramCrit : diskCrit;
                string err = ValidateOverlaySet(tSize, tWarn, tCrit, target == 0 ? 256 : 1024, tName);
                if (err != null) { MessageBox.Show(this, err, "数值不正确"); return; }

                // UWF 要求当前会话关闭才能切换覆盖层类型
                if (_state.FilterCurrent)
                {
                    if (MessageBox.Show(this,
                        "切换生效类型前，UWF 要求当前会话先『关闭保护』。\r\n\r\n现在帮你关闭保护并重启吗？\r\n" +
                        "重启后回到本页，再点一次「切换生效类型…」完成切换。",
                        "需要先关闭保护", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                    RunMasked(delegate { RunActionSilent(Uwf.DisableProtection()); });
                    Log.Info("为切换覆盖层生效类型，已排队关闭保护，等待重启。");
                    ReloadState(false);
                    UwfResult rr = Uwf.RestartNow();
                    if (!rr.Ok)
                        MessageBox.Show(this, "重启命令没有成功，请手动重启。\r\n\r\n" + rr.All, "提示",
                                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (!Confirm("切换生效类型",
                    "将生效类型切换为：" + tName + "\r\n" +
                    "上限：" + tSize + " MB\r\n警告阈值：" + tWarn + " MB\r\n严重阈值：" + tCrit + " MB\r\n\r\n" +
                    "重启后生效。切换完成后记得回『本机状态』重新『启用保护』。确定继续？")) return;

                StringBuilder fails = new StringBuilder();
                RunMasked(delegate
                {
                    CheckResult(Uwf.SetOverlayType(target == 1), fails);
                    CheckResult(Uwf.SetOverlaySize(tSize), fails);
                    CheckResult(Uwf.SetOverlayWarning(tWarn), fails);
                    CheckResult(Uwf.SetOverlayCritical(tCrit), fails);
                });
                RememberOverlaySettings(ramSize, ramWarn, ramCrit, diskSize, diskWarn, diskCrit);
                ReloadState(false);

                if (fails.Length > 0)
                {
                    Log.Error("切换覆盖层生效类型失败：" + fails);
                    MessageBox.Show(this, "有设置没有成功：\r\n\r\n" + fails +
                        "\r\n提示：从内存缓存切换到磁盘缓存时，系统要求当前会话 UWF 已关闭——可以先“关闭保护”并重启，再回来切换。",
                        "部分失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (MessageBox.Show(this, "已提交切换，重启一次后生效。\r\n\r\n现在重启吗？", "完成",
                                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    UwfResult r = Uwf.RestartNow();
                    if (!r.Ok)
                        MessageBox.Show(this, "重启命令没有成功，请手动重启。\r\n\r\n" + r.All, "提示",
                                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }
    }
}
