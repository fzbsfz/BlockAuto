using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace BlockAuto
{
    /// <summary>
    /// 「截图取图」：框选屏幕一块 → 直接存进脚本旁边的 templates/ → 返回相对路径。
    /// 鼠标点击积木的自动识图、判断积木的「有图片」都用它取模板，不再需要单独截图另存。
    /// </summary>
    internal static class TemplateCapture
    {
        public static string SaveRegionToTemplates(ScriptDocument doc, Rectangle region)
        {
            if (doc == null || region.Width < 4 || region.Height < 4) return null;
            try
            {
                string dir = Path.Combine(doc.Directory, "templates");
                Directory.CreateDirectory(dir);
                string name = NextName(dir);
                using (Bitmap bmp = ScreenCapture.Capture(region))
                    bmp.Save(Path.Combine(dir, name), ImageFormat.Png);
                return "templates/" + name;
            }
            catch (Exception ex)
            {
                MessageBox.Show("截图保存失败：" + ex.Message, "BlockAuto",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return null;
            }
        }

        /// <summary>
        /// 「截图位置」用：截取坐标点周围一小块存成图，作为“这一步点的是哪个东西”的凭证。
        /// 返回相对路径，失败返回 null。
        /// </summary>
        public static string SaveThumbAroundPoint(ScriptDocument doc, int x, int y, int size)
        {
            if (doc == null) return null;
            try
            {
                Rectangle vs = System.Windows.Forms.SystemInformation.VirtualScreen;
                int half = Math.Max(8, size / 2);
                Rectangle r = new Rectangle(x - half, y - half, half * 2, half * 2);
                Rectangle safe = Rectangle.Intersect(r, vs);
                if (safe.Width < 4 || safe.Height < 4) return null;

                string dir = Path.Combine(doc.Directory, "templates");
                Directory.CreateDirectory(dir);
                string name = "click-" + DateTime.Now.ToString("HHmmss-fff") + ".png";
                using (Bitmap bmp = ScreenCapture.Capture(safe))
                    bmp.Save(Path.Combine(dir, name), ImageFormat.Png);
                return "templates/" + name;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>框选一块屏幕区域并保存；用户取消或失败返回 null。</summary>
        public static string PickAndSave(IWin32Window owner, ScriptDocument doc, string title)
        {
            Rectangle r = RoiPickerForm.Pick(owner, string.IsNullOrEmpty(title) ? "截图取图" : title);
            if (r.Width < 4 || r.Height < 4) return null;
            return SaveRegionToTemplates(doc, r);
        }

        private static string NextName(string dir)
        {
            string stamp = DateTime.Now.ToString("HHmmss");
            for (int i = 0; i < 10000; i++)
            {
                string name = "shot-" + stamp + (i == 0 ? "" : "-" + i) + ".png";
                if (!File.Exists(Path.Combine(dir, name))) return name;
            }
            return "shot-" + DateTime.Now.ToString("HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".png";
        }
    }
}