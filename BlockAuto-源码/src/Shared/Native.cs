using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace BlockAuto
{
    // ---------------------------- Win32 声明 ----------------------------

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct INPUT_UNION
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct INPUT
    {
        public uint type;
        public INPUT_UNION u;
    }

    internal static class Win32
    {
        public const int INPUT_MOUSE = 0;
        public const int INPUT_KEYBOARD = 1;

        public const uint KEYEVENTF_KEYUP = 0x0002;
        public const uint KEYEVENTF_UNICODE = 0x0004;
        public const uint KEYEVENTF_SCANCODE = 0x0008;

        public const uint MOUSEEVENTF_WHEEL = 0x0800;
        public const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        public const uint MOUSEEVENTF_LEFTUP = 0x0004;
        public const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        public const uint MOUSEEVENTF_RIGHTUP = 0x0010;

        public const int VK_ESCAPE = 0x1B;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        public static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT p);

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        [DllImport("user32.dll")]
        public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);

        [DllImport("user32.dll")]
        public static extern IntPtr FindWindow(string cls, string title);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

        [DllImport("user32.dll")]
        public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("kernel32.dll")]
        public static extern IntPtr GetConsoleWindow();

        [DllImport("user32.dll")]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        public static string GetWindowTitle(IntPtr hwnd)
        {
            StringBuilder sb = new StringBuilder(512);
            GetWindowText(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }

        public static IntPtr FindWindowByTitle(string part)
        {
            if (string.IsNullOrEmpty(part)) return IntPtr.Zero;
            IntPtr found = IntPtr.Zero;
            EnumWindows(delegate(IntPtr h, IntPtr l)
            {
                if (!IsWindowVisible(h)) return true;
                string t = GetWindowTitle(h);
                if (t.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    found = h;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            return found;
        }
    }

    // ---------------------------- 区域解析 ----------------------------

    public static class RegionParser
    {
        public static Rectangle Parse(string region)
        {
            Rectangle vs = System.Windows.Forms.SystemInformation.VirtualScreen;
            string raw = (region ?? "").Trim();
            if (raw.Equals("active", StringComparison.OrdinalIgnoreCase) || raw.Equals("window", StringComparison.OrdinalIgnoreCase))
            {
                Rectangle active = ActiveWindowRect();
                if (active.Width > 4 && active.Height > 4) return active;
                return vs;
            }
            if (string.IsNullOrEmpty(region) || raw.Equals("full", StringComparison.OrdinalIgnoreCase))
                return vs;
            string s = region.Replace("，", ",").Trim();
            string[] parts = s.Split(',');
            if (parts.Length >= 4)
            {
                int a, b, c, d;
                if (int.TryParse(parts[0].Trim(), out a) && int.TryParse(parts[1].Trim(), out b)
                    && int.TryParse(parts[2].Trim(), out c) && int.TryParse(parts[3].Trim(), out d))
                {
                    int x1 = Math.Min(a, c);
                    int y1 = Math.Min(b, d);
                    int x2 = Math.Max(a, c);
                    int y2 = Math.Max(b, d);
                    Rectangle r = Rectangle.FromLTRB(x1, y1, x2, y2);
                    r.Intersect(vs);
                    if (r.Width > 0 && r.Height > 0) return r;
                }
            }
            return vs;
        }

        /// <summary>当前前台窗口的屏幕矩形；失败时返回全屏。</summary>
        public static Rectangle ActiveWindowRect()
        {
            try
            {
                IntPtr h = Win32.GetForegroundWindow();
                if (h != IntPtr.Zero)
                {
                    RECT rc;
                    if (Win32.GetWindowRect(h, out rc))
                    {
                        Rectangle r = Rectangle.FromLTRB(rc.Left, rc.Top, rc.Right, rc.Bottom);
                        r.Intersect(System.Windows.Forms.SystemInformation.VirtualScreen);
                        if (r.Width > 4 && r.Height > 4) return r;
                    }
                }
            }
            catch { }
            return System.Windows.Forms.SystemInformation.VirtualScreen;
        }

        public static string FromRectangle(Rectangle r)
        {
            return string.Format("{0},{1},{2},{3}", r.Left, r.Top, r.Right, r.Bottom);
        }
    }

    // ---------------------------- 屏幕采集 ----------------------------

    public static class ScreenCapture
    {
        private static bool _dpiSet;

        /// <summary>
        /// 默认让进程保持 DPI 非感知（由系统按逻辑像素缩放），保证 UI 尺寸、
        /// 屏幕坐标与截图坐标一致，跨缩放比例的机器点击不会错位。
        /// 设置环境变量 BLOCKAUTO_DPI_AWARE=1 可开启物理像素模式。
        /// </summary>
        /// <summary>始终按物理像素工作：高 DPI（125%/150%/200%）下截图与鼠标坐标才不会错位。</summary>
        public static void EnsureDpiAware()
        {
            if (_dpiSet) return;
            _dpiSet = true;
            try { Win32.SetProcessDPIAware(); } catch { }
        }

        /// <summary>当前系统 DPI 相对 96 的倍率（100% = 1，200% = 2）。</summary>
        public static float DpiScale
        {
            get
            {
                EnsureDpiAware();
                try
                {
                    using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                        return Math.Max(1f, g.DpiX / 96f);
                }
                catch { return 1f; }
            }
        }

        public static Bitmap Capture(Rectangle rect)
        {
            EnsureDpiAware();
            if (rect.Width <= 0 || rect.Height <= 0) throw new ArgumentException("截图区域无效");
            Bitmap bmp = new Bitmap(rect.Width, rect.Height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(rect.Left, rect.Top, 0, 0, rect.Size, CopyPixelOperation.SourceCopy);
            }
            return bmp;
        }

        public static Bitmap CaptureFullScreen()
        {
            return Capture(System.Windows.Forms.SystemInformation.VirtualScreen);
        }

        public static Bitmap CaptureWindow(IntPtr hwnd)
        {
            EnsureDpiAware();
            RECT rc;
            if (!Win32.GetWindowRect(hwnd, out rc)) return null;
            int w = rc.Right - rc.Left;
            int h = rc.Bottom - rc.Top;
            if (w <= 0 || h <= 0) return null;
            Bitmap bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                IntPtr hdc = g.GetHdc();
                bool ok = Win32.PrintWindow(hwnd, hdc, 2); // PW_RENDERFULLCONTENT
                g.ReleaseHdc(hdc);
                if (!ok)
                {
                    g.CopyFromScreen(rc.Left, rc.Top, 0, 0, new Size(w, h), CopyPixelOperation.SourceCopy);
                }
            }
            return bmp;
        }
    }

    // ---------------------------- 模板匹配 ----------------------------

    internal sealed class GrayImage
    {
        public int Width;
        public int Height;
        public byte[] Data;

        public GrayImage(int w, int h)
        {
            Width = w;
            Height = h;
            Data = new byte[Math.Max(1, w * h)];
        }
    }

    public static class TemplateMatcher
    {
        private const int Coarse = 4;

        public static Bitmap ToGrayBitmap(Bitmap src)
        {
            Bitmap g = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
            using (Graphics gr = Graphics.FromImage(g))
            {
                gr.DrawImage(src, new Rectangle(0, 0, src.Width, src.Height));
            }
            return g;
        }

        internal static GrayImage ToGray(Bitmap src)
        {
            return ToGray(src, 0, 0, src.Width, src.Height, 1);
        }

        internal static GrayImage ToGray(Bitmap src, int x, int y, int w, int h, int scale)
        {
            GrayImage g = new GrayImage(w / scale, h / scale);
            BitmapData bd = src.LockBits(new Rectangle(x, y, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                unsafe
                {
                    byte* p = (byte*)bd.Scan0;
                    int gw = g.Width, gh = g.Height;
                    for (int gy = 0; gy < gh; gy++)
                    {
                        for (int gx = 0; gx < gw; gx++)
                        {
                            int sum = 0;
                            int count = 0;
                            for (int sy = 0; sy < scale; sy++)
                            {
                                byte* row = p + (long)(gy * scale + sy) * bd.Stride;
                                for (int sx = 0; sx < scale; sx++)
                                {
                                    int px = (gx * scale + sx) * 4;
                                    int b = row[px], gg = row[px + 1], r = row[px + 2];
                                    sum += (r * 299 + gg * 587 + b * 114) / 1000;
                                    count++;
                                }
                            }
                            g.Data[gy * gw + gx] = (byte)(sum / Math.Max(1, count));
                        }
                    }
                }
            }
            finally
            {
                src.UnlockBits(bd);
            }
            return g;
        }

        public static FindImageResult Match(Bitmap screen, Bitmap template, Rectangle region, double threshold)
        {
            return Match(screen, template, region, threshold, null);
        }

        /// <summary>
        /// 模板匹配。scales 为额外尝试的缩放比例（如 0.9 / 1.1），用于适配系统缩放差异；
        /// 为 null 时只按 1:1 匹配。
        /// </summary>
        public static FindImageResult Match(Bitmap screen, Bitmap template, Rectangle region, double threshold, double[] scales)
        {
            FindImageResult best = MatchAtScale(screen, template, region, 1.0, threshold);
            if (scales == null || scales.Length == 0) return best;
            foreach (double s in scales)
            {
                if (Math.Abs(s - 1.0) < 0.001) continue;
                FindImageResult r = MatchAtScale(screen, template, region, s, threshold);
                if (r.Found && (!best.Found || r.Confidence > best.Confidence)) best = r;
            }
            return best;
        }

        private static Bitmap ScaleTemplate(Bitmap template, double scale)
        {
            int w = Math.Max(3, (int)Math.Round(template.Width * scale));
            int h = Math.Max(3, (int)Math.Round(template.Height * scale));
            Bitmap scaled = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(scaled))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                g.DrawImage(template, new Rectangle(0, 0, w, h));
            }
            return scaled;
        }

        private static FindImageResult MatchAtScale(Bitmap screen, Bitmap template, Rectangle region, double scale, double threshold)
        {
            FindImageResult result = new FindImageResult();
            if (screen == null || template == null) return result;
            Bitmap tplBitmap = template;
            bool temp = false;
            if (Math.Abs(scale - 1.0) > 0.001)
            {
                tplBitmap = ScaleTemplate(template, scale);
                temp = true;
            }
            try
            {
                if (region.Width < tplBitmap.Width || region.Height < tplBitmap.Height) return result;

                GrayImage hay = ToGray(screen, region.X, region.Y, region.Width, region.Height, 1);
                GrayImage tpl = ToGray(tplBitmap);
                if (tpl.Width < 3 || tpl.Height < 3) return result;

                GrayImage hayC = Downscale(hay, Coarse);
                GrayImage tplC = Downscale(tpl, Coarse);

                double bestVal;
                int bestX, bestY;
                NCC(hayC, tplC, out bestVal, out bestX, out bestY);

                // 全分辨率精修
                int cx = bestX * Coarse;
                int cy = bestY * Coarse;
                int radius = Coarse * 2 + 2;
                double refinedVal;
                int rx, ry;
                NCCLocal(hay, tpl, cx - radius, cy - radius, cx + radius, cy + radius, out refinedVal, out rx, out ry);
                if (refinedVal > bestVal)
                {
                    bestVal = refinedVal;
                    bestX = rx;
                    bestY = ry;
                }
                else
                {
                    bestX = cx;
                    bestY = cy;
                }

                double conf = Math.Max(0, Math.Min(1, bestVal));
                result.Confidence = conf;
                result.X = region.X + bestX + tpl.Width / 2.0;
                result.Y = region.Y + bestY + tpl.Height / 2.0;
                result.Found = conf >= threshold;
                return result;
            }
            finally
            {
                if (temp && tplBitmap != null) tplBitmap.Dispose();
            }
        }

        private static GrayImage Downscale(GrayImage src, int factor)
        {
            int w = Math.Max(1, src.Width / factor);
            int h = Math.Max(1, src.Height / factor);
            GrayImage g = new GrayImage(w, h);
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int sum = 0;
                    int count = 0;
                    for (int sy = 0; sy < factor; sy++)
                    {
                        int yy = y * factor + sy;
                        if (yy >= src.Height) break;
                        for (int sx = 0; sx < factor; sx++)
                        {
                            int xx = x * factor + sx;
                            if (xx >= src.Width) break;
                            sum += src.Data[yy * src.Width + xx];
                            count++;
                        }
                    }
                    g.Data[y * w + x] = (byte)(sum / Math.Max(1, count));
                }
            }
            return g;
        }

        private static void NCC(GrayImage hay, GrayImage tpl, out double best, out int bestX, out int bestY)
        {
            best = -2;
            bestX = 0;
            bestY = 0;
            int tw = tpl.Width, th = tpl.Height;
            int w = hay.Width - tw + 1;
            int h = hay.Height - th + 1;
            if (w <= 0 || h <= 0) return;

            double tmean = 0;
            for (int i = 0; i < tpl.Data.Length; i++) tmean += tpl.Data[i];
            tmean /= tpl.Data.Length;
            double[] tz = new double[tpl.Data.Length];
            double tnorm = 0;
            for (int i = 0; i < tpl.Data.Length; i++)
            {
                tz[i] = tpl.Data[i] - tmean;
                tnorm += tz[i] * tz[i];
            }
            tnorm = Math.Sqrt(tnorm);
            if (tnorm < 1e-9) return;
            int n = tpl.Data.Length;

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    double sum = 0, sum2 = 0, sumIT = 0;
                    for (int ty = 0; ty < th; ty++)
                    {
                        int rowOffset = (y + ty) * hay.Width + x;
                        int trow = ty * tw;
                        for (int tx = 0; tx < tw; tx++)
                        {
                            double v = hay.Data[rowOffset + tx];
                            sum += v;
                            sum2 += v * v;
                            sumIT += v * tz[trow + tx];
                        }
                    }
                    double var = sum2 - sum * sum / n;
                    if (var <= 1e-9) continue;
                    double denom = Math.Sqrt(var) * tnorm;
                    double v2 = sumIT / denom;
                    if (v2 > best)
                    {
                        best = v2;
                        bestX = x;
                        bestY = y;
                    }
                }
            }
        }

        private static void NCCLocal(GrayImage hay, GrayImage tpl, int x0, int y0, int x1, int y1, out double best, out int bestX, out int bestY)
        {
            best = -2; bestX = 0; bestY = 0;
            int tw = tpl.Width, th = tpl.Height;
            double tmean = 0;
            for (int i = 0; i < tpl.Data.Length; i++) tmean += tpl.Data[i];
            tmean /= tpl.Data.Length;
            double[] tz = new double[tpl.Data.Length];
            double tnorm = 0;
            for (int i = 0; i < tpl.Data.Length; i++)
            {
                tz[i] = tpl.Data[i] - tmean;
                tnorm += tz[i] * tz[i];
            }
            tnorm = Math.Sqrt(tnorm);
            if (tnorm < 1e-9) return;
            int n = tpl.Data.Length;

            int maxX = hay.Width - tw;
            int maxY = hay.Height - th;
            x0 = Math.Max(0, x0); y0 = Math.Max(0, y0);
            x1 = Math.Min(maxX, x1); y1 = Math.Min(maxY, y1);
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    double sum = 0, sum2 = 0, sumIT = 0;
                    for (int ty = 0; ty < th; ty++)
                    {
                        int rowOffset = (y + ty) * hay.Width + x;
                        int trow = ty * tw;
                        for (int tx = 0; tx < tw; tx++)
                        {
                            double v = hay.Data[rowOffset + tx];
                            sum += v;
                            sum2 += v * v;
                            sumIT += v * tz[trow + tx];
                        }
                    }
                    double var = sum2 - sum * sum / n;
                    if (var <= 1e-9) continue;
                    double denom = Math.Sqrt(var) * tnorm;
                    double val = sumIT / denom;
                    if (val > best)
                    {
                        best = val;
                        bestX = x;
                        bestY = y;
                    }
                }
            }
        }

        public static FindColorResult FindColor(Bitmap bmp, Rectangle region, Color color, int tolerance)
        {
            FindColorResult res = new FindColorResult();
            int w = region.Width, h = region.Height;
            if (w <= 0 || h <= 0) return res;
            BitmapData bd = bmp.LockBits(new Rectangle(region.X, region.Y, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                unsafe
                {
                    byte* p = (byte*)bd.Scan0;
                    for (int y = 0; y < h; y++)
                    {
                        byte* row = p + (long)y * bd.Stride;
                        for (int x = 0; x < w; x++)
                        {
                            int b = row[x * 4], g = row[x * 4 + 1], r = row[x * 4 + 2];
                            if (Math.Abs(r - color.R) <= tolerance && Math.Abs(g - color.G) <= tolerance && Math.Abs(b - color.B) <= tolerance)
                            {
                                res.Found = true;
                                res.X = region.X + x;
                                res.Y = region.Y + y;
                                return res;
                            }
                        }
                    }
                }
            }
            finally
            {
                bmp.UnlockBits(bd);
            }
            return res;
        }
    }

    // ---------------------------- 键鼠输入 ----------------------------

    public static class InputSimulator
    {
        private static readonly Dictionary<string, int> Keys = BuildKeys();

        private static Dictionary<string, int> BuildKeys()
        {
            Dictionary<string, int> d = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < 26; i++) d[((char)('a' + i)).ToString()] = 0x41 + i;
            for (int i = 0; i <= 9; i++) d[i.ToString()] = 0x30 + i;
            for (int i = 1; i <= 12; i++) d["f" + i] = 0x70 + (i - 1);
            d["enter"] = 0x0D;
            d["return"] = 0x0D;
            d["space"] = 0x20;
            d["tab"] = 0x09;
            d["esc"] = 0x1B;
            d["escape"] = 0x1B;
            d["backspace"] = 0x08;
            d["delete"] = 0x2E;
            d["del"] = 0x2E;
            d["insert"] = 0x2D;
            d["home"] = 0x24;
            d["end"] = 0x23;
            d["pageup"] = 0x21;
            d["pagedown"] = 0x22;
            d["up"] = 0x26;
            d["down"] = 0x28;
            d["left"] = 0x25;
            d["right"] = 0x27;
            d["capslock"] = 0x14;
            d["printscreen"] = 0x2C;
            d["win"] = 0x5B;
            d["minus"] = 0xBD;
            d["plus"] = 0xBB;
            d["comma"] = 0xBC;
            d["period"] = 0xBE;
            d["slash"] = 0xBF;
            d["semicolon"] = 0xBA;
            d["quote"] = 0xDE;
            d["bracketleft"] = 0xDB;
            d["bracketright"] = 0xDD;
            d["backslash"] = 0xDC;
            d["grave"] = 0xC0;
            return d;
        }

        public static bool MoveTo(int x, int y)
        {
            return Win32.SetCursorPos(x, y);
        }

        public static void Click(int x, int y, string button, string action)
        {
            MoveTo(x, y);
            Thread.Sleep(30);
            bool right = string.Equals(button, "right", StringComparison.OrdinalIgnoreCase);
            uint down = right ? Win32.MOUSEEVENTF_RIGHTDOWN : Win32.MOUSEEVENTF_LEFTDOWN;
            uint up = right ? Win32.MOUSEEVENTF_RIGHTUP : Win32.MOUSEEVENTF_LEFTUP;
            int count = string.Equals(action, "dblclick", StringComparison.OrdinalIgnoreCase) ? 2 : 1;
            for (int i = 0; i < count; i++)
            {
                SendMouse(down, 0);
                Thread.Sleep(25);
                SendMouse(up, 0);
                if (i + 1 < count) Thread.Sleep(40);
            }
        }

        public static void Wheel(string dir, int amount)
        {
            int delta = 120 * Math.Abs(amount);
            if (string.Equals(dir, "up", StringComparison.OrdinalIgnoreCase)) delta = -delta;
            SendMouse(Win32.MOUSEEVENTF_WHEEL, unchecked((uint)delta));
        }

        private static void SendMouse(uint flags, uint data)
        {
            INPUT[] inputs = new INPUT[1];
            inputs[0].type = Win32.INPUT_MOUSE;
            inputs[0].u.mi.dwFlags = flags;
            inputs[0].u.mi.mouseData = data;
            Win32.SendInput(1, inputs, Marshal.SizeOf(typeof(INPUT)));
        }

        public static void TypeText(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            foreach (char c in text)
            {
                SendUnicode(c, false);
                SendUnicode(c, true);
                Thread.Sleep(6);
            }
        }

        private static void SendUnicode(char c, bool up)
        {
            INPUT[] inputs = new INPUT[1];
            inputs[0].type = Win32.INPUT_KEYBOARD;
            inputs[0].u.ki.wVk = 0;
            inputs[0].u.ki.wScan = c;
            inputs[0].u.ki.dwFlags = Win32.KEYEVENTF_UNICODE | (up ? Win32.KEYEVENTF_KEYUP : 0);
            Win32.SendInput(1, inputs, Marshal.SizeOf(typeof(INPUT)));
        }

        /// <summary>校验按键写法是否可执行（只看键表，不真的发送按键）。</summary>
        public static bool IsValidKeySpec(string spec)
        {
            if (string.IsNullOrEmpty(spec)) return false;
            bool hasKey = false;
            foreach (string raw in spec.Replace(" ", "").Split('+'))
            {
                string p = raw.Trim().ToLowerInvariant();
                if (p.Length == 0) continue;
                if (p == "ctrl" || p == "control" || p == "alt" || p == "shift" || p == "win" || p == "meta") continue;
                int vk;
                if (!Keys.TryGetValue(p, out vk)) return false;
                hasKey = true;
            }
            return hasKey;
        }

        public static bool PressKey(string spec)
        {
            if (string.IsNullOrEmpty(spec)) return false;
            string[] parts = spec.Replace(" ", "").Split('+');
            List<int> mods = new List<int>();
            List<int> keys = new List<int>();
            foreach (string raw in parts)
            {
                string p = raw.Trim().ToLowerInvariant();
                if (p.Length == 0) continue;
                if (p == "ctrl" || p == "control") { mods.Add(0x11); continue; }
                if (p == "alt") { mods.Add(0x12); continue; }
                if (p == "shift") { mods.Add(0x10); continue; }
                if (p == "win" || p == "meta") { mods.Add(0x5B); continue; }
                int vk;
                if (!Keys.TryGetValue(p, out vk)) return false;
                keys.Add(vk);
            }
            if (keys.Count == 0) return false;
            foreach (int m in mods) KeyDown(m);
            foreach (int k in keys) { KeyDown(k); Thread.Sleep(15); KeyUp(k); }
            for (int i = mods.Count - 1; i >= 0; i--) KeyUp(mods[i]);
            return true;
        }

        private static void KeyDown(int vk)
        {
            INPUT[] inputs = new INPUT[1];
            inputs[0].type = Win32.INPUT_KEYBOARD;
            inputs[0].u.ki.wVk = (ushort)vk;
            inputs[0].u.ki.dwFlags = 0;
            Win32.SendInput(1, inputs, Marshal.SizeOf(typeof(INPUT)));
        }

        private static void KeyUp(int vk)
        {
            INPUT[] inputs = new INPUT[1];
            inputs[0].type = Win32.INPUT_KEYBOARD;
            inputs[0].u.ki.wVk = (ushort)vk;
            inputs[0].u.ki.dwFlags = Win32.KEYEVENTF_KEYUP;
            Win32.SendInput(1, inputs, Marshal.SizeOf(typeof(INPUT)));
        }

        public static bool EscapeDown()
        {
            return (Win32.GetAsyncKeyState(Win32.VK_ESCAPE) & 0x8000) != 0;
        }

        public static Point CursorPosition()
        {
            POINT p;
            Win32.GetCursorPos(out p);
            return new Point(p.X, p.Y);
        }
    }

    // ---------------------------- 自动化实现 ----------------------------

    /// <summary>识图/找色/键鼠的真实实现，带截图缓存与模板缓存。</summary>
    public sealed class DesktopAutomation : IAutomation, IDisposable
    {
        private readonly object _lock = new object();
        private Bitmap _cachedScreen;
        private DateTime _cacheTime = DateTime.MinValue;
        private int _cacheMs = 250;
        private readonly Dictionary<string, TemplateEntry> _templates = new Dictionary<string, TemplateEntry>(StringComparer.OrdinalIgnoreCase);
        private string _lastError = "";

        private sealed class TemplateEntry
        {
            public Bitmap Bitmap;
            public DateTime Modified;
        }

        public int CacheMilliseconds
        {
            get { return _cacheMs; }
            set { _cacheMs = Math.Max(0, value); }
        }

        /// <summary>识图时额外尝试的缩放比例（如 0.9/1.1），用于适配不同系统缩放；空表示只按 1:1。</summary>
        public double[] MatchScales { get; set; }

        public string LastError { get { return _lastError; } }

        public FindImageResult FindImage(string templatePath, string region, double threshold)
        {
            _lastError = "";
            try
            {
                Bitmap tpl = LoadTemplate(templatePath);
                if (tpl == null)
                {
                    _lastError = "模板加载失败：" + templatePath;
                    return new FindImageResult();
                }
                Rectangle area = RegionParser.Parse(region);
                lock (_lock)
                {
                    Bitmap screen = GetScreen();
                    Rectangle local = new Rectangle(area.X - ScreenBounds.VirtualScreen.X,
                        area.Y - ScreenBounds.VirtualScreen.Y, area.Width, area.Height);
                    FindImageResult r = TemplateMatcher.Match(screen, tpl, local, threshold, MatchScales);
                    // 还原为屏幕绝对坐标
                    r.X += ScreenBounds.VirtualScreen.X;
                    r.Y += ScreenBounds.VirtualScreen.Y;
                    return r;
                }
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                return new FindImageResult();
            }
        }

        public FindColorResult FindColor(string rgb, string region, int tolerance)
        {
            _lastError = "";
            try
            {
                Color color = ParseColor(rgb);
                Rectangle area = RegionParser.Parse(region);
                lock (_lock)
                {
                    Bitmap screen = GetScreen();
                    Rectangle local = new Rectangle(area.X - ScreenBounds.VirtualScreen.X,
                        area.Y - ScreenBounds.VirtualScreen.Y, area.Width, area.Height);
                    local.Intersect(new Rectangle(0, 0, screen.Width, screen.Height));
                    if (local.Width <= 0 || local.Height <= 0) return new FindColorResult();
                    FindColorResult r = TemplateMatcher.FindColor(screen, local, color, tolerance);
                    r.X += ScreenBounds.VirtualScreen.X;
                    r.Y += ScreenBounds.VirtualScreen.Y;
                    return r;
                }
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                return new FindColorResult();
            }
        }

        public bool MouseClick(bool hasX, double x, bool hasY, double y, string button, string action, double dx, double dy)
        {
            try
            {
                int px, py;
                if (hasX || hasY)
                {
                    Point cur = InputSimulator.CursorPosition();
                    px = (int)Math.Round((hasX ? x : cur.X) + dx);
                    py = (int)Math.Round((hasY ? y : cur.Y) + dy);
                }
                else
                {
                    Point cur = InputSimulator.CursorPosition();
                    px = cur.X;
                    py = cur.Y;
                }
                if (px < -32000) px = 0;
                if (py < -32000) py = 0;
                InputSimulator.Click(px, py, button, action);
                return true;
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                return false;
            }
        }

        public bool MouseMove(double x, double y)
        {
            try { return InputSimulator.MoveTo((int)Math.Round(x), (int)Math.Round(y)); }
            catch (Exception ex) { _lastError = ex.Message; return false; }
        }

        public bool MouseWheel(string dir, int amount)
        {
            try { InputSimulator.Wheel(dir, amount); return true; }
            catch (Exception ex) { _lastError = ex.Message; return false; }
        }

        public bool KeyType(string text)
        {
            try { InputSimulator.TypeText(text); return true; }
            catch (Exception ex) { _lastError = ex.Message; return false; }
        }

        public bool KeyPress(string key)
        {
            try { return InputSimulator.PressKey(key); }
            catch (Exception ex) { _lastError = ex.Message; return false; }
        }

        public bool CheckEscape()
        {
            return InputSimulator.EscapeDown();
        }

        private Bitmap GetScreen()
        {
            if (_cachedScreen != null && (DateTime.Now - _cacheTime).TotalMilliseconds <= _cacheMs)
                return _cachedScreen;
            if (_cachedScreen != null) { _cachedScreen.Dispose(); _cachedScreen = null; }
            _cachedScreen = ScreenCapture.CaptureFullScreen();
            _cacheTime = DateTime.Now;
            return _cachedScreen;
        }

        private Bitmap LoadTemplate(string path)
        {
            if (!File.Exists(path)) return null;
            DateTime mod = File.GetLastWriteTimeUtc(path);
            TemplateEntry e;
            if (_templates.TryGetValue(path, out e) && e.Modified == mod) return e.Bitmap;
            if (e != null && e.Bitmap != null) e.Bitmap.Dispose();
            Bitmap bmp = new Bitmap(path);
            _templates[path] = new TemplateEntry { Bitmap = bmp, Modified = mod };
            return bmp;
        }

        public void InvalidateCache()
        {
            lock (_lock)
            {
                if (_cachedScreen != null) { _cachedScreen.Dispose(); _cachedScreen = null; }
                _cacheTime = DateTime.MinValue;
            }
        }

        public static Color ParseColor(string rgb)
        {
            if (string.IsNullOrEmpty(rgb)) return Color.Red;
            string s = rgb.Trim();
            if (s.StartsWith("#")) s = s.Substring(1);
            if (s.Length == 3)
                s = new string(new char[] { s[0], s[0], s[1], s[1], s[2], s[2] });
            if (s.Length != 6) return Color.Red;
            try
            {
                int v = int.Parse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                return Color.FromArgb((v >> 16) & 0xFF, (v >> 8) & 0xFF, v & 0xFF);
            }
            catch { return Color.Red; }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_cachedScreen != null) { _cachedScreen.Dispose(); _cachedScreen = null; }
                foreach (TemplateEntry e in _templates.Values) if (e.Bitmap != null) e.Bitmap.Dispose();
                _templates.Clear();
            }
        }
    }

    public static class ScreenBounds
    {
        public static Rectangle VirtualScreen
        {
            get { return System.Windows.Forms.SystemInformation.VirtualScreen; }
        }
    }
}


