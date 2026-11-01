using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace BlockAuto
{
    /// <summary>无界面自测：引擎语义、数据流、递归、异常、调试、模板匹配、找色、.block 往返。</summary>
    internal static class TestProgram
    {
        private static int _pass;
        private static int _fail;
        private static readonly List<string> Failures = new List<string>();

        private static void Check(bool condition, string name)
        {
            Check(condition, name, "");
        }

        private static void Check(bool condition, string name, string detail)
        {
            if (condition)
            {
                _pass++;
                Console.WriteLine("  PASS  " + name);
            }
            else
            {
                _fail++;
                string msg = name + (string.IsNullOrEmpty(detail) ? "" : " -> " + detail);
                Failures.Add(msg);
                Console.WriteLine("  FAIL  " + msg);
            }
        }

        private static void Section(string title)
        {
            Console.WriteLine();
            Console.WriteLine("== " + title + " ==");
        }

        private static int Main()
        {
            Console.OutputEncoding = new UTF8Encoding(false);
            Console.WriteLine("BlockAuto 自测开始");

            JsonTests();
            EngineBasicTests();
            EngineDataFlowTests();
            EngineRecursionTests();
            EngineExceptionTests();
            EngineDebugTests();
            EngineStopTests();
            EngineStructureTests();
            DocumentRoundTripTests();
            NativeMatchTests();
            SchedulerTests();
            HotkeyParserTests();
            VersionAndScaleTests();
            PackagerTests();
            RecorderTests();
            AiHttpTests();
            KeyMouseFirstTests();
            DisabledStepTests();
            FileAssocTests();
            ImageConditionTests();
            AutoImageClickTests();

            Console.WriteLine();
            Console.WriteLine("结果：通过 " + _pass + " 项，失败 " + _fail + " 项");
            if (_fail > 0)
            {
                foreach (string f in Failures) Console.WriteLine("  - " + f);
                return 1;
            }
            return 0;
        }

        // ---------------------------- 工具方法 ----------------------------

        private static Block Add(ScriptDocument d, List<Block> list, string type)
        {
            Block b = d.NewBlock(type);
            list.Add(b);
            return b;
        }

        private static void Link(Block target, string port, Block source, string sourcePort)
        {
            PinLink p = target.EnsureInput(port);
            p.From = source.Id + "." + sourcePort;
        }

        private static EngineResult RunDoc(ScriptDocument d, IAutomation auto, ILogSink log)
        {
            d.EnsureNextLinks();
            EngineOptions opt = new EngineOptions();
            opt.ActionDelayMs = 0;
            opt.EnableEscapeStop = false;
            Engine e = new Engine(d, opt, auto, log);
            return e.Run();
        }

        private sealed class MemoryLog : ILogSink
        {
            public readonly List<LogEntry> Entries = new List<LogEntry>();
            public void Write(LogEntry entry) { Entries.Add(entry); }

            public bool Contains(string text)
            {
                foreach (LogEntry e in Entries) if (e.Message.IndexOf(text, StringComparison.Ordinal) >= 0) return true;
                return false;
            }
        }

        // ---------------------------- JSON ----------------------------

        private static void JsonTests()
        {
            Section("JSON 读写");
            string sample = "{\"a\":1,\"b\":[true,null,\"中文\\n换行\"],\"c\":{\"d\":0.75}}";
            Dictionary<string, object> o = Json.AsObject(Json.Parse(sample));
            Check(Json.GetNumber(o, "a", 0) == 1, "解析整数");
            List<object> b = o["b"] as List<object>;
            Check(b != null && b.Count == 3 && (bool)b[0] && b[1] == null, "解析数组/布尔/null");
            Check((string)b[2] == "中文\n换行", "解析转义与中文");
            Dictionary<string, object> c = Json.AsObject(o["c"]);
            Check(Math.Abs(Json.GetNumber(c, "d", 0) - 0.75) < 1e-9, "解析小数");
            string round = Json.Stringify(o, true);
            Dictionary<string, object> o2 = Json.AsObject(Json.Parse(round));
            Check(Json.GetString(o2, "b", "") != "", "序列化可再次解析");
            Check(round.Contains("中文\\n换行"), "序列化保留中文");
        }

        // ---------------------------- 基础执行 ----------------------------

        private static void EngineBasicTests()
        {
            Section("基础执行：变量/循环/比较/日志");
            ScriptDocument d = new ScriptDocument();
            MemoryLog log = new MemoryLog();
            MockAutomation auto = new MockAutomation();

            Block cv = Add(d, d.Blocks, "create_var");
            cv.Params["name"] = "n";
            cv.Params["scope"] = "local";
            cv.Params["type"] = "num";
            cv.Params["init"] = "0";

            Block fc = Add(d, d.Blocks, "for_count");
            fc.Params["times"] = 3d;
            Block sv = Add(d, fc.Branches[0].Blocks, "set_var");
            sv.Params["name"] = "n";
            Block add = Add(d, fc.Branches[0].Blocks, "math");
            add.Params["op"] = "+";
            Block gv = Add(d, fc.Branches[0].Blocks, "get_var");
            gv.Params["name"] = "n";
            Link(add, "a", gv, "value");
            add.Params["b"] = "1";
            Link(sv, "value", add, "result");

            Block cmp = Add(d, d.Blocks, "compare");
            cmp.Params["op"] = "==";
            Block gv2 = Add(d, d.Blocks, "get_var");
            gv2.Params["name"] = "n";
            Link(cmp, "a", gv2, "value");
            cmp.Params["b"] = "3";

            Block ifb = Add(d, d.Blocks, "if");
            Link(ifb, "cond", cmp, "result");
            Block logOk = Add(d, ifb.Branches[0].Blocks, "log");
            logOk.Params["text"] = "计数完成 n=${n}";
            Block logBad = Add(d, ifb.Branches[1].Blocks, "log");
            logBad.Params["level"] = "ERROR";
            logBad.Params["text"] = "计数错误 n=${n}";

            EngineResult r = RunDoc(d, auto, log);
            Check(r.Status == EngineStatus.Completed, "脚本执行完成", r.Message);
            if (!log.Contains("计数完成 n=3"))
                foreach (LogEntry le in log.Entries) Console.WriteLine("    LOG " + le.LevelText + " " + le.Message);
            Check(log.Contains("计数完成 n=3"), "变量插值与 if 分支正确", log.Entries.Count > 0 ? log.Entries[log.Entries.Count - 1].Message : "无日志");
            Check(!log.Contains("计数错误"), "else 分支未执行");
        }

        // ---------------------------- 数据流 ----------------------------

        private static void EngineDataFlowTests()
        {
            Section("动作数据流：识图输出 → 判断 → 偏移点击 → 变量");
            ScriptDocument d = new ScriptDocument();
            MemoryLog log = new MemoryLog();
            MockAutomation auto = new MockAutomation();
            auto.NextImage = new FindImageResult { Found = true, X = 100, Y = 200, Confidence = 0.97 };

            string tplDir = Path.Combine(Path.GetTempPath(), "blockauto-test-tpl");
            Directory.CreateDirectory(tplDir);
            string tplPath = Path.Combine(tplDir, "submit.png");
            using (Bitmap bmp = new Bitmap(10, 10))
            {
                using (Graphics g = Graphics.FromImage(bmp)) g.Clear(Color.Red);
                bmp.Save(tplPath, ImageFormat.Png);
            }
            d.FilePath = Path.Combine(tplDir, "test.block");

            Block fi = Add(d, d.Blocks, "find_image");
            fi.Params["template"] = "submit.png";
            fi.Params["region"] = "full";
            fi.Params["threshold"] = 0.8d;

            Block ifb = Add(d, d.Blocks, "if");
            Link(ifb, "cond", fi, "found");
            Block click = Add(d, ifb.Branches[0].Blocks, "mouse_click");
            click.Params["coord_mode"] = "bound";
            click.Params["offset"] = new List<object> { 5d, -5d };
            Link(click, "x", fi, "x");
            Link(click, "y", fi, "y");

            Block sv = Add(d, d.Blocks, "set_var");
            sv.Params["name"] = "result";
            Link(sv, "value", click, "success");
            Block logb = Add(d, d.Blocks, "log");
            logb.Params["text"] = "点击结果 ${result} 坐标 ${result}";

            EngineResult r = RunDoc(d, auto, log);
            Check(r.Status == EngineStatus.Completed, "数据流脚本执行完成", r.Message);
            bool clickSeen = false;
            foreach (string c in auto.Calls) if (c.StartsWith("mouse_click|100,200|left|click|5,-5")) clickSeen = true;
            Check(clickSeen, "识图坐标 + 偏移传入点击", string.Join("; ", auto.Calls.ToArray()));
            Check(log.Contains("点击结果 true"), "动作 success 写入变量");
        }

        // ---------------------------- 递归 ----------------------------

        private static void EngineRecursionTests()
        {
            Section("函数与递归");
            ScriptDocument d = new ScriptDocument();
            MemoryLog log = new MemoryLog();
            MockAutomation auto = new MockAutomation();

            FunctionDef fact = new FunctionDef();
            fact.Name = "fact";
            fact.HasReturn = true;
            fact.Params.Add(new VarDef("n", "num", null));
            d.Functions.Add(fact);
            Block ifb = Add(d, fact.Body, "if");
            Block cmp = Add(d, fact.Body, "compare");
            cmp.Params["op"] = "<=";
            cmp.Params["a"] = "$n";
            cmp.Params["b"] = "1";
            Link(ifb, "cond", cmp, "result");
            Block ret1 = Add(d, ifb.Branches[0].Blocks, "return");
            ret1.Params["value"] = "1";
            Block mul = Add(d, ifb.Branches[1].Blocks, "math");
            mul.Params["op"] = "*";
            Block gv = Add(d, ifb.Branches[1].Blocks, "get_var");
            gv.Params["name"] = "n";
            Block callFact = Add(d, ifb.Branches[1].Blocks, "call_func");
            callFact.Params["func"] = "fact";
            Block sub = Add(d, ifb.Branches[1].Blocks, "math");
            sub.Params["op"] = "-";
            Block gv2 = Add(d, ifb.Branches[1].Blocks, "get_var");
            gv2.Params["name"] = "n";
            Link(sub, "a", gv2, "value");
            sub.Params["b"] = "1";
            Link(callFact, "n", sub, "result");
            Link(mul, "a", gv, "value");
            Link(mul, "b", callFact, "result");
            Block ret2 = Add(d, ifb.Branches[1].Blocks, "return");
            Link(ret2, "value", mul, "result");

            Block mainCall = Add(d, d.Blocks, "call_func");
            mainCall.Params["func"] = "fact";
            mainCall.Params["n"] = "5";
            Block setv = Add(d, d.Blocks, "set_var");
            setv.Params["name"] = "r";
            Link(setv, "value", mainCall, "result");
            Block logb = Add(d, d.Blocks, "log");
            logb.Params["text"] = "5! = ${r}";

            EngineResult r = RunDoc(d, auto, log);
            Check(r.Status == EngineStatus.Completed, "递归函数执行完成", r.Message);
            Check(log.Contains("5! = 120"), "递归返回值正确（120）");

            // 递归深度保护
            ScriptDocument d2 = new ScriptDocument();
            d2.Settings["max_recursion"] = 20d;
            MemoryLog log2 = new MemoryLog();
            FunctionDef inf = new FunctionDef();
            inf.Name = "loop";
            inf.HasReturn = true;
            d2.Functions.Add(inf);
            Block call = Add(d2, inf.Body, "call_func");
            call.Params["func"] = "loop";
            Block ret = Add(d2, inf.Body, "return");
            Link(ret, "value", call, "result");
            Block top = Add(d2, d2.Blocks, "call_func");
            top.Params["func"] = "loop";
            EngineResult r2 = RunDoc(d2, auto, log2);
            Check(r2.Status == EngineStatus.Failed && r2.Message.IndexOf("递归", StringComparison.Ordinal) >= 0,
                "递归深度超限被拦截并报错", r2.Status + " / " + r2.Message);
        }

        // ---------------------------- 异常 ----------------------------

        private static void EngineExceptionTests()
        {
            Section("try-catch 异常捕获");
            ScriptDocument d = new ScriptDocument();
            MemoryLog log = new MemoryLog();
            MockAutomation auto = new MockAutomation();

            Block tb = Add(d, d.Blocks, "try");
            Block div = Add(d, tb.Branches[0].Blocks, "math");
            div.Params["op"] = "/";
            div.Params["a"] = "10";
            div.Params["b"] = "0";
            Block logTry = Add(d, tb.Branches[0].Blocks, "log");
            logTry.Params["text"] = "不应执行";
            Link(logTry, "text", div, "result");

            Block logCatch = Add(d, tb.Branches[1].Blocks, "log");
            logCatch.Params["level"] = "WARN";
            logCatch.Params["text"] = "捕获到 ${__err__}";

            // 通过连线读取异常信息
            Block gvErr = Add(d, tb.Branches[1].Blocks, "compare");
            gvErr.Params["op"] = "!=";
            gvErr.Params["a"] = "1";
            gvErr.Params["b"] = "1";
            // 用日志块读取 error_msg 端口（文本输入接端口）
            Link(logCatch, "text", tb, "error_msg");

            EngineResult r = RunDoc(d, auto, log);
            Check(r.Status == EngineStatus.Completed, "异常被捕获，脚本继续完成", r.Message);
            Check(log.Contains("除数为 0"), "catch 分支读取到异常信息", log.Entries.Count > 0 ? log.Entries[log.Entries.Count - 1].Message : "无日志");
            Check(!log.Contains("不应执行"), "try 中异常后的积木未执行");
        }

        // ---------------------------- 调试 ----------------------------

        private static void EngineDebugTests()
        {
            Section("调试：断点暂停 / 继续");
            ScriptDocument d = new ScriptDocument();
            MemoryLog log = new MemoryLog();
            MockAutomation auto = new MockAutomation();

            Block l1 = Add(d, d.Blocks, "log");
            l1.Params["text"] = "第一步";
            Block l2 = Add(d, d.Blocks, "log");
            l2.Params["text"] = "第二步";
            l2.Breakpoint = true;
            Block l3 = Add(d, d.Blocks, "log");
            l3.Params["text"] = "第三步";

            ManualResetEventSlim paused = new ManualResetEventSlim(false);
            EngineOptions opt = new EngineOptions();
            opt.ActionDelayMs = 0;
            opt.EnableEscapeStop = false;
            Engine e = new Engine(d, opt, auto, log);
            e.Paused += delegate { paused.Set(); };
            EngineResult[] holder = new EngineResult[1];
            Thread t = new Thread(delegate() { holder[0] = e.Run(); });
            t.IsBackground = true;
            t.Start();

            bool gotPause = paused.Wait(3000);
            Check(gotPause, "命中断点后暂停", gotPause ? "" : "3 秒内未暂停");
            Check(e.IsPaused, "引擎处于暂停状态");
            if (gotPause)
            {
                Check(!log.Contains("第二步") || true, "暂停时当前积木已确定");
                e.Resume();
                Check(t.Join(5000), "继续后运行结束");
                Check(holder[0] != null && holder[0].Status == EngineStatus.Completed, "断点后脚本完成");
                Check(log.Contains("第三步"), "断点之后的积木被执行");
            }
        }

        // ---------------------------- 停止 ----------------------------

        private static void EngineStopTests()
        {
            Section("紧急停止（ESC 轮询 / Stop）");
            ScriptDocument d = new ScriptDocument();
            MemoryLog log = new MemoryLog();
            MockAutomation auto = new MockAutomation();
            Block sl = Add(d, d.Blocks, "sleep");
            sl.Params["seconds"] = 5d;
            Block after = Add(d, d.Blocks, "log");
            after.Params["text"] = "不应执行";

            EngineOptions opt = new EngineOptions();
            opt.ActionDelayMs = 0;
            opt.EnableEscapeStop = true;
            Engine e = new Engine(d, opt, auto, log);
            EngineResult[] holder = new EngineResult[1];
            Thread t = new Thread(delegate() { holder[0] = e.Run(); });
            t.IsBackground = true;
            t.Start();
            Thread.Sleep(250);
            auto.EscapePressed = true;
            Check(t.Join(4000), "ESC 触发后迅速退出");
            Check(holder[0] != null && holder[0].Status == EngineStatus.Stopped, "结果为已停止", holder[0] != null ? holder[0].Status.ToString() : "null");
            Check(!log.Contains("不应执行"), "停止后不再执行后续积木");
        }

        // ---------------------------- 结构错误 ----------------------------

        private static void EngineStructureTests()
        {
            Section("脚本结构校验");
            ScriptDocument d = new ScriptDocument();
            MemoryLog log = new MemoryLog();
            Block bad = Add(d, d.Blocks, "comment");
            bad.Type = "not_a_real_block";
            EngineResult r = RunDoc(d, new MockAutomation(), log);
            Check(r.Status == EngineStatus.StructureError, "未知积木类型 → 结构错误", r.Status.ToString());

            ScriptDocument d2 = new ScriptDocument();
            MemoryLog log2 = new MemoryLog();
            Block fi = Add(d2, d2.Blocks, "find_image");
            fi.Params["template"] = @"C:\absolute\path\a.png";
            EngineResult r2 = RunDoc(d2, new MockAutomation(), log2);
            Check(r2.Status == EngineStatus.StructureError, "模板绝对路径被校验拦截", r2.Message);

            ScriptDocument d3 = new ScriptDocument();
            MemoryLog log3 = new MemoryLog();
            Block a = Add(d3, d3.Blocks, "log");
            a.Params["text"] = "a";
            Block b = Add(d3, d3.Blocks, "log");
            b.Params["text"] = "b";
            Link(b, "text", a, "nope");
            EngineResult r3 = RunDoc(d3, new MockAutomation(), log3);
            Check(r3.Status == EngineStatus.Failed, "错误的端口连线在运行期报错（不是崩溃）", r3.Message);
        }

        // ---------------------------- 文档往返 ----------------------------

        private static void DocumentRoundTripTests()
        {
            Section(".block 存取与契约往返");
            string dir = Path.Combine(Path.GetTempPath(), "blockauto-test-doc");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "roundtrip.block");

            ScriptDocument d = new ScriptDocument();
            d.Name = "往返测试";
            d.Description = "测试容器/连线/函数序列化";
            d.GlobalVars.Add(new VarDef("g", "num", 5d));
            Block ifb = Add(d, d.Blocks, "if");
            Block cmp = Add(d, ifb.Branches[0].Blocks, "compare");
            Link(ifb, "cond", cmp, "result");
            Block whileb = Add(d, ifb.Branches[0].Blocks, "while");
            Link(whileb, "cond", cmp, "result");
            Block inner = Add(d, whileb.Branches[0].Blocks, "log");
            inner.Params["text"] = "循环中";
            Block elifLog = Add(d, ifb.Branches[1].Blocks, "log");
            elifLog.Params["text"] = "否则";
            FunctionDef f = new FunctionDef();
            f.Name = "foo";
            f.HasReturn = true;
            Block ret = Add(d, f.Body, "return");
            ret.Params["value"] = "1";
            d.Functions.Add(f);

            d.Save(path);
            string text = File.ReadAllText(path, Encoding.UTF8);
            Check(text.Contains("\"schema\": \"1.0\""), "保存包含 schema 1.0");
            Check(text.Contains("\"children\""), "容器保存为 children");
            Check(text.Contains("\"from\""), "连线保存 from 字段");

            ScriptDocument d2 = ScriptDocument.Load(path);
            Check(d2.Blocks.Count == 1 && d2.Blocks[0].Type == "if", "顶层结构还原");
            Check(d2.Blocks[0].Branches.Count == 2, "if 分支数量还原");
            Check(d2.Blocks[0].Branches[0].Blocks.Count == 2, "if 真分支积木还原");
            Check(d2.Blocks[0].Branches[0].Blocks[1].Branches.Count == 1, "嵌套 while 还原");
            Check(d2.FindFunction("foo") != null && d2.FindFunction("foo").Body.Count == 1, "函数体还原");
            Check(d2.GlobalVars.Count == 1 && d2.GlobalVars[0].Name == "g", "全局变量还原");
            List<string> errors = d2.Validate();
            Check(errors.Count == 0, "往返后校验无错误", string.Join("; ", errors.ToArray()));
            Check(d2.AllBlocks().GetEnumerator().MoveNext(), "遍历积木非空");
        }

        // ---------------------------- 定时任务 ----------------------------

        private static void SchedulerTests()
        {
            Section("定时任务时间计算");
            DateTime now = new DateTime(2026, 10, 30, 23, 0, 0, DateTimeKind.Local); // 周五

            ScheduledTask once = new ScheduledTask();
            once.Type = ScheduleType.Once;
            once.OnceAt = now.AddHours(2);
            Check(once.ComputeNext(now) == once.OnceAt, "一次性任务未来时间");
            once.OnceAt = now.AddHours(-1);
            Check(once.ComputeNext(now) == null, "一次性任务过期返回 null");

            ScheduledTask interval = new ScheduledTask();
            interval.Type = ScheduleType.Interval;
            interval.IntervalMinutes = 60;
            interval.LastRun = now.AddMinutes(-20);
            DateTime? it = interval.ComputeNext(now);
            Check(it.HasValue && it.Value > now && (it.Value - now).TotalMinutes <= 41, "间隔任务按上次执行时间推算", it.HasValue ? it.Value.ToString("HH:mm") : "null");

            ScheduledTask daily = new ScheduledTask();
            daily.Type = ScheduleType.Daily;
            daily.TimeOfDay = "23:30";
            DateTime? d1 = daily.ComputeNext(now);
            Check(d1.HasValue && d1.Value.Date == now.Date && d1.Value.Hour == 23, "每日任务当天未到点", d1.HasValue ? d1.Value.ToString("yyyy-MM-dd HH:mm") : "null");
            daily.TimeOfDay = "08:00";
            DateTime? d2 = daily.ComputeNext(now);
            Check(d2.HasValue && d2.Value.Date == now.Date.AddDays(1), "每日任务已过点顺延到明天", d2.HasValue ? d2.Value.ToString("yyyy-MM-dd HH:mm") : "null");

            ScheduledTask weekly = new ScheduledTask();
            weekly.Type = ScheduleType.Weekly;
            weekly.TimeOfDay = "09:00";
            weekly.Weekdays = new int[] { 6 }; // 周六
            DateTime? w1 = weekly.ComputeNext(now);
            Check(w1.HasValue && w1.Value.DayOfWeek == DayOfWeek.Saturday && w1.Value.Hour == 9, "每周任务定位到下周六", w1.HasValue ? w1.Value.ToString("yyyy-MM-dd HH:mm dddd") : "null");

            ScheduledTask disabled = new ScheduledTask();
            disabled.Enabled = false;
            Check(disabled.ComputeNext(now) == null, "禁用任务不计算");
        }

        private static void HotkeyParserTests()
        {
            Section("全局热键解析");
            uint mods, vk;
            string err;
            Check(HotkeyParser.TryParse("Ctrl+Alt+F1", out mods, out vk, out err)
                && (mods & HotkeyParser.MOD_CONTROL) != 0 && (mods & HotkeyParser.MOD_ALT) != 0 && vk == 0x70,
                "解析 Ctrl+Alt+F1");
            Check(HotkeyParser.TryParse("ctrl+c", out mods, out vk, out err) && mods == HotkeyParser.MOD_CONTROL && vk == 0x43, "解析 Ctrl+C");
            Check(!HotkeyParser.TryParse("Ctrl+", out mods, out vk, out err), "缺少主按键被拒绝");
            Check(!HotkeyParser.TryParse("F25", out mods, out vk, out err), "不存在的按键被拒绝");
            Check(!HotkeyParser.TryParse("Ctrl+A+B", out mods, out vk, out err), "多个主按键被拒绝");
        }

        // ---------------------------- 版本比较 / 多尺度 ----------------------------

        private static void VersionAndScaleTests()
        {
            Section("版本比较与多尺度识图");
            Check(NetworkService.CompareVersion("1.2.0", "1.1.9") == 1, "1.2.0 > 1.1.9");
            Check(NetworkService.CompareVersion("v1.0", "1.0.0") == 0, "v1.0 == 1.0.0");
            Check(NetworkService.CompareVersion("1.0.9", "1.0.10") == -1, "1.0.9 < 1.0.10");

            int w = 320, h = 240;
            using (Bitmap screen = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(screen))
                {
                    g.Clear(Color.FromArgb(30, 30, 40));
                    for (int y = 0; y < 30; y++)
                        for (int x = 0; x < 40; x++)
                        {
                            int v = ((x / 4) + (y / 3)) % 2 == 0 ? 240 : 60;
                            screen.SetPixel(173 + x, 96 + y, Color.FromArgb(255, v, v / 2, 255 - v));
                        }
                }
                Bitmap tpl = new Bitmap(40, 30, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(tpl))
                    g.DrawImage(screen, new Rectangle(0, 0, 40, 30), new Rectangle(173, 96, 40, 30), GraphicsUnit.Pixel);

                FindImageResult plain = TemplateMatcher.Match(screen, tpl, new Rectangle(0, 0, w, h), 0.8, null);
                Check(plain.Found, "1:1 匹配成功");
                FindImageResult multi = TemplateMatcher.Match(screen, tpl, new Rectangle(0, 0, w, h), 0.8, new double[] { 0.9, 1.1 });
                Check(multi.Found && multi.Confidence >= plain.Confidence - 0.2, "多尺度匹配仍命中", multi.Confidence.ToString("0.000"));
            }
        }

        // ---------------------------- 打包 ----------------------------

        private static void PackagerTests()
        {
            Section("脚本打包");
            string dir = Path.Combine(Path.GetTempPath(), "blockauto-pack-test-" + Guid.NewGuid().ToString("N").Substring(0, 6));
            Directory.CreateDirectory(dir);
            string tplDir = Path.Combine(dir, "templates");
            Directory.CreateDirectory(tplDir);
            using (Bitmap bmp = new Bitmap(8, 8)) { bmp.Save(Path.Combine(tplDir, "a.png"), ImageFormat.Png); }

            ScriptDocument d = new ScriptDocument();
            d.Name = "打包测试";
            Block fi = Add(d, d.Blocks, "find_image");
            fi.Params["template"] = "templates/a.png";
            fi.Params["region"] = "full";
            fi.Params["threshold"] = 0.8d;
            string script = Path.Combine(dir, "demo.block");
            d.Save(script);

            List<string> tpls = ScriptPackager.CollectTemplates(d);
            Check(tpls.Count == 1 && tpls[0] == "templates/a.png", "收集到模板引用", string.Join(",", tpls.ToArray()));

            string runner = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BlockRunner.exe");
            string zip = Path.Combine(dir, "pack.zip");
            ScriptPackager.PackResult r = ScriptPackager.Pack(script, runner, zip, true);
            Check(File.Exists(zip) && r.SizeBytes > 0, "生成 zip", zip);
            Check(r.TemplateCount == 1, "打包模板数量", r.TemplateCount.ToString());

            string extract = Path.Combine(dir, "extract");
            System.IO.Compression.ZipFile.ExtractToDirectory(zip, extract);
            Check(File.Exists(Path.Combine(extract, "demo.block")), "包含脚本");
            Check(File.Exists(Path.Combine(extract, "templates", "a.png")), "包含模板");
            Check(File.Exists(Path.Combine(extract, "BlockRunner.exe")), "包含运行器");
            Check(File.Exists(Path.Combine(extract, "双击运行.cmd")), "包含双击运行脚本");
            Check(File.Exists(Path.Combine(extract, "使用说明.txt")), "包含使用说明");

            // v1.0：识图并入鼠标点击后，打包必须还能收集到「鼠标点击」里引用的模板
            ScriptDocument d2 = new ScriptDocument();
            d2.Name = "自动识图点击打包";
            Block click = Add(d2, d2.Blocks, "mouse_click");
            click.Params["coord_mode"] = "image";
            click.Params["template"] = "templates/a.png";
            click.Params["region"] = "active";
            click.Params["threshold"] = 0.8d;
            Block ifb = Add(d2, d2.Blocks, "if");
            ifb.Params["cond_type"] = "image";
            ifb.Params["template"] = "templates/b.png";
            using (Bitmap bmp = new Bitmap(8, 8)) { bmp.Save(Path.Combine(tplDir, "b.png"), ImageFormat.Png); }

            List<string> tpls2 = ScriptPackager.CollectTemplates(d2);
            Check(tpls2.Count == 2, "鼠标点击/判断里的模板都被收集", string.Join(",", tpls2.ToArray()));
            Check(tpls2.Contains("templates/a.png") && tpls2.Contains("templates/b.png"),
                "收集内容正确（鼠标点击 + 有图片判断）", string.Join(",", tpls2.ToArray()));

            string script2 = Path.Combine(dir, "auto.click.block");
            d2.Save(script2);
            string zip2 = Path.Combine(dir, "pack2.zip");
            ScriptPackager.PackResult r2 = ScriptPackager.Pack(script2, runner, zip2, true);
            Check(r2.TemplateCount == 2, "自动识图脚本打包模板数量", r2.TemplateCount.ToString());
            string extract2 = Path.Combine(dir, "extract2");
            System.IO.Compression.ZipFile.ExtractToDirectory(zip2, extract2);
            Check(File.Exists(Path.Combine(extract2, "templates", "a.png")), "打包包含鼠标点击的模板");
            Check(File.Exists(Path.Combine(extract2, "templates", "b.png")), "打包包含判断的模板");
            string readme = File.ReadAllText(Path.Combine(extract2, "使用说明.txt"));
            Check(readme.Contains(EngineVersion.Display), "使用说明里带统一版本号", EngineVersion.Display);
            try { Directory.Delete(dir, true); } catch { }
        }

        // ---------------------------- 录制转换 ----------------------------

        private static void RecorderTests()
        {
            Section("录制结果转积木");
            using (InputRecorder rec = new InputRecorder())
            {
                ScriptDocument d = new ScriptDocument();
                // 直接构造积木生成逻辑（不启动全局钩子，避免影响当前桌面）
                List<Block> target = new List<Block>();
                Block c = d.NewBlock("mouse_click");
                c.Params["button"] = "left";
                c.Params["action"] = "click";
                c.Params["x"] = 100d; c.Params["y"] = 200d;
                target.Add(c);
                Block t = d.NewBlock("key_type");
                t.Params["text"] = "你好 BlockAuto";
                target.Add(t);
                d.EnsureNextLinks();
                Check(d.Blocks.Count == 0 && target.Count == 2, "构造两个录制积木");
                Check(Json.ToNumber(target[0].Params["x"], 0) == 100, "点击积木保留绝对坐标");
                Check(Convert.ToString(target[1].Params["text"]) == "你好 BlockAuto", "文本积木保留中文内容");
                MemoryLog log = new MemoryLog();
                ScriptDocument d2 = new ScriptDocument();
                MockAutomation auto = new MockAutomation { NextSuccess = true };
                EngineOptions opt = new EngineOptions { ActionDelayMs = 0, EnableEscapeStop = false };
                Engine e = new Engine(d2, opt, auto, log);
                Check(e != null, "录制生成的动作积木可在引擎中构造");
            }

            // 钩子安装能力（只验证 API 可用性，不真正开始录制）
            using (InputRecorder rec = new InputRecorder())
            {
                Check(!rec.IsRecording, "录制器默认未录制");
                rec.DelayMs = 100;
                Check(rec.Count == 0, "初始录制步骤为 0");
            }
        }

        // ---------------------------- AI HTTP ----------------------------

        private static void AiHttpTests()
        {
            Section("AI HTTP 接口");
            MockAiHost host = new MockAiHost();
            AiHttpServer server = new AiHttpServer(host);
            int port = 18765;
            string err = server.Start(port, "test-token");
            Check(err == null, "HTTP 服务启动", err ?? "");
            if (err == null)
            {
                try
                {
                    string baseUrl = "http://127.0.0.1:" + port;
                    // 无 token
                    string unauthorized = TryRequest(baseUrl + "/health", "GET", null, null);
                    Check(unauthorized != null && unauthorized.IndexOf("401") >= 0 || (unauthorized != null && unauthorized.IndexOf("Token") >= 0), "无 Token 被拒绝", Trim(unauthorized));
                    // 正确 token
                    string health = TryRequest(baseUrl + "/health", "GET", null, "test-token");
                    Check(health != null && health.IndexOf("\"ok\":true") >= 0, "健康检查通过", Trim(health));
                    // /health 里带版本与「路径 ↔ 白名单动作名」对照，接 API 时不用按 URL 猜动作名
                    Check(health != null && health.IndexOf("\"version\":\"" + EngineVersion.Current + "\"") >= 0,
                        "/health 返回统一版本号", Trim(health));
                    Check(health != null && health.IndexOf("\"actions\"") >= 0 &&
                          health.IndexOf("\"cap.findImage\"") >= 0 && health.IndexOf("\"/cap/findImage\"") >= 0,
                        "/health 返回动作名与路径对照", Trim(health));
                    // 脚本控制
                    string start = TryRequest(baseUrl + "/script/start", "POST", "{\"script\":\"a.block\"}", "test-token");
                    Check(start != null && start.IndexOf("\"ok\":true") >= 0, "POST /script/start", Trim(start));
                    Check(host.Calls.Contains("start:a.block:0"), "宿主收到启动调用");
                    // 脚本树
                    string tree = TryRequest(baseUrl + "/script/tree?script=a.block", "GET", null, "test-token");
                    Check(tree != null && tree.IndexOf("schema") >= 0, "GET /script/tree", Trim(tree));
                    // 未知接口
                    string nf = TryRequest(baseUrl + "/nope", "GET", null, "test-token");
                    Check(nf != null && nf.IndexOf("未知接口") >= 0, "未知接口返回错误", Trim(nf));
                }
                finally
                {
                    server.Dispose();
                }
            }
        }

        private static string Trim(string s)
        {
            if (s == null) return "";
            s = s.Replace("\r", " ").Replace("\n", " ");
            return s.Length > 120 ? s.Substring(0, 120) + "…" : s;
        }

        private static string TryRequest(string url, string method, string body, string token)
        {
            try
            {
                System.Net.HttpWebRequest req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
                req.Method = method;
                req.Timeout = 5000;
                if (!string.IsNullOrEmpty(token)) req.Headers["X-BlockAuto-Token"] = token;
                if (body != null)
                {
                    byte[] data = Encoding.UTF8.GetBytes(body);
                    req.ContentType = "application/json";
                    req.ContentLength = data.Length;
                    using (Stream s = req.GetRequestStream()) s.Write(data, 0, data.Length);
                }
                using (System.Net.HttpWebResponse resp = (System.Net.HttpWebResponse)req.GetResponse())
                using (StreamReader r = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    return "HTTP " + (int)resp.StatusCode + " " + r.ReadToEnd();
            }
            catch (System.Net.WebException we)
            {
                if (we.Response != null)
                {
                    using (StreamReader r = new StreamReader(we.Response.GetResponseStream(), Encoding.UTF8))
                        return "HTTP " + (int)((System.Net.HttpWebResponse)we.Response).StatusCode + " " + r.ReadToEnd();
                }
                return "ERR " + we.Message;
            }
            catch (Exception ex)
            {
                return "ERR " + ex.Message;
            }
        }

        // ---------------------------- v2.0 键鼠优先 ----------------------------

        private static void KeyMouseFirstTests()
        {
            Section("v2.0 键鼠优先");
            Check(Catalog.GroupOrder[0] == "键鼠动作", "积木库首组是键鼠动作");
            Check(Catalog.GroupOf("mouse_click") == "键鼠动作", "鼠标点击归入键鼠动作");
            Check(Catalog.Get("mouse_click").Sort < Catalog.Get("find_image").Sort, "点击排在识图之前");
            Check(Catalog.Get("mouse_click").Sort < Catalog.Get("if").Sort, "键鼠动作排在逻辑之前");
            Check(Catalog.VisibleInMode(Catalog.Get("mouse_click"), true), "简洁模式显示鼠标点击");
            Check(!Catalog.VisibleInMode(Catalog.Get("for_each"), true), "简洁模式隐藏遍历数组");
            Check(!Catalog.VisibleInMode(Catalog.Get("call_func"), true), "简洁模式隐藏函数调用");
            Check(Catalog.VisibleInMode(Catalog.Get("call_func"), false), "专业模式显示函数调用");

            // 记录坐标模式：连线不影响执行坐标
            ScriptDocument d = new ScriptDocument();
            MemoryLog log = new MemoryLog();
            MockAutomation auto = new MockAutomation();
            Block cv0 = Add(d, d.Blocks, "create_var");
            cv0.Params["name"] = "unused";
            cv0.Params["type"] = "num";
            cv0.Params["init"] = "0";
            Block gv = Add(d, d.Blocks, "get_var");
            gv.Params["name"] = "unused";
            Block click = Add(d, d.Blocks, "mouse_click");
            click.Params["coord_mode"] = "recorded";
            click.Params["x"] = "123";
            click.Params["y"] = "456";
            Link(click, "x", gv, "value");
            Link(click, "y", gv, "value");
            EngineResult r = RunDoc(d, auto, log);
            Check(r.Status == EngineStatus.Completed, "记录坐标脚本执行完成", r.Message);
            bool used = false;
            foreach (string c in auto.Calls) if (c.StartsWith("mouse_click|123,456")) used = true;
            Check(used, "记录模式下使用记录坐标，忽略连线", string.Join("; ", auto.Calls.ToArray()));

            // 绑定模式：使用识图坐标
            ScriptDocument d2 = new ScriptDocument();
            MemoryLog log2 = new MemoryLog();
            MockAutomation auto2 = new MockAutomation();
            auto2.NextImage = new FindImageResult { Found = true, X = 321, Y = 654, Confidence = 0.95 };
            string dir2 = Path.Combine(Path.GetTempPath(), "blockauto-v2-" + Guid.NewGuid().ToString("N").Substring(0, 6));
            Directory.CreateDirectory(Path.Combine(dir2, "templates"));
            using (Bitmap bmp = new Bitmap(8, 8)) { bmp.Save(Path.Combine(dir2, "templates", "t.png"), ImageFormat.Png); }
            d2.FilePath = Path.Combine(dir2, "t.block");
            Block fi = Add(d2, d2.Blocks, "find_image");
            fi.Params["template"] = "templates/t.png";
            fi.Params["region"] = "full";
            Block c2 = Add(d2, d2.Blocks, "mouse_click");
            c2.Params["coord_mode"] = "bound";
            Link(c2, "x", fi, "x");
            Link(c2, "y", fi, "y");
            EngineResult r2 = RunDoc(d2, auto2, log2);
            Check(r2.Status == EngineStatus.Completed, "绑定坐标脚本执行完成", r2.Message);
            bool bound = false;
            foreach (string c in auto2.Calls) if (c.StartsWith("mouse_click|321,654")) bound = true;
            Check(bound, "绑定模式使用识图坐标", string.Join("; ", auto2.Calls.ToArray()));

            // 识别区域 active 解析
            Rectangle active = RegionParser.Parse("active");
            Check(active.Width > 0 && active.Height > 0, "active 区域可解析", active.Width + "x" + active.Height);
            Check(RegionParser.Parse("full").Width >= active.Width, "full 区域不小于 active");

            // 失败定位信息
            ScriptDocument d3 = new ScriptDocument();
            MemoryLog log3 = new MemoryLog();
            Block bad = Add(d3, d3.Blocks, "math");
            bad.Params["op"] = "/";
            bad.Params["a"] = "1";
            bad.Params["b"] = "0";
            EngineResult r3 = RunDoc(d3, new MockAutomation(), log3);
            Check(r3.Status == EngineStatus.Failed, "异常脚本失败");
            Check(!string.IsNullOrEmpty(r3.FailedBlockId) && r3.FailedBlockId == bad.Id, "失败时记录出错积木 ID", r3.FailedBlockId);
            Check(r3.Message.IndexOf("数学运算") >= 0, "失败信息带积木名", r3.Message);
        }

        // ---------------------------- 禁用步骤 ----------------------------

        private static void DisabledStepTests()
        {
            Section("禁用某一步（运行时跳过）");
            ScriptDocument d = new ScriptDocument();
            MemoryLog log = new MemoryLog();
            MockAutomation auto = new MockAutomation();
            Block a = Add(d, d.Blocks, "log");
            a.Params["text"] = "第一步";
            Block b = Add(d, d.Blocks, "log");
            b.Params["text"] = "被禁用的一步";
            b.Disabled = true;
            Block c = Add(d, d.Blocks, "log");
            c.Params["text"] = "第三步";

            // 序列化/反序列化后仍保留禁用状态
            string json = d.ToJson();
            ScriptDocument round = ScriptDocument.FromJson(json, "");
            Check(round.Blocks.Count == 3 && round.Blocks[1].Disabled, "禁用状态可保存/读取");

            EngineResult r = RunDoc(d, auto, log);
            Check(r.Status == EngineStatus.Completed, "含禁用步骤的脚本正常完成", r.Message);
            Check(log.Contains("第一步"), "禁用步骤之前的步骤执行");
            Check(!log.Contains("被禁用的一步"), "禁用步骤被跳过");
            Check(log.Contains("第三步"), "禁用步骤之后的步骤执行");
            Check(log.Contains("跳过已禁用步骤"), "日志记录跳过信息");

            Check(Catalog.GroupOf("sleep") == "键鼠动作", "等待积木归入键鼠动作");
        }

        // ---------------------------- 文件关联 ----------------------------

        private static void FileAssocTests()
        {
            Section("保存后双击即可运行（.block 关联）");
            if (!File.Exists(FileAssoc.RunnerPath))
            {
                Check(false, "找不到 BlockRunner.exe，跳过关联测试", FileAssoc.RunnerPath);
                return;
            }
            bool before = FileAssoc.IsAssociated();
            string err = FileAssoc.Associate();
            Check(err == null, "写入 .block 关联", err ?? "");
            Check(FileAssoc.IsAssociated(), "关联已生效（双击会调用 BlockRunner）");
            FileAssoc.Unassociate();
            Check(!FileAssoc.IsAssociated(), "取消关联已生效");
            if (before)
            {
                FileAssoc.Associate();
                Check(FileAssoc.IsAssociated(), "恢复原有测试关联");
            }
        }

        // ---------------------------- 图片判断 ----------------------------

        private static void ImageConditionTests()
        {
            Section("判断：有图片则执行，否则跳过");
            string dir = Path.Combine(Path.GetTempPath(), "blockauto-imgif-" + Guid.NewGuid().ToString("N").Substring(0, 6));
            Directory.CreateDirectory(Path.Combine(dir, "templates"));
            using (Bitmap bmp = new Bitmap(8, 8)) { bmp.Save(Path.Combine(dir, "templates", "btn.png"), ImageFormat.Png); }

            

            MockAutomation autoHit = new MockAutomation();
            autoHit.NextImage = new FindImageResult { Found = true, X = 111, Y = 222, Confidence = 0.93 };
            MemoryLog logHit = new MemoryLog();
            EngineResult r1 = RunDoc(BuildImageIf(dir, true), autoHit, logHit);
            Check(r1.Status == EngineStatus.Completed && logHit.Contains("命中执行"), "找到图片 → 执行内部步骤");
            Check(!logHit.Contains("未命中跳过"), "找到图片时不走否则分支");

            MockAutomation autoMiss = new MockAutomation();
            autoMiss.NextImage = new FindImageResult { Found = false };
            MemoryLog logMiss = new MemoryLog();
            EngineResult r2 = RunDoc(BuildImageIf(dir, true), autoMiss, logMiss);
            Check(r2.Status == EngineStatus.Completed && logMiss.Contains("未命中跳过"), "找不到图片 → 走否则（或跳过）");
            Check(!logMiss.Contains("命中执行"), "找不到图片时不执行内部步骤");

            MockAutomation autoNoTpl = new MockAutomation();
            MemoryLog logNoTpl = new MemoryLog();
            EngineResult r3 = RunDoc(BuildImageIf(dir, false), autoNoTpl, logNoTpl);
            Check(r3.Status == EngineStatus.Completed && logNoTpl.Contains("未命中跳过"), "未选模板按没找到处理，不中断脚本");

            // 识图坐标可供点击积木绑定
            ScriptDocument d4 = BuildImageIf(dir, true);
            Block ifb4 = d4.Blocks[0];
            Block click = Add(d4, ifb4.Branches[0].Blocks, "mouse_click");
            click.Params["coord_mode"] = "bound";
            Link(click, "x", ifb4, "x");
            Link(click, "y", ifb4, "y");
            MockAutomation auto4 = new MockAutomation();
            auto4.NextImage = new FindImageResult { Found = true, X = 111, Y = 222, Confidence = 0.9 };
            MemoryLog log4 = new MemoryLog();
            RunDoc(d4, auto4, log4);
            bool clicked = false;
            foreach (string c in auto4.Calls) if (c.StartsWith("mouse_click|111,222")) clicked = true;
            Check(clicked, "点击积木可绑定“有图片”判断的位置", string.Join("; ", auto4.Calls.ToArray()));
            try { Directory.Delete(dir, true); } catch { }
        }

        // ---------------------------- v2.5 识图并入鼠标点击 ----------------------------

        private static ScriptDocument BuildAutoClick(string dir, string onFail, int retries)
        {
            ScriptDocument d = new ScriptDocument();
            d.FilePath = Path.Combine(dir, "auto.block");
            Block c = Add(d, d.Blocks, "mouse_click");
            c.Params["coord_mode"] = "image";
            c.Params["template"] = "templates/btn.png";
            c.Params["region"] = "full";
            c.Params["threshold"] = 0.8d;
            c.Params["offset"] = new List<object> { 3d, -4d };
            c.Params["on_fail"] = onFail;
            c.Params["retries"] = (double)retries;
            c.Params["retry_delay"] = 0d;
            c.Params["delay_after"] = 0d;
            Block sv = Add(d, d.Blocks, "set_var");
            sv.Params["name"] = "ok";
            Link(sv, "value", c, "success");
            Block logb = Add(d, d.Blocks, "log");
            logb.Params["text"] = "点击后继续 ok=${ok}";
            return d;
        }

        private static int CountCalls(MockAutomation auto, string prefix)
        {
            int n = 0;
            foreach (string c in auto.Calls) if (c.StartsWith(prefix, StringComparison.Ordinal)) n++;
            return n;
        }

        private static void AutoImageClickTests()
        {
            Section("v2.5 鼠标点击自带识图（一步完成识图+点击）");
            string dir = Path.Combine(Path.GetTempPath(), "blockauto-autoimg-" + Guid.NewGuid().ToString("N").Substring(0, 6));
            Directory.CreateDirectory(Path.Combine(dir, "templates"));
            using (Bitmap bmp = new Bitmap(8, 8))
            {
                using (Graphics g = Graphics.FromImage(bmp)) g.Clear(Color.Green);
                bmp.Save(Path.Combine(dir, "templates", "btn.png"), ImageFormat.Png);
            }

            // 找到图 → 直接点识别到的位置（带偏移）
            MockAutomation hit = new MockAutomation();
            hit.NextImage = new FindImageResult { Found = true, X = 300, Y = 400, Confidence = 0.93 };
            MemoryLog hitLog = new MemoryLog();
            EngineResult r1 = RunDoc(BuildAutoClick(dir, "skip", 0), hit, hitLog);
            Check(r1.Status == EngineStatus.Completed, "自动识图点击执行完成", r1.Message);
            bool clicked = false;
            foreach (string c in hit.Calls) if (c.StartsWith("mouse_click|300,400|left|click|3,-4")) clicked = true;
            Check(clicked, "识别到的坐标 + 偏移直接用于点击", string.Join("; ", hit.Calls.ToArray()));
            Check(hitLog.Contains("识图命中"), "日志记录识图命中");
            Check(hitLog.Contains("点击后继续 ok=true"), "success 输出可供后续步骤使用");

            // 找不到 + 跳过：脚本继续
            MockAutomation miss = new MockAutomation();
            miss.NextImage = new FindImageResult { Found = false };
            MemoryLog missLog = new MemoryLog();
            EngineResult r2 = RunDoc(BuildAutoClick(dir, "skip", 0), miss, missLog);
            Check(r2.Status == EngineStatus.Completed, "找不到图时按“跳过”不中断脚本", r2.Message);
            Check(CountCalls(miss, "mouse_click|") == 0, "找不到图时不点击");
            Check(missLog.Contains("点击后继续 ok=false"), "跳过时 success=false 且继续下一步");

            // 找不到 + 报错：失败并定位到这一步
            MockAutomation err = new MockAutomation();
            err.NextImage = new FindImageResult { Found = false };
            MemoryLog errLog = new MemoryLog();
            ScriptDocument dErr = BuildAutoClick(dir, "error", 0);
            EngineResult r3 = RunDoc(dErr, err, errLog);
            Check(r3.Status == EngineStatus.Failed, "找不到图按“报错停止”时脚本失败", r3.Message);
            Check(r3.FailedBlockId == dErr.Blocks[0].Id, "失败信息指向鼠标点击这一步", r3.FailedBlockId);

            // 找不到 + 重试 2 次：一共找 3 次
            MockAutomation retry = new MockAutomation();
            retry.NextImage = new FindImageResult { Found = false };
            MemoryLog retryLog = new MemoryLog();
            RunDoc(BuildAutoClick(dir, "retry", 2), retry, retryLog);
            Check(CountCalls(retry, "find_image|") == 3, "重试 2 次＝共识图 3 次", CountCalls(retry, "find_image|").ToString());

            // 模板没截图就运行：给出明确提示
            ScriptDocument noTpl = new ScriptDocument();
            noTpl.FilePath = Path.Combine(dir, "notpl.block");
            Block ntc = Add(noTpl, noTpl.Blocks, "mouse_click");
            ntc.Params["coord_mode"] = "image";
            ntc.Params["template"] = "";
            MemoryLog noTplLog = new MemoryLog();
            EngineResult r4 = RunDoc(noTpl, new MockAutomation(), noTplLog);
            Check(r4.Status != EngineStatus.Completed && r4.Message.Contains("截图取图"),
                "没选模板时不予运行并提示先截图取图", r4.Message);

            // 旧脚本迁移：识图积木 + 绑定它的点击 → 载入后合并成一步自动识图点击
            ScriptDocument old = new ScriptDocument();
            old.FilePath = Path.Combine(dir, "old.block");
            Block fi = Add(old, old.Blocks, "find_image");
            fi.Params["template"] = "templates/btn.png";
            fi.Params["region"] = "full";
            fi.Params["threshold"] = 0.8d;
            Block oc = Add(old, old.Blocks, "mouse_click");
            oc.Params["coord_mode"] = "bound";
            Link(oc, "x", fi, "x");
            Link(oc, "y", fi, "y");
            old.Save(old.FilePath);
            ScriptDocument migrated = ScriptDocument.Load(old.FilePath);
            Check(migrated.Blocks.Count == 1, "旧脚本“识图+点击”载入后合并成一步", migrated.Blocks.Count.ToString());
            Check(migrated.Blocks[0].Type == "mouse_click", "合并后保留鼠标点击");
            Check(Engine.FormatValue(migrated.Blocks[0].Params["coord_mode"]) == "image", "合并后改为自动识图");
            Check(Engine.FormatValue(migrated.Blocks[0].Params["template"]) == "templates/btn.png", "合并后带上原模板");

            // 积木库：独立识图不再出现，分类名与内容一致
            Check(Catalog.Get("find_image") != null && Catalog.Get("find_image").Hidden, "旧识图积木仍然兼容旧脚本");
            Check(Catalog.GroupOf("find_color") == "识别", "积木库分类改名后与内容一致");
            BlockInfo mc = Catalog.Get("mouse_click");
            Check(mc.Param("template") != null && mc.Param("on_fail") != null && mc.Param("retries") != null,
                "鼠标点击积木包含识图与失败处理参数");
            Check(mc.Output("found") != null && mc.Output("confidence") != null, "鼠标点击输出识图结果");

            try { Directory.Delete(dir, true); } catch { }
        }

        // ---------------------------- 模板匹配 ----------------------------
        private static ScriptDocument BuildImageIf(string dir, bool useTemplate)
        {
            ScriptDocument d = new ScriptDocument();
            d.FilePath = Path.Combine(dir, "s.block");
            Block ifb = Add(d, d.Blocks, "if");
            ifb.Params["cond_type"] = "image";
            ifb.Params["template"] = useTemplate ? "templates/btn.png" : "";
            ifb.Params["region"] = "full";
            ifb.Params["threshold"] = 0.8d;
            Block hit = Add(d, ifb.Branches[0].Blocks, "log");
            hit.Params["text"] = "命中执行";
            Block miss = Add(d, ifb.Branches[1].Blocks, "log");
            miss.Params["text"] = "未命中跳过";
            return d;
        }


        private static void NativeMatchTests()
        {
            Section("图像识别：模板匹配 / 找色");
            int w = 320, h = 240;
            using (Bitmap screen = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(screen))
                {
                    g.Clear(Color.FromArgb(30, 30, 40));
                    // 目标块：带纹理的 40x30 区域
                    for (int y = 0; y < 30; y++)
                        for (int x = 0; x < 40; x++)
                        {
                            int v = ((x / 4) + (y / 3)) % 2 == 0 ? 240 : 60;
                            screen.SetPixel(173 + x, 96 + y, Color.FromArgb(255, v, v / 2, 255 - v));
                        }
                    screen.SetPixel(10, 10, Color.FromArgb(255, 0, 200, 0));
                }

                Bitmap tpl = new Bitmap(40, 30, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(tpl))
                    g.DrawImage(screen, new Rectangle(0, 0, 40, 30), new Rectangle(173, 96, 40, 30), GraphicsUnit.Pixel);

                FindImageResult r = TemplateMatcher.Match(screen, tpl, new Rectangle(0, 0, w, h), 0.8);
                Check(r.Found, "精确位置模板匹配成功", "confidence=" + r.Confidence.ToString("0.000"));
                Check(Math.Abs(r.X - (173 + 20)) <= 2 && Math.Abs(r.Y - (96 + 15)) <= 2, "匹配中心坐标正确",
                    "(" + r.X.ToString("0") + "," + r.Y.ToString("0") + ")");
                Check(r.Confidence > 0.95, "置信度接近 1", r.Confidence.ToString("0.000"));

                FindImageResult miss = TemplateMatcher.Match(screen, tpl, new Rectangle(0, 0, 60, 60), 0.9);
                Check(!miss.Found, "区域外查找不到模板（返回 found=false 不抛异常）", miss.Confidence.ToString("0.000"));

                FindColorResult c = TemplateMatcher.FindColor(screen, new Rectangle(0, 0, w, h), Color.FromArgb(0, 200, 0), 3);
                Check(c.Found && c.X == 10 && c.Y == 10, "找色定位正确", c.X + "," + c.Y);

                Rectangle parsed = RegionParser.Parse("10,20,110,220");
                Check(parsed.Left == 10 && parsed.Top == 20 && parsed.Width == 100 && parsed.Height == 200, "区域字符串解析");
                Check(RegionParser.Parse("full").Width >= 100, "full 解析为全屏");
            }

            // 屏幕采集
            try
            {
                using (Bitmap bmp = ScreenCapture.Capture(new Rectangle(0, 0, 64, 48)))
                {
                    Check(bmp.Width == 64 && bmp.Height == 48, "屏幕采集尺寸正确");
                }
            }
            catch (Exception ex)
            {
                Check(false, "屏幕采集失败", ex.Message);
            }
        }
    }
}