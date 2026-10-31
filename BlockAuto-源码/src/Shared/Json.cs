using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BlockAuto
{
    /// <summary>
    /// 轻量 JSON 读写器（无第三方依赖）。
    /// 对象 -> Dictionary&lt;string,object&gt;，数组 -> List&lt;object&gt;，数字 -> double，布尔 -> bool。
    /// </summary>
    public static class Json
    {
        public static object Parse(string text)
        {
            if (text == null) throw new JsonException("JSON 内容为空");
            Parser p = new Parser(text);
            object v = p.ParseValue();
            p.SkipWs();
            if (!p.Eof) throw new JsonException("JSON 尾部存在多余内容（位置 " + p.Position + "）");
            return v;
        }

        public static string Stringify(object value)
        {
            return Stringify(value, true);
        }

        public static string Stringify(object value, bool indented)
        {
            StringBuilder sb = new StringBuilder();
            Writer w = new Writer(sb, indented);
            w.WriteValue(value, 0);
            return sb.ToString();
        }

        public static Dictionary<string, object> AsObject(object o)
        {
            Dictionary<string, object> d = o as Dictionary<string, object>;
            if (d == null) throw new JsonException("期望 JSON 对象");
            return d;
        }

        public static List<object> AsArray(object o)
        {
            List<object> a = o as List<object>;
            if (a == null) throw new JsonException("期望 JSON 数组");
            return a;
        }

        public static string GetString(Dictionary<string, object> o, string key, string def)
        {
            object v;
            if (o != null && o.TryGetValue(key, out v) && v != null) return Convert.ToString(v, CultureInfo.InvariantCulture);
            return def;
        }

        public static double GetNumber(Dictionary<string, object> o, string key, double def)
        {
            object v;
            if (o != null && o.TryGetValue(key, out v) && v != null) return ToNumber(v, def);
            return def;
        }

        public static bool GetBool(Dictionary<string, object> o, string key, bool def)
        {
            object v;
            if (o != null && o.TryGetValue(key, out v) && v != null) return ToBool(v, def);
            return def;
        }

        public static List<object> ArrayAt(Dictionary<string, object> o, string key)
        {
            object v;
            if (o == null || !o.TryGetValue(key, out v)) return null;
            return v as List<object>;
        }

        public static Dictionary<string, object> ObjectAt(Dictionary<string, object> o, string key)
        {
            object v;
            if (o == null || !o.TryGetValue(key, out v)) return null;
            return v as Dictionary<string, object>;
        }

        public static double ToNumber(object v, double def)
        {
            if (v == null) return def;
            if (v is double) return (double)v;
            if (v is int) return (int)v;
            if (v is long) return (long)v;
            if (v is bool) return ((bool)v) ? 1 : 0;
            double d;
            if (double.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
            return def;
        }

        public static bool ToBool(object v, bool def)
        {
            if (v == null) return def;
            if (v is bool) return (bool)v;
            if (v is double) return Math.Abs((double)v) > double.Epsilon;
            string s = Convert.ToString(v, CultureInfo.InvariantCulture);
            if (string.IsNullOrEmpty(s)) return false;
            if (s.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
            if (s.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
            double d;
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d != 0;
            return true;
        }

        private sealed class Parser
        {
            private readonly string _s;
            private int _i;
            public Parser(string s) { _s = s; _i = 0; }
            public bool Eof { get { return _i >= _s.Length; } }
            public int Position { get { return _i; } }

            public void SkipWs()
            {
                while (_i < _s.Length)
                {
                    char c = _s[_i];
                    if (c == ' ' || c == '\t' || c == '\r' || c == '\n') { _i++; continue; }
                    if (c == '\uFEFF') { _i++; continue; }
                    break;
                }
            }

            public object ParseValue()
            {
                SkipWs();
                if (Eof) throw new JsonException("JSON 意外结束");
                char c = _s[_i];
                if (c == '{') return ParseObject();
                if (c == '[') return ParseArray();
                if (c == '"') return ParseString();
                if (c == 't') { Expect("true"); return true; }
                if (c == 'f') { Expect("false"); return false; }
                if (c == 'n') { Expect("null"); return null; }
                return ParseNumber();
            }

            private void Expect(string lit)
            {
                if (_i + lit.Length > _s.Length || string.CompareOrdinal(_s, _i, lit, 0, lit.Length) != 0)
                    throw new JsonException("非法字面量（位置 " + _i + "）");
                _i += lit.Length;
            }

            private Dictionary<string, object> ParseObject()
            {
                Dictionary<string, object> d = new Dictionary<string, object>();
                _i++; // {
                SkipWs();
                if (!Eof && _s[_i] == '}') { _i++; return d; }
                while (true)
                {
                    SkipWs();
                    if (Eof || _s[_i] != '"') throw new JsonException("对象键必须是字符串（位置 " + _i + "）");
                    string key = ParseString();
                    SkipWs();
                    if (Eof || _s[_i] != ':') throw new JsonException("缺少冒号（位置 " + _i + "）");
                    _i++;
                    object val = ParseValue();
                    d[key] = val;
                    SkipWs();
                    if (Eof) throw new JsonException("对象未闭合");
                    if (_s[_i] == ',') { _i++; continue; }
                    if (_s[_i] == '}') { _i++; return d; }
                    throw new JsonException("对象分隔符错误（位置 " + _i + "）");
                }
            }

            private List<object> ParseArray()
            {
                List<object> a = new List<object>();
                _i++; // [
                SkipWs();
                if (!Eof && _s[_i] == ']') { _i++; return a; }
                while (true)
                {
                    a.Add(ParseValue());
                    SkipWs();
                    if (Eof) throw new JsonException("数组未闭合");
                    if (_s[_i] == ',') { _i++; continue; }
                    if (_s[_i] == ']') { _i++; return a; }
                    throw new JsonException("数组分隔符错误（位置 " + _i + "）");
                }
            }

            private string ParseString()
            {
                _i++; // "
                StringBuilder sb = new StringBuilder();
                while (true)
                {
                    if (Eof) throw new JsonException("字符串未闭合");
                    char c = _s[_i++];
                    if (c == '"') return sb.ToString();
                    if (c == '\\')
                    {
                        if (Eof) throw new JsonException("转义未完成");
                        char e = _s[_i++];
                        switch (e)
                        {
                            case '"': sb.Append('"'); break;
                            case '\\': sb.Append('\\'); break;
                            case '/': sb.Append('/'); break;
                            case 'b': sb.Append('\b'); break;
                            case 'f': sb.Append('\f'); break;
                            case 'n': sb.Append('\n'); break;
                            case 'r': sb.Append('\r'); break;
                            case 't': sb.Append('\t'); break;
                            case 'u':
                                if (_i + 4 > _s.Length) throw new JsonException("\\u 转义长度不足");
                                sb.Append((char)int.Parse(_s.Substring(_i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                                _i += 4;
                                break;
                            default: throw new JsonException("未知转义 \\" + e);
                        }
                        continue;
                    }
                    sb.Append(c);
                }
            }

            private double ParseNumber()
            {
                int start = _i;
                if (!Eof && (_s[_i] == '-' || _s[_i] == '+')) _i++;
                while (!Eof)
                {
                    char c = _s[_i];
                    if ((c >= '0' && c <= '9') || c == '.' || c == 'e' || c == 'E' || c == '+' || c == '-') { _i++; continue; }
                    break;
                }
                if (_i == start) throw new JsonException("非法数值（位置 " + start + "）");
                string num = _s.Substring(start, _i - start);
                double d;
                if (!double.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                    throw new JsonException("非法数值：" + num);
                return d;
            }
        }

        private sealed class Writer
        {
            private readonly StringBuilder _sb;
            private readonly bool _indent;
            public Writer(StringBuilder sb, bool indented) { _sb = sb; _indent = indented; }

            private void NewLine(int depth)
            {
                if (!_indent) return;
                _sb.Append('\n');
                _sb.Append(' ', depth * 2);
            }

            public void WriteValue(object v, int depth)
            {
                if (v == null) { _sb.Append("null"); return; }
                if (v is string) { WriteString((string)v); return; }
                if (v is bool) { _sb.Append(((bool)v) ? "true" : "false"); return; }
                if (v is double || v is float || v is int || v is long || v is short || v is decimal)
                {
                    double d = Convert.ToDouble(v, CultureInfo.InvariantCulture);
                    if (double.IsNaN(d) || double.IsInfinity(d)) { _sb.Append("0"); return; }
                    if (Math.Abs(d - Math.Round(d)) < 1e-9 && Math.Abs(d) < 1e15)
                        _sb.Append(((long)Math.Round(d)).ToString(CultureInfo.InvariantCulture));
                    else
                        _sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
                    return;
                }
                Dictionary<string, object> obj = v as Dictionary<string, object>;
                if (obj != null)
                {
                    _sb.Append('{');
                    bool first = true;
                    foreach (KeyValuePair<string, object> kv in obj)
                    {
                        if (!first) _sb.Append(',');
                        first = false;
                        NewLine(depth + 1);
                        WriteString(kv.Key);
                        _sb.Append(_indent ? ": " : ":");
                        WriteValue(kv.Value, depth + 1);
                    }
                    if (!first) NewLine(depth);
                    _sb.Append('}');
                    return;
                }
                System.Collections.IEnumerable en = v as System.Collections.IEnumerable;
                if (en != null)
                {
                    _sb.Append('[');
                    bool first = true;
                    foreach (object item in en)
                    {
                        if (!first) _sb.Append(',');
                        first = false;
                        NewLine(depth + 1);
                        WriteValue(item, depth + 1);
                    }
                    if (!first) NewLine(depth);
                    _sb.Append(']');
                    return;
                }
                WriteString(Convert.ToString(v, CultureInfo.InvariantCulture));
            }

            private void WriteString(string s)
            {
                _sb.Append('"');
                foreach (char c in s)
                {
                    switch (c)
                    {
                        case '"': _sb.Append("\\\""); break;
                        case '\\': _sb.Append("\\\\"); break;
                        case '\n': _sb.Append("\\n"); break;
                        case '\r': _sb.Append("\\r"); break;
                        case '\t': _sb.Append("\\t"); break;
                        case '\b': _sb.Append("\\b"); break;
                        case '\f': _sb.Append("\\f"); break;
                        default:
                            if (c < 32) _sb.Append("\\u").Append(((int)c).ToString("x4"));
                            else _sb.Append(c);
                            break;
                    }
                }
                _sb.Append('"');
            }
        }
    }

    public class JsonException : Exception
    {
        public JsonException(string msg) : base(msg) { }
    }
}
