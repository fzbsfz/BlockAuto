using System;
using System.IO;
using Microsoft.Win32;

namespace BlockAuto
{
    /// <summary>
    /// .block 文件关联：让保存出来的脚本双击即可由 BlockRunner 运行，
    /// 而不是被记事本当成文本打开。只写当前用户 HKCU，不需要管理员。
    /// </summary>
    public static class FileAssoc
    {
        private const string ExtKey = @"Software\Classes\.block";
        private const string ProgId = "BlockAuto.Script";
        private const string ProgKey = @"Software\Classes\BlockAuto.Script";

        public static string RunnerPath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BlockRunner.exe"); }
        }

        public static bool IsAssociated()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(ExtKey))
                {
                    if (k == null) return false;
                    string v = Convert.ToString(k.GetValue(""));
                    if (!string.Equals(v, ProgId, StringComparison.OrdinalIgnoreCase)) return false;
                }
                using (RegistryKey cmd = Registry.CurrentUser.OpenSubKey(ProgKey + @"\shell\open\command"))
                {
                    if (cmd == null) return false;
                    string c = Convert.ToString(cmd.GetValue(""));
                    return c != null && c.IndexOf("BlockRunner.exe", StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
            catch { return false; }
        }

        public static string Associate()
        {
            try
            {
                string runner = RunnerPath;
                if (!File.Exists(runner)) return "找不到 BlockRunner.exe：" + runner;

                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(ExtKey))
                    k.SetValue("", ProgId);
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(ProgKey))
                    k.SetValue("", "BlockAuto 积木脚本");
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(ProgKey + @"\DefaultIcon"))
                    k.SetValue("", "\"" + runner + "\",0");
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(ProgKey + @"\shell\open\command"))
                    k.SetValue("", "\"" + runner + "\" run \"%1\"");

                // 通知资源管理器刷新图标/关联
                try { NativeMethods.SHChangeNotify(0x08000000, 0x1000, IntPtr.Zero, IntPtr.Zero); } catch { }
                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        public static string Unassociate()
        {
            try
            {
                try { Registry.CurrentUser.DeleteSubKeyTree(ExtKey, false); } catch { }
                try { Registry.CurrentUser.DeleteSubKeyTree(ProgKey, false); } catch { }
                try { NativeMethods.SHChangeNotify(0x08000000, 0x1000, IntPtr.Zero, IntPtr.Zero); } catch { }
                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        private static class NativeMethods
        {
            [System.Runtime.InteropServices.DllImport("shell32.dll")]
            public static extern void SHChangeNotify(int eventId, int flags, IntPtr item1, IntPtr item2);
        }
    }
}