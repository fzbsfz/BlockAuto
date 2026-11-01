using System;
using System.Drawing;
using System.Windows.Forms;

namespace BlockAuto
{
    /// <summary>
    /// 「吸取颜色」：全屏框选（放大镜式十字光标），取选区中心那个像素的颜色。
    /// 找色 / 颜色参数用，比手动输 #RRGGBB 直观。
    /// </summary>
    internal static class ScreenColorPicker
    {
        public static Color? Pick(IWin32Window owner)
        {
            Rectangle r = RoiPickerForm.Pick(owner, "吸取颜色：框住要取色的位置（取选区中心像素）");
            if (r.Width < 1 || r.Height < 1) return null;
            try
            {
                int x = r.Left + r.Width / 2;
                int y = r.Top + r.Height / 2;
                using (Bitmap bmp = ScreenCapture.Capture(new Rectangle(x, y, 1, 1)))
                    return bmp.GetPixel(0, 0);
            }
            catch
            {
                return null;
            }
        }
    }
}