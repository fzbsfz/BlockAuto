using System;
using System.Collections.Generic;

namespace BlockAuto
{
    /// <summary>
    /// 键位清单：键位选择器（侧栏 / 参数弹窗）与键名校验共用一份，避免两处各写一套导致对不上。
    /// 真正的“能不能按”由 <see cref="InputSimulator.IsValidKeySpec"/> 用引擎自己的键表判断。
    /// </summary>
    public static class KeyCatalog
    {
        public sealed class KeyItem
        {
            public string Key;
            public string Text;
            public KeyItem(string key, string text) { Key = key; Text = text; }
            public override string ToString() { return Text; }
        }

        /// <summary>修饰键（小写，和引擎解析一致）。</summary>
        public static readonly string[] Modifiers = new string[] { "ctrl", "alt", "shift", "win" };

        public static string ModifierText(string mod)
        {
            switch (mod)
            {
                case "ctrl": return "Ctrl";
                case "alt": return "Alt";
                case "shift": return "Shift";
                case "win": return "Win";
                default: return mod;
            }
        }

        private static List<KeyItem> _all;

        /// <summary>常用键（下拉框用）。</summary>
        public static List<KeyItem> All
        {
            get
            {
                if (_all != null) return _all;
                List<KeyItem> list = new List<KeyItem>();
                list.Add(new KeyItem("enter", "回车 Enter"));
                list.Add(new KeyItem("tab", "Tab"));
                list.Add(new KeyItem("space", "空格 Space"));
                list.Add(new KeyItem("esc", "Esc"));
                list.Add(new KeyItem("backspace", "退格 Backspace"));
                list.Add(new KeyItem("delete", "删除 Delete"));
                list.Add(new KeyItem("insert", "插入 Insert"));
                list.Add(new KeyItem("home", "Home"));
                list.Add(new KeyItem("end", "End"));
                list.Add(new KeyItem("pageup", "PageUp"));
                list.Add(new KeyItem("pagedown", "PageDown"));
                list.Add(new KeyItem("up", "方向键 ↑"));
                list.Add(new KeyItem("down", "方向键 ↓"));
                list.Add(new KeyItem("left", "方向键 ←"));
                list.Add(new KeyItem("right", "方向键 →"));
                for (int i = 1; i <= 12; i++) list.Add(new KeyItem("f" + i, "F" + i));
                for (char c = 'a'; c <= 'z'; c++) list.Add(new KeyItem(c.ToString(), char.ToUpperInvariant(c) + " 键"));
                for (int i = 0; i <= 9; i++) list.Add(new KeyItem(i.ToString(), "数字 " + i));
                list.Add(new KeyItem("minus", "- 减号"));
                list.Add(new KeyItem("plus", "= 等号"));
                list.Add(new KeyItem("comma", ", 逗号"));
                list.Add(new KeyItem("period", ". 句点"));
                list.Add(new KeyItem("slash", "/ 斜杠"));
                list.Add(new KeyItem("semicolon", "; 分号"));
                list.Add(new KeyItem("quote", "' 引号"));
                list.Add(new KeyItem("bracketleft", "[ 左方括号"));
                list.Add(new KeyItem("bracketright", "] 右方括号"));
                list.Add(new KeyItem("backslash", "\\ 反斜杠"));
                list.Add(new KeyItem("grave", "` 反引号"));
                list.Add(new KeyItem("capslock", "CapsLock"));
                list.Add(new KeyItem("printscreen", "PrintScreen"));
                _all = list;
                return _all;
            }
        }

        /// <summary>把 "Ctrl + Alt + F5" 规范成 "ctrl+alt+f5"。</summary>
        public static string Normalize(string spec)
        {
            if (string.IsNullOrEmpty(spec)) return "";
            List<string> parts = new List<string>();
            foreach (string raw in spec.Replace(" ", "").Split('+'))
            {
                string p = raw.Trim().ToLowerInvariant();
                if (p.Length == 0) continue;
                if (p == "control") p = "ctrl";
                if (p == "meta") p = "win";
                if (p == "return") p = "enter";
                if (p == "escape") p = "esc";
                if (p == "del") p = "delete";
                if (!parts.Contains(p)) parts.Add(p);
            }
            return string.Join("+", parts.ToArray());
        }

        /// <summary>拆出修饰键与主键。</summary>
        public static void Split(string spec, out List<string> mods, out string key)
        {
            mods = new List<string>();
            key = "";
            foreach (string raw in Normalize(spec).Split('+'))
            {
                if (raw.Length == 0) continue;
                bool isMod = false;
                foreach (string m in Modifiers) if (m == raw) { mods.Add(raw); isMod = true; break; }
                if (!isMod) key = raw;
            }
        }

        /// <summary>界面展示用，例如 "Ctrl + F5"。</summary>
        public static string Display(string spec)
        {
            List<string> mods;
            string key;
            Split(spec, out mods, out key);
            List<string> parts = new List<string>();
            foreach (string m in mods) parts.Add(ModifierText(m));
            if (key.Length > 0) parts.Add(KeyText(key));
            return string.Join(" + ", parts.ToArray());
        }

        public static string KeyText(string key)
        {
            foreach (KeyItem item in All) if (item.Key == key) return item.Text;
            return key.ToUpperInvariant();
        }

        /// <summary>校验：能被执行引擎认出来才算合法。</summary>
        public static bool IsValid(string spec)
        {
            return InputSimulator.IsValidKeySpec(spec);
        }
    }
}
