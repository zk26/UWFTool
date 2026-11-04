using System;
using System.Drawing;
using System.Windows.Forms;

namespace UwfTeacher
{
    /// <summary>
    /// 远程批量设置“保护层”（UWF 缓存）：上限 / 警告阈值 / 严重阈值，带常用预设与当前值参考。
    /// 只改当前生效类型（内存 RAM / 磁盘 DISK）那套参数——另一套要在切到那个类型时设置，
    /// 切换类型需要先关闭保护并重启，不在本对话框里做。
    /// </summary>
    public class OverlayBatchDialog : Form
    {
        private NumericUpDown _size;
        private NumericUpDown _warn;
        private NumericUpDown _crit;

        /// <summary>确定后给出的三项（MB）。</summary>
        public int SizeMb;
        public int WarnMb;
        public int CritMb;

        public OverlayBatchDialog(int machineCount, string refName, int curMax, int curWarn, int curCrit, int curType)
        {
            Text = "批量设置保护层（缓存）";
            Font = Ui.F9;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(560, 424);
            BackColor = Ui.CardBg;

            Label title = Ui.L("批量设置保护层（UWF 缓存）", Ui.F12B, Ui.TextColor);
            title.Location = new Point(20, 14);
            Controls.Add(title);

            string cur;
            if (curMax > 0)
            {
                cur = "当前参考（" + refName + "）：" + (curType == 1 ? "磁盘 DISK" : curType == 0 ? "内存 RAM" : "类型未知") +
                      " · 上限 " + curMax + " MB · 警告 " + (curWarn > 0 ? curWarn + " MB" : "未设") +
                      " · 严重 " + (curCrit > 0 ? curCrit + " MB" : "未设");
            }
            else
            {
                cur = "当前参考：未知（先在总览点「刷新全部状态」就能带出参考值）。下面按 4 GB 预设填好，可自行改。";
            }
            Label curL = Ui.Wrap(cur + "\r\n将应用到勾选的 " + machineCount + " 台计算机。", Ui.F9, Ui.Muted);
            curL.Location = new Point(20, 44);
            curL.Size = new Size(520, 44);
            Controls.Add(curL);

            Label presetL = Ui.L("常用预设", Ui.F9B, Ui.TextColor);
            presetL.Location = new Point(20, 98);
            Controls.Add(presetL);

            string[] presetNames = { "2 GB", "4 GB", "8 GB", "16 GB" };
            int[] presetSizes = { 2048, 4096, 8192, 16384 };
            for (int i = 0; i < presetNames.Length; i++)
            {
                Button b = Ui.B(presetNames[i]);
                b.Size = new Size(76, 30);
                b.Location = new Point(112 + i * 86, 94);
                int sz = presetSizes[i];
                b.Click += delegate
                {
                    _size.Value = sz;
                    _warn.Value = WarnOf(sz);
                    _crit.Value = CritOf(sz);
                };
                Controls.Add(b);
            }

            int rowY = 142;
            Label l1 = Ui.L("上限（MB）", Ui.F9, Ui.TextColor);
            l1.Location = new Point(20, rowY + 4);
            Controls.Add(l1);
            _size = NewNum(132, rowY, curMax > 0 ? curMax : 4096);

            Label l2 = Ui.L("警告阈值（MB）", Ui.F9, Ui.TextColor);
            l2.Location = new Point(20, rowY + 40);
            Controls.Add(l2);
            _warn = NewNum(132, rowY + 36, curWarn > 0 ? curWarn : WarnOf(curMax > 0 ? curMax : 4096));

            Label l3 = Ui.L("严重阈值（MB）", Ui.F9, Ui.TextColor);
            l3.Location = new Point(20, rowY + 76);
            Controls.Add(l3);
            _crit = NewNum(132, rowY + 72, curCrit > 0 ? curCrit : CritOf(curMax > 0 ? curMax : 4096));

            Label side = Ui.Wrap(
                "上限：写入能占用的最大缓存；缓存写满后写入会失败，严重时重启会黑屏。\r\n" +
                "警告 / 严重阈值：占用达到这两个值时事件日志会提醒，方便提前清理。\r\n" +
                "要求：警告 ≤ 上限，严重 ≤ 上限，严重 ≥ 警告。",
                Ui.F85, Ui.Muted);
            side.Location = new Point(240, rowY + 2);
            side.Size = new Size(300, 108);
            Controls.Add(side);

            Label note = Ui.Wrap(
                "说明：\r\n" +
                "• 只修改「当前生效类型」（内存 RAM 或 磁盘 DISK）的保护层参数；另一套参数要切到那个类型时再设置，" +
                "切换类型需要先关闭保护并重启，本对话框不做切换。\r\n" +
                "• 改动重启后生效（和本机「磁盘与缓存」页一样）。\r\n" +
                "• 机房建议 4–8 GB；内存紧张就用磁盘 DISK 类型并配 4 GB 以上。",
                Ui.F85, Ui.Muted);
            note.Location = new Point(20, rowY + 118);
            note.Size = new Size(520, 118);
            Controls.Add(note);

            Button ok = Ui.B("确定设置");
            ok.Size = new Size(120, 32);
            ok.Location = new Point(300, 376);
            ok.Click += delegate
            {
                int size = (int)_size.Value, warn = (int)_warn.Value, crit = (int)_crit.Value;
                string err = Validate(size, warn, crit);
                if (err != null)
                {
                    MessageBox.Show(this, err, "数值不合法", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                SizeMb = size;
                WarnMb = warn;
                CritMb = crit;
                DialogResult = DialogResult.OK;
                Close();
            };
            Controls.Add(ok);

            Button cancel = Ui.B("取消");
            cancel.Size = new Size(96, 32);
            cancel.Location = new Point(432, 376);
            cancel.Click += delegate { Close(); };
            CancelButton = cancel;
            Controls.Add(cancel);
        }

        private NumericUpDown NewNum(int x, int y, int value)
        {
            NumericUpDown n = new NumericUpDown();
            n.Font = Ui.F9;
            n.ThousandsSeparator = true;
            n.Minimum = 256;
            n.Maximum = 262144;
            n.Increment = 256;
            n.Size = new Size(96, 26);
            n.Location = new Point(x, y);
            n.Value = Math.Max(n.Minimum, Math.Min(n.Maximum, value));
            Controls.Add(n);
            return n;
        }

        private static int WarnOf(int size) { return (int)(size * 0.75); }
        private static int CritOf(int size) { return (int)(size * 0.90); }

        /// <summary>合法返回 null，否则返回原因。</summary>
        private static string Validate(int size, int warn, int crit)
        {
            if (size < 256) return "上限不能小于 256 MB。";
            if (warn > size) return "警告阈值不能大于上限。";
            if (crit > size) return "严重阈值不能大于上限。";
            if (crit < warn) return "严重阈值不能小于警告阈值。";
            return null;
        }
    }
}
