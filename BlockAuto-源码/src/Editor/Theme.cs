using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace BlockAuto
{
    /// <summary>界面调色板：默认浅色（键鼠小工具观感），可切换深色。</summary>
    public static class Theme
    {
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        public static bool IsLight = true;
        /// <summary>系统 DPI 倍率（100% = 1，200% = 2），界面按此放大。</summary>
        public static float Scale = 1f;

        public static void InitScale()
        {
            Scale = ScreenCapture.DpiScale;
        }

        /// <summary>把 96dpi 设计值换算成当前 DPI 的像素。</summary>
        public static int Px(float v)
        {
            return (int)Math.Round(v * Scale);
        }

        public static Color Background = Color.FromArgb(0xF3, 0xF4, 0xF6);
        public static Color Panel = Color.FromArgb(0xFF, 0xFF, 0xFF);
        public static Color PanelAlt = Color.FromArgb(0xF5, 0xF6, 0xF8);
        public static Color Canvas = Color.FromArgb(0xFF, 0xFF, 0xFF);
        public static Color Border = Color.FromArgb(0xE1, 0xE5, 0xEB);
        public static Color Text = Color.FromArgb(0x1F, 0x23, 0x29);
        public static Color TextDim = Color.FromArgb(0x6B, 0x72, 0x80);
        public static Color Accent = Color.FromArgb(0x2F, 0x6F, 0xEB);
        public static Color AccentHover = Color.FromArgb(0x4C, 0x8D, 0xFF);
        public static Color Success = Color.FromArgb(0x16, 0xA3, 0x4A);
        public static Color Warning = Color.FromArgb(0xD9, 0x77, 0x06);
        public static Color Danger = Color.FromArgb(0xDC, 0x26, 0x26);
        public static Color Selection = Color.FromArgb(0xF5, 0x9E, 0x0B);

        public static void ApplyLight()
        {
            IsLight = true;
            Background = Color.FromArgb(0xF3, 0xF4, 0xF6);
            Panel = Color.FromArgb(0xFF, 0xFF, 0xFF);
            PanelAlt = Color.FromArgb(0xF5, 0xF6, 0xF8);
            Canvas = Color.FromArgb(0xFF, 0xFF, 0xFF);
            Border = Color.FromArgb(0xE1, 0xE5, 0xEB);
            Text = Color.FromArgb(0x1F, 0x23, 0x29);
            TextDim = Color.FromArgb(0x6B, 0x72, 0x80);
            Accent = Color.FromArgb(0x2F, 0x6F, 0xEB);
            AccentHover = Color.FromArgb(0x4C, 0x8D, 0xFF);
            Success = Color.FromArgb(0x16, 0xA3, 0x4A);
            Warning = Color.FromArgb(0xD9, 0x77, 0x06);
            Danger = Color.FromArgb(0xDC, 0x26, 0x26);
            Selection = Color.FromArgb(0xF5, 0x9E, 0x0B);
        }

        public static void ApplyDark()
        {
            IsLight = false;
            Background = Color.FromArgb(0x1B, 0x1D, 0x23);
            Panel = Color.FromArgb(0x23, 0x26, 0x2F);
            PanelAlt = Color.FromArgb(0x2A, 0x2E, 0x39);
            Canvas = Color.FromArgb(0x17, 0x19, 0x1F);
            Border = Color.FromArgb(0x35, 0x39, 0x46);
            Text = Color.FromArgb(0xE8, 0xEA, 0xF1);
            TextDim = Color.FromArgb(0x9A, 0xA1, 0xB2);
            Accent = Color.FromArgb(0x7C, 0x5C, 0xFA);
            AccentHover = Color.FromArgb(0x8F, 0x73, 0xFF);
            Success = Color.FromArgb(0x2E, 0xCC, 0x8A);
            Warning = Color.FromArgb(0xF5, 0xA6, 0x23);
            Danger = Color.FromArgb(0xEF, 0x53, 0x50);
            Selection = Color.FromArgb(0xFF, 0xD5, 0x4F);
        }

        public static void ApplyTheme(string mode)
        {
            if (string.Equals(mode, "dark", StringComparison.OrdinalIgnoreCase)) ApplyDark();
            else ApplyLight();
        }

        public static void EnableDarkTitleBar(Form f)
        {
            if (IsLight) return;
            try
            {
                if (!f.IsHandleCreated) return;
                int v = 1;
                DwmSetWindowAttribute(f.Handle, 20, ref v, 4);
            }
            catch { }
        }

        public static Font UiFont(float size, FontStyle style)
        {
            return new Font("Microsoft YaHei UI", size, style, GraphicsUnit.Point);
        }

        private static readonly Dictionary<string, Font> FontCache = new Dictionary<string, Font>();

        /// <summary>
        /// 界面字体的缓存版：字号按“设计磅值”给（真实 DPI 下系统会自行放大），
        /// 供自绘控件里用 TextRenderer 直接在设备像素上绘制，高 DPI 下清晰不模糊。
        /// </summary>
        public static Font UiFontCached(float size, FontStyle style)
        {
            string key = size.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "|" + (int)style;
            Font f;
            if (FontCache.TryGetValue(key, out f)) return f;
            f = new Font("Microsoft YaHei UI", size, style, GraphicsUnit.Point);
            FontCache[key] = f;
            return f;
        }

        /// <summary>把设计坐标换算成设备像素矩形（自绘控件里画文字用）。</summary>
        public static Rectangle Device(RectangleF design)
        {
            return new Rectangle(
                (int)Math.Round(design.X * Scale),
                (int)Math.Round(design.Y * Scale),
                Math.Max(1, (int)Math.Round(design.Width * Scale)),
                Math.Max(1, (int)Math.Round(design.Height * Scale)));
        }

        /// <summary>画布在“设计坐标 + 整体缩放”下绘制，字号需先除以缩放倍率。</summary>
        public static Font ScaledFont(float size, FontStyle style)
        {
            return new Font("Microsoft YaHei UI", Math.Max(1f, size / Scale), style, GraphicsUnit.Point);
        }

        public static Font MonoFont(float size, FontStyle style)
        {
            return new Font("Consolas", size, style, GraphicsUnit.Point);
        }

        public static GraphicsPath RoundedRect(RectangleF r, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float d = radius * 2;
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            if (d <= 0)
            {
                path.AddRectangle(r);
                return path;
            }
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static Color Darken(Color c, float factor)
        {
            return Color.FromArgb(c.A,
                (int)Math.Max(0, c.R * factor),
                (int)Math.Max(0, c.G * factor),
                (int)Math.Max(0, c.B * factor));
        }

        public static Color Mix(Color a, Color b, float t)
        {
            return Color.FromArgb(
                (int)(a.A + (b.A - a.A) * t),
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        public static Color PortColor(string type)
        {
            switch (type)
            {
                case "bool": return Color.FromArgb(0xD9, 0x77, 0x06);
                case "num": return Color.FromArgb(0x2F, 0x6F, 0xEB);
                case "str": return Color.FromArgb(0x16, 0xA3, 0x4A);
                case "array": return Color.FromArgb(0xEA, 0x58, 0x0A);
                default: return Color.FromArgb(0x6B, 0x72, 0x80);
            }
        }

        /// <summary>积木类别在浅色主题下的强调色（左侧竖条与图标）。</summary>
        public static Color CategoryAccent(string category)
        {
            if (IsLight)
            {
                switch (category)
                {
                    case BlockCategory.Logic: return Color.FromArgb(0xB4, 0x53, 0x09);
                    case BlockCategory.Var: return Color.FromArgb(0x1D, 0x4E, 0xD8);
                    case BlockCategory.Func: return Color.FromArgb(0x7E, 0x22, 0xCE);
                    case BlockCategory.Tool: return Color.FromArgb(0x6B, 0x72, 0x80);
                    default: return Color.FromArgb(0x15, 0x80, 0x3D);
                }
            }
            return BlockCategory.CategoryColor(category);
        }

        public static void ApplyDarkForm(Form f)
        {
            f.BackColor = Background;
            f.ForeColor = Text;
            f.Font = UiFont(9f, FontStyle.Regular);
        }

        public static void ApplyDarkTextBox(TextBox t)
        {
            t.BackColor = Theme.IsLight ? Color.White : PanelAlt;
            t.ForeColor = Text;
            t.BorderStyle = BorderStyle.FixedSingle;
            t.Font = UiFont(9f, FontStyle.Regular);
        }

        public static void ApplyDarkCombo(ComboBox c)
        {
            c.BackColor = Theme.IsLight ? Color.White : PanelAlt;
            c.ForeColor = Text;
            c.Font = UiFont(9f, FontStyle.Regular);
            if (Theme.IsLight)
            {
                // 浅色下用系统标准外观：否则自绘下拉会没有边框、箭头跑到很远，看着像被裁切
                c.FlatStyle = FlatStyle.Standard;
                c.DrawMode = DrawMode.Normal;
                return;
            }
            c.FlatStyle = FlatStyle.Flat;
            c.DrawMode = DrawMode.OwnerDrawFixed;
            c.ItemHeight = 22;
            c.DrawItem += delegate(object s, DrawItemEventArgs e)
            {
                if (e.Index < 0) return;
                bool sel = (e.State & DrawItemState.Selected) != 0;
                Color back = sel ? Mix(PanelAlt, Accent, 0.45f) : (Theme.IsLight ? Color.White : PanelAlt);
                using (SolidBrush b = new SolidBrush(back)) e.Graphics.FillRectangle(b, e.Bounds);
                string text = Convert.ToString(c.Items[e.Index]);
                TextRenderer.DrawText(e.Graphics, text, c.Font, e.Bounds, sel ? Color.White : Text,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            };
        }

        public static void StyleListView(ListView list)
        {
            list.OwnerDraw = true;
            list.BackColor = PanelAlt;
            list.ForeColor = Text;
            list.BorderStyle = BorderStyle.FixedSingle;
            list.DrawColumnHeader += delegate(object s, DrawListViewColumnHeaderEventArgs e)
            {
                using (SolidBrush b = new SolidBrush(Panel)) e.Graphics.FillRectangle(b, e.Bounds);
                using (Pen p = new Pen(Border)) e.Graphics.DrawLine(p, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
                TextRenderer.DrawText(e.Graphics, e.Header.Text, UiFont(8.5f, FontStyle.Bold), Rectangle.Inflate(e.Bounds, -6, 0), TextDim,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            };
            list.DrawItem += delegate(object s, DrawListViewItemEventArgs e) { e.DrawDefault = false; };
            list.DrawSubItem += delegate(object s, DrawListViewSubItemEventArgs e)
            {
                bool selected = e.Item.Selected;
                Color back = selected ? Mix(PanelAlt, Accent, 0.22f) : (e.ItemIndex % 2 == 0 ? PanelAlt : Mix(PanelAlt, Border, 0.35f));
                using (SolidBrush b = new SolidBrush(back)) e.Graphics.FillRectangle(b, e.Bounds);
                TextRenderer.DrawText(e.Graphics, e.SubItem.Text, UiFont(9f, FontStyle.Regular), Rectangle.Inflate(e.Bounds, -4, 0),
                    Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            };
        }
    }

    /// <summary>右键菜单渲染：跟随当前主题（浅色/深色）。</summary>
    internal sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkColorTable()) { }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Theme.Text : Theme.TextDim;
            base.OnRenderItemText(e);
        }
    }

    internal sealed class DarkColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return Theme.Panel; } }
        public override Color ImageMarginGradientBegin { get { return Theme.Panel; } }
        public override Color ImageMarginGradientMiddle { get { return Theme.Panel; } }
        public override Color ImageMarginGradientEnd { get { return Theme.Panel; } }
        public override Color MenuBorder { get { return Theme.Border; } }
        public override Color MenuItemBorder { get { return Theme.Accent; } }
        public override Color MenuItemSelected { get { return Theme.Mix(Theme.Panel, Theme.Accent, 0.18f); } }
        public override Color SeparatorDark { get { return Theme.Border; } }
        public override Color SeparatorLight { get { return Theme.Border; } }
    }

    public sealed class FlatButton : Button
    {
        private bool _hover;
        private bool _pressed;

        public FlatButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = Theme.PanelAlt;
            ForeColor = Theme.Text;
            Font = Theme.UiFont(9f, FontStyle.Regular);
            Cursor = Cursors.Hand;
            Height = 30;
        }

        public Color HoverColor = Color.Empty;
        public Color PressColor = Theme.Accent;
        public bool Outline;
        /// <summary>可选矢量符号：record / run / stop</summary>
        public string IconKind = "";

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { _pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color hover = HoverColor.IsEmpty ? Theme.Mix(BackColor, Theme.Accent, 0.12f) : HoverColor;
            Color back = _pressed ? PressColor : (_hover ? hover : BackColor);
            using (GraphicsPath p = Theme.RoundedRect(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 5))
            using (SolidBrush b = new SolidBrush(back))
                g.FillPath(b, p);
            if (Outline)
            {
                using (Pen pen = new Pen(Theme.Border, 1f))
                using (GraphicsPath p = Theme.RoundedRect(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 5))
                    g.DrawPath(pen, p);
            }
            Color fore = _pressed ? Color.White : ForeColor;
            if (!Enabled) fore = Theme.TextDim;
            Rectangle textRect = ClientRectangle;
            if (!string.IsNullOrEmpty(IconKind))
            {
                BlockIcons.DrawToolIcon(g, IconKind, new RectangleF(6, (Height - 16) / 2f, 16, 16), fore);
                textRect = new Rectangle(24, 0, Width - 28, Height);
            }
            TextRenderer.DrawText(g, Text, Font, textRect, fore,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }
}