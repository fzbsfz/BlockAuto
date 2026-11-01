using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace BlockAuto
{
    /// <summary>BlockRunner：脱离编辑器执行 .block，退出码 0/1/2/3。</summary>
    internal static class RunnerProgram
    {
        private sealed class ConsoleSink : ILogSink
        {
            public bool Silent;
            public void Write(LogEntry e)
            {
                if (Silent) return;
                ConsoleColor old = Console.ForegroundColor;
                try
                {
                    if (e.Level == LogLevel.Warn) Console.ForegroundColor = ConsoleColor.Yellow;
                    else if (e.Level == LogLevel.Error) Console.ForegroundColor = ConsoleColor.Red;
                    else Console.ForegroundColor = ConsoleColor.Gray;
                    Console.WriteLine("[" + e.Time.ToString("HH:mm:ss.fff") + "] [" + e.LevelText + "] [" + e.Source + "] " + e.Message);
                    if (!string.IsNullOrEmpty(e.Detail)) Console.WriteLine(e.Detail);
                }
                finally { Console.ForegroundColor = old; }
            }
        }

        private static int Main(string[] args)
        {
            try { Console.OutputEncoding = new UTF8Encoding(false); }
            catch { }
            Console.WriteLine("BlockRunner - BlockAuto 独立运行器 v" + EngineVersion.Full);
            Console.WriteLine();

            if (args.Length == 0)
            {
                Usage();
                return 2;
            }

            string script = null;
            double? threshold = null;
            string logPath = null;
            bool silent = false;
            bool noEsc = false;

            int i = 0;
            if (string.Equals(args[0], "run", StringComparison.OrdinalIgnoreCase)) i = 1;
            for (; i < args.Length; i++)
            {
                string a = args[i];
                if (a == "--threshold" && i + 1 < args.Length)
                {
                    double t;
                    if (!double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out t) || t <= 0 || t > 1)
                    {
                        Console.Error.WriteLine("无效的 --threshold，应为 0~1 之间的小数");
                        return 2;
                    }
                    threshold = t;
                }
                else if (a == "--log" && i + 1 < args.Length)
                {
                    logPath = args[++i];
                }
                else if (a == "--silent")
                {
                    silent = true;
                }
                else if (a == "--no-esc")
                {
                    noEsc = true;
                }
                else if (a == "--help" || a == "-h" || a == "/?")
                {
                    Usage();
                    return 0;
                }
                else if (script == null)
                {
                    script = a;
                }
                else
                {
                    Console.Error.WriteLine("无法识别的参数：" + a);
                    Usage();
                    return 2;
                }
            }

            if (script == null)
            {
                Usage();
                return 2;
            }

            if (silent)
            {
                try { Win32.ShowWindow(Win32.GetConsoleWindow(), 0); } catch { }
            }

            if (!File.Exists(script))
            {
                Console.Error.WriteLine("脚本不存在：" + script);
                return 2;
            }

            ILogSink fileLog = null;
            if (!string.IsNullOrEmpty(logPath))
            {
                try
                {
                    FileSink fs = new FileSink(logPath);
                    fs.Write(new LogEntry
                    {
                        Level = LogLevel.Info,
                        Source = "运行器",
                        Message = "==== BlockRunner " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + script + " ===="
                    });
                    fileLog = fs;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("日志文件不可写：" + ex.Message);
                    return 2;
                }
            }
            else if (silent)
            {
                fileLog = new Logger(AppPaths.LogDir, 14);
            }

            ConsoleSink console = new ConsoleSink();
            console.Silent = silent;
            List<ILogSink> sinks = new List<ILogSink>();
            sinks.Add(console);
            if (fileLog != null) sinks.Add(fileLog);
            MultiSink sink = new MultiSink(sinks);

            ScriptDocument doc;
            try
            {
                doc = ScriptDocument.Load(script);
            }
            catch (Exception ex)
            {
                sink.Write(new LogEntry { Level = LogLevel.Error, Source = "结构", Message = "脚本加载失败：" + ex.Message });
                return 2;
            }

            List<string> errors = doc.Validate();
            if (errors.Count > 0)
            {
                foreach (string e in errors)
                    sink.Write(new LogEntry { Level = LogLevel.Error, Source = "结构", Message = e });
                sink.Write(new LogEntry { Level = LogLevel.Error, Source = "结构", Message = "脚本结构校验失败，共 " + errors.Count + " 个问题" });
                return 2;
            }

            AppConfig cfg = AppConfig.Load();
            EngineOptions opt = new EngineOptions();
            opt.ThresholdOverride = threshold;
            opt.ActionDelayMs = cfg.ActionDelayMs;
            opt.MaxRecursion = cfg.MaxRecursion;
            opt.EnableEscapeStop = !noEsc;
            opt.BaseDirectory = Path.GetDirectoryName(Path.GetFullPath(script));

            DesktopAutomation auto = new DesktopAutomation();
            auto.CacheMilliseconds = cfg.ScreenshotCacheMs;
            Engine engine = new Engine(doc, opt, auto, sink);
            sink.Write(new LogEntry { Level = LogLevel.Info, Source = "运行器", Message = "开始执行：" + Path.GetFullPath(script) });
            if (threshold.HasValue)
                sink.Write(new LogEntry { Level = LogLevel.Info, Source = "运行器", Message = "识图阈值覆盖为 " + threshold.Value.ToString("0.00", CultureInfo.InvariantCulture) });
            sink.Write(new LogEntry { Level = LogLevel.Info, Source = "运行器", Message = "按 ESC 可紧急停止（--no-esc 可关闭）" });

            EngineResult result;
            try
            {
                result = engine.Run();
            }
            catch (Exception ex)
            {
                sink.Write(new LogEntry { Level = LogLevel.Error, Source = "运行器", Message = "执行异常：" + ex.Message });
                return 1;
            }
            finally
            {
                auto.Dispose();
            }

            switch (result.Status)
            {
                case EngineStatus.Completed:
                    sink.Write(new LogEntry { Level = LogLevel.Info, Source = "运行器", Message = "执行成功，退出码 0" });
                    return 0;
                case EngineStatus.Stopped:
                    sink.Write(new LogEntry { Level = LogLevel.Warn, Source = "运行器", Message = "已人工停止，退出码 3" });
                    return 3;
                case EngineStatus.StructureError:
                    sink.Write(new LogEntry { Level = LogLevel.Error, Source = "运行器", Message = "脚本结构错误，退出码 2" });
                    return 2;
                default:
                    sink.Write(new LogEntry { Level = LogLevel.Error, Source = "运行器", Message = "业务异常：" + result.Message + "，退出码 1" });
                    return 1;
            }
        }

        private static void Usage()
        {
            Console.WriteLine("用法：");
            Console.WriteLine("  BlockRunner.exe run <script.block> [--threshold 0.8] [--log log.txt] [--silent] [--no-esc]");
            Console.WriteLine("  BlockRunner.exe <script.block>");
            Console.WriteLine();
            Console.WriteLine("选项：");
            Console.WriteLine("  --threshold <0~1>  覆盖脚本内的识图阈值");
            Console.WriteLine("  --log <file>       追加日志到指定文件");
            Console.WriteLine("  --silent           静默模式：不显示窗口与控制台输出，日志写入文件");
            Console.WriteLine("  --no-esc           关闭 ESC 紧急停止监听");
            Console.WriteLine();
            Console.WriteLine("退出码：0=成功  1=业务异常  2=脚本结构错误  3=人工 ESC 停止");
        }
    }

    internal sealed class FileSink : ILogSink
    {
        private readonly string _path;
        private readonly object _lock = new object();

        public FileSink(string path)
        {
            _path = Path.GetFullPath(path);
            string dir = Path.GetDirectoryName(_path);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        }

        public void Write(LogEntry e)
        {
            lock (_lock)
            {
                string line = string.Format("{0:yyyy-MM-dd HH:mm:ss.fff}\t{1}\t{2}\t{3}{4}",
                    e.Time, e.LevelText, e.Source, e.Message,
                    string.IsNullOrEmpty(e.Detail) ? "" : Environment.NewLine + e.Detail);
                File.AppendAllText(_path, line + Environment.NewLine, new UTF8Encoding(false));
            }
        }
    }

    internal sealed class MultiSink : ILogSink
    {
        private readonly List<ILogSink> _sinks;
        public MultiSink(List<ILogSink> sinks) { _sinks = sinks; }
        public void Write(LogEntry entry)
        {
            foreach (ILogSink s in _sinks)
            {
                try { s.Write(entry); } catch { }
            }
        }
    }
}
