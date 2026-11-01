using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BlockAuto
{
    /// <summary>由积木树反向生成只读伪代码（单向预览，不回写）。</summary>
    public static class CodeGen
    {
        public static string Generate(ScriptDocument doc)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("// ===== " + doc.Name + " =====");
            if (!string.IsNullOrEmpty(doc.Description)) sb.AppendLine("// " + doc.Description);
            sb.AppendLine("// 由积木树自动生成，仅供预览，修改不会同步回画布");
            sb.AppendLine();

            if (doc.GlobalVars.Count > 0)
            {
                sb.AppendLine("全局变量：");
                foreach (VarDef v in doc.GlobalVars)
                    sb.AppendLine("  " + v.Name + " : " + v.Type + " = " + Engine.FormatValue(v.Init));
                sb.AppendLine();
            }

            foreach (FunctionDef f in doc.Functions)
            {
                sb.Append("函数 ").Append(f.Name).Append('(');
                for (int i = 0; i < f.Params.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(f.Params[i].Name).Append(": ").Append(f.Params[i].Type);
                }
                sb.Append(')');
                if (f.HasReturn) sb.Append(" 返回 任意");
                sb.AppendLine(" {");
                EmitList(sb, doc, f.Body, 1);
                sb.AppendLine("}");
                sb.AppendLine();
            }

            sb.AppendLine("主流程 {");
            EmitList(sb, doc, doc.Blocks, 1);
            sb.AppendLine("}");

            if (doc.Settings.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("// 脚本设置");
                foreach (KeyValuePair<string, object> kv in doc.Settings)
                    sb.AppendLine("//   " + kv.Key + " = " + Engine.FormatValue(kv.Value));
            }
            return sb.ToString();
        }

        private static void EmitList(StringBuilder sb, ScriptDocument doc, List<Block> list, int depth)
        {
            if (list == null) return;
            foreach (Block b in list) EmitBlock(sb, doc, b, depth);
        }

        private static string Indent(int depth)
        {
            return new string(' ', depth * 2);
        }

        private static void EmitBlock(StringBuilder sb, ScriptDocument doc, Block b, int depth)
        {
            string ind = Indent(depth);
            switch (b.Type)
            {
                case "comment":
                    foreach (string line in Text(b.Params, "text").Split('\n'))
                        sb.AppendLine(ind + "// " + line);
                    return;
                case "if":
                    {
                        List<BranchSpec> specs = Catalog.Branches(b);
                        for (int i = 0; i < b.Branches.Count && i < specs.Count; i++)
                        {
                            BranchSpec s = specs[i];
                            if (i == 0) sb.Append(ind).Append("如果 ").Append(Cond(b, s.ConditionPort)).AppendLine(" {");
                            else if (s.ConditionPort == null) sb.AppendLine(ind + "否则 {");
                            else sb.Append(ind).Append("否则如果 ").Append(Cond(b, s.ConditionPort)).AppendLine(" {");
                            EmitList(sb, doc, b.Branches[i].Blocks, depth + 1);
                            sb.AppendLine(ind + "}");
                        }
                        return;
                    }
                case "while":
                    sb.Append(ind).Append("当 ").Append(Cond(b, "cond")).AppendLine(" 时循环 {");
                    EmitList(sb, doc, b.Branches.Count > 0 ? b.Branches[0].Blocks : null, depth + 1);
                    sb.AppendLine(ind + "}");
                    return;
                case "for_count":
                    sb.Append(ind).Append("循环 ").Append(InputExpr(b, "count", "times")).AppendLine(" 次 {");
                    EmitList(sb, doc, b.Branches.Count > 0 ? b.Branches[0].Blocks : null, depth + 1);
                    sb.AppendLine(ind + "}");
                    return;
                case "for_each":
                    sb.Append(ind).Append("遍历 ").Append(InputExpr(b, "list", null)).Append(" 记作 ")
                      .Append(Text(b.Params, "item_var")).AppendLine(" {");
                    EmitList(sb, doc, b.Branches.Count > 0 ? b.Branches[0].Blocks : null, depth + 1);
                    sb.AppendLine(ind + "}");
                    return;
                case "try":
                    sb.AppendLine(ind + "尝试 {");
                    EmitList(sb, doc, b.Branches.Count > 0 ? b.Branches[0].Blocks : null, depth + 1);
                    sb.AppendLine(ind + "} 捕获异常 {");
                    EmitList(sb, doc, b.Branches.Count > 1 ? b.Branches[1].Blocks : null, depth + 1);
                    sb.AppendLine(ind + "}");
                    return;
                case "break":
                    sb.AppendLine(ind + "跳出循环");
                    return;
                case "continue":
                    sb.AppendLine(ind + "跳过本次循环");
                    return;
                case "sleep":
                    sb.Append(ind).Append("等待 ").Append(Text(b.Params, "seconds")).AppendLine(" 秒");
                    return;
                case "create_var":
                    sb.Append(ind).Append("创建 ").Append(Text(b.Params, "scope")).Append(" 变量 ")
                      .Append(Text(b.Params, "name")).Append(" : ").Append(Text(b.Params, "type"));
                    if (b.Params.ContainsKey("init") && Text(b.Params, "init").Length > 0)
                        sb.Append(" = ").Append(Text(b.Params, "init"));
                    sb.AppendLine();
                    return;
                case "set_var":
                    sb.Append(ind).Append(Text(b.Params, "name")).Append(" = ").Append(InputExpr(b, "value", "value")).AppendLine();
                    return;
                case "get_var":
                    sb.Append(ind).Append("读取 ").Append(Text(b.Params, "name")).AppendLine();
                    return;
                case "compare":
                    sb.Append(ind).Append("比较 ").Append(InputExpr(b, "a", "a")).Append(' ').Append(Text(b.Params, "op")).Append(' ')
                      .Append(InputExpr(b, "b", "b")).AppendLine();
                    return;
                case "logic_op":
                    sb.Append(ind).Append("逻辑 ").Append(Text(b.Params, "op")).Append(' ')
                      .Append(InputExpr(b, "a", "a")).Append(' ').Append(InputExpr(b, "b", "b")).AppendLine();
                    return;
                case "math":
                    sb.Append(ind).Append(InputExpr(b, "a", "a")).Append(' ').Append(Text(b.Params, "op")).Append(' ')
                      .Append(InputExpr(b, "b", "b")).AppendLine();
                    return;
                case "concat":
                    sb.Append(ind).Append("拼接 ").Append(InputExpr(b, "a", "a")).Append(" + ").Append(InputExpr(b, "b", "b")).AppendLine();
                    return;
                case "find_image":
                    sb.Append(ind).Append("识图(模板=").Append(Text(b.Params, "template"))
                      .Append(", 区域=").Append(Text(b.Params, "region"))
                      .Append(", 阈值=").Append(Text(b.Params, "threshold")).AppendLine(")");
                    return;
                case "find_color":
                    sb.Append(ind).Append("找色(颜色=").Append(Text(b.Params, "rgb")).Append(", 区域=")
                      .Append(Text(b.Params, "region")).AppendLine(")");
                    return;
                case "mouse_click":
                    sb.Append(ind).Append("鼠标").Append(Text(b.Params, "button") == "right" ? "右键" : "左键")
                      .Append(Text(b.Params, "action") == "dblclick" ? "双击" : "单击");
                    if (Text(b.Params, "coord_mode") == "image")
                        sb.Append(" 自动识图 ").Append(Text(b.Params, "template")).Append("（区域=").Append(Text(b.Params, "region")).Append("）");
                    else
                        sb.Append(" (").Append(InputExpr(b, "x", null)).Append(", ").Append(InputExpr(b, "y", null)).Append(")");
                    if (Text(b.Params, "offset") != "0,0") sb.Append(" 偏移 ").Append(Text(b.Params, "offset"));
                    sb.AppendLine();
                    return;
                case "mouse_move":
                    sb.Append(ind).Append("鼠标移动到 (").Append(InputExpr(b, "x", null)).Append(", ").Append(InputExpr(b, "y", null)).AppendLine(")");
                    return;
                case "mouse_wheel":
                    sb.Append(ind).Append("滚轮 ").Append(Text(b.Params, "dir")).Append(' ').Append(Text(b.Params, "amount")).AppendLine(" 格");
                    return;
                case "key_type":
                    sb.Append(ind).Append("输入文本 \"").Append(Escape(Text(b.Params, "text"))).AppendLine("\"");
                    return;
                case "key_press":
                    sb.Append(ind).Append("按键 ").Append(Text(b.Params, "key")).AppendLine();
                    return;
                case "log":
                    sb.Append(ind).Append("日志 ").Append(Text(b.Params, "level")).Append(" \"").Append(Escape(Text(b.Params, "text"))).AppendLine("\"");
                    return;
                case "call_func":
                    sb.Append(ind).Append("调用 ").Append(Text(b.Params, "func")).Append('(');
                    FunctionDef f = doc.FindFunction(Text(b.Params, "func"));
                    if (f != null)
                    {
                        for (int i = 0; i < f.Params.Count; i++)
                        {
                            if (i > 0) sb.Append(", ");
                            sb.Append(f.Params[i].Name).Append("=").Append(InputExpr(b, f.Params[i].Name, null));
                        }
                    }
                    sb.AppendLine(")");
                    return;
                case "return":
                    sb.Append(ind).Append("返回 ").Append(InputExpr(b, "value", "value")).AppendLine();
                    return;
                default:
                    sb.Append(ind).Append("[").Append(b.Type).AppendLine("]");
                    return;
            }
        }

        private static string Text(Dictionary<string, object> p, string key)
        {
            object v;
            if (p != null && p.TryGetValue(key, out v)) return Engine.FormatValue(v);
            return "";
        }

        private static string Cond(Block b, string port)
        {
            if (port == null) return "true";
            // 「有图片」判断的条件不在端口上：写成模板匹配，避免代码视图里出现“如果 null”
            object ct;
            if (b.Type == "if" && port == "cond" && b.Params.TryGetValue("cond_type", out ct) &&
                string.Equals(Convert.ToString(ct), "image", StringComparison.OrdinalIgnoreCase))
            {
                string tpl = Text(b.Params, "template");
                return "有图片 " + (tpl.Length > 0 ? tpl : "（未选模板）");
            }
            return InputExpr(b, port, port);
        }

        private static string InputExpr(Block b, string port, string literalKey)
        {
            PinLink link = b.Input(port);
            if (link != null && link.HasSource) return link.From;
            if (literalKey != null && b.Params.ContainsKey(literalKey))
            {
                string s = Engine.FormatValue(b.Params[literalKey]);
                if (s.Length == 0) s = "null";
                return s;
            }
            return "null";
        }

        private static string Escape(string s)
        {
            return s == null ? "" : s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "\\n");
        }
    }
}
