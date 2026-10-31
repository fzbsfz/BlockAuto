using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace BlockAuto
{
    public sealed class HotkeyBinding
    {
        public string Id = Guid.NewGuid().ToString("N").Substring(0, 8);
        public string Keys = "Ctrl+Alt+F1";
        public string ScriptPath = "";
        public bool Enabled = true;
        public string LastError = "";

        public string Display
        {
            get
            {
                string file = string.IsNullOrEmpty(ScriptPath) ? "（未选择脚本）" : Path.GetFileName(ScriptPath);
                return Keys + "  →  " + file;
            }
        }
    }

    public static class HotkeyParser
    {
        public const uint MOD_ALT = 0x0001;
        public const uint MOD_CONTROL = 0x0002;
        public const uint MOD_SHIFT = 0x0004;
        public const uint MOD_WIN = 0x0008;
        public const uint MOD_NOREPEAT = 0x4000;

        private static readonly Dictionary<string, uint> Vk = BuildVk();

        private static Dictionary<string, uint> BuildVk()
        {
            Dictionary<string, uint> d = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < 26; i++) d[((char)('A' + i)).ToString()] = (uint)(0x41 + i);
            for (int i = 0; i <= 9; i++) d[i.ToString()] = (uint)(0x30 + i);
            for (int i = 1; i <= 24; i++) d["F" + i] = (uint)(0x70 + i - 1);
            d["ESC"] = 0x1B; d["ESCAPE"] = 0x1B;
            d["ENTER"] = 0x0D; d["RETURN"] = 0x0D;
            d["SPACE"] = 0x20; d["TAB"] = 0x09;
            d["BACKSPACE"] = 0x08; d["DELETE"] = 0x2E; d["INSERT"] = 0x2D;
            d["HOME"] = 0x24; d["END"] = 0x23;
            d["PAGEUP"] = 0x21; d["PAGEDOWN"] = 0x22;
            d["UP"] = 0x26; d["DOWN"] = 0x28; d["LEFT"] = 0x25; d["RIGHT"] = 0x27;
            d["PRINTSCREEN"] = 0x2C; d["PAUSE"] = 0x13;
            d["NUMLOCK"] = 0x90; d["CAPSLOCK"] = 0x14;
            d["PLUS"] = 0xBB; d["MINUS"] = 0xBD;
            d["COMMA"] = 0xBC; d["PERIOD"] = 0xBE;
            d["SLASH"] = 0xBF; d["SEMICOLON"] = 0xBA;
            d["QUOTE"] = 0xDE; d["BRACKETLEFT"] = 0xDB; d["BRACKETRIGHT"] = 0xDD;
            d["BACKSLASH"] = 0xDC; d["GRAVE"] = 0xC0;
            return d;
        }

        public static bool TryParse(string spec, out uint modifiers, out uint vk, out string error)
        {
            modifiers = 0;
            vk = 0;
            error = "";
            if (string.IsNullOrEmpty(spec)) { error = "快捷键为空"; return false; }
            string[] parts = spec.Replace(" ", "").Split('+');
            foreach (string raw in parts)
            {
                string p = raw.Trim();
                if (p.Length == 0) continue;
                string up = p.ToUpperInvariant();
                if (up == "CTRL" || up == "CONTROL") { modifiers |= MOD_CONTROL; continue; }
                if (up == "ALT") { modifiers |= MOD_ALT; continue; }
                if (up == "SHIFT") { modifiers |= MOD_SHIFT; continue; }
                if (up == "WIN" || up == "META") { modifiers |= MOD_WIN; continue; }
                uint code;
                if (!Vk.TryGetValue(up, out code)) { error = "无法识别的按键：" + p; return false; }
                if (vk != 0) { error = "只能包含一个主按键"; return false; }
                vk = code;
            }
            if (vk == 0) { error = "缺少主按键（如 F1、A、F5）"; return false; }
            return true;
        }
    }

    /// <summary>全局热键管理：使用 RegisterHotKey + 隐藏窗口消息循环。</summary>
    public sealed class HotkeyManager : IDisposable
    {
        private sealed class HotkeyWindow : NativeWindow
        {
            public HotkeyManager Owner;

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == 0x0312 && Owner != null)
                {
                    int id = m.WParam.ToInt32();
                    Owner.OnHotkey(id);
                }
                base.WndProc(ref m);
            }
        }

        private readonly List<HotkeyBinding> _bindings = new List<HotkeyBinding>();
        private readonly HotkeyWindow _window = new HotkeyWindow();
        private readonly bool[] _registered = new bool[64];
        private const int BaseId = 0xA000;

        public event Action<HotkeyBinding> Triggered;

        public HotkeyManager()
        {
            _window.Owner = this;
            _window.CreateHandle(new CreateParams());
            Load();
            RegisterAll();
        }

        public List<HotkeyBinding> Bindings
        {
            get { return new List<HotkeyBinding>(_bindings); }
        }

        public void Load()
        {
            _bindings.Clear();
            try
            {
                if (!File.Exists(AppPaths.HotkeysFile)) return;
                Dictionary<string, object> root = Json.AsObject(Json.Parse(File.ReadAllText(AppPaths.HotkeysFile, Encoding.UTF8)));
                List<object> arr = Json.ArrayAt(root, "hotkeys");
                if (arr == null) return;
                foreach (object it in arr)
                {
                    Dictionary<string, object> o = Json.AsObject(it);
                    HotkeyBinding b = new HotkeyBinding();
                    b.Id = Json.GetString(o, "id", b.Id);
                    b.Keys = Json.GetString(o, "keys", "");
                    b.ScriptPath = Json.GetString(o, "script", "");
                    b.Enabled = Json.GetBool(o, "enabled", true);
                    _bindings.Add(b);
                }
            }
            catch { }
        }

        public void Save()
        {
            List<object> arr = new List<object>();
            foreach (HotkeyBinding b in _bindings)
            {
                Dictionary<string, object> o = new Dictionary<string, object>();
                o["id"] = b.Id;
                o["keys"] = b.Keys;
                o["script"] = b.ScriptPath;
                o["enabled"] = b.Enabled;
                arr.Add(o);
            }
            Dictionary<string, object> root = new Dictionary<string, object>();
            root["hotkeys"] = arr;
            try { File.WriteAllText(AppPaths.HotkeysFile, Json.Stringify(root, true), new UTF8Encoding(false)); }
            catch { }
        }

        public void RegisterAll()
        {
            for (int i = 0; i < _registered.Length; i++)
            {
                if (_registered[i])
                {
                    Win32.UnregisterHotKey(_window.Handle, BaseId + i);
                    _registered[i] = false;
                }
            }
            for (int i = 0; i < _bindings.Count && i < _registered.Length; i++)
            {
                HotkeyBinding b = _bindings[i];
                b.LastError = "";
                if (!b.Enabled) continue;
                uint mods, vk;
                string err;
                if (!HotkeyParser.TryParse(b.Keys, out mods, out vk, out err))
                {
                    b.LastError = err;
                    continue;
                }
                if (!Win32.RegisterHotKey(_window.Handle, BaseId + i, mods | HotkeyParser.MOD_NOREPEAT, vk))
                {
                    b.LastError = "注册失败，快捷键可能已被占用";
                    continue;
                }
                _registered[i] = true;
            }
        }

        public bool Add(HotkeyBinding b, out string error)
        {
            error = "";
            uint mods, vk;
            if (!HotkeyParser.TryParse(b.Keys, out mods, out vk, out error)) return false;
            foreach (HotkeyBinding other in _bindings)
            {
                if (!string.Equals(other.Keys, b.Keys, StringComparison.OrdinalIgnoreCase)) continue;
                error = "快捷键已被其他任务使用：" + other.Keys;
                return false;
            }
            _bindings.Add(b);
            Save();
            RegisterAll();
            if (!string.IsNullOrEmpty(b.LastError)) { error = b.LastError; return false; }
            return true;
        }

        public void ReplaceAt(int index, HotkeyBinding b)
        {
            if (index < 0 || index >= _bindings.Count) return;
            b.Id = _bindings[index].Id;
            _bindings[index] = b;
            Save();
            RegisterAll();
        }

        public void RemoveAt(int index)
        {
            if (index < 0 || index >= _bindings.Count) return;
            _bindings.RemoveAt(index);
            Save();
            RegisterAll();
        }

        public void Refresh()
        {
            Save();
            RegisterAll();
        }

        private void OnHotkey(int id)
        {
            int index = id - BaseId;
            if (index < 0 || index >= _bindings.Count) return;
            HotkeyBinding b = _bindings[index];
            if (!b.Enabled) return;
            Action<HotkeyBinding> h = Triggered;
            if (h != null) { try { h(b); } catch { } }
        }

        public void Dispose()
        {
            for (int i = 0; i < _registered.Length; i++)
                if (_registered[i]) Win32.UnregisterHotKey(_window.Handle, BaseId + i);
            _window.DestroyHandle();
        }
    }
}
