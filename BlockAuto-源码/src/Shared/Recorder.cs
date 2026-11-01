using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace BlockAuto
{
    public enum RecordedKind { MouseClick, MouseDoubleClick, MouseRightClick, MouseWheel, MouseMove, KeyText, KeyPress }

    public sealed class RecordedAction
    {
        public RecordedKind Kind;
        public int X;
        public int Y;
        public int WheelDelta;
        public string Text = "";
        public string Key = "";

        public string Describe()
        {
            switch (Kind)
            {
                case RecordedKind.MouseClick: return "左键单击 (" + X + "," + Y + ")";
                case RecordedKind.MouseDoubleClick: return "左键双击 (" + X + "," + Y + ")";
                case RecordedKind.MouseRightClick: return "右键单击 (" + X + "," + Y + ")";
                case RecordedKind.MouseWheel: return "滚轮 " + (WheelDelta > 0 ? "上" : "下") + " (" + X + "," + Y + ")";
                case RecordedKind.MouseMove: return "移动到 (" + X + "," + Y + ")";
                case RecordedKind.KeyText: return "[键盘] 输入文本 \"" + Text + "\"";
                case RecordedKind.KeyPress: return "[键盘] 按键 " + Key;
                default: return Kind.ToString();
            }
        }
    }

    /// <summary>
    /// 操作录制：全局低级键鼠钩子，忠实记录时序，不识别画面、不加判断循环。
    /// 录制结果转换成与手动拖拽完全一致的键鼠积木序列。
    /// </summary>
    public sealed class InputRecorder : IDisposable
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WH_MOUSE_LL = 14;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WM_RBUTTONUP = 0x0205;
        private const int WM_MOUSEWHEEL = 0x020A;
        private const int WM_MOUSEMOVE = 0x0200;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll")]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        private static extern bool GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        private static extern int GetKeyNameText(int lParam, StringBuilder lpString, int nSize);

        [DllImport("user32.dll")]
        private static extern uint MapVirtualKey(uint uCode, uint uMapType);

        private readonly object _sync = new object();
        private readonly List<RecordedAction> _actions = new List<RecordedAction>();
        private readonly HookProc _keyboardProc;
        private readonly HookProc _mouseProc;
        private IntPtr _keyboardHook = IntPtr.Zero;
        private IntPtr _mouseHook = IntPtr.Zero;

        private bool _recording;
        private bool _paused;
        private bool _dropNextKeyText;

        private int _pendingMoveX = -1;
        private int _pendingMoveY = -1;
        private readonly Timer _moveTimer;
        private DateTime _lastClickTime = DateTime.MinValue;
        private int _lastClickX;
        private int _lastClickY;
        private bool _injectedMouse;
        private bool _injectedKey;
        private DateTime _lastInjection = DateTime.MinValue;

        private readonly StringBuilder _textBuffer = new StringBuilder();
        private readonly Timer _textFlushTimer;
        private DateTime _lastKeyText = DateTime.MinValue;

        public event EventHandler Changed;

        public InputRecorder()
        {
            _keyboardProc = KeyboardHook;
            _mouseProc = MouseHook;
            _moveTimer = new Timer();
            _moveTimer.Interval = 260;
            _moveTimer.Tick += delegate
            {
                _moveTimer.Stop();
                FlushMove();
            };
            _textFlushTimer = new Timer();
            _textFlushTimer.Interval = 400;
            _textFlushTimer.Tick += delegate
            {
                _textFlushTimer.Stop();
                FlushText();
            };
        }

        public bool IsRecording { get { return _recording; } }
        public bool IsPaused { get { return _paused; } }

        public bool AddDelayAfterAction
        {
            get { return _addDelay; }
            set { _addDelay = value; }
        }
        private bool _addDelay;

        public int DelayMs { get; set; }

        public List<RecordedAction> Actions
        {
            get { lock (_sync) return new List<RecordedAction>(_actions); }
        }

        public int Count
        {
            get { lock (_sync) return _actions.Count; }
        }

        /// <summary>键盘类动作（输入文本 / 按键）的条数：录制界面用来告诉用户“键盘也录进去了”。</summary>
        public int KeyActionCount
        {
            get
            {
                lock (_sync)
                {
                    int n = 0;
                    foreach (RecordedAction a in _actions)
                        if (a.Kind == RecordedKind.KeyText || a.Kind == RecordedKind.KeyPress) n++;
                    return n;
                }
            }
        }

        /// <summary>删除最后一条录制步骤（录错了可回退）。</summary>
        public void RemoveLast()
        {
            lock (_sync)
            {
                if (_actions.Count == 0) return;
                _actions.RemoveAt(_actions.Count - 1);
            }
            FireChanged();
        }

        public void Clear()
        {
            lock (_sync) _actions.Clear();
            FireChanged();
        }

        public void Start()
        {
            if (_recording) return;
            lock (_sync) _actions.Clear();
            _pendingMoveX = -1;
            _pendingMoveY = -1;
            _textBuffer.Length = 0;
            _recording = true;
            _paused = false;
            _keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, GetModuleHandle(null), 0);
            _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, GetModuleHandle(null), 0);
            if (_keyboardHook == IntPtr.Zero || _mouseHook == IntPtr.Zero)
            {
                if (_keyboardHook != IntPtr.Zero) UnhookWindowsHookEx(_keyboardHook);
                if (_mouseHook != IntPtr.Zero) UnhookWindowsHookEx(_mouseHook);
                _keyboardHook = IntPtr.Zero;
                _mouseHook = IntPtr.Zero;
                _recording = false;
                throw new InvalidOperationException("安装全局键鼠钩子失败，请尝试以管理员身份运行");
            }
            FireChanged();
        }

        public void Pause()
        {
            if (!_recording) return;
            FlushText();
            FlushMove();
            _paused = true;
            FireChanged();
        }

        public void Resume()
        {
            if (!_recording) return;
            _paused = false;
            FireChanged();
        }

        public void Stop()
        {
            if (!_recording) return;
            FlushText();
            FlushMove();
            _moveTimer.Stop();
            _textFlushTimer.Stop();
            _recording = false;
            _paused = false;
            if (_keyboardHook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_keyboardHook);
                _keyboardHook = IntPtr.Zero;
            }
            if (_mouseHook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_mouseHook);
                _mouseHook = IntPtr.Zero;
            }
            FireChanged();
        }

        /// <summary>把录制结果转成积木序列（不复用文档，直接追加到目标列表）。</summary>
        public List<Block> BuildBlocks(ScriptDocument doc, bool withDelay, out int skipped)
        {
            skipped = 0;
            List<Block> result = new List<Block>();
            List<RecordedAction> actions = Actions;
            foreach (RecordedAction a in actions)
            {
                Block b = null;
                switch (a.Kind)
                {
                    case RecordedKind.MouseClick:
                    case RecordedKind.MouseDoubleClick:
                    case RecordedKind.MouseRightClick:
                        b = doc.NewBlock("mouse_click");
                        b.Params["x"] = (double)a.X;
                        b.Params["y"] = (double)a.Y;
                        b.Params["button"] = a.Kind == RecordedKind.MouseRightClick ? "right" : "left";
                        b.Params["action"] = a.Kind == RecordedKind.MouseDoubleClick ? "dblclick" : "click";
                        break;
                    case RecordedKind.MouseMove:
                        b = doc.NewBlock("mouse_move");
                        b.Params["x"] = (double)a.X;
                        b.Params["y"] = (double)a.Y;
                        break;
                    case RecordedKind.MouseWheel:
                        b = doc.NewBlock("mouse_wheel");
                        b.Params["dir"] = a.WheelDelta > 0 ? "up" : "down";
                        b.Params["amount"] = (double)Math.Max(1, Math.Abs(a.WheelDelta) / 120);
                        break;
                    case RecordedKind.KeyText:
                        b = doc.NewBlock("key_type");
                        b.Params["text"] = a.Text;
                        break;
                    case RecordedKind.KeyPress:
                        b = doc.NewBlock("key_press");
                        b.Params["key"] = a.Key;
                        break;
                }
                if (b == null)
                {
                    skipped++;
                    continue;
                }
                b.Comment = "录制";
                result.Add(b);
                if (withDelay && DelayMs > 0 && b.Type != "mouse_move")
                {
                    Block d = doc.NewBlock("sleep");
                    d.Params["seconds"] = Math.Round(DelayMs / 1000.0, 3);
                    d.Comment = "录制延时";
                    result.Add(d);
                }
            }
            return result;
        }

        /// <summary>把录制结果写入编辑器文档（主流程末尾或指定容器）。</summary>
        public int ApplyToDocument(ScriptDocument doc, List<Block> target, bool withDelay)
        {
            int skipped;
            List<Block> blocks = BuildBlocks(doc, withDelay, out skipped);
            foreach (Block b in blocks) target.Add(b);
            doc.EnsureNextLinks();
            return blocks.Count;
        }

        private void FireChanged()
        {
            EventHandler h = Changed;
            if (h != null)
            {
                try { h(this, EventArgs.Empty); } catch { }
            }
        }

        // ---------------------------- 钩子回调 ----------------------------

        private IntPtr KeyboardHook(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode >= 0 && _recording && !_paused)
                {
                    int msg = wParam.ToInt32();
                    if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
                    {
                        KBDLLHOOKSTRUCT k = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT));
                        if (_injectedKey) { _injectedKey = false; }
                        else HandleKeyDown(k);
                    }
                }
            }
            catch { }
            return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
        }

        private IntPtr MouseHook(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode >= 0 && _recording && !_paused)
                {
                    int msg = wParam.ToInt32();
                    MSLLHOOKSTRUCT m = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
                    if (msg == WM_LBUTTONDOWN || msg == WM_RBUTTONDOWN)
                    {
                        bool right = msg == WM_RBUTTONDOWN;
                        HandleClick(m.pt.x, m.pt.y, right);
                    }
                    else if (msg == WM_MOUSEWHEEL)
                    {
                        int delta = (short)((m.mouseData >> 16) & 0xFFFF);
                        HandleWheel(m.pt.x, m.pt.y, delta);
                    }
                    else if (msg == WM_MOUSEMOVE)
                    {
                        HandleMove(m.pt.x, m.pt.y);
                    }
                }
            }
            catch { }
            return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
        }

        private void HandleClick(int x, int y, bool right)
        {
            FlushMove();
            _moveTimer.Stop();

            DateTime now = DateTime.Now;
            lock (_sync)
            {
                if (!right && _actions.Count > 0
                    && (now - _lastClickTime).TotalMilliseconds < 400
                    && Math.Abs(x - _lastClickX) <= 4 && Math.Abs(y - _lastClickY) <= 4)
                {
                    RecordedAction prev = _actions[_actions.Count - 1];
                    if (prev.Kind == RecordedKind.MouseClick)
                    {
                        prev.Kind = RecordedKind.MouseDoubleClick;
                        _lastClickTime = now;
                        FireChanged();
                        return;
                    }
                }
                RecordedAction a = new RecordedAction();
                a.Kind = right ? RecordedKind.MouseRightClick : RecordedKind.MouseClick;
                a.X = x;
                a.Y = y;
                _actions.Add(a);
                _lastClickTime = now;
                _lastClickX = x;
                _lastClickY = y;
            }
            FireChanged();
        }

        private void HandleWheel(int x, int y, int delta)
        {
            FlushMove();
            lock (_sync)
            {
                RecordedAction a = new RecordedAction();
                a.Kind = RecordedKind.MouseWheel;
                a.X = x;
                a.Y = y;
                a.WheelDelta = delta;
                _actions.Add(a);
            }
            FireChanged();
        }

        private void HandleMove(int x, int y)
        {
            if (_injectedMouse) { _injectedMouse = false; return; }
            _pendingMoveX = x;
            _pendingMoveY = y;
            if (!_moveTimer.Enabled) _moveTimer.Start();
        }

        private void FlushMove()
        {
            if (_pendingMoveX < 0) return;
            int x = _pendingMoveX;
            int y = _pendingMoveY;
            _pendingMoveX = -1;
            _pendingMoveY = -1;
            lock (_sync)
            {
                if (_actions.Count > 0)
                {
                    RecordedAction last = _actions[_actions.Count - 1];
                    if (last.Kind == RecordedKind.MouseMove && Math.Abs(last.X - x) <= 2 && Math.Abs(last.Y - y) <= 2) return;
                }
                RecordedAction a = new RecordedAction();
                a.Kind = RecordedKind.MouseMove;
                a.X = x;
                a.Y = y;
                _actions.Add(a);
            }
            FireChanged();
        }

        private void HandleKeyDown(KBDLLHOOKSTRUCT k)
        {
            int vk = (int)k.vkCode;
            bool ctrl = (Win32.GetAsyncKeyState(0x11) & 0x8000) != 0;
            bool alt = (Win32.GetAsyncKeyState(0x12) & 0x8000) != 0;
            bool shift = (Win32.GetAsyncKeyState(0x10) & 0x8000) != 0;
            bool win = (Win32.GetAsyncKeyState(0x5B) & 0x8000) != 0;

            if (vk == 0x11 || vk == 0x12 || vk == 0x10 || vk == 0x5B || vk == 0x5C) return; // 修饰键单独按下不记录

            if (ctrl || alt || win)
            {
                FlushText();
                FlushMove();
                string combo = "";
                if (ctrl) combo += "ctrl+";
                if (alt) combo += "alt+";
                if (shift) combo += "shift+";
                if (win) combo += "win+";
                string keyName = KeyName(vk);
                if (keyName != null)
                {
                    lock (_sync)
                    {
                        RecordedAction a = new RecordedAction();
                        a.Kind = RecordedKind.KeyPress;
                        a.Key = combo + keyName;
                        _actions.Add(a);
                    }
                    FireChanged();
                }
                return;
            }

            string text = KeyText(k, shift);
            if (text == null)
            {
                if (IsSpecialKey(vk))
                {
                    FlushText();
                    string name = KeyName(vk);
                    if (name != null)
                    {
                        lock (_sync)
                        {
                            RecordedAction a = new RecordedAction();
                            a.Kind = RecordedKind.KeyPress;
                            a.Key = name;
                            _actions.Add(a);
                        }
                        FireChanged();
                    }
                }
                return;
            }

            lock (_sync) _textBuffer.Append(text);
            _lastKeyText = DateTime.Now;
            _textFlushTimer.Stop();
            _textFlushTimer.Start();
            FireChanged();
        }

        private void FlushText()
        {
            string text;
            lock (_sync)
            {
                if (_textBuffer.Length == 0) return;
                text = _textBuffer.ToString();
                _textBuffer.Length = 0;
                RecordedAction a = new RecordedAction();
                a.Kind = RecordedKind.KeyText;
                a.Text = text;
                _actions.Add(a);
            }
            _textFlushTimer.Stop();
            FireChanged();
        }

        private static bool IsSpecialKey(int vk)
        {
            if (vk >= 0x70 && vk <= 0x7B) return true; // F1-F12
            switch (vk)
            {
                case 0x0D: case 0x1B: case 0x09: case 0x08: case 0x2E: case 0x2D:
                case 0x24: case 0x23: case 0x21: case 0x22: case 0x25: case 0x26:
                case 0x27: case 0x28: case 0x14: case 0x2C: case 0x13:
                    return true;
                default:
                    return false;
            }
        }

        private static string KeyText(KBDLLHOOKSTRUCT k, bool shift)
        {
            int vk = (int)k.vkCode;
            if (vk >= 0x30 && vk <= 0x39)
            {
                char c = (char)vk;
                if (shift)
                {
                    string[] shifted = { ")", "!", "@", "#", "$", "%", "^", "&", "*", "(" };
                    return shifted[vk - 0x30];
                }
                return c.ToString();
            }
            if (vk >= 0x60 && vk <= 0x69) return ((char)('0' + (vk - 0x60))).ToString();
            if (vk >= 0x41 && vk <= 0x5A)
            {
                bool caps = (Win32.GetAsyncKeyState(0x14) & 1) != 0;
                bool upper = shift ^ caps;
                return upper ? ((char)vk).ToString() : ((char)vk).ToString().ToLowerInvariant();
            }
            switch (vk)
            {
                case 0x20: return " ";
                case 0xBE: return shift ? ">" : ".";
                case 0xBC: return shift ? "<" : ",";
                case 0xBD: return shift ? "_" : "-";
                case 0xBB: return shift ? "+" : "=";
                case 0xBA: return shift ? ":" : ";";
                case 0xDE: return shift ? "\"" : "'";
                case 0xBF: return shift ? "?" : "/";
                case 0xDC: return shift ? "|" : "\\";
                case 0xC0: return shift ? "~" : "`";
                case 0xDB: return shift ? "{" : "[";
                case 0xDD: return shift ? "}" : "]";
                default: return null;
            }
        }

        private static string KeyName(int vk)
        {
            if (vk >= 0x70 && vk <= 0x7B) return "f" + (vk - 0x70 + 1);
            if (vk >= 0x41 && vk <= 0x5A) return ((char)vk).ToString().ToLowerInvariant();
            if (vk >= 0x30 && vk <= 0x39) return ((char)vk).ToString();
            switch (vk)
            {
                case 0x0D: return "enter";
                case 0x09: return "tab";
                case 0x1B: return "esc";
                case 0x08: return "backspace";
                case 0x2E: return "delete";
                case 0x2D: return "insert";
                case 0x24: return "home";
                case 0x23: return "end";
                case 0x21: return "pageup";
                case 0x22: return "pagedown";
                case 0x25: return "left";
                case 0x26: return "up";
                case 0x27: return "right";
                case 0x28: return "down";
                case 0x20: return "space";
                case 0x14: return "capslock";
                case 0x2C: return "printscreen";
                case 0x13: return "pause";
                case 0xBE: return "period";
                case 0xBC: return "comma";
                case 0xBD: return "minus";
                case 0xBB: return "plus";
                case 0xBA: return "semicolon";
                case 0xDE: return "quote";
                case 0xBF: return "slash";
                case 0xDC: return "backslash";
                case 0xC0: return "grave";
                case 0xDB: return "bracketleft";
                case 0xDD: return "bracketright";
                default: return null;
            }
        }

        public void Dispose()
        {
            Stop();
            _moveTimer.Dispose();
            _textFlushTimer.Dispose();
        }
    }
}