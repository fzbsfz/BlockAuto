using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace BlockAuto
{
    // ---------------------------- 日志 ----------------------------

    public enum LogLevel { Info, Warn, Error }

    public sealed class LogEntry
    {
        public DateTime Time = DateTime.Now;
        public LogLevel Level;
        public string Source = "引擎";
        public string Message = "";
        public string Detail = "";

        public string LevelText
        {
            get
            {
                switch (Level)
                {
                    case LogLevel.Warn: return "WARN";
                    case LogLevel.Error: return "ERROR";
                    default: return "INFO";
                }
            }
        }

        public override string ToString()
        {
            string s = string.Format("[{0:HH:mm:ss}] [{1}] [{2}] {3}", Time, LevelText, Source, Message);
            if (!string.IsNullOrEmpty(Detail)) s += Environment.NewLine + Detail;
            return s;
        }
    }

    public interface ILogSink
    {
        void Write(LogEntry entry);
    }

    public sealed class NullLogSink : ILogSink
    {
        public void Write(LogEntry entry) { }
    }

    /// <summary>按天写文件的日志器，可同时转发到 UI 面板。</summary>
    public sealed class Logger : ILogSink
    {
        private readonly object _lock = new object();
        private readonly string _dir;
        private readonly int _retentionDays;
        private string _currentFile;
        private DateTime _currentDay;

        public event Action<LogEntry> EntryWritten;

        public Logger(string dir, int retentionDays)
        {
            _dir = dir;
            _retentionDays = retentionDays;
            try { if (!System.IO.Directory.Exists(_dir)) System.IO.Directory.CreateDirectory(_dir); }
            catch { }
        }

        public void Write(LogEntry entry)
        {
            lock (_lock)
            {
                try
                {
                    EnsureFile();
                    string line = string.Format("{0:yyyy-MM-dd HH:mm:ss.fff}\t{1}\t{2}\t{3}{4}",
                        entry.Time, entry.LevelText, entry.Source, entry.Message,
                        string.IsNullOrEmpty(entry.Detail) ? "" : Environment.NewLine + entry.Detail);
                    File.AppendAllText(_currentFile, line + Environment.NewLine, new UTF8Encoding(false));
                }
                catch { }
            }
            Action<LogEntry> h = EntryWritten;
            if (h != null) { try { h(entry); } catch { } }
        }

        private void EnsureFile()
        {
            DateTime today = DateTime.Today;
            if (_currentFile != null && _currentDay == today) return;
            if (!System.IO.Directory.Exists(_dir)) System.IO.Directory.CreateDirectory(_dir);
            _currentDay = today;
            _currentFile = Path.Combine(_dir, "blockauto-" + today.ToString("yyyyMMdd") + ".log");
            Cleanup();
        }

        private void Cleanup()
        {
            if (_retentionDays <= 0) return;
            try
            {
                DateTime limit = DateTime.Today.AddDays(-_retentionDays);
                foreach (string f in System.IO.Directory.GetFiles(_dir, "blockauto-*.log"))
                {
                    if (File.GetLastWriteTime(f) < limit) File.Delete(f);
                }
            }
            catch { }
        }
    }

    // ---------------------------- 自动化抽象 ----------------------------

    public sealed class FindImageResult
    {
        public bool Found;
        public double X;
        public double Y;
        public double Confidence;
    }

    public sealed class FindColorResult
    {
        public bool Found;
        public double X;
        public double Y;
    }

    public interface IAutomation
    {
        FindImageResult FindImage(string templatePath, string region, double threshold);
        FindColorResult FindColor(string rgb, string region, int tolerance);
        bool MouseClick(bool hasX, double x, bool hasY, double y, string button, string action, double dx, double dy);
        bool MouseMove(double x, double y);
        bool MouseWheel(string dir, int amount);
        bool KeyType(string text);
        bool KeyPress(string key);
        bool CheckEscape();
        string LastError { get; }
    }

    /// <summary>仅用于单元测试 / 无界面验证的自动化桩。</summary>
    public sealed class MockAutomation : IAutomation
    {
        public readonly List<string> Calls = new List<string>();
        public FindImageResult NextImage = new FindImageResult { Found = false };
        public FindColorResult NextColor = new FindColorResult { Found = false };
        public bool NextSuccess = true;
        private string _error = "";

        public FindImageResult FindImage(string templatePath, string region, double threshold)
        {
            Calls.Add("find_image|" + templatePath + "|" + region + "|" + threshold.ToString("0.##", CultureInfo.InvariantCulture));
            return NextImage;
        }

        public FindColorResult FindColor(string rgb, string region, int tolerance)
        {
            Calls.Add("find_color|" + rgb + "|" + region + "|" + tolerance);
            return NextColor;
        }

        public bool MouseClick(bool hasX, double x, bool hasY, double y, string button, string action, double dx, double dy)
        {
            Calls.Add(string.Format("mouse_click|{0},{1}|{2}|{3}|{4},{5}", hasX ? x.ToString() : "cur", hasY ? y.ToString() : "cur", button, action, dx, dy));
            return NextSuccess;
        }

        public bool MouseMove(double x, double y) { Calls.Add("mouse_move|" + x + "," + y); return NextSuccess; }
        public bool MouseWheel(string dir, int amount) { Calls.Add("mouse_wheel|" + dir + "|" + amount); return NextSuccess; }
        public bool KeyType(string text) { Calls.Add("key_type|" + text); return NextSuccess; }
        public bool KeyPress(string key) { Calls.Add("key_press|" + key); return NextSuccess; }

        public bool EscapePressed;
        public bool CheckEscape() { return EscapePressed; }
        public string LastError { get { return _error; } }
    }

    // ---------------------------- 引擎选项与结果 ----------------------------

    public sealed class EngineOptions
    {
        public double? ThresholdOverride;
        public double ActionDelayMs = 80;
        public int MaxRecursion = 100;
        public bool EnableEscapeStop = true;
        public string BaseDirectory;
        public bool LogToConsole;
    }

    public enum EngineStatus { Completed, Failed, Stopped, StructureError }

    public sealed class EngineResult
    {
        public EngineStatus Status;
        public string Message = "";
        public long DurationMs;
        /// <summary>出错积木（用于界面定位高亮）</summary>
        public string FailedBlockId = "";
        public string FailedBlockTitle = "";
        /// <summary>出错时已经执行的积木步数</summary>
        public long FailedStep;
    }

    // ---------------------------- 控制流信号 ----------------------------

    internal sealed class StopSignal : Exception { }
    internal sealed class BreakSignal : Exception { }
    internal sealed class ContinueSignal : Exception { }
    internal sealed class ReturnSignal : Exception
    {
        public readonly object Value;
        public ReturnSignal(object v) { Value = v; }
    }

    public sealed class EngineException : Exception
    {
        public string BlockId;
        public string BlockTitle;
        public EngineException(string message) : base(message) { }
        public EngineException(string message, Exception inner) : base(message, inner) { }
    }

    internal sealed class FunctionFrame
    {
        public string Name;
        public int Depth;
    }

    internal sealed class RunContext
    {
        public Dictionary<string, object> Globals = new Dictionary<string, object>();
        public List<Dictionary<string, object>> Locals = new List<Dictionary<string, object>>();
        public List<FunctionFrame> CallStack = new List<FunctionFrame>();
        public Dictionary<string, Dictionary<string, object>> Outputs = new Dictionary<string, Dictionary<string, object>>();
        public HashSet<string> Evaluating = new HashSet<string>();

        public Dictionary<string, object> CurrentLocals
        {
            get { return Locals.Count > 0 ? Locals[Locals.Count - 1] : null; }
        }
    }

    // ---------------------------- 解释引擎 ----------------------------

    /// <summary>
    /// 积木解释执行引擎：画布调试 / 定时调度 / BlockRunner 共用同一实现。
    /// </summary>
    public sealed class Engine
    {
        private readonly ScriptDocument _doc;
        private readonly EngineOptions _opt;
        private readonly IAutomation _auto;
        private readonly ILogSink _log;

        private volatile bool _stopRequested;
        private volatile bool _paused;
        private volatile bool _stepMode;
        private readonly object _gate = new object();

        private DateTime _startTime;
        private long _blockCounter;
        private int _actionDelayMs;
        private double _defaultThreshold;

        public event Action<Block> BlockEntering;
        public event Action<Block> BlockExecuted;
        public event Action Paused;
        public event Action Resumed;

        public Block CurrentBlock { get; private set; }
        public bool IsPaused { get { return _paused; } }
        public bool IsRunning { get; private set; }
        public bool StartPaused;
        public EngineStatus? FinalStatus { get; private set; }

        public Engine(ScriptDocument doc, EngineOptions opt, IAutomation automation, ILogSink log)
        {
            _doc = doc;
            _opt = opt ?? new EngineOptions();
            _auto = automation;
            _log = log ?? new NullLogSink();

            _actionDelayMs = (int)Math.Max(0, _opt.ActionDelayMs);
            _defaultThreshold = _opt.ThresholdOverride ?? Json.GetNumber(doc.Settings, "default_threshold", 0.8);
            if (_defaultThreshold <= 0) _defaultThreshold = 0.8;
        }

        public static string NormalizeScriptKey(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            try { return Path.GetFullPath(path).ToLowerInvariant(); }
            catch { return path.ToLowerInvariant(); }
        }

        private static readonly HashSet<string> RunningKeys = new HashSet<string>();
        private static readonly object RunningLock = new object();

        public static bool TryAcquire(string path)
        {
            lock (RunningLock)
            {
                string k = NormalizeScriptKey(path);
                if (RunningKeys.Contains(k)) return false;
                RunningKeys.Add(k);
                return true;
            }
        }

        public static void Release(string path)
        {
            lock (RunningLock) RunningKeys.Remove(NormalizeScriptKey(path));
        }

        public static bool IsScriptRunning(string path)
        {
            lock (RunningLock) return RunningKeys.Contains(NormalizeScriptKey(path));
        }

        // ---------------------------- 外部控制 ----------------------------

        public void Stop()
        {
            _stopRequested = true;
            lock (_gate) Monitor.PulseAll(_gate);
        }

        public void Pause()
        {
            if (!IsRunning) return;
            _paused = true;
            _stepMode = false;
            Log(LogLevel.Info, "调试", "已暂停");
            Fire(Paused);
        }

        public void Resume()
        {
            _paused = false;
            _stepMode = false;
            lock (_gate) Monitor.PulseAll(_gate);
            Log(LogLevel.Info, "调试", "继续运行");
            Fire(Resumed);
        }

        public void StepOnce()
        {
            _stepMode = true;
            _paused = false;
            lock (_gate) Monitor.PulseAll(_gate);
            Fire(Resumed);
        }

        // ---------------------------- 主流程 ----------------------------

        public EngineResult Run()
        {
            EngineResult result = new EngineResult();
            _startTime = DateTime.Now;
            IsRunning = true;
            _stopRequested = false;
            _paused = false;
            _stepMode = false;
            _blockCounter = 0;
            FinalStatus = null;

            List<string> errors = _doc.Validate();
            if (errors.Count > 0)
            {
                foreach (string e in errors) Log(LogLevel.Error, "结构", e);
                result.Status = EngineStatus.StructureError;
                FinalStatus = result.Status;
                result.Message = "脚本结构错误：" + errors[0];
                IsRunning = false;
                result.DurationMs = (long)(DateTime.Now - _startTime).TotalMilliseconds;
                return result;
            }

            RunContext ctx = new RunContext();
            foreach (VarDef v in _doc.GlobalVars)
                ctx.Globals[v.Name] = v.DefaultValue();
            ctx.Locals.Add(new Dictionary<string, object>());
            try
            {
                ExecuteList(_doc.Blocks, ctx);
                result.Status = EngineStatus.Completed;
                result.Message = "执行完成";
            }
            catch (StopSignal)
            {
                result.Status = EngineStatus.Stopped;
                result.Message = "已停止";
            }
            catch (ReturnSignal)
            {
                result.Status = EngineStatus.Failed;
                result.Message = "return 只能出现在函数内部";
                Log(LogLevel.Error, "引擎", result.Message);
            }
            catch (BreakSignal)
            {
                result.Status = EngineStatus.Failed;
                result.Message = "break 不在循环内";
                Log(LogLevel.Error, "引擎", result.Message);
            }
            catch (ContinueSignal)
            {
                result.Status = EngineStatus.Failed;
                result.Message = "continue 不在循环内";
                Log(LogLevel.Error, "引擎", result.Message);
            }
            catch (EngineException ex)
            {
                result.Status = EngineStatus.Failed;
                result.FailedBlockId = ex.BlockId;
                result.FailedBlockTitle = ex.BlockTitle;
                result.FailedStep = _blockCounter;
                result.Message = string.IsNullOrEmpty(ex.BlockTitle)
                    ? ex.Message
                    : "「" + ex.BlockTitle + "」执行失败：" + ex.Message;
                Log(LogLevel.Error, "引擎", result.Message);
            }
            catch (Exception ex)
            {
                result.Status = EngineStatus.Failed;
                result.Message = ex.Message;
                Log(LogLevel.Error, "引擎", "未预期的异常：" + ex.Message);
                Log(LogLevel.Error, "引擎", ex.GetType().Name);
            }
            finally
            {
                IsRunning = false;
                CurrentBlock = null;
                FinalStatus = result.Status;
                result.DurationMs = (long)(DateTime.Now - _startTime).TotalMilliseconds;
                string statusText;
                switch (result.Status)
                {
                    case EngineStatus.Completed: statusText = "执行完成"; break;
                    case EngineStatus.Stopped: statusText = "已停止"; break;
                    case EngineStatus.StructureError: statusText = "结构错误"; break;
                    default: statusText = "执行失败"; break;
                }
                Log(result.Status == EngineStatus.Completed ? LogLevel.Info : (result.Status == EngineStatus.Failed ? LogLevel.Error : LogLevel.Warn),
                    "引擎", string.Format("脚本结束：{0}（{1} ms）", statusText, result.DurationMs));
            }
            return result;
        }

        private void ExecuteList(List<Block> list, RunContext ctx)
        {
            if (list == null) return;
            foreach (Block b in list)
            {
                CheckStop();
                if (b.Disabled)
                {
                    Log(LogLevel.Info, "引擎", "跳过已禁用步骤：" + Catalog.TitleOf(b.Type));
                    continue;
                }
                BeforeBlock(b);
                CurrentBlock = b;
                try
                {
                    ExecuteBlock(b, ctx);
                }
                catch (EngineException bex)
                {
                    if (string.IsNullOrEmpty(bex.BlockId))
                    {
                        bex.BlockId = b.Id;
                        bex.BlockTitle = Catalog.TitleOf(b.Type);
                    }
                    throw;
                }
                _blockCounter++;
                Fire2(BlockExecuted, b);
                AfterBlock();
            }
            CurrentBlock = null;
        }

        private void BeforeBlock(Block b)
        {
            CheckStop();
            if (StartPaused)
            {
                StartPaused = false;
                _paused = true;
                CurrentBlock = b;
                Log(LogLevel.Info, "调试", "已暂停，等待单步或继续");
                Fire(Paused);
            }
            if (_paused) WaitResume();
            if (b.Breakpoint)
            {
                _paused = true;
                CurrentBlock = b;
                Log(LogLevel.Info, "调试", "命中断点：" + Catalog.TitleOf(b.Type) + "（" + b.Id + "）");
                Fire(Paused);
                WaitResume();
            }
            FireBlock(BlockEntering, b);
        }

        private void AfterBlock()
        {
            if (_stepMode)
            {
                _paused = true;
                _stepMode = false;
                Log(LogLevel.Info, "调试", "单步暂停");
                Fire(Paused);
            }
        }

        private void WaitResume()
        {
            while (_paused && !_stopRequested)
            {
                CheckStop();
                lock (_gate) Monitor.Wait(_gate, 100);
            }
            CheckStop();
        }

        private void CheckStop()
        {
            if (_stopRequested) throw new StopSignal();
            if (_opt.EnableEscapeStop && _auto != null && _auto.CheckEscape())
            {
                _stopRequested = true;
                Log(LogLevel.Warn, "引擎", "检测到 ESC，紧急停止");
                throw new StopSignal();
            }
        }

        // ---------------------------- 积木执行 ----------------------------

        private void ExecuteBlock(Block b, RunContext ctx)
        {
            switch (b.Type)
            {
                case "comment":
                    return;
                case "if":
                    ExecuteIf(b, ctx);
                    return;
                case "while":
                    ExecuteWhile(b, ctx);
                    return;
                case "for_count":
                    ExecuteForCount(b, ctx);
                    return;
                case "for_each":
                    ExecuteForEach(b, ctx);
                    return;
                case "break":
                    throw new BreakSignal();
                case "continue":
                    throw new ContinueSignal();
                case "sleep":
                    Sleep(Json.ToNumber(ParamValue(b, "seconds", ctx), 0));
                    return;
                case "try":
                    ExecuteTry(b, ctx);
                    return;
                case "create_var":
                    ExecuteCreateVar(b, ctx);
                    return;
                case "set_var":
                    ExecuteSetVar(b, ctx);
                    return;
                case "get_var":
                    SetOutput(ctx, b, "value", ReadVar(ctx, Json.GetString(b.Params, "name", "")));
                    return;
                case "compare":
                    SetOutput(ctx, b, "result", CompareValue(b, ctx));
                    return;
                case "logic_op":
                    SetOutput(ctx, b, "result", LogicValue(b, ctx));
                    return;
                case "math":
                    SetOutput(ctx, b, "result", MathValue(b, ctx));
                    return;
                case "concat":
                    SetOutput(ctx, b, "result", ConcatValue(b, ctx));
                    return;
                case "return":
                    throw new ReturnSignal(EvalInput(b, "value", ctx));
                case "call_func":
                    SetOutput(ctx, b, "result", CallFunction(b, ctx));
                    return;
                case "find_image":
                    ExecuteFindImage(b, ctx);
                    return;
                case "find_color":
                    ExecuteFindColor(b, ctx);
                    return;
                case "mouse_click":
                    ExecuteMouseClick(b, ctx);
                    return;
                case "mouse_move":
                    ExecuteMouseMove(b, ctx);
                    return;
                case "mouse_wheel":
                    ExecuteMouseWheel(b, ctx);
                    return;
                case "key_type":
                    ExecuteKeyType(b, ctx);
                    return;
                case "key_press":
                    ExecuteKeyPress(b, ctx);
                    return;
                case "log":
                    ExecuteLog(b, ctx);
                    return;
                default:
                    throw new EngineException("未知积木类型：" + b.Type + "（" + b.Id + "）");
            }
        }

        private void ExecuteIf(Block b, RunContext ctx)
        {
            List<BranchSpec> specs = Catalog.Branches(b);
            for (int i = 0; i < b.Branches.Count && i < specs.Count; i++)
            {
                BranchSpec spec = specs[i];
                // 没有条件端口 = 「否则」分支：前面都不成立就走这里
                if (spec.ConditionPort == null)
                {
                    ExecuteList(b.Branches[i].Blocks, ctx);
                    return;
                }
                string ctype = BranchConditionType(b, spec, i);
                bool take;
                if (ctype == "image") take = EvaluateImageCondition(b, spec, i, ctx);
                else if (ctype == "color") take = EvaluateColorCondition(b, spec, i, ctx);
                else take = Json.ToBool(EvalInput(b, spec.ConditionPort, ctx), false);
                if (take)
                {
                    ExecuteList(b.Branches[i].Blocks, ctx);
                    return;
                }
            }
        }

        /// <summary>分支条件类型：cond（积木连线）/ image（有图片）/ color（找色）。</summary>
        private string BranchConditionType(Block b, BranchSpec spec, int index)
        {
            string t = Json.GetString(b.Params, "condtype_" + spec.Name, "");
            if (t.Length > 0) return t;
            // 兼容旧脚本：主分支沿用 cond_type
            if (index == 0) return Json.GetString(b.Params, "cond_type", "cond");
            return "cond";
        }

        /// <summary>分支参数取值：主分支用旧参数名，其它分支用「参数名_分支名」。</summary>
        private string BranchText(Block b, BranchSpec spec, int index, string baseName, string def)
        {
            if (index > 0)
            {
                string v = Json.GetString(b.Params, baseName + "_" + spec.Name, "");
                if (v.Length > 0) return v;
            }
            return Json.GetString(b.Params, baseName, def);
        }

        private double BranchNumber(Block b, BranchSpec spec, int index, string baseName, double def)
        {
            if (index > 0)
            {
                string key = baseName + "_" + spec.Name;
                if (b.Params.ContainsKey(key)) return Json.GetNumber(b.Params, key, def);
            }
            return Json.GetNumber(b.Params, baseName, def);
        }

        private bool EvaluateImageCondition(Block b, BranchSpec spec, int index, RunContext ctx)
        {
            string tpl = BranchText(b, spec, index, "template", "");
            string path = _doc.ResolveTemplatePath(tpl);
            if (string.IsNullOrEmpty(tpl) || path == null || !File.Exists(path))
            {
                SetOutput(ctx, b, "found", false);
                Log(LogLevel.Warn, "判断", "未选择模板图片，按“没找到”处理：" + b.Id);
                return false;
            }
            double th = BranchNumber(b, spec, index, "threshold", 0);
            if (th <= 0) th = _defaultThreshold;
            string region = BranchText(b, spec, index, "region", "");
            if (region.Length == 0) region = "active";
            FindImageResult fr = _auto.FindImage(path, region, th);
            SetOutput(ctx, b, "found", fr.Found);
            SetOutput(ctx, b, "x", fr.X);
            SetOutput(ctx, b, "y", fr.Y);
            SetOutput(ctx, b, "confidence", fr.Confidence);
            Log(LogLevel.Info, "判断", fr.Found
                ? string.Format("分支「{0}」找到图片 {1}，坐标 ({2:0}, {3:0})", spec.Label, tpl, fr.X, fr.Y)
                : "分支「" + spec.Label + "」没找到图片 " + tpl + "，看下一个分支");
            return fr.Found;
        }

        private bool EvaluateColorCondition(Block b, BranchSpec spec, int index, RunContext ctx)
        {
            string rgb = BranchText(b, spec, index, "rgb", "");
            if (rgb.Length == 0) rgb = "#FF0000";
            string region = BranchText(b, spec, index, "region", "");
            if (region.Length == 0) region = "full";
            int tol = (int)BranchNumber(b, spec, index, "tolerance", 8);
            FindColorResult r = _auto.FindColor(rgb, region, tol);
            SetOutput(ctx, b, "found", r.Found);
            SetOutput(ctx, b, "x", r.X);
            SetOutput(ctx, b, "y", r.Y);
            Log(LogLevel.Info, "判断", r.Found
                ? string.Format("分支「{0}」找到颜色 {1}，坐标 ({2:0}, {3:0})", spec.Label, rgb, r.X, r.Y)
                : "分支「" + spec.Label + "」没找到颜色 " + rgb + "，看下一个分支");
            return r.Found;
        }

        private void ExecuteWhile(Block b, RunContext ctx)
        {
            long guard = 0;
            while (Json.ToBool(EvalInput(b, "cond", ctx), false))
            {
                CheckStop();
                if (++guard > 10000000L) throw new EngineException("while 循环次数超过上限（可能是死循环）");
                try
                {
                    ExecuteList(b.Branches.Count > 0 ? b.Branches[0].Blocks : null, ctx);
                }
                catch (BreakSignal) { break; }
                catch (ContinueSignal) { continue; }
            }
        }

        private void ExecuteForCount(Block b, RunContext ctx)
        {
            object cv = EvalInput(b, "count", ctx);
            if (cv == null) cv = ParamValue(b, "times", ctx);
            int times = (int)Json.ToNumber(cv, 0);
            if (times < 0) times = 0;
            if (times > 10000000) throw new EngineException("循环次数超过上限：" + times);
            for (int i = 0; i < times; i++)
            {
                CheckStop();
                try
                {
                    ExecuteList(b.Branches.Count > 0 ? b.Branches[0].Blocks : null, ctx);
                }
                catch (BreakSignal) { break; }
                catch (ContinueSignal) { continue; }
            }
        }

        private void ExecuteForEach(Block b, RunContext ctx)
        {
            object val = EvalInput(b, "list", ctx);
            List<object> list = val as List<object>;
            if (list == null)
            {
                string s = Convert.ToString(val, CultureInfo.InvariantCulture);
                if (!string.IsNullOrEmpty(s)) throw new EngineException("遍历数组积木需要数组类型，实际为：" + s);
                list = new List<object>();
            }
            string itemVar = Json.GetString(b.Params, "item_var", "item");
            Dictionary<string, object> scope = ctx.CurrentLocals;
            bool existed = scope.ContainsKey(itemVar);
            object old = existed ? scope[itemVar] : null;
            try
            {
                for (int i = 0; i < list.Count; i++)
                {
                    CheckStop();
                    scope[itemVar] = list[i];
                    try
                    {
                        ExecuteList(b.Branches.Count > 0 ? b.Branches[0].Blocks : null, ctx);
                    }
                    catch (BreakSignal) { break; }
                    catch (ContinueSignal) { continue; }
                }
            }
            finally
            {
                if (existed) scope[itemVar] = old;
                else scope.Remove(itemVar);
            }
        }

        private void ExecuteTry(Block b, RunContext ctx)
        {
            try
            {
                ExecuteList(b.Branches.Count > 0 ? b.Branches[0].Blocks : null, ctx);
            }
            catch (EngineException ex)
            {
                SetOutput(ctx, b, "error_msg", ex.Message);
                Log(LogLevel.Warn, "异常", "捕获异常：" + ex.Message);
                ExecuteList(b.Branches.Count > 1 ? b.Branches[1].Blocks : null, ctx);
            }
            catch (SystemException ex)
            {
                SetOutput(ctx, b, "error_msg", ex.Message);
                Log(LogLevel.Warn, "异常", "捕获异常：" + ex.Message);
                ExecuteList(b.Branches.Count > 1 ? b.Branches[1].Blocks : null, ctx);
            }
        }

        private void ExecuteCreateVar(Block b, RunContext ctx)
        {
            string name = Json.GetString(b.Params, "name", "");
            if (string.IsNullOrEmpty(name)) throw new EngineException("创建变量积木的变量名为空");
            string scope = Json.GetString(b.Params, "scope", "local");
            string type = Json.GetString(b.Params, "type", "num");
            object init = ParamValue(b, "init", ctx);
            object val = ScriptDocument.CoerceDefault(type, init);
            if (scope == "global") ctx.Globals[name] = val;
            else
            {
                Dictionary<string, object> locals = ctx.CurrentLocals;
                locals[name] = val;
            }
        }

        private void ExecuteSetVar(Block b, RunContext ctx)
        {
            string name = Json.GetString(b.Params, "name", "");
            if (string.IsNullOrEmpty(name)) throw new EngineException("变量赋值积木的变量名为空");
            object val = EvalInput(b, "value", ctx);
            SetVar(ctx, name, val);
        }

        private void SetVar(RunContext ctx, string name, object val)
        {
            for (int i = ctx.Locals.Count - 1; i >= 0; i--)
            {
                if (ctx.Locals[i].ContainsKey(name)) { ctx.Locals[i][name] = val; return; }
            }
            if (ctx.Globals.ContainsKey(name)) { ctx.Globals[name] = val; return; }
            ctx.CurrentLocals[name] = val;
        }

        private object ReadVar(RunContext ctx, string name)
        {
            if (string.IsNullOrEmpty(name)) throw new EngineException("变量名为空");
            for (int i = ctx.Locals.Count - 1; i >= 0; i--)
            {
                object v;
                if (ctx.Locals[i].TryGetValue(name, out v)) return v;
            }
            object g;
            if (ctx.Globals.TryGetValue(name, out g)) return g;
            throw new EngineException("变量未定义：" + name);
        }

        private object CompareValue(Block b, RunContext ctx)
        {
            object av = EvalInput(b, "a", ctx);
            object bv = EvalInput(b, "b", ctx);
            string op = Json.GetString(b.Params, "op", "==");
            double an, bn;
            bool aNum = double.TryParse(FormatValue(av), NumberStyles.Float, CultureInfo.InvariantCulture, out an);
            bool bNum = double.TryParse(FormatValue(bv), NumberStyles.Float, CultureInfo.InvariantCulture, out bn);
            if (aNum && bNum)
            {
                switch (op)
                {
                    case "==": return Math.Abs(an - bn) < 1e-9;
                    case "!=": return Math.Abs(an - bn) >= 1e-9;
                    case ">": return an > bn;
                    case "<": return an < bn;
                    case ">=": return an >= bn;
                    case "<=": return an <= bn;
                }
            }
            string asv = FormatValue(av);
            string bsv = FormatValue(bv);
            int c = string.Compare(asv, bsv, StringComparison.Ordinal);
            switch (op)
            {
                case "==": return c == 0;
                case "!=": return c != 0;
                case ">": return c > 0;
                case "<": return c < 0;
                case ">=": return c >= 0;
                case "<=": return c <= 0;
            }
            return false;
        }

        private object LogicValue(Block b, RunContext ctx)
        {
            string op = Json.GetString(b.Params, "op", "and");
            bool a = Json.ToBool(EvalInput(b, "a", ctx), false);
            if (op == "not") return !a;
            bool bb = Json.ToBool(EvalInput(b, "b", ctx), false);
            if (op == "or") return a || bb;
            return a && bb;
        }

        private object MathValue(Block b, RunContext ctx)
        {
            double a = Json.ToNumber(EvalInput(b, "a", ctx), 0);
            double bb = Json.ToNumber(EvalInput(b, "b", ctx), 0);
            string op = Json.GetString(b.Params, "op", "+");
            switch (op)
            {
                case "+": return a + bb;
                case "-": return a - bb;
                case "*": return a * bb;
                case "/":
                    if (Math.Abs(bb) < 1e-12) throw new EngineException("数学运算除数为 0");
                    return a / bb;
                case "%":
                    if (Math.Abs(bb) < 1e-12) throw new EngineException("数学运算取余的除数为 0");
                    return a % bb;
            }
            return 0d;
        }

        private object ConcatValue(Block b, RunContext ctx)
        {
            string s1 = FormatValue(EvalInput(b, "a", ctx));
            string s2 = FormatValue(EvalInput(b, "b", ctx));
            return s1 + s2;
        }

        private object CallFunction(Block b, RunContext ctx)
        {
            string name = Json.GetString(b.Params, "func", "");
            FunctionDef f = _doc.FindFunction(name);
            if (f == null) throw new EngineException("函数不存在：" + name);
            int maxDepth = _opt.MaxRecursion;
            object mr;
            if (_doc.Settings.TryGetValue("max_recursion", out mr)) maxDepth = (int)Json.ToNumber(mr, maxDepth);
            if (maxDepth <= 0) maxDepth = 100;
            if (ctx.CallStack.Count >= maxDepth)
                throw new EngineException("函数递归深度超过上限（" + maxDepth + " 层）：" + name);

            Dictionary<string, object> frame = new Dictionary<string, object>();
            foreach (VarDef p in f.Params)
            {
                object val = EvalInput(b, p.Name, ctx);
                frame[p.Name] = ScriptDocument.CoerceDefault(p.Type, val);
            }
            ctx.Locals.Add(frame);
            ctx.CallStack.Add(new FunctionFrame { Name = name, Depth = ctx.CallStack.Count + 1 });
            try
            {
                ExecuteList(f.Body, ctx);
                return null;
            }
            catch (ReturnSignal ret)
            {
                return ret.Value;
            }
            finally
            {
                ctx.Locals.RemoveAt(ctx.Locals.Count - 1);
                ctx.CallStack.RemoveAt(ctx.CallStack.Count - 1);
            }
        }

        private void ExecuteFindImage(Block b, RunContext ctx)
        {
            string tpl = Json.GetString(b.Params, "template", "");
            string path = _doc.ResolveTemplatePath(tpl);
            if (!File.Exists(path))
            {
                if (path != null) throw new EngineException("识图模板不存在：" + path, null);
                throw new EngineException("识图积木未指定模板（" + b.Id + "）");
            }
            double threshold = Json.GetNumber(b.Params, "threshold", 0);
            if (threshold <= 0) threshold = _defaultThreshold;
            string region = Json.GetString(b.Params, "region", "full");
            FindImageResult r = _auto.FindImage(path, region, threshold);
            SetOutput(ctx, b, "found", r.Found);
            SetOutput(ctx, b, "x", r.X);
            SetOutput(ctx, b, "y", r.Y);
            SetOutput(ctx, b, "confidence", r.Confidence);
            if (r.Found)
                Log(LogLevel.Info, "识图", string.Format("找到模板 {0}，坐标 ({1:0}, {2:0})，置信度 {3:0.000}", tpl, r.X, r.Y, r.Confidence));
            else
                Log(LogLevel.Info, "识图", string.Format("未找到模板 {0}（阈值 {1:0.00}）", tpl, threshold));
        }

        private void ExecuteFindColor(Block b, RunContext ctx)
        {
            string rgb = Json.GetString(b.Params, "rgb", "#FF0000");
            string region = Json.GetString(b.Params, "region", "full");
            int tol = (int)Json.GetNumber(b.Params, "tolerance", 8);
            FindColorResult r = _auto.FindColor(rgb, region, tol);
            SetOutput(ctx, b, "found", r.Found);
            SetOutput(ctx, b, "x", r.X);
            SetOutput(ctx, b, "y", r.Y);
            if (r.Found) Log(LogLevel.Info, "找色", string.Format("找到颜色 {0}，坐标 ({1:0}, {2:0})", rgb, r.X, r.Y));
            else Log(LogLevel.Info, "找色", "未找到颜色 " + rgb);
        }

        private void ExecuteMouseClick(Block b, RunContext ctx)
        {
            string coordMode = Json.GetString(b.Params, "coord_mode", "recorded");
            if (coordMode == "image")
            {
                ExecuteMouseClickByImage(b, ctx);
                return;
            }
            bool hasX, hasY;
            double x, y;
            if (coordMode == "bound")
            {
                hasX = HasValue(b, "x", ctx);
                hasY = HasValue(b, "y", ctx);
                x = Json.ToNumber(EvalInput(b, "x", ctx), 0);
                y = Json.ToNumber(EvalInput(b, "y", ctx), 0);
            }
            else
            {
                // 记录模式：只使用积木里保存的坐标，忽略连线
                object px = ParamValue(b, "x", ctx);
                object py = ParamValue(b, "y", ctx);
                hasX = px != null && Convert.ToString(px).Trim().Length > 0;
                hasY = py != null && Convert.ToString(py).Trim().Length > 0;
                x = Json.ToNumber(px, 0);
                y = Json.ToNumber(py, 0);
            }
            List<object> off = ToOffset(ParamValue(b, "offset", ctx));
            double dx = off != null && off.Count > 0 ? Json.ToNumber(off[0], 0) : 0;
            double dy = off != null && off.Count > 1 ? Json.ToNumber(off[1], 0) : 0;
            string button = Json.GetString(b.Params, "button", "left");
            string action = Json.GetString(b.Params, "action", "click");
            bool ok = _auto.MouseClick(hasX, x, hasY, y, button, action, dx, dy);
            SetOutput(ctx, b, "success", ok);
            DelayAfterClick(b);
        }

        /// <summary>
        /// v2.5：鼠标点击自带识图——运行时先在指定区域找模板图，找到就点它的中心；
        /// 找不到按积木里的「失败处理」重试 / 跳过 / 报错，不再需要单独放识图积木。
        /// </summary>
        private void ExecuteMouseClickByImage(Block b, RunContext ctx)
        {
            string tpl = Json.GetString(b.Params, "template", "");
            string path = _doc.ResolveTemplatePath(tpl);
            if (string.IsNullOrEmpty(tpl) || path == null || !File.Exists(path))
            {
                SetOutput(ctx, b, "found", false);
                SetOutput(ctx, b, "success", false);
                throw new EngineException("这一步选了「自动识图」，但模板图片不存在，请点「截图取图」重新截一张");
            }

            double threshold = Json.GetNumber(b.Params, "threshold", 0);
            if (threshold <= 0) threshold = _defaultThreshold;
            if (threshold > 1) threshold = 1;
            string region = Json.GetString(b.Params, "region", "active");
            string onFail = Json.GetString(b.Params, "on_fail", "retry");
            int retries = (int)Json.GetNumber(b.Params, "retries", 3);
            if (retries < 0) retries = 0;
            if (retries > 200) retries = 200;
            double wait = Json.GetNumber(b.Params, "retry_delay", 0.3);
            if (wait < 0) wait = 0;
            if (wait > 3600) wait = 3600;

            int attempts = (onFail == "retry") ? retries + 1 : 1;
            FindImageResult r = new FindImageResult();
            for (int i = 0; i < attempts; i++)
            {
                CheckStop();
                r = _auto.FindImage(path, region, threshold);
                if (r.Found) break;
                if (i + 1 < attempts)
                {
                    Log(LogLevel.Warn, "点击", string.Format("第 {0} 次没找到 {1}，{2:0.#} 秒后重试（共 {3} 次）",
                        i + 1, Path.GetFileName(tpl), wait, attempts));
                    Sleep(wait);
                }
            }

            SetOutput(ctx, b, "found", r.Found);
            SetOutput(ctx, b, "x", r.X);
            SetOutput(ctx, b, "y", r.Y);
            SetOutput(ctx, b, "confidence", r.Confidence);

            if (!r.Found)
            {
                SetOutput(ctx, b, "success", false);
                string msg = string.Format("在「{0}」区域里没找到 {1}（阈值 {2:0.00}）", region, Path.GetFileName(tpl), threshold);
                if (onFail == "error")
                    throw new EngineException(msg + "，已按「找不到图时 = 报错停止」结束脚本");
                Log(LogLevel.Warn, "点击", msg + "，按「跳过这一步」继续");
                DelayAfterClick(b);
                return;
            }

            List<object> off = ToOffset(ParamValue(b, "offset", ctx));
            double dx = off != null && off.Count > 0 ? Json.ToNumber(off[0], 0) : 0;
            double dy = off != null && off.Count > 1 ? Json.ToNumber(off[1], 0) : 0;
            string button = Json.GetString(b.Params, "button", "left");
            string action = Json.GetString(b.Params, "action", "click");
            bool ok = _auto.MouseClick(true, r.X, true, r.Y, button, action, dx, dy);
            SetOutput(ctx, b, "success", ok);
            Log(LogLevel.Info, "点击", string.Format("识图命中 {0}，点击 ({1:0}, {2:0})，置信度 {3:0.000}",
                Path.GetFileName(tpl), r.X + dx, r.Y + dy, r.Confidence));
            DelayAfterClick(b);
        }

        /// <summary>点击后延时：积木里填了正数就用它，填 0（或旧脚本没这个参数）就用全局动作间隔。</summary>
        private void DelayAfterClick(Block b)
        {
            double after = Json.GetNumber(b.Params, "delay_after", 0d);
            if (after > 0) Sleep(Math.Min(after, 3600d));
            else ActionDelay();
        }

        private void ExecuteMouseMove(Block b, RunContext ctx)
        {
            double x = Json.ToNumber(EvalInput(b, "x", ctx), 0);
            double y = Json.ToNumber(EvalInput(b, "y", ctx), 0);
            bool ok = _auto.MouseMove(x, y);
            SetOutput(ctx, b, "success", ok);
            ActionDelay();
        }

        private void ExecuteMouseWheel(Block b, RunContext ctx)
        {
            string dir = Json.GetString(b.Params, "dir", "down");
            int amount = (int)Json.ToNumber(ParamValue(b, "amount", ctx), 3);
            bool ok = _auto.MouseWheel(dir, amount);
            SetOutput(ctx, b, "success", ok);
            ActionDelay();
        }

        private void ExecuteKeyType(Block b, RunContext ctx)
        {
            string text = FormatValue(EvalInput(b, "text", ctx));
            bool ok = _auto.KeyType(text);
            SetOutput(ctx, b, "success", ok);
            ActionDelay();
        }

        private void ExecuteKeyPress(Block b, RunContext ctx)
        {
            string key = Json.GetString(b.Params, "key", "");
            bool ok = _auto.KeyPress(key);
            SetOutput(ctx, b, "success", ok);
            ActionDelay();
        }

        private void ExecuteLog(Block b, RunContext ctx)
        {
            string text = FormatValue(EvalInput(b, "text", ctx));
            string level = Json.GetString(b.Params, "level", "INFO");
            LogLevel lv = level == "ERROR" ? LogLevel.Error : (level == "WARN" ? LogLevel.Warn : LogLevel.Info);
            Log(lv, "脚本", text);
        }

        private static List<object> ToOffset(object v)
        {
            List<object> l = v as List<object>;
            if (l != null) return l;
            string s = v as string;
            if (string.IsNullOrEmpty(s)) return new List<object> { 0d, 0d };
            string[] parts = s.Replace("，", ",").Split(',');
            List<object> r = new List<object> { 0d, 0d };
            if (parts.Length > 0) r[0] = Json.ToNumber(parts[0].Trim(), 0);
            if (parts.Length > 1) r[1] = Json.ToNumber(parts[1].Trim(), 0);
            return r;
        }

        private void ActionDelay()
        {
            if (_actionDelayMs <= 0) return;
            SleepMs(_actionDelayMs);
        }

        private void Sleep(double seconds)
        {
            if (seconds <= 0) return;
            SleepMs((int)Math.Min(int.MaxValue, seconds * 1000.0));
        }

        private void SleepMs(int ms)
        {
            int elapsed = 0;
            while (elapsed < ms)
            {
                CheckStop();
                int chunk = Math.Min(50, ms - elapsed);
                Thread.Sleep(chunk);
                elapsed += chunk;
            }
        }

        // ---------------------------- 数据流 ----------------------------

        private bool HasValue(Block b, string port, RunContext ctx)
        {
            PinLink p = b.Input(port);
            if (p != null && p.HasSource) return true;
            if (!b.Params.ContainsKey(port)) return false;
            object v = ParamValue(b, port, ctx);
            if (v == null) return false;
            if (v is string && ((string)v).Trim().Length == 0) return false;
            return true;
        }

        private object EvalInput(Block b, string port, RunContext ctx)
        {
            PinLink p = b.Input(port);
            if (p != null && p.HasSource) return ResolveRef(ctx, p.From);
            if (b.Params.ContainsKey(port)) return ParamValue(b, port, ctx);
            return null;
        }

        private object ResolveRef(RunContext ctx, string from)
        {
            int dot = from.IndexOf('.');
            if (dot <= 0) throw new EngineException("连线格式错误：" + from);
            string id = from.Substring(0, dot);
            string port = from.Substring(dot + 1);
            object cached;
            bool hasCache = TryGetOutput(ctx, id, port, out cached);
            Block src = _doc.FindBlock(id);
            if (src == null) throw new EngineException("连线引用了不存在的积木：" + id);
            // 纯求值积木每次引用都重新计算，避免循环中读到上一轮的旧值；
            // 函数调用有副作用，已执行过则复用结果。
            if (hasCache && !(IsPure(src.Type) && src.Type != "call_func")) return cached;
            if (IsPure(src.Type))
            {
                // 循环依赖检测按调用栈深度区分，允许递归（每次调用是新栈帧）
                string key = id + "@" + ctx.CallStack.Count;
                if (!ctx.Evaluating.Add(key))
                    throw new EngineException("检测到循环数据依赖：" + id);
                try
                {
                    EvaluatePure(src, ctx);
                }
                finally
                {
                    ctx.Evaluating.Remove(key);
                }
                if (TryGetOutput(ctx, id, port, out cached)) return cached;
                throw new EngineException("积木没有输出端口 " + port + "：" + id);
            }
            throw new EngineException(string.Format("数据来源尚未执行：{0}.{1}（请把来源积木放在前面执行）", id, port));
        }

        private static bool TryGetOutput(RunContext ctx, string id, string port, out object value)
        {
            value = null;
            Dictionary<string, object> map;
            if (!ctx.Outputs.TryGetValue(id, out map)) return false;
            if (map == null) return false;
            return map.TryGetValue(port, out value);
        }

        private static bool IsPure(string type)
        {
            switch (type)
            {
                case "compare":
                case "logic_op":
                case "math":
                case "concat":
                case "get_var":
                case "call_func":
                    return true;
                default:
                    return false;
            }
        }

        private void EvaluatePure(Block b, RunContext ctx)
        {
            switch (b.Type)
            {
                case "compare": SetOutput(ctx, b, "result", CompareValue(b, ctx)); break;
                case "logic_op": SetOutput(ctx, b, "result", LogicValue(b, ctx)); break;
                case "math": SetOutput(ctx, b, "result", MathValue(b, ctx)); break;
                case "concat": SetOutput(ctx, b, "result", ConcatValue(b, ctx)); break;
                case "get_var": SetOutput(ctx, b, "value", ReadVar(ctx, Json.GetString(b.Params, "name", ""))); break;
                case "call_func": SetOutput(ctx, b, "result", CallFunction(b, ctx)); break;
                default: throw new EngineException("不是可求值积木：" + b.Type);
            }
        }

        private void SetOutput(RunContext ctx, Block b, string port, object value)
        {
            Dictionary<string, object> map;
            if (!ctx.Outputs.TryGetValue(b.Id, out map))
            {
                map = new Dictionary<string, object>();
                ctx.Outputs[b.Id] = map;
            }
            map[port] = value;
        }

        private object ParamValue(Block b, string key, RunContext ctx)
        {
            object v;
            if (!b.Params.TryGetValue(key, out v)) return null;
            string s = v as string;
            if (s != null)
            {
                if (s.Length > 1 && s[0] == '$') return ReadVar(ctx, s.Substring(1));
                if (s.IndexOf("${", StringComparison.Ordinal) >= 0) return Interpolate(s, ctx);
            }
            return v;
        }

        private string Interpolate(string text, RunContext ctx)
        {
            StringBuilder sb = new StringBuilder();
            int i = 0;
            while (i < text.Length)
            {
                int start = text.IndexOf("${", i, StringComparison.Ordinal);
                if (start < 0) { sb.Append(text, i, text.Length - i); break; }
                sb.Append(text, i, start - i);
                int end = text.IndexOf('}', start + 2);
                if (end < 0) { sb.Append(text, start, text.Length - start); break; }
                string name = text.Substring(start + 2, end - start - 2).Trim();
                try
                {
                    sb.Append(FormatValue(ReadVar(ctx, name)));
                }
                catch (EngineException)
                {
                    sb.Append("${").Append(name).Append('}');
                }
                i = end + 1;
            }
            return sb.ToString();
        }

        public static string FormatValue(object v)
        {
            if (v == null) return "";
            if (v is bool) return ((bool)v) ? "true" : "false";
            if (v is double)
            {
                double d = (double)v;
                if (Math.Abs(d - Math.Round(d)) < 1e-9 && Math.Abs(d) < 1e15)
                    return ((long)Math.Round(d)).ToString(CultureInfo.InvariantCulture);
                return d.ToString("0.####", CultureInfo.InvariantCulture);
            }
            List<object> l = v as List<object>;
            if (l != null)
            {
                StringBuilder sb = new StringBuilder("[");
                for (int i = 0; i < l.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(FormatValue(l[i]));
                }
                return sb.Append(']').ToString();
            }
            Dictionary<string, object> d2 = v as Dictionary<string, object>;
            if (d2 != null)
            {
                StringBuilder sb = new StringBuilder("{");
                bool first = true;
                foreach (KeyValuePair<string, object> kv in d2)
                {
                    if (!first) sb.Append(", ");
                    first = false;
                    sb.Append(kv.Key).Append(": ").Append(FormatValue(kv.Value));
                }
                return sb.Append('}').ToString();
            }
            return Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        // ---------------------------- 事件 ----------------------------

        private void Log(LogLevel level, string source, string message)
        {
            _log.Write(new LogEntry { Level = level, Source = source, Message = message });
        }

        private void Fire(Action a)
        {
            if (a == null) return;
            try { a(); } catch { }
        }

        private void FireBlock(Action<Block> a, Block b)
        {
            if (a == null) return;
            try { a(b); } catch { }
        }

        private void Fire2(Action<Block> a, Block b)
        {
            FireBlock(a, b);
        }
    }
}
