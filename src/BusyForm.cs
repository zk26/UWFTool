using System;
using System.Drawing;
using System.Windows.Forms;

namespace UwfTeacher
{
    /// <summary>
    /// 等待遮罩：模态、无关闭按钮，把父窗口的所有点击都挡住。
    /// 远程操作被连点会在教师端和学生机上堆并发（严重的会卡死），
    /// 所以统一用这个窗口顶住界面，代替原先底部那根容易被忽略的细进度条。
    /// </summary>
    public class BusyForm : Form
    {
        /// <summary>自绘的细进度条：系统 ProgressBar 在开了视觉样式后改不了颜色，只能自己画。</summary>
        private class ThinBar : Control
        {
            public int Max = 1;
            public int Val;

            public ThinBar()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                System.Drawing.Drawing2D.SmoothingMode old = e.Graphics.SmoothingMode;
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                e.Graphics.Clear(BackColor);
                int r = Height / 2;
                // 背景轨道
                using (System.Drawing.Drawing2D.GraphicsPath track = Ui.RoundRect(new Rectangle(0, 0, Width - 1, Height - 1), r))
                using (SolidBrush tb = new SolidBrush(Ui.SoftBlue))
                {
                    e.Graphics.FillPath(tb, track);
                }
                // 填充进度
                int w = Max <= 0 ? 0 : (int)Math.Round((double)Width * Math.Max(0, Math.Min(Val, Max)) / Max);
                if (w > r * 2)
                {
                    using (System.Drawing.Drawing2D.GraphicsPath fill = Ui.RoundRect(new Rectangle(0, 0, w, Height), r))
                    using (SolidBrush b = new SolidBrush(Ui.Accent))
                    {
                        e.Graphics.FillPath(b, fill);
                    }
                }
                e.Graphics.SmoothingMode = old;
            }
        }

        private readonly Label _title;
        private readonly Label _text;
        private readonly ThinBar _bar;
        private readonly Label _count;
        private readonly Button _cancel;

        private bool _allowClose;
        private bool _closeRequested;

        // 后台任务可能在 ShowDialog 之前就开始报进度（那时句柄还没建），先缓存起来
        private bool _hasPending;
        private int _pendingDone;
        private int _pendingTotal;
        private string _pendingText;

        public BusyForm(string title, string text, int total, Action onCancel)
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            ControlBox = false;
            Text = title;          // 无边框不显示标题，留给任务切换与排查用
            BackColor = Ui.CardBg;
            ClientSize = new Size(460, 188);
            Font = Ui.F9;

            Panel accent = new Panel();
            accent.Dock = DockStyle.Top;
            accent.Height = 4;
            accent.BackColor = Ui.Accent;
            Controls.Add(accent);

            _title = Ui.L(title, Ui.F10B, Ui.TextColor);
            _title.Location = new Point(24, 22);
            _title.Size = new Size(412, 24);
            Controls.Add(_title);

            // 三行高度：复制安装包时要显示“312 / 698 MB (45%)、4.2 MB/s、约剩 1 分 12 秒”这类细节
            _text = Ui.Wrap(text, Ui.F9, Ui.Muted);
            _text.Location = new Point(24, 50);
            _text.Size = new Size(412, 58);
            Controls.Add(_text);

            _bar = new ThinBar();
            _bar.Location = new Point(24, 118);
            _bar.Size = new Size(296, 10);
            _bar.Max = Math.Max(1, total);
            _bar.Val = 0;
            _bar.BackColor = Ui.SoftBlue;
            Controls.Add(_bar);

            _count = Ui.L(total > 0 ? "0 / " + total + " 台" : "", Ui.F85, Ui.Muted);
            _count.Location = new Point(328, 111);
            _count.Size = new Size(108, 22);
            Controls.Add(_count);

            Label tip = Ui.L("正在执行，请等待；期间无法操作其他功能。", Ui.F85, Ui.MutedLight);
            tip.Location = new Point(24, 146);
            tip.Size = new Size(300, 22);
            Controls.Add(tip);

            if (onCancel != null)
            {
                _cancel = Ui.B("取消");
                _cancel.Size = new Size(92, 36);
                _cancel.Location = new Point(340, 138);
                _cancel.Click += delegate
                {
                    _cancel.Enabled = false;
                    _cancel.Text = "正在取消…";
                    _text.Text = "已请求取消，正在结束学生机上已经发出的操作…";
                    try { onCancel(); } catch { }
                    // 立刻放行界面：后台任务收尾自己继续，别让老师盯着遮罩等它慢慢退
                    CloseFromOwner();
                };
                Controls.Add(_cancel);
            }

            Paint += delegate(object s, PaintEventArgs e)
            {
                using (Pen p = new Pen(Ui.BorderStrong))
                    e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            };
        }

        /// <summary>更新进度；total 小于等于 0 时只刷新说明文字。</summary>
        public void SetProgress(int done, int total, string text)
        {
            if (IsDisposed || Disposing) return;
            if (!IsHandleCreated)
            {
                _pendingDone = done;
                _pendingTotal = total;
                if (text != null) _pendingText = text;
                _hasPending = true;
                return;
            }
            RunOnUi(delegate { ApplyProgress(done, total, text); });
        }

        private void ApplyProgress(int done, int total, string text)
        {
            if (text != null) _text.Text = text;
            if (total > 0)
            {
                _bar.Max = Math.Max(1, total);
                _bar.Val = Math.Max(0, Math.Min(done, total));
                _bar.Invalidate();
                _count.Text = done + " / " + total + " 台";
            }
        }

        /// <summary>任务结束时由调用方关闭；未真正显示时会记下来，等显示后立即关闭。</summary>
        public void CloseFromOwner()
        {
            _allowClose = true;
            try
            {
                if (IsDisposed || !IsHandleCreated || !Visible) { _closeRequested = true; return; }
                Close();
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (_hasPending)
            {
                _hasPending = false;
                ApplyProgress(_pendingDone, _pendingTotal, _pendingText);
            }
            if (_closeRequested)
            {
                _allowClose = true;
                try { BeginInvoke((MethodInvoker)delegate { Close(); }); } catch (InvalidOperationException) { }
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // 任务没结束时不允许用户关掉：Alt+F4、任务栏关闭都会走这里
            if (!_allowClose) { e.Cancel = true; return; }
            base.OnFormClosing(e);
        }

        private void RunOnUi(Action action)
        {
            if (IsDisposed || Disposing) return;
            if (InvokeRequired)
            {
                try { BeginInvoke(action); } catch (InvalidOperationException) { }
                return;
            }
            action();
        }
    }
}
