using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace UwfTeacher
{
    public static class Ui
    {
        // ---------------- 色彩系统（Ant Design 品牌色 + 冷灰，亮/暗双主题） ----------------
        // 运行期可切换：调用 SetTheme(bool)。静态属性保证各处读到的是当前主题色。
        // 色值参考 Ant Design 色彩算法 + Linear/Vercel 暗色设计 token。
        private static Color _bg, _cardBg, _border, _borderStrong, _textColor, _muted, _mutedLight;
        private static Color _accent, _accentDark, _accentSoft, _green, _amber, _red;
        private static Color _sidebar, _sidebarHover, _sidebarSelected, _sidebarText;
        private static Color _softBlue, _softGreen, _softAmber, _softRed, _softGray, _inputBg;
        private static Color _tintIndigo, _tintBlue, _tintGreen, _tintAmber, _tintRed, _indigo;
        private static Color _gridHeadBg, _gridHeadFg, _gridAlt, _gridSel, _gridLine, _rawBg;

        public static Color Bg { get { return _bg; } }
        public static Color CardBg { get { return _cardBg; } }
        public static Color Border { get { return _border; } }
        public static Color BorderStrong { get { return _borderStrong; } }
        public static Color TextColor { get { return _textColor; } }
        public static Color Muted { get { return _muted; } }
        public static Color MutedLight { get { return _mutedLight; } }
        public static Color Accent { get { return _accent; } }
        public static Color AccentDark { get { return _accentDark; } }
        public static Color AccentSoft { get { return _accentSoft; } }
        public static Color Green { get { return _green; } }
        public static Color Amber { get { return _amber; } }
        public static Color Red { get { return _red; } }
        public static Color Sidebar { get { return _sidebar; } }
        public static Color SidebarHover { get { return _sidebarHover; } }
        public static Color SidebarSelected { get { return _sidebarSelected; } }
        public static Color SidebarText { get { return _sidebarText; } }
        public static Color SoftBlue { get { return _softBlue; } }
        public static Color SoftGreen { get { return _softGreen; } }
        public static Color SoftAmber { get { return _softAmber; } }
        public static Color SoftRed { get { return _softRed; } }
        public static Color SoftGray { get { return _softGray; } }
        public static Color InputBg { get { return _inputBg; } }
        public static Color TintIndigo { get { return _tintIndigo; } }
        public static Color TintBlue { get { return _tintBlue; } }
        public static Color TintGreen { get { return _tintGreen; } }
        public static Color TintAmber { get { return _tintAmber; } }
        public static Color TintRed { get { return _tintRed; } }
        public static Color Indigo { get { return _indigo; } }
        public static Color GridHeadBg { get { return _gridHeadBg; } }
        public static Color GridHeadFg { get { return _gridHeadFg; } }
        public static Color GridAlt { get { return _gridAlt; } }
        public static Color GridSel { get { return _gridSel; } }
        public static Color GridLine { get { return _gridLine; } }
        public static Color RawBg { get { return _rawBg; } }

        public static bool Dark { get; private set; }

        static Ui()
        {
            SetTheme(false);
        }

        /// <summary>切换亮/暗主题：更新色板，并把已建控件上的颜色按对应关系翻转。</summary>
        public static void SetTheme(bool dark)
        {
            SetPalette(dark);
            foreach (Form f in Application.OpenForms)
            {
                RemapColors(f);
                f.Invalidate(true);
            }
        }

        /// <summary>对指定控件树应用主题（窗口还没进 Application.OpenForms 时用它）。</summary>
        public static void ApplyTheme(Control root, bool dark)
        {
            SetPalette(dark);
            if (root != null)
            {
                RemapColors(root);
                root.Invalidate(true);
            }
        }

        /// <summary>把一次性设置的字段颜色按当前主题方向翻转（字段级颜色专用）。</summary>
        public static Color Map(Color c)
        {
            Color[][] pairs = BuildPairs();
            foreach (Color[] p in pairs)
            {
                if (Dark && p[0] == c) return p[1];
                if (!Dark && p[1] == c) return p[0];
            }
            return c;
        }

        private static void SetPalette(bool dark)
        {
            Dark = dark;
            if (dark)
            {
                _bg = C(15, 23, 42); _cardBg = C(22, 30, 46); _border = C(40, 52, 72); _borderStrong = C(58, 72, 96);
                _textColor = C(240, 244, 250); _muted = C(165, 180, 210); _mutedLight = C(130, 148, 180);
                _accent = C(59, 130, 246); _accentDark = C(37, 99, 235); _accentSoft = C(30, 44, 72);
                _green = C(52, 211, 153); _amber = C(251, 191, 36); _red = C(248, 113, 113);
                _sidebar = C(13, 20, 35); _sidebarHover = C(28, 38, 56); _sidebarSelected = C(36, 48, 70); _sidebarText = C(134, 150, 176);
                _softBlue = C(26, 40, 66); _softGreen = C(22, 48, 42); _softAmber = C(52, 42, 24); _softRed = C(56, 30, 32);
                _softGray = C(28, 36, 52); _inputBg = C(24, 32, 48);
                _tintIndigo = C(32, 42, 72); _tintBlue = C(26, 42, 72); _tintGreen = C(22, 48, 44); _tintAmber = C(52, 44, 26); _tintRed = C(56, 32, 34);
                _indigo = C(129, 140, 248);
                _gridHeadBg = C(26, 34, 52); _gridHeadFg = C(136, 152, 180); _gridAlt = C(20, 28, 44); _gridSel = C(32, 50, 78); _gridLine = C(36, 46, 64);
                _rawBg = C(26, 34, 52);
            }
            else
            {
                _bg = C(244, 246, 250); _cardBg = Color.White; _border = C(228, 232, 240); _borderStrong = C(206, 212, 224);
                _textColor = C(15, 23, 42); _muted = C(100, 116, 148); _mutedLight = C(148, 163, 198);
                _accent = C(37, 99, 235); _accentDark = C(29, 78, 216); _accentSoft = C(235, 243, 255);
                _green = C(5, 150, 105); _amber = C(217, 119, 6); _red = C(220, 38, 38);
                _sidebar = C(248, 250, 253); _sidebarHover = C(238, 242, 249); _sidebarSelected = C(230, 238, 250); _sidebarText = C(71, 85, 110);
                _softBlue = C(235, 243, 255); _softGreen = C(232, 248, 240); _softAmber = C(255, 247, 230); _softRed = C(254, 238, 238);
                _softGray = C(241, 244, 249); _inputBg = Color.White;
                _tintIndigo = C(240, 243, 255); _tintBlue = C(235, 243, 255); _tintGreen = C(232, 248, 240); _tintAmber = C(255, 248, 232); _tintRed = C(254, 240, 240);
                _indigo = C(79, 70, 229);
                _gridHeadBg = C(246, 248, 252); _gridHeadFg = C(100, 116, 148); _gridAlt = Color.White; _gridSel = C(232, 240, 254); _gridLine = C(232, 236, 244);
                _rawBg = C(246, 248, 252);
            }
        }

        private static Color C(int r, int g, int b)
        {
            return Color.FromArgb(r, g, b);
        }

        /// <summary>把控件树上已设置的旧主题颜色翻转为新主题颜色（含表格样式）。</summary>
        private static void RemapColors(Control root)
        {
            Dictionary<Color, Color> map = BuildRemapMap();
            RemapTree(root, map);
        }

        private static Color[][] BuildPairs()
        {
            return new Color[][]
            {
                new Color[] { C(244, 246, 250), C(15, 23, 42) },
                new Color[] { Color.White, C(22, 30, 46) },
                new Color[] { C(228, 232, 240), C(40, 52, 72) },
                new Color[] { C(206, 212, 224), C(58, 72, 96) },
                new Color[] { C(15, 23, 42), C(240, 244, 250) },
                new Color[] { C(100, 116, 148), C(165, 180, 210) },
                new Color[] { C(148, 163, 198), C(130, 148, 180) },
                new Color[] { C(37, 99, 235), C(59, 130, 246) },
                new Color[] { C(29, 78, 216), C(37, 99, 235) },
                new Color[] { C(235, 243, 255), C(30, 44, 72) },
                new Color[] { C(5, 150, 105), C(52, 211, 153) },
                new Color[] { C(217, 119, 6), C(251, 191, 36) },
                new Color[] { C(220, 38, 38), C(248, 113, 113) },
                new Color[] { C(248, 250, 253), C(13, 20, 35) },
                new Color[] { C(238, 242, 249), C(28, 38, 56) },
                new Color[] { C(230, 238, 250), C(36, 48, 70) },
                new Color[] { C(71, 85, 110), C(134, 150, 176) },
                new Color[] { C(235, 243, 255), C(26, 40, 66) },
                new Color[] { C(232, 248, 240), C(22, 48, 42) },
                new Color[] { C(255, 247, 230), C(52, 42, 24) },
                new Color[] { C(254, 238, 238), C(56, 30, 32) },
                new Color[] { C(241, 244, 249), C(28, 36, 52) },
                new Color[] { C(255, 255, 255), C(24, 32, 48) },
                new Color[] { C(240, 243, 255), C(32, 42, 72) },
                new Color[] { C(235, 243, 255), C(26, 42, 72) },
                new Color[] { C(255, 248, 232), C(52, 44, 26) },
                new Color[] { C(254, 240, 240), C(56, 32, 34) },
                new Color[] { C(79, 70, 229), C(129, 140, 248) },
                new Color[] { C(246, 248, 252), C(26, 34, 52) },
                new Color[] { C(100, 116, 148), C(136, 152, 180) },
                new Color[] { C(232, 248, 240), C(22, 48, 44) },
                new Color[] { C(232, 240, 254), C(32, 50, 78) },
                new Color[] { C(232, 236, 244), C(36, 46, 64) },
                new Color[] { C(246, 248, 252), C(26, 34, 52) },
                new Color[] { C(220, 228, 240), C(50, 62, 84) },
                new Color[] { C(230, 238, 252), C(34, 48, 78) },
                new Color[] { C(244, 248, 254), C(28, 38, 58) },
                new Color[] { C(249, 251, 255), C(28, 36, 54) },
            };
        }

        private static Dictionary<Color, Color> BuildRemapMap()
        {
            // 亮 → 暗，同时写入暗 → 亮，两个方向都能翻
            Color[][] pairs = BuildPairs();
            Dictionary<Color, Color> map = new Dictionary<Color, Color>();
            foreach (Color[] p in pairs)
            {
                map[p[0]] = p[1];
                map[p[1]] = p[0];
            }
            return map;
        }

        private static void RemapTree(Control c, Dictionary<Color, Color> map)
        {
            Color v;
            if (map.TryGetValue(c.BackColor, out v)) c.BackColor = v;
            if (map.TryGetValue(c.ForeColor, out v)) c.ForeColor = v;
            Pill pill = c as Pill;
            if (pill != null) pill.RemapTheme();

            Button button = c as Button;
            if (button != null && button.Tag is ButtonKind) StyleButton(button);
            CCWin.SkinControl.SkinButton sb = c as CCWin.SkinControl.SkinButton;
            if (sb != null) StyleSkin(sb);
            CCWin.CCSkinMain shell = c as CCWin.CCSkinMain;
            if (shell != null) StyleShell(shell);

            DataGridView g = c as DataGridView;
            if (g != null)
            {
                RemapStyle(g.DefaultCellStyle, map);
                RemapStyle(g.AlternatingRowsDefaultCellStyle, map);
                RemapStyle(g.ColumnHeadersDefaultCellStyle, map);
                RemapStyle(g.RowHeadersDefaultCellStyle, map);
                if (map.TryGetValue(g.GridColor, out v)) g.GridColor = v;
                if (map.TryGetValue(g.BackgroundColor, out v)) g.BackgroundColor = v;
            }
            foreach (Control child in c.Controls) RemapTree(child, map);
        }

        private static void RemapStyle(DataGridViewCellStyle s, Dictionary<Color, Color> map)
        {
            if (s == null) return;
            Color v;
            if (map.TryGetValue(s.BackColor, out v)) s.BackColor = v;
            if (map.TryGetValue(s.ForeColor, out v)) s.ForeColor = v;
            if (map.TryGetValue(s.SelectionBackColor, out v)) s.SelectionBackColor = v;
            if (map.TryGetValue(s.SelectionForeColor, out v)) s.SelectionForeColor = v;
        }

        // ---------------- 校园小草装饰（青春元素） ----------------

        /// <summary>一簇小草 + 一朵小花。</summary>
        public static void DrawGrassTuft(Graphics g, float x, float baselineY, float scale, Color leaf, Color flower)
        {
            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen leafPen = new Pen(leaf, Math.Max(1.3f, 2.1f * scale)))
            {
                leafPen.StartCap = LineCap.Round;
                leafPen.EndCap = LineCap.Round;
                float[][] blades = new float[][]
                {
                    new float[] { -17f, -11f, -25f },
                    new float[] { -8f, -2f, -33f },
                    new float[] { 2f, 7f, -36f },
                    new float[] { 11f, 15f, -30f },
                    new float[] { 19f, 23f, -22f },
                };
                foreach (float[] b in blades)
                {
                    g.DrawCurve(leafPen, new PointF[]
                    {
                        new PointF(x + b[0] * scale, baselineY),
                        new PointF(x + (b[0] + b[1]) * 0.5f * scale, baselineY + b[2] * 0.55f * scale),
                        new PointF(x + b[1] * scale, baselineY + b[2] * scale),
                    });
                }
            }
            float fx = x + 24f * scale;
            float fy = baselineY - 29f * scale;
            if (flower != Color.Empty)
            {
                using (SolidBrush petal = new SolidBrush(flower))
                {
                    for (int i = 0; i < 5; i++)
                    {
                        double a = i * Math.PI * 2.0 / 5.0 - Math.PI / 2.0;
                        float px = fx + (float)Math.Cos(a) * 5.6f * scale;
                        float py = fy + (float)Math.Sin(a) * 5.6f * scale;
                        g.FillEllipse(petal, px - 3.6f * scale, py - 3.6f * scale, 7.2f * scale, 7.2f * scale);
                    }
                }
                using (SolidBrush core = new SolidBrush(Color.FromArgb(255, 214, 112)))
                {
                    g.FillEllipse(core, fx - 3.2f * scale, fy - 3.2f * scale, 6.4f * scale, 6.4f * scale);
                }
            }
            g.SmoothingMode = old;
        }

        /// <summary>一条小草带（页面留白处的青春装饰）。</summary>
        public static void DrawGrassStrip(Graphics g, Rectangle area)
        {
            Color leaf = Dark ? C(56, 118, 84) : C(156, 208, 148);
            Color flower = Dark ? C(100, 130, 230) : Color.FromArgb(255, 190, 210);
            float step = 118f;
            int n = Math.Max(1, (int)(area.Width / step));
            for (int i = 0; i <= n; i++)
            {
                float scale = 0.82f + (i % 3) * 0.2f;
                DrawGrassTuft(g, area.X + 26 + i * step, area.Bottom - 4, scale, leaf, (i % 2 == 0) ? flower : Color.Empty);
            }
        }

        // ---------------- 间距（Ant Design 8pt 网格） ----------------
        public const int SpXs = 4;
        public const int SpSm = 8;
        public const int SpMd = 12;
        public const int SpLg = 16;
        public const int SpXl = 20;
        public const int SpXxl = 24;
        public const int Sp3xl = 32;

        // ---------------- 圆角（Ant Design 圆角体系） ----------------
        public const int RadiusCard = 12;
        public const int RadiusControl = 8;
        public const int RadiusInput = 8;
        /// <summary>页面外边距（所有页面统一）。</summary>
        public const int PagePad = 24;

        // ---------------- 字体（Ant Design 字阶系统） ----------------
        public static readonly Font F85 = new Font("Microsoft YaHei UI", 8.5f);
        public static readonly Font F9 = new Font("Microsoft YaHei UI", 9f);
        public static readonly Font F9B = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold);
        public static readonly Font F10 = new Font("Microsoft YaHei UI", 10f);
        public static readonly Font F10B = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold);
        public static readonly Font F11 = new Font("Microsoft YaHei UI", 11f);
        public static readonly Font F11B = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold);
        public static readonly Font F12B = new Font("Microsoft YaHei UI", 12f, FontStyle.Bold);
        public static readonly Font F14B = new Font("Microsoft YaHei UI", 14f, FontStyle.Bold);
        public static readonly Font F15B = new Font("Microsoft YaHei UI", 15f, FontStyle.Bold);
        public static readonly Font F19B = new Font("Microsoft YaHei UI", 19f, FontStyle.Bold);
        public static readonly Font Mono = new Font("Consolas", 9f);
        public static readonly Font MonoB = new Font("Consolas", 9f, FontStyle.Bold);

        // ---------------- 绘图基础 ----------------
        public static GraphicsPath RoundRect(Rectangle r, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            if (radius <= 1)
            {
                path.AddRectangle(r);
                return path;
            }
            int d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d < 2) d = 2;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void FillRound(Graphics g, Rectangle r, int radius, Color color)
        {
            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath p = RoundRect(r, radius))
            using (SolidBrush b = new SolidBrush(color))
            {
                g.FillPath(b, p);
            }
            g.SmoothingMode = old;
        }

        public static void DrawRound(Graphics g, Rectangle r, int radius, Color color, float width)
        {
            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath p = RoundRect(r, radius))
            using (Pen pen = new Pen(color, width))
            {
                g.DrawPath(pen, p);
            }
            g.SmoothingMode = old;
        }

        public static void ApplyRegion(Control c, int radius)
        {
            if (c.Width <= 0 || c.Height <= 0) return;
            using (GraphicsPath p = RoundRect(new Rectangle(0, 0, c.Width, c.Height), radius))
            {
                Region old = c.Region;
                c.Region = new Region(p);
                if (old != null) old.Dispose();
            }
        }

        /// <summary>柔和阴影：多层半透明矩形叠加模拟高斯模糊阴影。</summary>
        public static void DrawShadow(Graphics g, Rectangle r, int radius, int depth)
        {
            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            for (int i = depth; i >= 1; i--)
            {
                int alpha = (int)(6 * (1.0 - (double)i / (depth + 1)));
                if (alpha < 1) alpha = 1;
                Color sc = Dark ? Color.FromArgb(alpha, 0, 0, 0) : Color.FromArgb(alpha, 40, 60, 100);
                Rectangle sr = new Rectangle(r.X - i, r.Y - i + 1, r.Width + i * 2, r.Height + i * 2);
                using (GraphicsPath p = RoundRect(sr, radius + i))
                using (SolidBrush b = new SolidBrush(sc))
                {
                    g.FillPath(b, p);
                }
            }
            g.SmoothingMode = old;
        }

        /// <summary>带阴影的圆角填充（Card/StatCard 统一入口）。</summary>
        public static void FillRoundShadow(Graphics g, Rectangle r, int radius, Color fill, Color border)
        {
            DrawShadow(g, r, radius, 4);
            FillRound(g, r, radius, fill);
            if (border != Color.Empty)
            {
                DrawRound(g, r, radius, border, 1f);
            }
        }

        // ---------------- 控件工厂 ----------------
        public static Label L(string text)
        {
            return L(text, F9, TextColor);
        }

        public static Label L(string text, Font f, Color c)
        {
            Label l = new Label();
            l.Text = text;
            l.Font = f;
            l.ForeColor = c;
            l.AutoSize = true;
            l.BackColor = Color.Transparent;
            return l;
        }

        public static Label Wrap(string text, Font f, Color c)
        {
            Label l = new Label();
            l.Text = text;
            l.Font = f;
            l.ForeColor = c;
            l.BackColor = Color.Transparent;
            l.AutoSize = false;
            l.Tag = "fillw";
            return l;
        }

        public static Button B(string text)
        {
            return B(text, false);
        }

        public static Button B(string text, bool primary)
        {
            return SkinBtn(text, primary ? ButtonKind.Primary : ButtonKind.Secondary);
        }

        public static Button Danger(string text)
        {
            return SkinBtn(text, ButtonKind.Danger);
        }

        public static Button Ghost(string text)
        {
            return SkinBtn(text, ButtonKind.Ghost);
        }

        private static Button SkinBtn(string text, ButtonKind kind)
        {
            Button b = new Button();
            b.Text = text;
            b.Tag = kind;
            b.Height = 36;
            b.Width = 108;
            b.AutoSize = false;
            b.UseVisualStyleBackColor = false;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 1;
            b.Cursor = Cursors.Hand;
            StyleButton(b);
            return b;
        }

        private static void StyleButton(Button b)
        {
            if (b == null || !(b.Tag is ButtonKind)) return;
            ButtonKind kind = (ButtonKind)b.Tag;
            b.Font = kind == ButtonKind.Primary ? F9B : F9;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 1;
            b.UseVisualStyleBackColor = false;
            b.Cursor = Cursors.Hand;
            switch (kind)
            {
                case ButtonKind.Primary:
                    b.BackColor = Accent;
                    b.ForeColor = Color.White;
                    b.FlatAppearance.BorderColor = AccentDark;
                    b.FlatAppearance.MouseOverBackColor = AccentDark;
                    b.FlatAppearance.MouseDownBackColor = Mix(AccentDark, Color.Black, 0.15f);
                    break;
                case ButtonKind.Danger:
                    b.BackColor = Red;
                    b.ForeColor = Color.White;
                    b.FlatAppearance.BorderColor = Mix(Red, Color.Black, 0.15f);
                    b.FlatAppearance.MouseOverBackColor = Mix(Red, Color.Black, 0.12f);
                    b.FlatAppearance.MouseDownBackColor = Mix(Red, Color.Black, 0.25f);
                    break;
                case ButtonKind.Ghost:
                    b.BackColor = CardBg;
                    b.ForeColor = Muted;
                    b.FlatAppearance.BorderColor = Border;
                    b.FlatAppearance.MouseOverBackColor = SoftGray;
                    b.FlatAppearance.MouseDownBackColor = Mix(SoftGray, Border, 0.3f);
                    break;
                default:
                    b.BackColor = CardBg;
                    b.ForeColor = TextColor;
                    b.FlatAppearance.BorderColor = BorderStrong;
                    b.FlatAppearance.MouseOverBackColor = SoftBlue;
                    b.FlatAppearance.MouseDownBackColor = AccentSoft;
                    break;
            }
        }

        /// <summary>按当前主题给 CSkin 按钮上色（kind 存在 Tag 里，主题切换时重刷）。</summary>
        public static void StyleSkin(CCWin.SkinControl.SkinButton b)
        {
            if (b == null) return;
            ButtonKind kind = b.Tag is ButtonKind ? (ButtonKind)b.Tag : ButtonKind.Secondary;
            b.Font = kind == ButtonKind.Primary ? F9B : F9;
            b.Radius = 4;
            b.RoundStyle = CCWin.SkinClass.RoundStyle.All;
            b.IsDrawGlass = false;
            b.IsDrawBorder = false;
            b.InheritColor = false;
            b.ForeColorSuit = false;
            b.FadeGlow = false;
            switch (kind)
            {
                case ButtonKind.Primary:
                    // 降亮：深一档的蓝 + 无玻璃/无发光，稳重不刺眼
                    b.BaseColor = Mix(Accent, Color.Black, 0.12f);
                    b.MouseBaseColor = Mix(Accent, Color.Black, 0.02f);
                    b.DownBaseColor = Mix(AccentDark, Color.Black, 0.12f);
                    b.BorderColor = Mix(Accent, Color.Black, 0.26f);
                    b.InnerBorderColor = Mix(Accent, Color.White, 0.16f);
                    b.GlowColor = AccentSoft;
                    b.FadeGlow = false;
                    b.ForeColor = Color.White;
                    break;
                case ButtonKind.Danger:
                    b.BaseColor = CardBg;
                    b.MouseBaseColor = SoftRed;
                    b.DownBaseColor = SoftRed;
                    b.BorderColor = Mix(Red, CardBg, 0.55f);
                    b.InnerBorderColor = Mix(CardBg, Dark ? Color.White : Color.Black, 0.05f);
                    b.GlowColor = SoftRed;
                    b.FadeGlow = true;
                    b.ForeColor = Red;
                    break;
                case ButtonKind.Ghost:
                    b.BaseColor = CardBg;
                    b.MouseBaseColor = SoftGray;
                    b.DownBaseColor = SoftGray;
                    b.BorderColor = Border;
                    b.InnerBorderColor = CardBg;
                    b.FadeGlow = false;
                    b.ForeColor = Muted;
                    break;
                default:
                    b.BaseColor = CardBg;
                    b.MouseBaseColor = SoftBlue;
                    b.DownBaseColor = AccentSoft;
                    b.BorderColor = BorderStrong;
                    b.InnerBorderColor = Mix(CardBg, Dark ? Color.White : Color.Black, 0.05f);
                    b.GlowColor = AccentSoft;
                    b.FadeGlow = true;
                    b.ForeColor = TextColor;
                    break;
            }
            b.InnerBorderColor = b.BaseColor;
            b.GlowColor = b.BaseColor;
            b.FadeGlow = false;
        }

        /// <summary>CSkin 主窗体外壳配色（圆角 + 窗口阴影 + 标题栏），随主题重刷。</summary>
        public static void StyleShell(CCWin.CCSkinMain f)
        {
            if (f == null) return;
            f.BackColor = Bg;
            f.TitleColor = TextColor;
            f.BorderColor = BorderStrong;
            f.InnerBorderColor = CardBg;
            f.CaptionBackColorTop = CardBg;
            f.CaptionBackColorBottom = CardBg;
            f.ShowBorder = true;
            f.Shadow = true;
            f.ShadowWidth = 8;
            f.ShadowColor = Dark ? Color.FromArgb(140, 0, 0, 0) : Color.FromArgb(60, 50, 80, 130);
            f.Radius = 12;
            f.RoundStyle = CCWin.SkinClass.RoundStyle.All;
            f.ShowDrawIcon = true;
        }

        /// <summary>颜色混合（t=0 取 a，t=1 取 b）。</summary>
        public static Color Mix(Color a, Color b, float t)
        {
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        public static Label Tag(string text, Color fg, Color bg)
        {
            Label l = new Label();
            l.Text = text;
            l.Font = F85;
            l.ForeColor = fg;
            l.BackColor = bg;
            l.AutoSize = false;
            l.Height = 22;
            l.TextAlign = ContentAlignment.MiddleCenter;
            l.Padding = new Padding(8, 0, 8, 0);
            ApplyRegion(l, 6);
            return l;
        }

        public static FlowLayoutPanel Stack()
        {
            FlowLayoutPanel f = new FlowLayoutPanel();
            f.Dock = DockStyle.Fill;
            f.FlowDirection = FlowDirection.TopDown;
            f.WrapContents = false;
            f.AutoScroll = true;
            f.Padding = new Padding(SpXxl, SpXl, SpXxl, SpXxl);
            f.BackColor = Bg;
            f.Resize += delegate
            {
                int w = f.ClientSize.Width - f.Padding.Horizontal - 2;
                if (f.VerticalScroll.Visible) w -= SystemInformation.VerticalScrollBarWidth;
                if (w < 320) w = 320;
                foreach (Control c in f.Controls)
                {
                    if (c is Card || c is StepRow || Convert.ToString(c.Tag) == "wide") c.Width = w;
                }
            };
            return f;
        }

        public static FlowLayoutPanel Row()
        {
            FlowLayoutPanel f = new FlowLayoutPanel();
            f.FlowDirection = FlowDirection.LeftToRight;
            f.WrapContents = false;
            f.AutoSize = true;
            f.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            f.BackColor = Color.Transparent;
            f.Margin = new Padding(0);
            return f;
        }

        public static TableLayoutPanel Grid(int cols, int rows)
        {
            TableLayoutPanel t = new TableLayoutPanel();
            t.ColumnCount = cols;
            t.RowCount = rows;
            t.BackColor = Color.Transparent;
            t.Margin = new Padding(0);
            for (int i = 0; i < cols; i++) t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / cols));
            for (int i = 0; i < rows; i++) t.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            return t;
        }

        public static void Fill(Control c, Color color)
        {
            c.BackColor = color;
        }

        public static void StyleInput(TextBox t)
        {
            t.Font = F9;
            t.BorderStyle = BorderStyle.FixedSingle;
            t.BackColor = InputBg;
            t.ForeColor = TextColor;
            t.ForeColor = TextColor;
        }

        /// <summary>统一的表格皮肤：柔和表头、隔行底色、轻网格线、整行选中。</summary>
        public static void StyleGrid(DataGridView g)
        {
            g.BackgroundColor = CardBg;
            g.BorderStyle = BorderStyle.None;
            g.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            g.GridColor = GridLine;
            g.RowHeadersVisible = false;
            g.AllowUserToAddRows = false;
            g.AllowUserToDeleteRows = false;
            g.AllowUserToResizeRows = false;
            g.EnableHeadersVisualStyles = false;
            g.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            g.ColumnHeadersHeight = 38;
            g.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;

            Color headBg = GridHeadBg;
            Color headFg = GridHeadFg;
            g.ColumnHeadersDefaultCellStyle.BackColor = headBg;
            g.ColumnHeadersDefaultCellStyle.ForeColor = headFg;
            g.ColumnHeadersDefaultCellStyle.Font = F9B;
            g.ColumnHeadersDefaultCellStyle.Padding = new Padding(10, 0, 8, 0);
            g.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
            g.ColumnHeadersDefaultCellStyle.SelectionBackColor = headBg;
            g.ColumnHeadersDefaultCellStyle.SelectionForeColor = headFg;

            g.DefaultCellStyle.Font = F9;
            g.DefaultCellStyle.ForeColor = TextColor;
            g.DefaultCellStyle.BackColor = CardBg;
            g.DefaultCellStyle.SelectionBackColor = GridSel;
            g.DefaultCellStyle.SelectionForeColor = TextColor;
            g.DefaultCellStyle.Padding = new Padding(10, 0, 8, 0);
            g.DefaultCellStyle.WrapMode = DataGridViewTriState.False;
            g.AlternatingRowsDefaultCellStyle.BackColor = GridAlt;
            g.AlternatingRowsDefaultCellStyle.SelectionBackColor = GridSel;
            g.AlternatingRowsDefaultCellStyle.SelectionForeColor = TextColor;

            g.RowTemplate.Height = 34;
            g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            g.StandardTab = true;

            try
            {
                System.Reflection.PropertyInfo pi = typeof(DataGridView).GetProperty("DoubleBuffered",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (pi != null) pi.SetValue(g, true, null);
            }
            catch
            {
            }
        }

        /// <summary>ListView 统一配色（日志、清单等）。</summary>
        public static void StyleList(ListView lv)
        {
            lv.BackColor = CardBg;
            lv.ForeColor = TextColor;
            lv.Font = F9;
            lv.BorderStyle = BorderStyle.None;
            lv.FullRowSelect = true;
            lv.HideSelection = false;
        }

        public static string Short(string text, int max)
        {
            if (text == null) return "";
            string s = text.Replace("\r", " ").Replace("\n", " ").Trim();
            while (s.IndexOf("  ") >= 0) s = s.Replace("  ", " ");
            if (s.Length <= max) return s;
            return s.Substring(0, max) + "…";
        }

        public static string Prompt(string title, string label, string def)
        {
            using (Form f = new Form())
            {
                f.Text = title;
                f.Font = F9;
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.StartPosition = FormStartPosition.CenterParent;
                f.MinimizeBox = false;
                f.MaximizeBox = false;
                f.ShowInTaskbar = false;
                f.ClientSize = new Size(540, 168);
                f.BackColor = Ui.CardBg;

                Label l = Wrap(label, F9, TextColor);
                l.Location = new Point(24, 20);
                l.Size = new Size(492, 40);
                f.Controls.Add(l);

                TextBox t = new TextBox();
                t.Text = def;
                StyleInput(t);
                t.Location = new Point(24, 68);
                t.Width = 492;
                t.Height = 32;
                f.Controls.Add(t);

                Button ok = B("确定", true);
                ok.Location = new Point(332, 118);
                ok.Width = 92;
                ok.DialogResult = DialogResult.OK;
                f.Controls.Add(ok);

                Button cancel = B("取消");
                cancel.Location = new Point(432, 118);
                cancel.Width = 84;
                cancel.DialogResult = DialogResult.Cancel;
                f.Controls.Add(cancel);

                f.AcceptButton = ok;
                f.CancelButton = cancel;
                return f.ShowDialog() == DialogResult.OK ? t.Text.Trim() : null;
            }
        }

        public static int Choose(string title, string label, string[] items)
        {
            using (Form f = new Form())
            {
                f.Text = title;
                f.Font = F9;
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.StartPosition = FormStartPosition.CenterParent;
                f.MinimizeBox = false;
                f.MaximizeBox = false;
                f.ShowInTaskbar = false;
                f.ClientSize = new Size(540, 188);
                f.BackColor = Ui.CardBg;

                Label l = Wrap(label, F9, TextColor);
                l.Location = new Point(24, 18);
                l.Size = new Size(492, 40);
                f.Controls.Add(l);

                ComboBox c = new ComboBox();
                c.DropDownStyle = ComboBoxStyle.DropDownList;
                c.Font = F9;
                c.Location = new Point(24, 64);
                c.Width = 492;
                c.Height = 32;
                c.Items.AddRange(items);
                c.SelectedIndex = 0;
                f.Controls.Add(c);

                Button ok = B("确定", true);
                ok.Location = new Point(332, 128);
                ok.Width = 92;
                ok.DialogResult = DialogResult.OK;
                f.Controls.Add(ok);

                Button cancel = B("取消");
                cancel.Location = new Point(432, 128);
                cancel.Width = 84;
                cancel.DialogResult = DialogResult.Cancel;
                f.Controls.Add(cancel);

                f.AcceptButton = ok;
                f.CancelButton = cancel;
                return f.ShowDialog() == DialogResult.OK ? c.SelectedIndex : -1;
            }
        }

        // ---------------- 文字溢出检查（开发用） ----------------
        public static System.Collections.Generic.List<string> AuditText(Control root)
        {
            System.Collections.Generic.List<string> issues = new System.Collections.Generic.List<string>();
            Walk(root, issues, "");
            return issues;
        }

        private static void Walk(Control c, System.Collections.Generic.List<string> issues, string path)
        {
            if (!c.Visible) return;
            string label = c.GetType().Name;
            string text = "";
            try { text = c.Text; } catch { }
            string p = path + "/" + label + (string.IsNullOrEmpty(text) ? "" : "[" + Short(text, 10) + "]");

            try
            {
                if (c is Label && !((Label)c).AutoSize && !string.IsNullOrEmpty(c.Text))
                {
                    int w = c.ClientSize.Width - c.Padding.Horizontal;
                    Size need = TextRenderer.MeasureText(c.Text, c.Font, new Size(w, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
                    if (need.Height > c.ClientSize.Height - c.Padding.Vertical + 1)
                        issues.Add(p + " 文字超出高度 need=" + need.Height + " have=" + c.ClientSize.Height);
                }
                else if (c is Button && !string.IsNullOrEmpty(c.Text))
                {
                    Size need = TextRenderer.MeasureText(c.Text, c.Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                    if (need.Width > c.Width - 10)
                        issues.Add(p + " 按钮文字过宽 need=" + need.Width + " have=" + (c.Width - 10));
                    if (need.Height > c.Height - 6)
                        issues.Add(p + " 按钮文字过高 need=" + need.Height + " have=" + (c.Height - 6));
                }
                else if (c is Card)
                {
                    Card card = (Card)c;
                    if (!string.IsNullOrEmpty(card.Title))
                    {
                        Size need = TextRenderer.MeasureText(card.Title, F10B, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                        if (need.Width > card.Width - 44) issues.Add(p + " 卡片标题过宽 need=" + need.Width + " have=" + (card.Width - 44));
                    }
                }
                else if (c is StatCard)
                {
                    StatCard sc = (StatCard)c;
                    Size n1 = TextRenderer.MeasureText(sc.Caption, F9, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                    if (n1.Width > sc.Width - 36) issues.Add(p + " 统计标题过宽 need=" + n1.Width + " have=" + (sc.Width - 36));
                    Size n2 = TextRenderer.MeasureText(sc.Value, F19B, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                    if (n2.Width > sc.Width - 34) issues.Add(p + " 统计数值过宽 need=" + n2.Width + " have=" + (sc.Width - 34));
                    Size n3 = TextRenderer.MeasureText(sc.Sub, F85, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                    if (n3.Width > sc.Width - 34) issues.Add(p + " 统计说明过宽 need=" + n3.Width + " have=" + (sc.Width - 34));
                }
                else if (c is StepRow)
                {
                    StepRow sr = (StepRow)c;
                    Size n1 = TextRenderer.MeasureText(sr.Title, F10B, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                    if (n1.Width > sr.Width - 240) issues.Add(p + " 步骤标题过宽 need=" + n1.Width + " have=" + (sr.Width - 240));
                    Size n2 = TextRenderer.MeasureText(sr.Desc, F85, new Size(sr.Width - 256, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
                    if (n2.Height > 30) issues.Add(p + " 步骤说明过高 need=" + n2.Height + " have=30");
                }
                else if (c is Pill)
                {
                    Pill pill = (Pill)c;
                    Size n = TextRenderer.MeasureText(pill.Text, pill.Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                    if (n.Width > pill.Width - pill.Padding.Left - 8) issues.Add(p + " 胶囊文字过宽 need=" + n.Width + " have=" + (pill.Width - pill.Padding.Left - 8));
                }
            }
            catch
            {
            }

            foreach (Control child in c.Controls) Walk(child, issues, p);
        }
        public static void Fatal(Exception ex)
        {
            try
            {
                Log.Write("错误", "未处理异常：" + ex.Message);
                Log.Write("错误", ex.ToString());
            }
            catch
            {
            }
            MessageBox.Show("程序遇到错误：\r\n\r\n" + ex.Message + "\r\n\r\n详细信息已写入日志。",
                            "UWFtool", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        /// <summary>AntdUI 风格消息通知（替代部分 MessageBox，更轻量现代）。</summary>
        public static void Notify(Form owner, string text, AntdUI.TType type = AntdUI.TType.Info)
        {
            try
            {
                if (owner != null && !owner.IsDisposed && owner.IsHandleCreated)
                {
                    AntdUI.Message.open(owner, text, F9);
                }
            }
            catch
            {
                MessageBox.Show(text, "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
    }

    public enum ButtonKind
    {
        Primary,
        Secondary,
        Ghost,
        Danger
    }



    /// <summary>应用标志：蓝色渐变圆角方块 + 白色盾牌 + 对勾。图标文件用同一套几何。</summary>
    /// <summary>亮暗切换的圆形扩散过渡层：以按钮为圆心，新主题像水波一样漫出去。</summary>
    internal class ThemeRevealOverlay : Form
    {
        public Bitmap Before;
        public Bitmap After;
        public Point Center;
        public float Radius;
        public float MaxRadius;

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        public ThemeRevealOverlay()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (Before == null) return;
            e.Graphics.DrawImage(Before, 0, 0);
            if (After != null && Radius > 0.5f)
            {
                SmoothingMode old = e.Graphics.SmoothingMode;
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath p = new GraphicsPath())
                {
                    p.AddEllipse(Center.X - Radius, Center.Y - Radius, Radius * 2, Radius * 2);
                    e.Graphics.SetClip(p);
                    e.Graphics.DrawImage(After, 0, 0);
                    e.Graphics.ResetClip();
                }
                e.Graphics.SmoothingMode = old;
            }
        }
    }

    /// <summary>圆形扩散的亮暗主题切换动画。</summary>
    public static class ThemeReveal
    {
        public static void Toggle(Form owner, Control source, bool toDark)
        {
            if (owner == null || owner.IsDisposed || source == null || !owner.Visible)
            {
                Ui.SetTheme(toDark);
                return;
            }
            Point center = owner.PointToClient(source.PointToScreen(new Point(source.Width / 2, source.Height / 2)));
            int w = owner.ClientSize.Width;
            int h = owner.ClientSize.Height;
            if (w <= 0 || h <= 0)
            {
                Ui.SetTheme(toDark);
                return;
            }

            ThemeRevealOverlay overlay = new ThemeRevealOverlay();
            overlay.Bounds = owner.Bounds;
            Bitmap before = new Bitmap(w, h);
            owner.DrawToBitmap(before, new Rectangle(0, 0, w, h));

            Ui.SetTheme(toDark);
            owner.Update();

            Bitmap after = new Bitmap(w, h);
            owner.DrawToBitmap(after, new Rectangle(0, 0, w, h));

            overlay.Before = before;
            overlay.After = after;
            overlay.Center = center;
            overlay.Radius = 0f;
            float dx = Math.Max(center.X, w - center.X);
            float dy = Math.Max(center.Y, h - center.Y);
            overlay.MaxRadius = (float)Math.Sqrt(dx * dx + dy * dy) + 8f;

            System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
            timer.Interval = 15;
            DateTime start = DateTime.Now;
            const float duration = 420f;
            timer.Tick += delegate
            {
                float t = (float)(DateTime.Now - start).TotalMilliseconds / duration;
                if (t > 1f) t = 1f;
                float ease = t * t * (3f - 2f * t);
                overlay.Radius = ease * overlay.MaxRadius;
                overlay.Invalidate();
                if (t >= 1f)
                {
                    timer.Stop();
                    overlay.Close();
                    timer.Dispose();
                    before.Dispose();
                    after.Dispose();
                    overlay.Dispose();
                }
            };
            overlay.Show(owner);
            timer.Start();
        }
    }

    /// <summary>透明的小草装饰带，放在侧边栏/页面留白处。</summary>
    public class GrassStrip : Control
    {
        public GrassStrip()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.UserPaint |
                     ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Ui.DrawGrassStrip(e.Graphics, ClientRectangle);
        }
    }

    /// <summary>亮暗切换按钮（太阳 / 月亮，纯矢量绘制）。</summary>
    public class ThemeToggleButton : Button
    {
        private bool _hover;

        public ThemeToggleButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Size = new Size(40, 34);
            Cursor = Cursors.Hand;
            Text = "";
            BackColor = Ui.CardBg;
            TabStop = false;
        }

        protected override void OnPaintBackground(PaintEventArgs pevent)
        {
            pevent.Graphics.Clear(BackColor);
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Color bg = _hover ? Ui.SoftGray : Ui.CardBg;
            Ui.FillRound(g, r, 8, bg);
            Color fg = Ui.Muted;
            float cx = Width / 2f, cy = Height / 2f;
            if (Ui.Dark)
            {
                // 太阳
                using (SolidBrush b = new SolidBrush(Color.FromArgb(255, 214, 112)))
                {
                    g.FillEllipse(b, cx - 5, cy - 5, 10, 10);
                }
                using (Pen p = new Pen(Color.FromArgb(255, 214, 112), 1.8f))
                {
                    p.StartCap = LineCap.Round;
                    p.EndCap = LineCap.Round;
                    for (int i = 0; i < 8; i++)
                    {
                        double a = i * Math.PI / 4.0;
                        g.DrawLine(p,
                            cx + (float)Math.Cos(a) * 7.5f, cy + (float)Math.Sin(a) * 7.5f,
                            cx + (float)Math.Cos(a) * 10.5f, cy + (float)Math.Sin(a) * 10.5f);
                    }
                }
            }
            else
            {
                // 月亮
                using (SolidBrush b = new SolidBrush(Color.FromArgb(96, 120, 200)))
                {
                    g.FillEllipse(b, cx - 7, cy - 7, 14, 14);
                }
                using (SolidBrush b = new SolidBrush(Ui.CardBg))
                {
                    g.FillEllipse(b, cx - 2.5f, cy - 7.5f, 11, 11);
                }
            }
            g.SmoothingMode = SmoothingMode.Default;
        }
    }

    public static class Logo
    {
        private static Image _img;

        /// <summary>内嵌的 logo 位图（build.ps1 以资源 logo.png 打进 exe）。缺失时回退到内置矢量图标。</summary>
        private static Image Img
        {
            get
            {
                if (_img == null)
                {
                    try
                    {
                        using (System.IO.Stream s = typeof(Logo).Assembly.GetManifestResourceStream("logo.png"))
                        {
                            if (s != null)
                            {
                                using (Image tmp = Image.FromStream(s))
                                {
                                    _img = new Bitmap(tmp);
                                }
                            }
                        }
                    }
                    catch
                    {
                    }
                }
                return _img;
            }
        }

        public static void Draw(Graphics g, RectangleF r)
        {
            float size = Math.Min(r.Width, r.Height);
            RectangleF box = new RectangleF(r.X + (r.Width - size) / 2, r.Y + (r.Height - size) / 2, size, size);

            Image img = Img;
            if (img != null)
            {
                SmoothingMode oldSm = g.SmoothingMode;
                InterpolationMode oldIn = g.InterpolationMode;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(img, box);
                g.SmoothingMode = oldSm;
                g.InterpolationMode = oldIn;
                return;
            }
            DrawVector(g, box);
        }

        private static void DrawVector(Graphics g, RectangleF box)
        {
            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            float size = Math.Min(box.Width, box.Height);
            float radius = size * 0.23f;

            using (GraphicsPath bg = RoundedRectF(box, radius))
            using (LinearGradientBrush brush = new LinearGradientBrush(box,
                       Color.FromArgb(96, 140, 255), Color.FromArgb(40, 92, 236), LinearGradientMode.Vertical))
            {
                g.FillPath(brush, bg);
            }

            RectangleF glossBox = new RectangleF(box.X, box.Y, box.Width, box.Height * 0.52f);
            using (GraphicsPath glossPath = RoundedRectF(glossBox, radius))
            using (LinearGradientBrush gloss = new LinearGradientBrush(
                       new RectangleF(box.X, box.Y, box.Width, box.Height * 0.55f),
                       Color.FromArgb(44, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), LinearGradientMode.Vertical))
            {
                g.FillPath(gloss, glossPath);
            }

            float sw = size * 0.58f;
            float sh = size * 0.66f;
            float sx = box.X + (size - sw) / 2;
            float sy = box.Y + (size - sh) / 2 - size * 0.01f;
            using (GraphicsPath shield = ShieldPath(sx, sy, sw, sh))
            using (SolidBrush white = new SolidBrush(Color.White))
            {
                g.FillPath(white, shield);
            }

            float penW = Math.Max(1.2f, size * 0.075f);
            using (Pen pen = new Pen(Color.FromArgb(58, 108, 240), penW))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                g.DrawLines(pen, new PointF[]
                {
                    new PointF(sx + 0.28f * sw, sy + 0.52f * sh),
                    new PointF(sx + 0.44f * sw, sy + 0.68f * sh),
                    new PointF(sx + 0.74f * sw, sy + 0.34f * sh)
                });
            }
            g.SmoothingMode = old;
        }

        public static GraphicsPath RoundedRectF(RectangleF r, float radius)
        {
            GraphicsPath p = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d < 2) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static GraphicsPath ShieldPath(float x, float y, float w, float h)
        {
            GraphicsPath p = new GraphicsPath();
            p.AddLine(x + 0.10f * w, y + 0.16f * h, x + 0.50f * w, y + 0.04f * h);
            p.AddLine(x + 0.50f * w, y + 0.04f * h, x + 0.90f * w, y + 0.16f * h);
            p.AddBezier(x + 0.90f * w, y + 0.16f * h, x + 0.90f * w, y + 0.58f * h, x + 0.72f * w, y + 0.82f * h, x + 0.50f * w, y + 0.96f * h);
            p.AddBezier(x + 0.50f * w, y + 0.96f * h, x + 0.28f * w, y + 0.82f * h, x + 0.10f * w, y + 0.58f * h, x + 0.10f * w, y + 0.16f * h);
            p.CloseFigure();
            return p;
        }
    }

    public class LogoView : Control
    {
        public LogoView()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.UserPaint |
                     ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
            Size = new Size(34, 34);
            BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Logo.Draw(e.Graphics, new RectangleF(0, 0, Width, Height));
        }
    }
    /// <summary>白底圆角面板，用于表格容器、详情区等。</summary>
    public class RoundedPanel : Panel
    {
        public int Radius = Ui.RadiusCard;
        public Color Fill = Color.White;
        public Color BorderColor = Ui.Border;

        private Color RealFill { get { return Fill == Color.White ? Ui.CardBg : Ui.Map(Fill); } }

        public RoundedPanel()
        {
            BackColor = Ui.CardBg;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Ui.ApplyRegion(this, Radius);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Ui.DrawShadow(g, r, Radius, 3);
            using (GraphicsPath path = Ui.RoundRect(r, Radius))
            {
                using (SolidBrush b = new SolidBrush(RealFill)) g.FillPath(b, path);
                if (BorderColor != Color.Empty)
                {
                    using (Pen p = new Pen(Ui.Map(BorderColor))) g.DrawPath(p, path);
                }
            }
        }
    }
    public class Card : Panel
    {
        public string Title;
        public Panel Body;
        public Color Tint = Color.White;
        private readonly Color _accent;

        public Card(string title, int height)
            : this(title, height, Color.Empty)
        {
        }

        public Card(string title, int height, Color accent)
        {
            Title = title;
            _accent = accent;
            BackColor = Ui.CardBg;
            Height = height;
            Width = 1030;
            Margin = new Padding(0, 0, 0, Ui.SpMd);
            Padding = new Padding(0);
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);

            Body = new Panel();
            Body.BackColor = Ui.CardBg;
            Body.Location = new Point(Ui.SpXl, string.IsNullOrEmpty(title) ? 18 : 46);
            Body.Size = new Size(Width - Ui.SpXl * 2, Height - Body.Location.Y - 16);
            Body.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            Body.Resize += delegate
            {
                foreach (Control c in Body.Controls)
                {
                    if (Convert.ToString(c.Tag) == "fillw") c.Width = Body.ClientSize.Width;
                }
            };
            Controls.Add(Body);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Ui.ApplyRegion(this, Ui.RadiusCard);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            bool plain = Tint == Color.White;

            Ui.DrawShadow(g, r, Ui.RadiusCard, 5);

            using (GraphicsPath path = Ui.RoundRect(r, Ui.RadiusCard))
            {
                using (SolidBrush b = new SolidBrush(plain ? Ui.CardBg : Ui.Map(Tint))) g.FillPath(b, path);
                if (_accent != Color.Empty && !plain)
                {
                    Region old = g.Clip;
                    g.SetClip(path, CombineMode.Intersect);
                    using (SolidBrush ab = new SolidBrush(Ui.Map(_accent))) g.FillRectangle(ab, 0, 0, 3, Height);
                    g.Clip = old;
                    if (old != null) old.Dispose();
                }
                using (Pen p = new Pen(Ui.Border)) g.DrawPath(p, path);
            }

            if (!string.IsNullOrEmpty(Title))
            {
                TextRenderer.DrawText(g, Title, Ui.F10B, new Point(Ui.SpXl, 15), Ui.TextColor,
                                      TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                using (Pen p = new Pen(Ui.Border))
                {
                    g.DrawLine(p, Ui.SpXl, 42, Width - Ui.SpXl, 42);
                }
            }
        }
    }

    public class StatCard : Panel
    {
        public string Caption = "";
        public string Value = "--";
        public string Sub = "";
        /// <summary>单位（如“台”），画在数字右侧同一行，不再单独一行。</summary>
        public string Unit = "";
        public Color ValueColor = Ui.TextColor;
        public int Progress = -1;
        public Color ProgressColor = Ui.Accent;
        public Color Tint = Color.White;

        public StatCard()
        {
            BackColor = Ui.CardBg;
            Margin = new Padding(0, 0, Ui.SpMd, 0);
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Ui.ApplyRegion(this, Ui.RadiusCard);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);

            Ui.DrawShadow(g, r, Ui.RadiusCard, 4);

            using (GraphicsPath path = Ui.RoundRect(r, Ui.RadiusCard))
            {
                using (SolidBrush b = new SolidBrush(Tint == Color.White ? Ui.CardBg : Ui.Map(Tint))) g.FillPath(b, path);
                using (Pen p = new Pen(Ui.Border)) g.DrawPath(p, path);
            }

            TextRenderer.DrawText(g, Caption, Ui.F85, new Point(16, 12), Ui.Muted,
                                  TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);

            bool compact = Height < 96;
            Font valueFont = compact ? Ui.F15B : Ui.F19B;
            Rectangle valueRect = compact
                ? new Rectangle(15, 28, Width - 30, 30)
                : new Rectangle(15, 34, Width - 30, 36);
            TextFormatFlags vf = TextFormatFlags.NoPadding | TextFormatFlags.Left |
                                 TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine;
            TextRenderer.DrawText(g, Value, valueFont, valueRect, ValueColor, vf);
            if (!string.IsNullOrEmpty(Unit))
            {
                int vw = TextRenderer.MeasureText(Value, valueFont, valueRect.Size, vf).Width;
                Rectangle unitRect = new Rectangle(valueRect.X + vw + 6, valueRect.Y + (compact ? 4 : 6), 120, valueRect.Height);
                TextRenderer.DrawText(g, Unit, Ui.F85, unitRect, Ui.Muted, vf);
            }

            if (!string.IsNullOrEmpty(Sub))
            {
                Rectangle subRect = new Rectangle(16, Height - 28, Width - 32, 16);
                TextRenderer.DrawText(g, Sub, Ui.F85, subRect, Ui.MutedLight,
                                      TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            }
            if (Progress >= 0)
            {
                Rectangle track = new Rectangle(Ui.SpLg + 2, Height - 13, Width - Ui.SpLg * 2 - 4, 4);
                using (GraphicsPath tp = Ui.RoundRect(track, 2))
                using (SolidBrush bg = new SolidBrush(Ui.SoftGray))
                {
                    g.FillPath(bg, tp);
                }
                int w = (int)(track.Width * (Math.Min(100, Math.Max(0, Progress)) / 100.0));
                if (w > 2)
                {
                    Rectangle fill = new Rectangle(track.X, track.Y, w, track.Height);
                    using (GraphicsPath fp = Ui.RoundRect(fill, 2))
                    using (SolidBrush fg = new SolidBrush(Ui.Map(ProgressColor)))
                    {
                        g.FillPath(fg, fp);
                    }
                }
            }
        }
    }

    public enum StepStatus
    {
        Pending = 0,
        Current = 1,
        Done = 2
    }

    public class StepRow : Panel
    {
        public int Number = 1;
        public string Title = "";
        public string Desc = "";
        public StepStatus Status = StepStatus.Pending;
        public Button Action;

        public StepRow(int number, string title, string desc, string buttonText)
        {
            Number = number;
            Title = title;
            Desc = desc;
            Height = 76;
            BackColor = Ui.SoftGray;
            Margin = new Padding(0, 0, 0, Ui.SpSm);
            Padding = new Padding(0);
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);

            Action = Ui.B(buttonText);
            Action.Height = 32;
            Action.Width = 150;
            Action.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            Action.Location = new Point(Width - 166, 22);
            Controls.Add(Action);
        }

        public void SetStatus(StepStatus s)
        {
            if (Status == s) return;
            Status = s;
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Ui.ApplyRegion(this, 10);
            if (Action != null) Action.Location = new Point(Width - Action.Width - 16, 22);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color bg = Ui.SoftGray;
            Color fg = Ui.MutedLight;
            if (Status == StepStatus.Current) { bg = Ui.SoftBlue; fg = Ui.Accent; }
            else if (Status == StepStatus.Done) { bg = Ui.SoftGreen; fg = Ui.Green; }

            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = Ui.RoundRect(r, 10))
            {
                using (SolidBrush b = new SolidBrush(bg)) g.FillPath(b, path);
                Color borderColor = Status == StepStatus.Current
                    ? Ui.Mix(Ui.Accent, bg, 0.65f)
                    : (Status == StepStatus.Done ? Ui.Mix(Ui.Green, bg, 0.7f) : Ui.Border);
                using (Pen pen = new Pen(borderColor))
                {
                    g.DrawPath(pen, path);
                }
            }

            Rectangle circle = new Rectangle(16, 20, 36, 36);
            Color circleBg = Status == StepStatus.Pending
                ? Ui.Mix(Ui.MutedLight, bg, 0.6f)
                : fg;
            using (SolidBrush cb = new SolidBrush(circleBg))
            {
                g.FillEllipse(cb, circle);
            }
            if (Status == StepStatus.Done)
            {
                using (Pen pen = new Pen(Color.White, 2.4f))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    float cx = circle.X + circle.Width / 2f;
                    float cy = circle.Y + circle.Height / 2f;
                    g.DrawLines(pen, new PointF[]
                    {
                        new PointF(cx - 7, cy + 0.5f),
                        new PointF(cx - 2, cy + 5.5f),
                        new PointF(cx + 7, cy - 5)
                    });
                }
            }
            else
            {
                TextRenderer.DrawText(g, Number.ToString(), Ui.F10B, circle,
                                      Status == StepStatus.Pending ? Ui.MutedLight : Color.White,
                                      TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }

            TextRenderer.DrawText(g, Title, Ui.F10B, new Point(66, 17), Status == StepStatus.Pending ? Ui.Muted : Ui.TextColor,
                                  TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            Rectangle descRect = new Rectangle(66, 38, Width - 66 - 190, 30);
            TextRenderer.DrawText(g, Desc, Ui.F85, descRect, Ui.Muted,
                                  TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
        }
    }

    public class Pill : Label
    {
        public Color Dot = Ui.MutedLight;
        private Color _bg = Ui.SoftGray;

        public Pill(string text)
        {
            Text = text;
            AutoSize = false;
            Height = 26;
            Width = 118;
            Font = Ui.F9B;
            TextAlign = ContentAlignment.MiddleLeft;
            Padding = new Padding(22, 0, 10, 0);
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        public void Update(string text, Color fg, Color bg, Color dot, int width)
        {
            Text = text;
            ForeColor = fg;
            _bg = bg;
            Dot = dot;
            Width = width;
            Invalidate();
        }

        public void RemapTheme()
        {
            _bg = Ui.Map(_bg);
            ForeColor = Ui.Map(ForeColor);
            Dot = Ui.Map(Dot);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath p = Ui.RoundRect(r, Height / 2))
            {
                using (SolidBrush b = new SolidBrush(_bg)) g.FillPath(b, p);
                using (Pen border = new Pen(Ui.Mix(Ui.Border, _bg, 0.4f))) g.DrawPath(border, p);
            }
            using (SolidBrush d = new SolidBrush(Dot))
            {
                g.FillEllipse(d, 10, (Height - 7) / 2, 7, 7);
            }
            TextRenderer.DrawText(g, Text, Font, new Rectangle(Padding.Left, 0, Width - Padding.Left - 6, Height), ForeColor,
                                  TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        }
    }

    public class NavButton : Button
    {
        public bool Selected;
        private bool _hover;

        public NavButton(string text)
        {
            Text = text;
            Height = 40;
            Width = 224;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            TextAlign = ContentAlignment.MiddleLeft;
            Padding = new Padding(28, 0, 0, 0);
            Font = Ui.F10;
            ForeColor = Ui.SidebarText;
            BackColor = Ui.Sidebar;
            Cursor = Cursors.Hand;
            UseVisualStyleBackColor = false;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        public void SetSelected(bool value)
        {
            if (Selected == value && Font == (value ? Ui.F10B : Ui.F10)) return;
            Selected = value;
            ForeColor = value ? Ui.Accent : Ui.SidebarText;
            Font = value ? Ui.F10B : Ui.F10;
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hover = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = false;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush bg = new SolidBrush(Ui.Sidebar))
            {
                g.FillRectangle(bg, ClientRectangle);
            }

            Rectangle pill = new Rectangle(8, 2, Width - 16, Height - 4);
            if (Selected)
            {
                Ui.FillRound(g, pill, 8, Ui.SidebarSelected);
                Ui.FillRound(g, new Rectangle(8, Height / 2 - 9, 3, 18), 1, Ui.Accent);
            }
            else if (_hover)
            {
                Ui.FillRound(g, pill, 8, Ui.SidebarHover);
            }

            TextRenderer.DrawText(g, Text, Font, new Rectangle(28, 0, Width - 36, Height),
                                  Selected ? Ui.Accent : (_hover ? Ui.TextColor : Ui.SidebarText),
                                  TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
                                  TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        }
    }
}
