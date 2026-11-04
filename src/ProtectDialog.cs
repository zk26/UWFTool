using System;
using System.Drawing;
using System.Windows.Forms;

namespace UwfTeacher
{
    /// <summary>启用保护时同时设置“保护层”（UWF 缓存）：生效类型用单选，两种类型各自整套参数（上限、警告、严重阈值）。</summary>
    public class ProtectDialog : Form
    {
        private RadioButton _ramRadio;
        private RadioButton _diskRadio;
        private NumericUpDown _ramSize;
        private NumericUpDown _ramWarn;
        private NumericUpDown _ramCrit;
        private NumericUpDown _diskSize;
        private NumericUpDown _diskWarn;
        private NumericUpDown _diskCrit;
        private CheckBox _enable;
        private bool _protectedNow;

        public bool WantApplyOverlay;
        public bool WantEnableProtection;
        /// <summary>过滤器已开但没有磁盘受保护时，把系统盘加入保护列表。</summary>
        public bool WantProtectVolume;
        /// <summary>生效类型：0 = 内存 RAM，1 = 磁盘 DISK。</summary>
        public int ActiveType;
        public int RamSizeMb;
        public int RamWarnMb;
        public int RamCritMb;
        public int DiskSizeMb;
        public int DiskWarnMb;
        public int DiskCritMb;

        public ProtectDialog(UwfState state, AppSettings settings)
        {
            Text = "启用保护 · 设置保护层";
            Font = Ui.F9;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(580, 392);
            BackColor = Ui.CardBg;

            AppSettings cfg = settings ?? new AppSettings();
            bool installed = state != null && state.Installed;
            bool protectedNow = installed && state.ProtectedNow;
            bool typeKnown = installed && state.OverlayType >= 0 && state.OverlayType < 2;
            _protectedNow = protectedNow;

            Label title = Ui.L(protectedNow ? "保护层（缓存）设置" : "启用保护 · 设置保护层", Ui.F12B, Ui.TextColor);
            title.Location = new Point(20, 14);
            Controls.Add(title);

            string current = !installed
                ? "本机没有检测到 UWF 组件，下面的设置无法应用。"
                : "当前状态：" + (state.ProtectedNow ? "保护已开启" : (state.InServicing ? "维修模式" : "保护已关闭")) +
                  "    生效类型：" + (typeKnown ? (state.OverlayType == 0 ? "内存 RAM" : "磁盘 DISK") : "未知") +
                  "    上限：" + (state.OverlayMaxMb > 0 ? state.OverlayMaxMb + " MB" : "未知") +
                  "    已用：" + (state.OverlayUsedMb >= 0 ? state.OverlayUsedMb + " MB" : "未知");
            Label cur = Ui.Wrap(current, Ui.F9, Ui.Muted);
            cur.Location = new Point(20, 44);
            cur.Size = new Size(544, 22);
            Controls.Add(cur);

            Label desc = Ui.Wrap("“保护层”就是 UWF 的缓存：学生写的内容先放在这里，重启时丢弃。上限太小会写满（变慢、报错），" +
                                 "机房建议磁盘缓存 8192 MB 以上。内存与磁盘各自整套设置，互不影响。", Ui.F85, Ui.Muted);
            desc.Location = new Point(20, 70);
            desc.Size = new Size(544, 36);
            Controls.Add(desc);

            // 生效类型：单选，不再和大小纠缠在一个下拉里
            Label lt = Ui.L("生效类型", Ui.F9, Ui.TextColor);
            lt.Location = new Point(20, 110);
            Controls.Add(lt);
            _ramRadio = new RadioButton();
            _ramRadio.Font = Ui.F9;
            _ramRadio.Text = "内存（RAM）";
            _ramRadio.AutoSize = true;
            _ramRadio.Location = new Point(20, 132);
            _ramRadio.Checked = typeKnown && state.OverlayType == 0;
            Controls.Add(_ramRadio);
            _diskRadio = new RadioButton();
            _diskRadio.Font = Ui.F9;
            _diskRadio.Text = "磁盘（DISK）";
            _diskRadio.AutoSize = true;
            _diskRadio.Location = new Point(125, 132);
            _diskRadio.Checked = !typeKnown || state.OverlayType != 0;   // 读不到时给机房推荐的磁盘，由用户确认
            Controls.Add(_diskRadio);
            Label typeHint = Ui.L("切换后重启生效；内存→磁盘需先关闭保护", Ui.F85, Ui.Muted);
            typeHint.Location = new Point(245, 134);
            typeHint.Size = new Size(310, 14);
            Controls.Add(typeHint);

            // 两种类型各自整套参数：表头 + 两行输入
            Label h1 = Ui.L("上限（MB）", Ui.F9B, Ui.Muted);
            h1.Location = new Point(105, 164);
            h1.Size = new Size(125, 14);
            Controls.Add(h1);
            Label h2 = Ui.L("警告阈值（MB）", Ui.F9B, Ui.Muted);
            h2.Location = new Point(240, 164);
            h2.Size = new Size(125, 14);
            Controls.Add(h2);
            Label h3 = Ui.L("严重阈值（MB）", Ui.F9B, Ui.Muted);
            h3.Location = new Point(375, 164);
            h3.Size = new Size(125, 14);
            Controls.Add(h3);

            Label lr = Ui.L("内存（RAM）", Ui.F9, Ui.TextColor);
            lr.Location = new Point(20, 188);
            lr.Size = new Size(80, 14);
            Controls.Add(lr);
            _ramSize = MakeNum(256, 65536, 512);
            _ramSize.Location = new Point(105, 184);
            _ramSize.Value = Clamp(typeKnown && state.OverlayType == 0 && state.OverlayMaxMb > 0 ? state.OverlayMaxMb : cfg.RamSizeMb, 256, 65536);
            Controls.Add(_ramSize);
            _ramWarn = MakeNum(0, 65536, 256);
            _ramWarn.Location = new Point(240, 184);
            _ramWarn.Value = Clamp(typeKnown && state.OverlayType == 0 && state.OverlayWarnMb > 0 ? state.OverlayWarnMb : cfg.RamWarnMb, 0, 65536);
            Controls.Add(_ramWarn);
            _ramCrit = MakeNum(0, 65536, 256);
            _ramCrit.Location = new Point(375, 184);
            _ramCrit.Value = Clamp(typeKnown && state.OverlayType == 0 && state.OverlayCritMb > 0 ? state.OverlayCritMb : cfg.RamCritMb, 0, 65536);
            Controls.Add(_ramCrit);

            Label ld = Ui.L("磁盘（DISK）", Ui.F9, Ui.TextColor);
            ld.Location = new Point(20, 224);
            ld.Size = new Size(80, 14);
            Controls.Add(ld);
            _diskSize = MakeNum(1024, 65536, 512);
            _diskSize.Location = new Point(105, 220);
            _diskSize.Value = Clamp(typeKnown && state.OverlayType == 1 && state.OverlayMaxMb > 0 ? state.OverlayMaxMb : cfg.DiskSizeMb, 1024, 65536);
            Controls.Add(_diskSize);
            _diskWarn = MakeNum(0, 65536, 256);
            _diskWarn.Location = new Point(240, 220);
            _diskWarn.Value = Clamp(typeKnown && state.OverlayType == 1 && state.OverlayWarnMb > 0 ? state.OverlayWarnMb : cfg.DiskWarnMb, 0, 65536);
            Controls.Add(_diskWarn);
            _diskCrit = MakeNum(0, 65536, 256);
            _diskCrit.Location = new Point(375, 220);
            _diskCrit.Value = Clamp(typeKnown && state.OverlayType == 1 && state.OverlayCritMb > 0 ? state.OverlayCritMb : cfg.DiskCritMb, 0, 65536);
            Controls.Add(_diskCrit);

            // 过滤器开着但没有磁盘受保护 = 实际上什么都没还原，必须提示
            bool volMissing = installed && !state.HasProtectedVolume && !state.HasProtectedVolumeNext;

            _enable = new CheckBox();
            if (!protectedNow) _enable.Text = "应用后启用保护 + 保护系统盘（重启一次生效）";
            else if (volMissing) _enable.Text = "把系统盘加入保护列表（当前没有磁盘受保护！）";
            else _enable.Text = "保持保护开启（只改缓存设置）";
            _enable.Checked = true;
            _enable.Enabled = installed && (!protectedNow || volMissing);
            _enable.Font = Ui.F9;
            _enable.AutoSize = true;
            _enable.Location = new Point(20, 256);
            Controls.Add(_enable);

            Label note = Ui.Wrap("说明：① 修改缓存设置需要重启才生效；② 切换生效类型（内存↔磁盘）需要先『关闭保护』并重启一次，" +
                                 "重启后回来点应用，用的就是你给这种类型配好的一整套；③ 磁盘缓存需要在硬盘上预留同样大小的空间，" +
                                 "切到磁盘时至少 1024 MB；④ 只开过滤器不加保护列表 = 什么都不会还原，本对话框会一并把系统盘加进去。", Ui.F85, Ui.Muted);
            note.Location = new Point(20, 282);
            note.Size = new Size(544, 44);
            Controls.Add(note);

            Button ok = Ui.B(protectedNow ? "应用缓存设置" : "应用并启用保护", true);
            ok.Location = new Point(310, 336);
            ok.Width = 160;
            ok.Height = 36;
            ok.Click += delegate { Accept(installed); };
            Controls.Add(ok);

            Button only = Ui.B("只保存设置");
            only.Location = new Point(196, 336);
            only.Width = 106;
            only.Height = 36;
            only.Visible = installed && !protectedNow;
            only.Click += delegate
            {
                WantApplyOverlay = true;
                WantEnableProtection = false;
                WantProtectVolume = false;
                DialogResult = DialogResult.OK;
                Close();
            };
            Controls.Add(only);

            Button cancel = Ui.B("取消");
            cancel.Location = new Point(478, 336);
            cancel.Width = 84;
            cancel.Height = 36;
            cancel.DialogResult = DialogResult.Cancel;
            Controls.Add(cancel);
            CancelButton = cancel;
            AcceptButton = ok;
        }

        private void Accept(bool installed)
        {
            if (!installed)
            {
                MessageBox.Show(this, "本机没有可用的 UWF 组件。");
                return;
            }
            if (!_ramRadio.Checked && !_diskRadio.Checked)
            {
                MessageBox.Show(this, "请先选择生效类型（内存 RAM 或磁盘 DISK）。");
                return;
            }
            int ramSize = (int)_ramSize.Value, ramWarn = (int)_ramWarn.Value, ramCrit = (int)_ramCrit.Value;
            int diskSize = (int)_diskSize.Value, diskWarn = (int)_diskWarn.Value, diskCrit = (int)_diskCrit.Value;
            if (ramSize < 256) { MessageBox.Show(this, "内存（RAM）缓存上限至少 256 MB。"); return; }
            if (diskSize < 1024) { MessageBox.Show(this, "磁盘（DISK）缓存上限至少 1024 MB——切到磁盘缓存时，UWF 会按这个大小在系统盘上创建缓存文件。"); return; }
            if (ramWarn > ramSize || ramCrit > ramSize) { MessageBox.Show(this, "内存（RAM）的阈值不能大于它的缓存上限（" + ramSize + " MB）。"); return; }
            if (diskWarn > diskSize || diskCrit > diskSize) { MessageBox.Show(this, "磁盘（DISK）的阈值不能大于它的缓存上限（" + diskSize + " MB）。"); return; }
            if (ramWarn > 0 && ramCrit > 0 && ramCrit < ramWarn) { MessageBox.Show(this, "内存（RAM）的严重阈值不能小于警告阈值。"); return; }
            if (diskWarn > 0 && diskCrit > 0 && diskCrit < diskWarn) { MessageBox.Show(this, "磁盘（DISK）的严重阈值不能小于警告阈值。"); return; }

            ActiveType = _ramRadio.Checked ? 0 : 1;
            RamSizeMb = ramSize; RamWarnMb = ramWarn; RamCritMb = ramCrit;
            DiskSizeMb = diskSize; DiskWarnMb = diskWarn; DiskCritMb = diskCrit;
            WantApplyOverlay = true;
            WantEnableProtection = !_protectedNow && _enable.Checked;
            WantProtectVolume = _enable.Enabled && _enable.Checked;
            DialogResult = DialogResult.OK;
            Close();
        }

        private NumericUpDown MakeNum(int min, int max, int step)
        {
            NumericUpDown n = new NumericUpDown();
            n.Minimum = min;
            n.Maximum = max;
            n.Increment = step;
            n.Font = Ui.F9;
            n.Width = 125;
            return n;
        }

        private decimal Clamp(int value, int min, int max)
        {
            if (value < min) value = min;
            if (value > max) value = max;
            return value;
        }
    }
}
