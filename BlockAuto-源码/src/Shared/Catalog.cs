using System;
using System.Collections.Generic;
using System.Drawing;

namespace BlockAuto
{
    public static class BlockCategory
    {
        public const string Logic = "logic";
        public const string Var = "var";
        public const string Action = "action";
        public const string Func = "func";
        public const string Tool = "tool";

        public static string Title(string cat)
        {
            switch (cat)
            {
                case Logic: return "逻辑控制";
                case Var: return "变量与运算";
                case Action: return "动作";
                case Func: return "函数";
                case Tool: return "工具";
                default: return cat;
            }
        }

        public static Color CategoryColor(string cat)
        {
            switch (cat)
            {
                case Logic: return Color.FromArgb(0xF3, 0x9C, 0x12);
                case Var: return Color.FromArgb(0x35, 0x9B, 0xDB);
                case Func: return Color.FromArgb(0x9B, 0x59, 0xB6);
                case Tool: return Color.FromArgb(0x7F, 0x8C, 0x8D);
                default: return System.Drawing.Color.FromArgb(0x27, 0xAE, 0x60);
            }
        }
    }

    public sealed class PortSpec
    {
        public string Name;
        public string Label;
        public string Type;   // num / bool / str / any / array
        public PortSpec(string name, string label, string type)
        {
            Name = name; Label = label; Type = type;
        }
    }

    public sealed class BranchSpec
    {
        public string Name;
        public string Label;
        public string ConditionPort; // null = 无独立条件
        public BranchSpec(string name, string label, string condPort)
        {
            Name = name; Label = label; ConditionPort = condPort;
        }
    }

    public sealed class ParamSpec
    {
        public string Name;
        public string Label;
        public string Kind;     // text / number / bool / choice / template / region / rgb / offset / multiline
        public object Default;
        public string[] Options;
        public string Hint;

        public ParamSpec(string name, string label, string kind, object def)
        {
            Name = name; Label = label; Kind = kind; Default = def;
        }

        public ParamSpec WithOptions(params string[] opts)
        {
            Options = opts;
            return this;
        }

        public ParamSpec WithHint(string hint)
        {
            Hint = hint;
            return this;
        }
    }

    public sealed class BlockInfo
    {
        public string Type;
        public string Category;
        public string Title;
        public string Summary;
        public int Width = 250;
        public List<PortSpec> Inputs = new List<PortSpec>();
        public List<PortSpec> Outputs = new List<PortSpec>();
        public List<BranchSpec> Branches = new List<BranchSpec>();
        public List<ParamSpec> Params = new List<ParamSpec>();
        public bool DynamicBranches;
        public bool DynamicInputs;
        /// <summary>basic = 简洁模式可见；advanced = 仅专业模式可见</summary>
        public string Level = "basic";
        /// <summary>同组内排序号，越小越靠前</summary>
        public int Sort = 100;
        /// <summary>旧版兼容积木：不在积木库显示，但旧脚本仍能打开与运行</summary>
        public bool Hidden;
        /// <summary>判断类积木：输出 bool，可直接接「如果 / 当…时循环」的条件端口</summary>
        public bool IsCondition;

        public PortSpec Input(string name)
        {
            foreach (PortSpec p in Inputs) if (p.Name == name) return p;
            return null;
        }

        public PortSpec Output(string name)
        {
            foreach (PortSpec p in Outputs) if (p.Name == name) return p;
            return null;
        }

        public ParamSpec Param(string name)
        {
            foreach (ParamSpec p in Params) if (p.Name == name) return p;
            return null;
        }
    }

    /// <summary>积木类型目录：标题、颜色、端口、参数、容器分支。</summary>
    public static class Catalog
    {
        private static readonly Dictionary<string, BlockInfo> Map = new Dictionary<string, BlockInfo>();
        private static readonly List<string> Order = new List<string>();

        public static IEnumerable<BlockInfo> All
        {
            get
            {
                foreach (string t in Order) yield return Map[t];
            }
        }

        public static BlockInfo Get(string type)
        {
            BlockInfo info;
            if (Map.TryGetValue(type, out info)) return info;
            return null;
        }

        public static string CategoryOf(string type)
        {
            BlockInfo b = Get(type);
            return b != null ? b.Category : BlockCategory.Tool;
        }

        public static string TitleOf(string type)
        {
            BlockInfo b = Get(type);
            return b != null ? b.Title : type;
        }

        public static bool IsContainer(string type)
        {
            BlockInfo b = Get(type);
            return b != null && b.Branches.Count > 0;
        }

        private static BlockInfo Add(BlockInfo b)
        {
            Map[b.Type] = b;
            Order.Add(b.Type);
            return b;
        }

        private static PortSpec P(string name, string label, string type)
        {
            return new PortSpec(name, label, type);
        }

        private static ParamSpec Pa(string name, string label, string kind, object def)
        {
            return new ParamSpec(name, label, kind, def);
        }

        static Catalog()
        {
            // ---------------- 逻辑控制 ----------------
            BlockInfo ifb = new BlockInfo
            {
                Type = "if",
                Category = BlockCategory.Logic,
                Title = "如果 / 否则如果 / 否则",
                Summary = "条件判断",
                Width = 340,
                DynamicBranches = true
            };
            ifb.Inputs.Add(P("cond", "如果", "bool"));
            ifb.Branches.Add(new BranchSpec("main", "如果", "cond"));
            ifb.Branches.Add(new BranchSpec("else", "否则", null));
            ifb.Params.Add(Pa("branches", "分支", "choice", null));
            ifb.Params.Add(Pa("cond_type", "判断方式", "choice", "cond").WithOptions("cond", "image"));
            ifb.Params.Add(Pa("template", "模板图片", "template", ""));
            ifb.Params.Add(Pa("region", "识别区域", "region", "active"));
            ifb.Params.Add(Pa("threshold", "相似度", "number", 0.8d));
            ifb.Outputs.Add(P("found", "找到", "bool"));
            ifb.Outputs.Add(P("x", "中心X", "num"));
            ifb.Outputs.Add(P("y", "中心Y", "num"));
            ifb.Outputs.Add(P("confidence", "置信度", "num"));
            Add(ifb);

            BlockInfo w = new BlockInfo
            {
                Type = "while",
                Category = BlockCategory.Logic,
                Title = "当……时循环",
                Summary = "条件循环",
                Width = 340
            };
            w.Inputs.Add(P("cond", "条件", "bool"));
            w.Branches.Add(new BranchSpec("body", "循环体", "cond"));
            Add(w);

            BlockInfo fc = new BlockInfo
            {
                Type = "for_count",
                Category = BlockCategory.Logic,
                Title = "循环 N 次",
                Summary = "固定次数循环",
                Width = 340
            };
            fc.Inputs.Add(P("count", "次数", "num"));
            fc.Params.Add(Pa("times", "次数", "number", 3d));
            fc.Branches.Add(new BranchSpec("body", "循环体", null));
            Add(fc);

            BlockInfo fe = new BlockInfo
            {
                Type = "for_each",
                Category = BlockCategory.Logic,
                Title = "遍历数组",
                Summary = "遍历数组元素",
                Width = 340
            };
            fe.Inputs.Add(P("list", "数组", "array"));
            fe.Params.Add(Pa("item_var", "元素变量名", "text", "item"));
            fe.Branches.Add(new BranchSpec("body", "循环体", null));
            Add(fe);

            Add(new BlockInfo { Type = "break", Category = BlockCategory.Logic, Title = "跳出循环", Summary = "break", Width = 160 });
            Add(new BlockInfo { Type = "continue", Category = BlockCategory.Logic, Title = "跳过本次", Summary = "continue", Width = 170 });

            BlockInfo sl = new BlockInfo { Type = "sleep", Category = BlockCategory.Logic, Title = "等待", Summary = "延时（秒）", Width = 210 };
            sl.Params.Add(Pa("seconds", "秒", "number", 1d).WithHint("支持小数，例如 0.5"));
            Add(sl);

            BlockInfo tc = new BlockInfo
            {
                Type = "try",
                Category = BlockCategory.Logic,
                Title = "异常捕获",
                Summary = "try / catch",
                Width = 340
            };
            tc.Branches.Add(new BranchSpec("try", "尝试", null));
            tc.Branches.Add(new BranchSpec("catch", "捕获异常", null));
            tc.Outputs.Add(P("error_msg", "异常信息", "str"));
            Add(tc);

            // ---------------- 变量与运算 ----------------
            BlockInfo cv = new BlockInfo { Type = "create_var", Category = BlockCategory.Var, Title = "创建变量", Summary = "声明变量", Width = 250 };
            cv.Params.Add(Pa("name", "变量名", "text", "value"));
            cv.Params.Add(Pa("scope", "作用域", "choice", "local").WithOptions("local", "global"));
            cv.Params.Add(Pa("type", "类型", "choice", "num").WithOptions("num", "str", "bool", "array", "dict"));
            cv.Params.Add(Pa("init", "初始值", "text", ""));
            Add(cv);

            BlockInfo sv = new BlockInfo { Type = "set_var", Category = BlockCategory.Var, Title = "变量赋值", Summary = "写入变量", Width = 250 };
            sv.Inputs.Add(P("value", "值", "any"));
            sv.Params.Add(Pa("name", "变量名", "text", "value"));
            sv.Params.Add(Pa("value", "值", "text", ""));
            Add(sv);

            BlockInfo gv = new BlockInfo { Type = "get_var", Category = BlockCategory.Var, Title = "读取变量", Summary = "读取变量值", Width = 220 };
            gv.Params.Add(Pa("name", "变量名", "text", "value"));
            gv.Outputs.Add(P("value", "值", "any"));
            Add(gv);

            BlockInfo cmp = new BlockInfo { Type = "compare", Category = BlockCategory.Var, Title = "比较", Summary = "比较两个值", Width = 260 };
            cmp.Inputs.Add(P("a", "A", "any"));
            cmp.Inputs.Add(P("b", "B", "any"));
            cmp.Params.Add(Pa("op", "运算符", "choice", "==").WithOptions("==", "!=", ">", "<", ">=", "<="));
            cmp.Params.Add(Pa("a", "A 字面值", "text", "0"));
            cmp.Params.Add(Pa("b", "B 字面值", "text", "0"));
            cmp.Outputs.Add(P("result", "结果", "bool"));
            Add(cmp);

            BlockInfo lo = new BlockInfo { Type = "logic_op", Category = BlockCategory.Var, Title = "逻辑运算", Summary = "and / or / not", Width = 260 };
            lo.Inputs.Add(P("a", "A", "bool"));
            lo.Inputs.Add(P("b", "B", "bool"));
            lo.Params.Add(Pa("op", "运算符", "choice", "and").WithOptions("and", "or", "not"));
            lo.Params.Add(Pa("a", "A 字面值", "text", "false"));
            lo.Params.Add(Pa("b", "B 字面值", "text", "false"));
            lo.Outputs.Add(P("result", "结果", "bool"));
            Add(lo);

            BlockInfo m = new BlockInfo { Type = "math", Category = BlockCategory.Var, Title = "数学运算", Summary = "+ - * / %", Width = 250 };
            m.Inputs.Add(P("a", "A", "num"));
            m.Inputs.Add(P("b", "B", "num"));
            m.Params.Add(Pa("op", "运算符", "choice", "+").WithOptions("+", "-", "*", "/", "%"));
            m.Params.Add(Pa("a", "A 字面值", "text", "0"));
            m.Params.Add(Pa("b", "B 字面值", "text", "0"));
            m.Outputs.Add(P("result", "结果", "num"));
            Add(m);

            BlockInfo cc = new BlockInfo { Type = "concat", Category = BlockCategory.Var, Title = "字符串拼接", Summary = "拼接多个值", Width = 260 };
            cc.Inputs.Add(P("a", "A", "any"));
            cc.Inputs.Add(P("b", "B", "any"));
            cc.Params.Add(Pa("a", "A 字面值", "text", ""));
            cc.Params.Add(Pa("b", "B 字面值", "text", ""));
            cc.Outputs.Add(P("result", "结果", "str"));
            Add(cc);

            // ---------------- 动作 ----------------
            // 旧版独立「识图」积木：v2.5 起不再出现在积木库，
            // 识图能力合并进「鼠标点击」；保留定义只为让旧脚本仍能打开与运行。
            // 识图：判断类积木（输出 found，可直接当 if/while 的条件）
            BlockInfo fi = new BlockInfo { Type = "find_image", Category = BlockCategory.Action, Title = "识图", Summary = "模板匹配，找到=真（判断类）", Width = 290 };
            fi.IsCondition = true;
            fi.Params.Add(Pa("template", "模板", "template", "templates/sample.png"));
            fi.Params.Add(Pa("region", "识别区域", "region", "active").WithHint("active=当前窗口，full=全屏，或 x1,y1,x2,y2"));
            fi.Params.Add(Pa("threshold", "相似度", "number", 0.8d).WithHint("0 ~ 1，留空使用全局默认"));
            fi.Outputs.Add(P("found", "找到", "bool"));
            fi.Outputs.Add(P("x", "中心X", "num"));
            fi.Outputs.Add(P("y", "中心Y", "num"));
            fi.Outputs.Add(P("confidence", "置信度", "num"));
            Add(fi);

            BlockInfo fcolor = new BlockInfo { Type = "find_color", Category = BlockCategory.Action, Title = "找色", Summary = "查找颜色，找到=真（判断类）", Width = 280 };
            fcolor.IsCondition = true;
            fcolor.Params.Add(Pa("rgb", "颜色", "rgb", "#FF0000"));
            fcolor.Params.Add(Pa("region", "识别区域", "region", "full"));
            fcolor.Params.Add(Pa("tolerance", "颜色容差", "number", 8d));
            fcolor.Outputs.Add(P("found", "找到", "bool"));
            fcolor.Outputs.Add(P("x", "X", "num"));
            fcolor.Outputs.Add(P("y", "Y", "num"));
            Add(fcolor);

            // v2.5：鼠标点击＝识图 + 点击一步完成，不再需要前置识图积木
            BlockInfo mc = new BlockInfo { Type = "mouse_click", Category = BlockCategory.Action, Title = "鼠标点击", Summary = "固定坐标或自动识图后点击", Width = 270 };
            mc.Inputs.Add(P("x", "X", "num"));
            mc.Inputs.Add(P("y", "Y", "num"));
            mc.Params.Add(Pa("coord_mode", "坐标来源", "choice", "recorded").WithOptions("recorded", "image", "bound")
                .WithHint("recorded=固定坐标；image=自动识图（推荐）；bound=旧版识图连线"));
            mc.Params.Add(Pa("template", "识别模板", "template", "").WithHint("点「截图取图」框选目标，运行时自动找到并点击"));
            mc.Params.Add(Pa("region", "识别区域", "region", "active").WithHint("active=当前窗口，full=全屏，或 x1,y1,x2,y2"));
            mc.Params.Add(Pa("threshold", "相似度", "number", 0.8d).WithHint("0 ~ 1，留空使用全局默认"));
            mc.Params.Add(Pa("button", "按键", "choice", "left").WithOptions("left", "right"));
            mc.Params.Add(Pa("action", "动作", "choice", "click").WithOptions("click", "dblclick"));
            mc.Params.Add(Pa("offset", "偏移(dx,dy)", "offset", "0,0"));
            mc.Params.Add(Pa("on_fail", "找不到图时", "choice", "retry").WithOptions("retry", "skip", "error")
                .WithHint("retry=重试；skip=跳过这一步；error=报错停止"));
            mc.Params.Add(Pa("retries", "重试次数", "number", 3d));
            mc.Params.Add(Pa("retry_delay", "重试间隔(秒)", "number", 0.3d).WithHint("识别本身要花时间，间隔越大整段越慢"));
            mc.Params.Add(Pa("delay_after", "点击后延时(秒)", "number", 0.2d).WithHint("0 表示使用全局动作间隔"));
            mc.Params.Add(Pa("x", "记录 X", "text", "").WithHint("点“记录位置”自动获取"));
            mc.Params.Add(Pa("y", "记录 Y", "text", ""));
            mc.Outputs.Add(P("success", "成功", "bool"));
            mc.Outputs.Add(P("found", "找到", "bool"));
            mc.Outputs.Add(P("x", "中心X", "num"));
            mc.Outputs.Add(P("y", "中心Y", "num"));
            mc.Outputs.Add(P("confidence", "置信度", "num"));
            Add(mc);

            BlockInfo mm = new BlockInfo { Type = "mouse_move", Category = BlockCategory.Action, Title = "鼠标移动", Summary = "移动到坐标", Width = 250 };
            mm.Inputs.Add(P("x", "X", "num"));
            mm.Inputs.Add(P("y", "Y", "num"));
            mm.Params.Add(Pa("x", "X 字面值", "text", ""));
            mm.Params.Add(Pa("y", "Y 字面值", "text", ""));
            mm.Outputs.Add(P("success", "成功", "bool"));
            Add(mm);

            BlockInfo mw = new BlockInfo { Type = "mouse_wheel", Category = BlockCategory.Action, Title = "鼠标滚轮", Summary = "滚轮向上/向下", Width = 250 };
            mw.Params.Add(Pa("dir", "方向", "choice", "down").WithOptions("up", "down"));
            mw.Params.Add(Pa("amount", "格数", "number", 3d));
            mw.Outputs.Add(P("success", "成功", "bool"));
            Add(mw);

            BlockInfo kt = new BlockInfo { Type = "key_type", Category = BlockCategory.Action, Title = "输入文本", Summary = "键入文本（支持中文）", Width = 280 };
            kt.Inputs.Add(P("text", "文本", "str"));
            kt.Params.Add(Pa("text", "文本内容", "multiline", ""));
            kt.Outputs.Add(P("success", "成功", "bool"));
            Add(kt);

            BlockInfo kp = new BlockInfo { Type = "key_press", Category = BlockCategory.Action, Title = "按键 / 快捷键", Summary = "如 F5、enter、ctrl+c", Width = 270 };
            kp.Params.Add(Pa("key", "按键", "key", "enter").WithHint("可选常用键 + Ctrl/Alt/Shift 组合，也可手填 ctrl+c"));
            kp.Outputs.Add(P("success", "成功", "bool"));
            Add(kp);

            BlockInfo lg = new BlockInfo { Type = "log", Category = BlockCategory.Action, Title = "输出日志", Summary = "打印到日志面板", Width = 270 };
            lg.Inputs.Add(P("text", "内容", "any"));
            lg.Params.Add(Pa("level", "级别", "choice", "INFO").WithOptions("INFO", "WARN", "ERROR"));
            lg.Params.Add(Pa("text", "内容", "text", ""));
            Add(lg);

            // ---------------- 函数 ----------------
            BlockInfo cf = new BlockInfo { Type = "call_func", Category = BlockCategory.Func, Title = "调用函数", Summary = "调用自定义函数", Width = 280, DynamicInputs = true };
            cf.Params.Add(Pa("func", "函数", "text", ""));
            cf.Outputs.Add(P("result", "返回值", "any"));
            Add(cf);

            BlockInfo ret = new BlockInfo { Type = "return", Category = BlockCategory.Func, Title = "返回", Summary = "函数返回值", Width = 230 };
            ret.Inputs.Add(P("value", "返回值", "any"));
            ret.Outputs.Add(P("value", "值", "any"));
            Add(ret);

            // ---------------- 工具 ----------------
            BlockInfo cm = new BlockInfo { Type = "comment", Category = BlockCategory.Tool, Title = "注释", Summary = "备注，不执行", Width = 300 };
            cm.Params.Add(Pa("text", "注释", "multiline", ""));
            Add(cm);

            // ---------------- v2.0 键鼠优先：排序 / 分级 ----------------
            Meta("mouse_click", 10);
            Meta("mouse_move", 20);
            Meta("key_type", 30);
            Meta("key_press", 40);
            Meta("mouse_wheel", 50);
            Meta("find_image", 60);
            Meta("find_color", 70);
            Meta("sleep", 15);
            Meta("if", 110);
            Meta("while", 120);
            Meta("for_count", 130);
            Meta("break", 140, "advanced");
            Meta("continue", 145, "advanced");
            Meta("try", 150, "advanced");
            Meta("create_var", 160);
            Meta("set_var", 170);
            Meta("get_var", 180);
            Meta("compare", 190);
            Meta("logic_op", 195);
            Meta("math", 200, "advanced");
            Meta("concat", 210, "advanced");
            Meta("for_each", 220, "advanced");
            Meta("call_func", 300, "advanced");
            Meta("return", 310, "advanced");
            Meta("log", 400);
            Meta("comment", 410);

            // 紧凑化：整体缩小一档，键鼠积木再窄一些
            foreach (BlockInfo bi in Map.Values)
            {
                if (bi.Type == "mouse_click" || bi.Type == "key_type" || bi.Type == "find_image") bi.Width = 250;
                else if (bi.Width > 300) bi.Width = 300;
            }
        }

        private static void Meta(string type, int sort, string level)
        {
            BlockInfo b = Get(type);
            if (b == null) return;
            b.Sort = sort;
            b.Level = level;
        }

        private static void Meta(string type, int sort)
        {
            Meta(type, sort, "basic");
        }

        /// <summary>积木库分组顺序（v2.0：键鼠动作置顶；v2.5：识图并入鼠标点击，原「图像识别」改为「识别」）。</summary>
        public static readonly string[] GroupOrder = new string[]
        {
            "键鼠动作", "识别", "流程控制", "变量与条件", "函数", "其他"
        };

        public static string GroupOf(string type)
        {
            switch (type)
            {
                case "mouse_click":
                case "mouse_move":
                case "mouse_wheel":
                case "key_type":
                case "key_press":
                case "sleep":
                    return "键鼠动作";
                case "find_image":
                case "find_color":
                    return "识别";
                case "if":
                case "while":
                case "for_count":
                case "for_each":
                case "break":
                case "continue":
                case "try":
                    return "流程控制";
                case "create_var":
                case "set_var":
                case "get_var":
                case "compare":
                case "logic_op":
                case "math":
                case "concat":
                    return "变量与条件";
                case "call_func":
                case "return":
                    return "函数";
                default:
                    return "其他";
            }
        }

        /// <summary>该积木在简洁模式下是否可见。</summary>
        public static bool VisibleInMode(BlockInfo info, bool simpleMode)
        {
            if (info == null) return false;
            if (!simpleMode) return true;
            return string.Equals(info.Level, "basic", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>获取积木的实际输入端口（含 if 的 elif 条件、call_func 的入参）。</summary>
        public static List<PortSpec> InputPorts(Block b, ScriptDocument doc)
        {
            List<PortSpec> list = new List<PortSpec>();
            BlockInfo info = Get(b.Type);
            if (info == null) return list;
            // 记录坐标模式下点击积木不需要 X/Y 输入引脚，界面更干净
            if (b.Type == "mouse_click" && Json.GetString(b.Params, "coord_mode", "recorded") != "bound")
                return list;
            if (b.Type == "if")
            {
                foreach (PortSpec p in info.Inputs) list.Add(p);
                foreach (BranchSpec br in Branches(b))
                {
                    if (br.ConditionPort != null && br.ConditionPort != "cond")
                    {
                        list.Add(new PortSpec(br.ConditionPort, br.Label, "bool"));
                    }
                }
            }
            else if (b.Type == "call_func")
            {
                string fn = Json.GetString(b.Params, "func", "");
                FunctionDef fd = doc != null ? doc.FindFunction(fn) : null;
                if (fd != null)
                {
                    foreach (VarDef v in fd.Params)
                        list.Add(new PortSpec(v.Name, v.Name, v.Type));
                }
            }
            else
            {
                for (int i = 0; i < info.Inputs.Count; i++)
                {
                    PortSpec p = info.Inputs[i];
                    if (b.Type == "logic_op" && Json.GetString(b.Params, "op", "and") == "not" && p.Name == "b") continue;
                    list.Add(p);
                }
            }
            return list;
        }

        /// <summary>参数选项的中文显示（左键/单击/重试几次…），避免弹窗里出现 left/click/retry。</summary>
        public static string ChoiceLabel(string type, string param, string value)
        {
            switch (type + "." + param)
            {
                case "mouse_click.button": return value == "right" ? "右键" : "左键";
                case "mouse_click.action": return value == "dblclick" ? "双击" : "单击";
                case "mouse_click.on_fail":
                    if (value == "skip") return "跳过这一步";
                    if (value == "error") return "报错停止";
                    return "重试几次";
                case "mouse_click.coord_mode":
                    if (value == "image") return "自动识图";
                    if (value == "bound") return "旧版绑定识图";
                    return "固定坐标";
                case "if.cond_type": return value == "image" ? "有图片" : "用积木判断";
                case "create_var.type":
                    if (value == "str") return "文本";
                    if (value == "bool") return "真假";
                    if (value == "array") return "数组";
                    if (value == "dict") return "字典";
                    return "数字";
                case "create_var.scope": return value == "global" ? "全局" : "局部";
                case "logic_op.op": return value == "or" ? "或" : (value == "not" ? "非" : "且");
                default: return value;
            }
        }

        public static List<PortSpec> OutputPorts(Block b)
        {
            BlockInfo info = Get(b.Type);
            List<PortSpec> list = new List<PortSpec>();
            if (info != null) list.AddRange(info.Outputs);
            return list;
        }

        public static List<BranchSpec> Branches(Block b)
        {
            List<BranchSpec> list = new List<BranchSpec>();
            BlockInfo info = Get(b.Type);
            if (info == null || info.Branches.Count == 0) return list;
            if (b.Type == "if")
            {
                List<object> names = b.Params.ContainsKey("branches") ? b.Params["branches"] as List<object> : null;
                if (names == null || names.Count == 0)
                {
                    list.Add(new BranchSpec("main", "如果", "cond"));
                    list.Add(new BranchSpec("else", "否则", null));
                    return list;
                }
                for (int i = 0; i < names.Count; i++)
                {
                    string n = Convert.ToString(names[i]);
                    string label;
                    if (n == "main") label = "如果";
                    else if (n == "else") label = "否则";
                    else label = "否则如果 " + (i);
                    string port = (n == "main") ? "cond" : (n == "else" ? null : "cond_" + n);
                    list.Add(new BranchSpec(n, label, port));
                }
                return list;
            }
            return new List<BranchSpec>(info.Branches);
        }
    }
}
