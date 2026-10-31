using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace BlockAuto
{
    /// <summary>
    /// 自绘积木小图标（16x16，GDI+），不依赖外部图片资源：
    /// 参考成熟键鼠工具的「图标化工具栏」观感，同时适配深色主题与任意 DPI。
    /// </summary>
    public static class BlockIcons
    {
        public const int Size = 16;

        public static void Draw(Graphics g, string type, RectangleF bounds, Color color)
        {
            GraphicsState state = g.Save();
            try
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                float s = Math.Min(bounds.Width, bounds.Height) / 16f;
                g.TranslateTransform(bounds.X, bounds.Y);
                g.ScaleTransform(s, s);
                using (Pen pen = new Pen(color, 1.4f))
                using (SolidBrush brush = new SolidBrush(color))
                {
                    switch (type)
                    {
                        case "start":
                            // 开始节点：绿色实心 ▶
                            using (GraphicsPath tri = new GraphicsPath())
                            {
                                tri.AddPolygon(new PointF[] { new PointF(3.5f, 2f), new PointF(13.5f, 8f), new PointF(3.5f, 14f) });
                                g.FillPath(brush, tri);
                            }
                            break;
                        case "mouse_click":
                            DrawMouse(g, pen, brush, false);
                            g.FillEllipse(brush, 1.5f, 1.5f, 4f, 4f);
                            break;
                        case "mouse_move":
                            DrawMouse(g, pen, brush, true);
                            break;
                        case "mouse_wheel":
                            DrawMouse(g, pen, brush, false);
                            using (Pen thin = new Pen(color, 1.2f))
                            {
                                g.DrawLine(thin, 8f, 3f, 8f, 6f);
                                g.DrawLine(thin, 5.5f, 5f, 8f, 2.6f);
                                g.DrawLine(thin, 10.5f, 5f, 8f, 2.6f);
                            }
                            break;
                        case "key_type":
                            DrawKeyboard(g, pen, brush, true);
                            break;
                        case "key_press":
                            DrawKeyboard(g, pen, brush, false);
                            break;
                        case "find_image":
                            DrawSearch(g, pen, brush);
                            break;
                        case "find_color":
                            g.FillEllipse(brush, 2f, 2f, 12f, 12f);
                            using (SolidBrush hole = new SolidBrush(Color.FromArgb(255, 40, 44, 54)))
                                g.FillEllipse(hole, 5.5f, 5.5f, 5f, 5f);
                            break;
                        case "if":
                            using (GraphicsPath p = new GraphicsPath())
                            {
                                p.AddPolygon(new PointF[] { new PointF(8f, 1f), new PointF(15f, 8f), new PointF(8f, 15f), new PointF(1f, 8f) });
                                g.DrawPath(pen, p);
                            }
                            g.FillEllipse(brush, 6.5f, 6.5f, 3f, 3f);
                            break;
                        case "while":
                        case "for_count":
                        case "for_each":
                            using (Pen arc = new Pen(color, 1.4f))
                            {
                                g.DrawArc(arc, 2f, 2f, 12f, 12f, 40f, 280f);
                            }
                            using (GraphicsPath arrow = new GraphicsPath())
                            {
                                arrow.AddPolygon(new PointF[] { new PointF(12.5f, 1.5f), new PointF(15.5f, 5f), new PointF(11f, 5.2f) });
                                g.FillPath(brush, arrow);
                            }
                            break;
                        case "sleep":
                            g.DrawEllipse(pen, 2f, 2f, 12f, 12f);
                            g.DrawLine(pen, 8f, 4.5f, 8f, 8.5f);
                            g.DrawLine(pen, 8f, 8.5f, 10.5f, 10f);
                            break;
                        case "create_var":
                            g.DrawRectangle(pen, 1.5f, 3.5f, 13f, 9f);
                            g.DrawLine(pen, 5.5f, 6f, 5.5f, 10f);
                            g.DrawLine(pen, 3.8f, 6f, 7.2f, 6f);
                            break;
                        case "set_var":
                        case "get_var":
                            DrawText(g, "x", color, false);
                            break;
                        case "compare":
                            DrawText(g, "=", color, true);
                            break;
                        case "logic_op":
                            DrawText(g, "&", color, true);
                            break;
                        case "math":
                            DrawText(g, "+", color, true);
                            break;
                        case "concat":
                            DrawText(g, "+", color, false);
                            break;
                        case "log":
                            g.DrawRectangle(pen, 2f, 3f, 12f, 10f);
                            g.DrawLine(pen, 4f, 6f, 12f, 6f);
                            g.DrawLine(pen, 4f, 8.5f, 12f, 8.5f);
                            g.DrawLine(pen, 4f, 11f, 9f, 11f);
                            break;
                        case "comment":
                            g.FillRectangle(brush, 1.5f, 2.5f, 2f, 11f);
                            g.DrawLine(pen, 5f, 4.5f, 14.5f, 4.5f);
                            g.DrawLine(pen, 5f, 8f, 14.5f, 8f);
                            g.DrawLine(pen, 5f, 11.5f, 11f, 11.5f);
                            break;
                        case "call_func":
                            g.DrawEllipse(pen, 2f, 2f, 12f, 12f);
                            g.DrawLine(pen, 6f, 5f, 10f, 5f);
                            g.DrawLine(pen, 8f, 5f, 8f, 11f);
                            break;
                        case "return":
                            g.DrawLine(pen, 2f, 8f, 12f, 8f);
                            g.DrawLine(pen, 8.5f, 4f, 13f, 8f);
                            g.DrawLine(pen, 8.5f, 12f, 13f, 8f);
                            break;
                        case "try":
                            g.DrawPolygon(pen, new PointF[] { new PointF(8f, 1.5f), new PointF(14f, 4f), new PointF(14f, 9f), new PointF(8f, 14.5f), new PointF(2f, 9f), new PointF(2f, 4f) });
                            break;
                        case "break":
                            g.DrawLine(pen, 3f, 3f, 3f, 13f);
                            g.FillPolygon(brush, new PointF[] { new PointF(5f, 3f), new PointF(13f, 8f), new PointF(5f, 13f) });
                            break;
                        case "continue":
                            DrawText(g, ">>", color, true);
                            break;
                        default:
                            g.DrawEllipse(pen, 3f, 3f, 10f, 10f);
                            break;
                    }
                }
            }
            finally
            {
                g.Restore(state);
            }
        }

        private static void DrawMouse(Graphics g, Pen pen, SolidBrush brush, bool move)
        {
            using (GraphicsPath p = new GraphicsPath())
            {
                p.AddArc(3.5f, 1.5f, 9f, 10f, 180, 180);
                p.AddLine(12.5f, 7f, 12.5f, 9.5f);
                p.AddArc(3.5f, 4.5f, 9f, 10f, 0, 180);
                p.CloseFigure();
                g.DrawPath(pen, p);
            }
            g.DrawLine(pen, 8f, 2f, 8f, 6.5f);
            if (move)
            {
                using (Pen thin = new Pen(pen.Color, 1.1f))
                {
                    g.DrawLine(thin, 11f, 12f, 14.5f, 15f);
                    g.DrawLine(thin, 14.5f, 15f, 14.5f, 12f);
                    g.DrawLine(thin, 14.5f, 15f, 11.5f, 15f);
                }
            }
        }

        private static void DrawKeyboard(Graphics g, Pen pen, SolidBrush brush, bool type)
        {
            g.DrawRectangle(pen, 1.5f, 4f, 13f, 8f);
            for (int i = 0; i < 3; i++)
                g.DrawLine(pen, 3.5f + i * 3.2f, 6.5f, 5f + i * 3.2f, 6.5f);
            g.DrawLine(pen, 3.5f, 9.5f, 12.5f, 9.5f);
            if (type)
            {
                using (SolidBrush b = new SolidBrush(Color.FromArgb(200, pen.Color)))
                    g.FillRectangle(b, 6.5f, 1f, 3f, 1.6f);
            }
        }

        private static void DrawSearch(Graphics g, Pen pen, SolidBrush brush)
        {
            g.DrawEllipse(pen, 1.8f, 1.8f, 9f, 9f);
            g.DrawLine(pen, 10f, 10f, 14.2f, 14.2f);
            using (SolidBrush b = new SolidBrush(Color.FromArgb(120, pen.Color)))
                g.FillEllipse(b, 5f, 5f, 2.6f, 2.6f);
        }

        private static void DrawText(Graphics g, string text, Color color, bool centered)
        {
            using (Font f = new Font("Segoe UI", 9f, FontStyle.Bold, GraphicsUnit.Pixel))
            using (SolidBrush b = new SolidBrush(color))
            {
                SizeF sz = g.MeasureString(text, f);
                g.DrawString(text, f, b, (16f - sz.Width) / 2f, (16f - sz.Height) / 2f);
            }
        }

        /// <summary>工具栏用的矢量符号：录制 / 运行 / 停止。</summary>
        public static void DrawToolIcon(Graphics g, string kind, RectangleF r, Color color)
        {
            GraphicsState st = g.Save();
            try
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (SolidBrush b = new SolidBrush(color))
                {
                    if (kind == "record")
                    {
                        g.FillEllipse(b, r.X + 3f, r.Y + 3f, r.Height - 6f, r.Height - 6f);
                    }
                    else if (kind == "run")
                    {
                        g.FillPolygon(b, new PointF[]
                        {
                            new PointF(r.X + 4f, r.Y + 3f),
                            new PointF(r.X + r.Width - 3f, r.Y + r.Height / 2f),
                            new PointF(r.X + 4f, r.Y + r.Height - 3f)
                        });
                    }
                    else if (kind == "stop")
                    {
                        g.FillRectangle(b, r.X + 4f, r.Y + 4f, r.Width - 8f, r.Height - 8f);
                    }
                }
            }
            finally
            {
                g.Restore(st);
            }
        }
    }
}