using System;
using System.Drawing;
using System.Windows.Forms;

namespace UwfTeacher
{
    /// <summary>
    /// 风险提示框：正文支持 **加粗** 标记，用来突出“必须 / 不要 / 会黑屏”这类关键句。
    /// 维修模式必须用真加粗提醒，普通的 MessageBox 只能整段一种字重，老师容易一带而过。
    /// </summary>
    public class WarnDialog : Form
    {
        private readonly RichTextBox _body;
        private readonly CheckBox _ack;
        private readonly bool _requireAck;
        private readonly Font _boldFont;

        private WarnDialog(string title, string headline, string body, string ackText, string okText, bool danger)
        {
            Text = title;
            Font = Ui.F9;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            BackColor = Ui.CardBg;
            Padding = new Padding(0);

            _boldFont = new Font(Ui.F9, FontStyle.Bold);

            const int pad = 24;
            const int bodyWidth = 600;

            // 顶部：警示图标 + 结论
            Panel icon = new Panel();
            icon.Location = new Point(pad, pad);
            icon.Size = new Size(34, 34);
            icon.Paint += delegate(object s, PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                Ui.FillRound(g, new Rectangle(0, 0, 33, 33), 9, danger ? Ui.SoftRed : Ui.SoftAmber);
                Color fg = danger ? Ui.Red : Ui.Amber;
                using (Pen pen = new Pen(fg, 2.4f))
                {
                    pen.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                    pen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                    g.DrawLine(pen, 16.5f, 9f, 16.5f, 19f);
                    g.DrawEllipse(pen, 15.4f, 23.2f, 2.4f, 2.4f);
                }
            };
            Controls.Add(icon);

            Label head = Wrap(headline, Ui.F10B, danger ? Ui.Red : Ui.TextColor);
            head.Location = new Point(pad + 46, pad + 6);
            head.Size = new Size(bodyWidth - 46, 24);
            Controls.Add(head);

            // 正文（**...** 加粗）
            // 加粗的字更宽，按常规字重测量会偏小；多留一行余量，避免最后一行被裁掉
            Size need = TextRenderer.MeasureText(
                StripMarkup(body), Ui.F9, new Size(bodyWidth - 4, int.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.TextBoxControl);
            int bodyHeight = Math.Min(430, Math.Max(60, need.Height + 26));

            _body = new RichTextBox();
            _body.BorderStyle = BorderStyle.None;
            _body.ReadOnly = true;
            _body.BackColor = Ui.CardBg;
            _body.ForeColor = Ui.TextColor;
            _body.Font = Ui.F9;
            _body.ScrollBars = need.Height + 12 > 430 ? RichTextBoxScrollBars.Vertical : RichTextBoxScrollBars.None;
            _body.Location = new Point(pad, pad + 50);
            _body.Size = new Size(bodyWidth, bodyHeight);
            _body.TabStop = false;
            _body.ShortcutsEnabled = false;
            _body.DetectUrls = false;
            AppendMarkup(_body, body);
            Controls.Add(_body);

            int y = pad + 50 + bodyHeight + 10;

            _requireAck = !string.IsNullOrEmpty(ackText);
            _ack = new CheckBox();
            _ack.Text = ackText ?? "";
            _ack.Font = Ui.F9;
            _ack.ForeColor = Ui.TextColor;
            _ack.AutoSize = false;
            _ack.Location = new Point(pad, y);
            _ack.Size = new Size(bodyWidth, 44);
            _ack.Visible = _requireAck;
            if (_requireAck) y += 48;
            Controls.Add(_ack);

            // 底部按钮
            Button cancel = Ui.B("取消");
            cancel.Size = new Size(92, 36);
            cancel.Location = new Point(pad + bodyWidth - 212, y + 8);
            cancel.DialogResult = DialogResult.Cancel;
            Controls.Add(cancel);

            Button ok = danger ? Ui.Danger(okText) : Ui.B(okText, true);
            ok.Size = new Size(112, 36);
            ok.Location = new Point(pad + bodyWidth - 112, y + 8);
            ok.Click += delegate
            {
                if (_requireAck && !_ack.Checked)
                {
                    MessageBox.Show(this, "请先勾选下面的确认项，再做这个操作。", "还需要确认", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    _ack.Focus();
                    return;
                }
                DialogResult = DialogResult.OK;
                Close();
            };
            Controls.Add(ok);

            CancelButton = cancel;
            ClientSize = new Size(pad * 2 + bodyWidth, y + 8 + 36 + 18);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            FitBodyToText();
            FitAckToText();
        }

        /// <summary>确认项文字在高 DPI 下会占两行，勾选框高度要跟着长，否则第二行被裁掉。</summary>
        private void FitAckToText()
        {
            if (!_requireAck || _ack == null || _ack.IsDisposed) return;
            Size need = TextRenderer.MeasureText(_ack.Text, _ack.Font, new Size(_ack.Width - 24, int.MaxValue),
                                                 TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
            int want = Math.Max(44, need.Height + 12);
            int delta = want - _ack.Height;
            if (delta <= 0) return;
            SuspendLayout();
            _ack.Height = want;
            foreach (Control c in Controls)
            {
                if (c != _ack && c.Top > _ack.Top) c.Top += delta;
            }
            ClientSize = new Size(ClientSize.Width, ClientSize.Height + delta);
            ResumeLayout(true);
        }

        /// <summary>
        /// 按 RichTextBox 的真实排版结果反过来调整高度。
        /// 预先用 TextRenderer 量出来的高度在 125% / 150% 缩放下会偏小，正文最后一行会被裁掉；
        /// 这里以控件自己排出来的最后一个字符位置为准，不够就长高、把下面的确认框和按钮一起下移。
        /// </summary>
        private void FitBodyToText()
        {
            if (_body == null || _body.IsDisposed) return;
            int chars = _body.TextLength;
            if (chars == 0) return;

            Point last = _body.GetPositionFromCharIndex(chars - 1);
            int lineHeight = (int)Math.Ceiling(_body.Font.GetHeight()) + 4;
            int need = last.Y + lineHeight + 10;
            if (need <= _body.Height) return;

            const int maxBody = 440;
            if (need > maxBody)
            {
                _body.ScrollBars = RichTextBoxScrollBars.Vertical;
                need = maxBody;
            }

            int delta = need - _body.Height;
            SuspendLayout();
            _body.Height = need;
            foreach (Control c in Controls)
            {
                if (c != _body && c.Top > _body.Top) c.Top += delta;
            }
            ClientSize = new Size(ClientSize.Width, ClientSize.Height + delta);
            ResumeLayout(true);
        }

        private static Label Wrap(string text, Font font, Color color)
        {
            Label l = new Label();
            l.Text = text;
            l.Font = font;
            l.ForeColor = color;
            l.BackColor = Color.Transparent;
            l.AutoSize = false;
            l.TextAlign = ContentAlignment.MiddleLeft;
            return l;
        }

        private static string StripMarkup(string text)
        {
            return (text ?? "").Replace("**", "");
        }

        private void AppendMarkup(RichTextBox box, string text)
        {
            box.Text = "";
            if (string.IsNullOrEmpty(text)) return;
            Font normal = Ui.F9;
            int i = 0;
            while (i < text.Length)
            {
                int start = text.IndexOf("**", i, StringComparison.Ordinal);
                if (start < 0)
                {
                    AppendRun(box, text.Substring(i), normal);
                    break;
                }
                if (start > i) AppendRun(box, text.Substring(i, start - i), normal);
                int end = text.IndexOf("**", start + 2, StringComparison.Ordinal);
                if (end < 0)
                {
                    AppendRun(box, text.Substring(start), normal);
                    break;
                }
                AppendRun(box, text.Substring(start + 2, end - start - 2), _boldFont);
                i = end + 2;
            }
            box.SelectionStart = 0;
            box.SelectionLength = 0;
        }

        private void AppendRun(RichTextBox box, string s, Font font)
        {
            if (string.IsNullOrEmpty(s)) return;
            box.SelectionStart = box.TextLength;
            box.SelectionLength = 0;
            box.SelectionFont = font;
            box.AppendText(s);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _boldFont != null) _boldFont.Dispose();
            base.Dispose(disposing);
        }

        /// <summary>
        /// 弹出风险提示。正文里用 **文字** 表示加粗；ackText 不为空时要求勾选确认。
        /// 返回 true 表示用户确认继续。
        /// </summary>
        public static bool Show(IWin32Window owner, string title, string headline, string body,
                                string ackText, string okText, bool danger)
        {
            using (WarnDialog dlg = new WarnDialog(title, headline, body, ackText, okText, danger))
            {
                return dlg.ShowDialog(owner) == DialogResult.OK;
            }
        }

        public static bool Show(IWin32Window owner, string title, string headline, string body,
                                string ackText, string okText)
        {
            return Show(owner, title, headline, body, ackText, okText, true);
        }

#if DEMO
        /// <summary>开发用：只造一个实例用于截图，不显示也不阻塞。</summary>
        public static WarnDialog CreatePreview(string title, string headline, string body)
        {
            return new WarnDialog(title, headline, body, "我已确认所有电脑的 Windows 更新已经全部安装完成", "确认", true);
        }

        /// <summary>开发用：检查正文最后一行有没有落在可视区内（防止加粗换行后被裁掉）。</summary>
        public string PreviewDiagnostics()
        {
            int chars = _body.TextLength;
            if (chars == 0) return "body is EMPTY";
            Point last = _body.GetPositionFromCharIndex(chars - 1);
            int bottom = last.Y + (int)_body.Font.GetHeight() + 2;
            return "chars=" + chars + " lastLineBottom=" + bottom + " bodyH=" + _body.ClientSize.Height +
                   (bottom > _body.ClientSize.Height ? "  <-- CLIPPED" : "  OK") +
                   "  formH=" + Height;
        }
#endif
    }
}
