using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace BlockAuto
{
    /// <summary>
    /// 键位选择器：修饰键开关 + 主键下拉（含「自定义」），组合结果形如 ctrl+alt+f5。
    /// 侧栏抽屉与「全部参数…」弹窗共用，避免两处行为不一致。
    /// </summary>
    public sealed class KeyPicker : Panel
    {
        private readonly CheckBox[] _mods = new CheckBox[4];
        private readonly ComboBox _keys;
        private readonly TextBox _custom;
        private readonly Label _preview;
        private readonly FlatButton _capture;
        private bool _loading;
        private bool _capturing;

        /// <summary>正在等用户按键（主窗口据此临时让出快捷键，比如 F5）。</summary>
        public static bool Capturing;
        private static KeyPicker _capturingPicker;

        /// <summary>
        /// 由主窗口 ProcessCmdKey 转发：不管焦点在哪个控件，按键都能被捕获。
        /// 返回 true 表示这次按键已被捕获/取消，调用方不要再处理。
        /// </summary>
        public static bool HandleGlobalKey(Keys keyData)
        {
            KeyPicker p = _capturingPicker;
            if (p == null) return false;
            if (keyData == Keys.Escape) { p.EndCapture(true); return true; }
            Keys code = keyData & Keys.KeyCode;
            if (code == Keys.ControlKey || code == Keys.Menu || code == Keys.ShiftKey ||
                code == Keys.LWin || code == Keys.RWin || code == Keys.LControlKey ||
                code == Keys.RControlKey || code == Keys.LShiftKey || code == Keys.RShiftKey)
                return true;   // 只按了修饰键，等主键

            string name = KeyCatalog.FromKeys(code);
            if (name.Length == 0)
            {
                p._capture.Text = "这个键不支持，换一个…（Esc 取消）";
                return true;
            }
            bool ctrl = (keyData & Keys.Control) == Keys.Control;
            bool alt = (keyData & Keys.Alt) == Keys.Alt;
            bool shift = (keyData & Keys.Shift) == Keys.Shift;
            p.LoadValue(KeyCatalog.BuildSpec(ctrl, alt, shift, false, name));
            p.EndCapture(false);
            EventHandler h = p.ValueChanged;
            if (h != null) h(p, EventArgs.Empty);
            return true;
        }

        public event EventHandler ValueChanged;

        public KeyPicker()
        {
            BackColor = Theme.Panel;
            Height = Theme.Px(96);
            Width = Theme.Px(216);

            _capture = Ui.Button("⌨ 点这里，然后按下要按的键", delegate { BeginCapture(); });
            _capture.Outline = true;
            _capture.ForeColor = Theme.Accent;
            _capture.Width = Theme.Px(216);
            _capture.Height = Theme.Px(30);
            _capture.Location = new Point(0, 0);
            Controls.Add(_capture);

            FlowLayoutPanel modRow = new FlowLayoutPanel();
            modRow.FlowDirection = FlowDirection.LeftToRight;
            modRow.WrapContents = false;
            modRow.AutoSize = true;
            modRow.Location = new Point(0, Theme.Px(34));
            modRow.BackColor = Theme.Panel;
            for (int i = 0; i < KeyCatalog.Modifiers.Length; i++)
            {
                CheckBox cb = new CheckBox();
                cb.Text = KeyCatalog.ModifierText(KeyCatalog.Modifiers[i]);
                cb.ForeColor = Theme.Text;
                cb.AutoSize = true;
                cb.Margin = new Padding(0, 0, Theme.Px(10), 0);
                cb.CheckedChanged += delegate { Compose(); };
                _mods[i] = cb;
                modRow.Controls.Add(cb);
            }
            Controls.Add(modRow);

            _keys = new ComboBox();
            _keys.DropDownStyle = ComboBoxStyle.DropDownList;
            Theme.ApplyDarkCombo(_keys);
            _keys.Width = Theme.Px(216);
            _keys.Location = new Point(0, Theme.Px(64));
            _keys.Margin = new Padding(0);
            foreach (KeyCatalog.KeyItem item in KeyCatalog.All) _keys.Items.Add(item);
            KeyCatalog.KeyItem custom = new KeyCatalog.KeyItem("", "自定义…");
            _keys.Items.Add(custom);
            _keys.SelectedIndexChanged += delegate { Compose(); };
            Controls.Add(_keys);

            _custom = new TextBox();
            Theme.ApplyDarkTextBox(_custom);
            _custom.Width = Theme.Px(216);
            _custom.Location = new Point(0, Theme.Px(92));
            _custom.Visible = false;
            _custom.TextChanged += delegate { Compose(); };
            Controls.Add(_custom);

            _preview = new Label();
            _preview.AutoSize = true;
            _preview.ForeColor = Theme.TextDim;
            _preview.Font = Theme.UiFont(8.5f, FontStyle.Regular);
            _preview.Location = new Point(0, Theme.Px(120));
            Controls.Add(_preview);

            Height = Theme.Px(146);
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
            Click += delegate { BeginCapture(); };
        }

        private void BeginCapture()
        {
            if (_capturing) return;
            _capturing = true;
            Capturing = true;
            _capturingPicker = this;
            _capture.Text = "请按下要按的键…（Esc 取消）";
            _capture.ForeColor = Theme.Danger;
            Focus();
            Select();
            Invalidate();
        }

        private void EndCapture(bool cancelled)
        {
            _capturing = false;
            Capturing = false;
            if (_capturingPicker == this) _capturingPicker = null;
            _capture.Text = cancelled ? "⌨ 点这里，然后按下要按的键" : "⌨ 重新按一次（当前：" + KeyCatalog.Display(Value) + "）";
            _capture.ForeColor = Theme.Accent;
            Invalidate();
        }

        protected override bool IsInputKey(Keys keyData)
        {
            return _capturing || base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (_capturing && HandleGlobalKey(e.KeyData))
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }
            if (false)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                if (e.KeyCode == Keys.Escape) { EndCapture(true); return; }
                if (e.KeyCode == Keys.ControlKey || e.KeyCode == Keys.Menu || e.KeyCode == Keys.ShiftKey ||
                    e.KeyCode == Keys.LWin || e.KeyCode == Keys.RWin || e.KeyCode == Keys.LControlKey ||
                    e.KeyCode == Keys.RControlKey || e.KeyCode == Keys.LShiftKey || e.KeyCode == Keys.RShiftKey)
                    return;   // 只按了修饰键，等主键

                string name = KeyCatalog.FromKeys(e.KeyCode);
                if (name.Length == 0)
                {
                    _capture.Text = "这个键不支持，换一个…（Esc 取消）";
                    return;
                }
                bool ctrl = (e.Modifiers & Keys.Control) == Keys.Control;
                bool alt = (e.Modifiers & Keys.Alt) == Keys.Alt;
                bool shift = (e.Modifiers & Keys.Shift) == Keys.Shift;
                string spec = KeyCatalog.BuildSpec(ctrl, alt, shift, false, name);
                LoadValue(spec);
                EndCapture(false);
                EventHandler h = ValueChanged;
                if (h != null) h(this, EventArgs.Empty);
                return;
            }
            base.OnKeyDown(e);
        }

        /// <summary>当前值（形如 ctrl+alt+f5），为空表示还没选。</summary>
        public string Value
        {
            get { return _keys.SelectedIndex < 0 ? "" : Build(); }
            set { LoadValue(value); }
        }

        private void LoadValue(string spec)
        {
            _loading = true;
            try
            {
                List<string> mods;
                string key;
                KeyCatalog.Split(spec, out mods, out key);
                for (int i = 0; i < _mods.Length; i++)
                    _mods[i].Checked = mods.Contains(KeyCatalog.Modifiers[i]);

                int idx = -1;
                for (int i = 0; i < _keys.Items.Count; i++)
                {
                    KeyCatalog.KeyItem item = _keys.Items[i] as KeyCatalog.KeyItem;
                    if (item != null && item.Key.Length > 0 && item.Key == key) { idx = i; break; }
                }
                if (idx < 0)
                {
                    _custom.Text = key;
                    _custom.Visible = true;
                    idx = _keys.Items.Count - 1;      // 自定义…
                }
                else
                {
                    _custom.Visible = false;
                }
                if (idx >= 0) _keys.SelectedIndex = idx;
            }
            finally
            {
                _loading = false;
                UpdatePreview();
            }
        }

        private void Compose()
        {
            if (_loading) return;
            bool custom = _keys.SelectedIndex == _keys.Items.Count - 1;
            _custom.Visible = custom;
            UpdatePreview();
            EventHandler h = ValueChanged;
            if (h != null) h(this, EventArgs.Empty);
        }

        private string Build()
        {
            List<string> parts = new List<string>();
            for (int i = 0; i < _mods.Length; i++)
                if (_mods[i].Checked) parts.Add(KeyCatalog.Modifiers[i]);

            string key;
            if (_keys.SelectedIndex == _keys.Items.Count - 1)
                key = KeyCatalog.Normalize(_custom.Text);
            else
            {
                KeyCatalog.KeyItem item = _keys.SelectedItem as KeyCatalog.KeyItem;
                key = item != null ? item.Key : "";
            }
            if (key.Length > 0) parts.Add(key);
            return string.Join("+", parts.ToArray());
        }

        private void UpdatePreview()
        {
            if (_preview == null) return;
            string v = Build();
            if (string.IsNullOrEmpty(v)) { _preview.Text = "还没选按键"; _preview.ForeColor = Theme.TextDim; return; }
            bool ok = KeyCatalog.IsValid(v);
            _preview.Text = ok ? ("将要按：" + KeyCatalog.Display(v)) : ("键名无效：" + v);
            _preview.ForeColor = ok ? Theme.Success : Theme.Danger;
        }
    }
}
