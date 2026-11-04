using System;
using System.Drawing;
using System.Windows.Forms;

namespace UwfTeacher
{
    /// <summary>切换缓存生效类型（内存 RAM ↔ 磁盘 DISK）。
    /// 大小和阈值不在这里改——主页面上两种类型都能整套设置，这里只负责“切到哪一种”。</summary>
    public class OverlaySwitchDialog : Form
    {
        private RadioButton _ram;
        private RadioButton _disk;

        /// <summary>用户选的目标类型：0 = 内存 RAM，1 = 磁盘 DISK。</summary>
        public int TargetType = -1;

        public OverlaySwitchDialog(int activeType,
                                   int ramSize, int ramWarn, int ramCrit,
                                   int diskSize, int diskWarn, int diskCrit)
        {
            Text = "切换缓存生效类型";
            Font = Ui.F9;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(540, 268);
            BackColor = Ui.CardBg;

            Label title = Ui.L("切换缓存生效类型", Ui.F12B, Ui.TextColor);
            title.Location = new Point(20, 14);
            Controls.Add(title);

            Label cur = Ui.Wrap("当前生效：" + (activeType == 0 ? "内存（RAM）" : "磁盘（DISK）") +
                                "。选择要切换到的类型，重启后生效。", Ui.F9, Ui.Muted);
            cur.Location = new Point(20, 44);
            cur.Size = new Size(500, 20);
            Controls.Add(cur);

            _ram = new RadioButton();
            _ram.Font = Ui.F9;
            _ram.AutoSize = true;
            _ram.Text = "内存（RAM）—— 上限 " + ramSize + " MB · 警告 " + ramWarn + " · 严重 " + ramCrit;
            _ram.Location = new Point(24, 84);
            _ram.Checked = activeType != 0;
            Controls.Add(_ram);

            _disk = new RadioButton();
            _disk.Font = Ui.F9;
            _disk.AutoSize = true;
            _disk.Text = "磁盘（DISK）—— 上限 " + diskSize + " MB · 警告 " + diskWarn + " · 严重 " + diskCrit;
            _disk.Location = new Point(24, 116);
            _disk.Checked = activeType == 0;
            Controls.Add(_disk);

            Label note = Ui.Wrap(
                "说明：① 只负责切换类型，数值在「缓存（Overlay）设置」里改——两种类型都能整套设置；" +
                "② 切换用的是该类型当前配置的值；③ 从内存切到磁盘前，系统要求先『关闭保护』并重启；" +
                "④ 切换完成后，记得回『本机状态』重新『启用保护』。", Ui.F85, Ui.Muted);
            note.Location = new Point(20, 150);
            note.Size = new Size(500, 56);
            Controls.Add(note);

            Button ok = Ui.B("切换到所选类型", true);
            ok.Location = new Point(278, 218);
            ok.Width = 150;
            ok.Height = 34;
            ok.Click += delegate
            {
                if (!_ram.Checked && !_disk.Checked)
                {
                    MessageBox.Show(this, "请先选择要切换到的类型。");
                    return;
                }
                TargetType = _ram.Checked ? 0 : 1;
                DialogResult = DialogResult.OK;
                Close();
            };
            Controls.Add(ok);

            Button cancel = Ui.B("取消");
            cancel.Location = new Point(436, 218);
            cancel.Width = 84;
            cancel.Height = 34;
            cancel.DialogResult = DialogResult.Cancel;
            Controls.Add(cancel);
            CancelButton = cancel;
            AcceptButton = ok;
        }
    }
}
