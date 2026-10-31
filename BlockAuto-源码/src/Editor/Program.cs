using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Collections.Generic;
using System.Text;
using System.Windows.Forms;

namespace BlockAuto
{
    internal static class Program
    {
        internal static bool SuppressWelcome;
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            ScreenCapture.EnsureDpiAware();
            Theme.InitScale();
            if (args != null && args.Length >= 2 && args[0] == "--uicheck")
            {
                RunUiCheck(args);
                return;
            }
            if (args != null && args.Length >= 2 && args[0] == "--capture")
            {
                RunCaptureCheck(args);
                return;
            }
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object sender, System.Threading.ThreadExceptionEventArgs e)
            {
                MessageBox.Show("发生未处理的异常：\n" + e.Exception.Message + "\n\n" + e.Exception.StackTrace,
                    "BlockAuto", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
            {
                Exception ex = e.ExceptionObject as Exception;
                MessageBox.Show("发生严重错误：\n" + (ex != null ? ex.Message : "未知错误"),
                    "BlockAuto", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            try
            {
                Application.Run(new MainForm());
            }
            catch (Exception ex)
            {
                MessageBox.Show("启动失败：\n" + ex.Message + "\n\n" + ex.StackTrace, "BlockAuto",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>诊断：按屏幕坐标截取指定区域，用于确认截图没有缩放/偏移。</summary>
        private static void RunCaptureCheck(string[] args)
        {
            try
            {
                string outPath = Path.GetFullPath(args[1]);
                Rectangle vs = System.Windows.Forms.SystemInformation.VirtualScreen;
                Rectangle region = vs;
                string log = "screen=" + vs + "\r\n";
                if (args.Length >= 6)
                {
                    int x = int.Parse(args[2]), y = int.Parse(args[3]), w = int.Parse(args[4]), h = int.Parse(args[5]);
                    region = new Rectangle(x, y, w, h);
                }
                using (Bitmap bmp = ScreenCapture.Capture(region))
                {
                    bmp.Save(outPath, ImageFormat.Png);
                    log += "captured=" + bmp.Width + "x" + bmp.Height + " region=" + region + "\r\n";
                }
                File.WriteAllText(outPath + ".txt", log);
            }
            catch (Exception ex)
            {
                try { File.WriteAllText(args[1] + ".txt", "ERR " + ex); } catch { }
            }
        }

        private static void RunDialogChecks(MainForm owner, string outPath, StringBuilder report)
        {
            ScriptDocument doc = owner.CurrentDocument;
            Block first = doc.Blocks.Count > 0 ? doc.Blocks[0] : null;
            List<KeyValuePair<string, Func<Form>>> items = new List<KeyValuePair<string, Func<Form>>>();
            if (first != null)
                items.Add(new KeyValuePair<string, Func<Form>>("param", delegate { return new ParamDialog(doc, first); }));
            items.Add(new KeyValuePair<string, Func<Form>>("function", delegate { return new FunctionDialog(doc, null); }));
            items.Add(new KeyValuePair<string, Func<Form>>("settings", delegate { return new SettingsForm(AppConfig.Load()); }));
            items.Add(new KeyValuePair<string, Func<Form>>("hotkeyedit", delegate { return new HotkeyEditDialog(null); }));
            items.Add(new KeyValuePair<string, Func<Form>>("scheduleedit", delegate { return new ScheduleEditDialog(null); }));
            items.Add(new KeyValuePair<string, Func<Form>>("textinput", delegate { return new TextInputDialog("输入", "名称", "demo"); }));
            items.Add(new KeyValuePair<string, Func<Form>>("templates", delegate { return new TemplateManagerForm(doc, true); }));
            HotkeyManager hk = new HotkeyManager();
            items.Add(new KeyValuePair<string, Func<Form>>("hotkeys", delegate { return new HotkeyForm(hk); }));
            items.Add(new KeyValuePair<string, Func<Form>>("schedules", delegate { return new ScheduleForm(new Scheduler()); }));
            items.Add(new KeyValuePair<string, Func<Form>>("recorder", delegate { return new RecorderForm(doc, doc.Blocks); }));
            items.Add(new KeyValuePair<string, Func<Form>>("network", delegate { return new NetworkForm(AppConfig.Load(), null); }));
            if (!string.IsNullOrEmpty(doc.FilePath))
                items.Add(new KeyValuePair<string, Func<Form>>("pack", delegate { return new ScriptPackDialog(doc, System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BlockRunner.exe")); }));

            foreach (KeyValuePair<string, Func<Form>> item in items)
            {
                Form dlg = null;
                try
                {
                    dlg = item.Value();
                    dlg.StartPosition = FormStartPosition.Manual;
                    dlg.Location = new Point(0, 0);
                    dlg.Show(owner);
                    Application.DoEvents();
                    using (Bitmap b = new Bitmap(dlg.Width, dlg.Height, PixelFormat.Format32bppArgb))
                    {
                        dlg.DrawToBitmap(b, new Rectangle(0, 0, dlg.Width, dlg.Height));
                        b.Save(outPath + "." + item.Key + ".png", ImageFormat.Png);
                    }
                    report.AppendLine("DIALOG " + item.Key + " OK " + dlg.Width + "x" + dlg.Height);
                }
                catch (Exception ex)
                {
                    report.AppendLine("DIALOG " + item.Key + " ERR " + ex.GetType().Name + ": " + ex.Message);
                }
                finally
                {
                    if (dlg != null)
                    {
                        try { dlg.Close(); dlg.Dispose(); } catch { }
                    }
                }
            }
            try { hk.Dispose(); } catch { }
        }

        /// <summary>界面自检：启动主窗口、加载示例、截图保存后退出（用于自动化验收）。</summary>
        private static void RunUiCheck(string[] args)
        {
            string outPath = Path.GetFullPath(args[1]);
            string block = args.Length >= 3 ? args[2] : null;
            string logPath = outPath + ".txt";
            try
            {
                Application.ThreadException += delegate(object s, System.Threading.ThreadExceptionEventArgs te)
                {
                    try { File.AppendAllText(logPath, "\nTHREAD_ERR " + te.Exception); } catch { }
                };
                SuppressWelcome = true;
                MainForm form = new MainForm();
                if (!string.IsNullOrEmpty(block)) form.LoadDocumentForCheck(block);
                form.Show();
                form.Activate();
                form.RunForCheck();
                Timer timer = new Timer();
                timer.Interval = 1600;
                timer.Tick += delegate
                {
                    timer.Stop();
                    try
                    {
                        form.PerformLayout();
                        Application.DoEvents();
                        using (Bitmap bmp = new Bitmap(form.Width, form.Height, PixelFormat.Format32bppArgb))
                        {
                            form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));
                            bmp.Save(outPath, ImageFormat.Png);
                        }
                        form.ExpandStepForCheck(0);
                        Application.DoEvents();
                        form.SnapSidePanelForCheck();
                        DateTime untilSide = DateTime.Now.AddMilliseconds(200);
                        while (DateTime.Now < untilSide) { Application.DoEvents(); System.Threading.Thread.Sleep(10); }
                        using (Bitmap sideBmp = new Bitmap(form.Width, form.Height, PixelFormat.Format32bppArgb))
                        {
                            form.DrawToBitmap(sideBmp, new Rectangle(0, 0, form.Width, form.Height));
                            sideBmp.Save(outPath + ".side.png", ImageFormat.Png);
                        }
                        form.HideSidePanelForCheck();
                        form.ExpandPaletteForCheck();
                        Application.DoEvents();
                        using (Bitmap palBmp = new Bitmap(form.Width, form.Height, PixelFormat.Format32bppArgb))
                        {
                            form.DrawToBitmap(palBmp, new Rectangle(0, 0, form.Width, form.Height));
                            palBmp.Save(outPath + ".palette.png", ImageFormat.Png);
                        }
                        form.CollapsePaletteForCheck();
                        Application.DoEvents();
                        form.ExpandStepForCheck(1);
                        Application.DoEvents();
                        form.SnapSidePanelForCheck();
                        DateTime until = DateTime.Now.AddMilliseconds(200);
                        while (DateTime.Now < until) { Application.DoEvents(); System.Threading.Thread.Sleep(10); }
                        using (Bitmap expBmp = new Bitmap(form.Width, form.Height, PixelFormat.Format32bppArgb))
                        {
                            form.DrawToBitmap(expBmp, new Rectangle(0, 0, form.Width, form.Height));
                            expBmp.Save(outPath + ".expanded.png", ImageFormat.Png);
                            expBmp.Save(outPath + ".drawer.png", ImageFormat.Png);
                        }
                        form.HideSidePanelForCheck();
                        Application.DoEvents();
                        form.ExpandStepForCheck(4);
                        Application.DoEvents();
                        using (Bitmap boxBmp = new Bitmap(form.Width, form.Height, PixelFormat.Format32bppArgb))
                        {
                            form.DrawToBitmap(boxBmp, new Rectangle(0, 0, form.Width, form.Height));
                            boxBmp.Save(outPath + ".container.png", ImageFormat.Png);
                        }
                        form.ExpandStepForCheck(3);
                        Application.DoEvents();
                        using (Bitmap ifBmp = new Bitmap(form.Width, form.Height, PixelFormat.Format32bppArgb))
                        {
                            form.DrawToBitmap(ifBmp, new Rectangle(0, 0, form.Width, form.Height));
                            ifBmp.Save(outPath + ".expanded-if.png", ImageFormat.Png);
                        }
                        form.ShowCodeForCheck();
                        Application.DoEvents();
                        using (Bitmap codeBmp = new Bitmap(form.Width, form.Height, PixelFormat.Format32bppArgb))
                        {
                            form.DrawToBitmap(codeBmp, new Rectangle(0, 0, form.Width, form.Height));
                            codeBmp.Save(outPath + ".code.png", ImageFormat.Png);
                        }
                        File.WriteAllText(outPath + ".code.txt", form.CodeTextForCheck());
                        form.LoadCanvasTabForCheck();
                        Application.DoEvents();
                        StringBuilder report = new StringBuilder();
                        report.AppendLine("OK " + form.LayoutInfo() + " title=" + form.Text);
                        report.AppendLine("RUN " + form.RunStateForCheck());
                        RunDialogChecks(form, outPath, report);
                        File.WriteAllText(logPath, report.ToString());
                    }
                    catch (Exception ex)
                    {
                        File.WriteAllText(logPath, "CAPTURE_ERR " + ex);
                    }
                    form.Close();
                    Application.Exit();
                };
                timer.Start();
                Application.Run(form);
            }
            catch (Exception ex)
            {
                try { File.WriteAllText(logPath, "START_ERR " + ex); } catch { }
                Environment.ExitCode = 1;
            }
        }
    }
}
