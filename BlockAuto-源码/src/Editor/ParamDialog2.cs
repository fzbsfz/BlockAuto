using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace BlockAuto
{
    /// <summary>
    /// v2.0 参数面板：常驻参数少而直接，高级参数折叠；鼠标点击用「记录位置」取坐标；
    /// 识图模板用缩略图点选。
    /// </summary>
    public sealed class ParamDialog : DarkDialog
    {
        private readonly ScriptDocument _doc;
        private readonly Block _block;
        private readonly Dictionary<string, Control> _controls = new Dictionary<string, Control>();
        private readonly Dictionary<string, string> _kinds = new Dictionary<string, string>();
        private readonly List<Panel> _advancedRows = new List<Panel>();
        private readonly List<Image> _thumbs = new List<Image>();
        private Label _advToggle;
        private bool _advVisible;

        // 鼠标点击坐标
        private int? _recX;
        private int? _recY;
        private bool _coordBound;
        private string _coordMode = "recorded";
        private Label _coordLabel;
        private Panel _coordRow;
        private Panel _templateRowPanel;

        // 模板
        private string _templateValue = "";
        private Label _templateLabel;

        public ParamDialog(ScriptDocument doc, Block block)
        {
            _doc = doc;
            _block = block;
            Text = "编辑 - " + Catalog.TitleOf(block.Type);
            Size = new Size(580, 640);
            MaximizeBox = false;
            MinimizeBox = false;
            BuildUi();
        }

        private void BuildUi()
        {
            Label title = new Label();
            title.Text = Catalog.TitleOf(_block.Type);
            title.ForeColor = Theme.Text;
            title.Font = Theme.UiFont(12.5f, FontStyle.Bold);
            title.Dock = DockStyle.Top;
            title.Height = 38;
            title.TextAlign = ContentAlignment.MiddleLeft;
            title.Padding = new Padding(14, 0, 0, 0);
            Controls.Add(title);

            FlowLayoutPanel flow = new FlowLayoutPanel();
            flow.Dock = DockStyle.Fill;
            flow.FlowDirection = FlowDirection.TopDown;
            flow.WrapContents = false;
            flow.AutoScroll = true;
            flow.Padding = new Padding(10, 4, 10, 10);
            flow.BackColor = Theme.Panel;
            Controls.Add(flow);

            BlockInfo info = Catalog.Get(_block.Type);
            bool simple = !string.Equals(Environment.GetEnvironmentVariable("BLOCKAUTO_PRO"), "1") && !ConfigModeIsPro();

            if (info != null)
            {
                foreach (ParamSpec spec in info.Params)
                {
                    if (spec.Name == "branches") continue;
                    if (_block.Type == "mouse_click" && (spec.Name == "x" || spec.Name == "y" || spec.Name == "coord_mode")) continue;
                    if (_block.Type == "mouse_click" && spec.Name == "template") continue;
                    if (_block.Type == "find_image" && spec.Name == "template") continue;

                    bool advanced = IsAdvancedParam(_block.Type, spec.Name);
                    if (advanced)
                    {
                        AddParam(flow, spec, true);
                    }
                    else
                    {
                        AddParam(flow, spec, false);
                    }
                }
            }

            if (_block.Type == "mouse_click") AddCoordinateRow(flow);
            if (_block.Type == "mouse_click") AddTemplateRow(flow, info);
            if (_block.Type == "find_image") AddTemplateRow(flow, info);

            if (info == null || (info.Params.Count == 0 && _block.Type != "mouse_click"))
            {
                flow.Controls.Add(Ui.Label("该积木没有可编辑参数。"));
            }

            if (_advancedRows.Count > 0)
            {
                _advToggle = new Label();
                _advToggle.Text = "显示高级参数 ▾";
                _advToggle.ForeColor = Theme.TextDim;
                _advToggle.Font = Theme.UiFont(9f, FontStyle.Underline);
                _advToggle.AutoSize = true;
                _advToggle.Cursor = Cursors.Hand;
                _advToggle.Margin = new Padding(2, 10, 0, 0);
                _advToggle.Click += delegate
                {
                    _advVisible = !_advVisible;
                    foreach (Panel row in _advancedRows) row.Visible = _advVisible;
                    _advToggle.Text = (_advVisible ? "隐藏高级参数 ▴" : "显示高级参数 ▾");
                };
                flow.Controls.Add(_advToggle);
                foreach (Panel row in _advancedRows) row.Visible = false;
            }

            Panel bottom = new Panel();
            bottom.Dock = DockStyle.Bottom;
            bottom.Height = 54;
            bottom.BackColor = Theme.Panel;
            FlatButton ok = Ui.Button("确定", delegate { Apply(); });
            ok.Width = 96;
            ok.BackColor = Theme.Accent;
            ok.ForeColor = Color.White;
            ok.Location = new Point(356, 11);
            FlatButton cancel = Ui.Button("取消", delegate { DialogResult = DialogResult.Cancel; Close(); });
            cancel.Location = new Point(460, 11);
            bottom.Controls.Add(ok);
            bottom.Controls.Add(cancel);
            Controls.Add(bottom);
            AcceptButton = ok;
            CancelButton = cancel;
        }

        private bool ConfigModeIsPro()
        {
            try { return !AppConfig.Load().IsSimpleMode; }
            catch { return false; }
        }

        private static bool IsAdvancedParam(string type, string name)
        {
            if (type == "find_image" && (name == "region" || name == "threshold")) return true;
            if (type == "mouse_click" && name == "offset") return true;
            if (type == "create_var" && name == "scope") return true;
            if (type == "find_color" && (name == "region" || name == "tolerance")) return true;
            return false;
        }

        private void AddParam(FlowLayoutPanel flow, ParamSpec spec, bool advanced)
        {
            Panel row = new Panel();
            row.Width = 520;
            row.Height = 34;
            row.Margin = new Padding(0, 3, 0, 3);
            Label label = new Label();
            label.Text = spec.Label;
            label.ForeColor = Theme.TextDim;
            label.AutoSize = false;
            label.Width = 108;
            label.Height = 24;
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.Location = new Point(4, 4);
            row.Controls.Add(label);

            object current = _block.Param(spec.Name, spec.Default);

            if (_block.Type == "call_func" && spec.Name == "func")
            {
                ComboBox cb = new ComboBox();
                cb.DropDownStyle = ComboBoxStyle.DropDownList;
                Theme.ApplyDarkCombo(cb);
                foreach (FunctionDef f in _doc.Functions) cb.Items.Add(f.Name);
                string cur = Convert.ToString(current);
                if (cb.Items.Contains(cur)) cb.SelectedItem = cur;
                else if (cb.Items.Count > 0) cb.SelectedIndex = 0;
                cb.Location = new Point(116, 4);
                cb.Width = 380;
                row.Controls.Add(cb);
                _controls[spec.Name] = cb;
                _kinds[spec.Name] = "choice";
                AddRow(flow, row, advanced);
                return;
            }

            if (spec.Kind == "choice" && spec.Options != null)
            {
                ComboBox cb = new ComboBox();
                cb.DropDownStyle = ComboBoxStyle.DropDownList;
                Theme.ApplyDarkCombo(cb);
                foreach (string opt in spec.Options)
                {
                    if (_block.Type == "create_var" && spec.Name == "type" && IsSimpleMode() && (opt == "dict" || opt == "array")) continue;
                    cb.Items.Add(opt);
                }
                string cur = Convert.ToString(current);
                int idx = cb.Items.IndexOf(cur);
                if (idx < 0) idx = 0;
                if (cb.Items.Count > 0) cb.SelectedIndex = idx;
                cb.Location = new Point(116, 4);
                cb.Width = 190;
                row.Controls.Add(cb);
                _controls[spec.Name] = cb;
                _kinds[spec.Name] = "choice";
                AddRow(flow, row, advanced);
                return;
            }

            if (spec.Kind == "bool")
            {
                CheckBox chk = new CheckBox();
                chk.Text = spec.Label;
                chk.ForeColor = Theme.Text;
                chk.Checked = Json.ToBool(current, false);
                chk.Location = new Point(116, 4);
                chk.Width = 260;
                row.Controls.Add(chk);
                _controls[spec.Name] = chk;
                _kinds[spec.Name] = "bool";
                AddRow(flow, row, advanced);
                return;
            }

            if (spec.Kind == "number")
            {
                NumericUpDown num = new NumericUpDown();
                num.DecimalPlaces = 3;
                num.Minimum = -100000000m;
                num.Maximum = 100000000m;
                num.Increment = 0.1m;
                num.Value = ToDecimal(current);
                num.Location = new Point(116, 4);
                num.Width = 150;
                num.BackColor = Theme.PanelAlt;
                num.ForeColor = Theme.Text;
                num.BorderStyle = BorderStyle.FixedSingle;
                row.Controls.Add(num);
                if (!string.IsNullOrEmpty(spec.Hint))
                {
                    Label hint = new Label();
                    hint.Text = spec.Hint;
                    hint.ForeColor = Theme.TextDim;
                    hint.Font = Theme.UiFont(8f, FontStyle.Regular);
                    hint.Location = new Point(276, 7);
                    hint.Width = 230;
                    row.Controls.Add(hint);
                }
                _controls[spec.Name] = num;
                _kinds[spec.Name] = "number";
                AddRow(flow, row, advanced);
                return;
            }

            if (spec.Kind == "multiline")
            {
                row.Height = 92;
                TextBox tb = Ui.TextBox(Convert.ToString(current), true);
                tb.Location = new Point(116, 4);
                tb.Width = 380;
                row.Controls.Add(tb);
                _controls[spec.Name] = tb;
                _kinds[spec.Name] = "text";
                AddRow(flow, row, advanced);
                return;
            }

            if (spec.Kind == "key")
            {
                KeyPicker picker = new KeyPicker();
                picker.Location = new Point(116, 2);
                picker.Width = 300;
                picker.Value = Convert.ToString(current);
                picker.ValueChanged += delegate { _controls[spec.Name] = picker; };
                row.Height = 108;
                row.Controls.Add(picker);
                _controls[spec.Name] = picker;
                _kinds[spec.Name] = "key";
                AddRow(flow, row, advanced);
                return;
            }

            if (spec.Kind == "region")
            {
                TextBox tb = Ui.TextBox(string.IsNullOrEmpty(Convert.ToString(current)) ? "active" : Convert.ToString(current), false);
                tb.Location = new Point(116, 4);
                tb.Width = 190;
                row.Controls.Add(tb);
                FlatButton pick = Ui.Button("框选", delegate
                {
                    Rectangle r = RoiPickerForm.Pick(this, "框选识别区域");
                    if (r.Width > 0) tb.Text = string.Format("{0},{1},{2},{3}", r.Left, r.Top, r.Right, r.Bottom);
                });
                pick.Width = 58;
                pick.Height = 26;
                pick.Location = new Point(312, 4);
                row.Controls.Add(pick);
                FlatButton act = Ui.Button("当前窗口", delegate { tb.Text = "active"; });
                act.Width = 76;
                act.Height = 26;
                act.Location = new Point(374, 4);
                row.Controls.Add(act);
                _controls[spec.Name] = tb;
                _kinds[spec.Name] = "text";
                AddRow(flow, row, advanced);
                return;
            }

            if (spec.Kind == "rgb")
            {
                TextBox tb = Ui.TextBox(Convert.ToString(current), false);
                tb.Location = new Point(116, 4);
                tb.Width = 130;
                row.Controls.Add(tb);
                FlatButton pick = Ui.Button("取色", delegate
                {
                    ColorDialog cd = new ColorDialog();
                    cd.Color = DesktopAutomation.ParseColor(tb.Text);
                    if (cd.ShowDialog(this) == DialogResult.OK)
                        tb.Text = "#" + cd.Color.R.ToString("X2") + cd.Color.G.ToString("X2") + cd.Color.B.ToString("X2");
                });
                pick.Width = 60;
                pick.Height = 26;
                pick.Location = new Point(252, 4);
                row.Controls.Add(pick);
                Panel swatch = new Panel();
                swatch.BackColor = DesktopAutomation.ParseColor(tb.Text);
                swatch.Location = new Point(318, 5);
                swatch.Size = new Size(22, 22);
                swatch.BorderStyle = BorderStyle.FixedSingle;
                row.Controls.Add(swatch);
                tb.TextChanged += delegate { swatch.BackColor = DesktopAutomation.ParseColor(tb.Text); };
                _controls[spec.Name] = tb;
                _kinds[spec.Name] = "text";
                AddRow(flow, row, advanced);
                return;
            }

            // text / offset
            string currentText = Convert.ToString(current);
            List<object> currentList = current as List<object>;
            if (currentList != null)
                currentText = string.Join(",", currentList.ConvertAll<string>(delegate(object o) { return Convert.ToString(o, CultureInfo.InvariantCulture); }).ToArray());
            TextBox box = Ui.TextBox(currentText, false);
            box.Location = new Point(116, 4);
            box.Width = 380;
            if (spec.Kind == "offset" && box.Text.Trim().Length == 0) box.Text = "0,0";
            row.Controls.Add(box);
            if (!string.IsNullOrEmpty(spec.Hint))
            {
                Label hint = new Label();
                hint.Text = spec.Hint;
                hint.ForeColor = Theme.TextDim;
                hint.Font = Theme.UiFont(8f, FontStyle.Regular);
                hint.Location = new Point(116, 26);
                hint.Width = 380;
                row.Controls.Add(hint);
                row.Height = 44;
            }
            _controls[spec.Name] = box;
            _kinds[spec.Name] = spec.Kind;
            AddRow(flow, row, advanced);
        }

        private bool IsSimpleMode()
        {
            try { return AppConfig.Load().IsSimpleMode; } catch { return true; }
        }

        private void AddRow(FlowLayoutPanel flow, Panel row, bool advanced)
        {
            flow.Controls.Add(row);
            if (advanced) _advancedRows.Add(row);
        }

        // ---------------------------- 点击坐标 ----------------------------

        private void AddCoordinateRow(FlowLayoutPanel flow)
        {
            object mode = _block.Param("coord_mode", "recorded");
            string modeText = string.Equals(Convert.ToString(mode), "bound", StringComparison.OrdinalIgnoreCase) ? "bound"
                : (string.Equals(Convert.ToString(mode), "image", StringComparison.OrdinalIgnoreCase) ? "image" : "recorded");
            _coordMode = modeText == "image" ? "image" : (modeText == "bound" ? "bound" : "recorded");
            _coordBound = modeText == "bound";
            object px = _block.Param("x", null);
            object py = _block.Param("y", null);
            if (px != null && Convert.ToString(px).Trim().Length > 0)
            {
                _recX = (int)Json.ToNumber(px, 0);
                _recY = (int)Json.ToNumber(py, 0);
            }

            Panel row = new Panel();
            row.Width = 520;
            row.Height = 40;
            Label label = new Label();
            label.Text = "坐标来源";
            label.ForeColor = Theme.TextDim;
            label.AutoSize = false;
            label.Width = 108;
            label.Height = 24;
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.Location = new Point(4, 8);
            row.Controls.Add(label);

            FlatButton fixedBtn = Ui.Button("固定坐标", null);
            fixedBtn.Width = 84;
            fixedBtn.Height = 26;
            fixedBtn.Location = new Point(116, 6);
            fixedBtn.Outline = true;
            row.Controls.Add(fixedBtn);

            FlatButton imageBtn = Ui.Button("自动识图", null);
            imageBtn.Width = 84;
            imageBtn.Height = 26;
            imageBtn.Location = new Point(206, 6);
            imageBtn.Outline = true;
            row.Controls.Add(imageBtn);

            _coordLabel = new Label();
            _coordLabel.ForeColor = Theme.Text;
            _coordLabel.Font = Theme.UiFont(9.5f, FontStyle.Bold);
            _coordLabel.Location = new Point(300, 10);
            _coordLabel.AutoSize = true;
            row.Controls.Add(_coordLabel);

            FlatButton record = Ui.Button("◎ 记录位置", delegate { RecordCoordinate(); });
            record.Width = 94;
            record.Height = 26;
            record.Location = new Point(116, 42);
            row.Controls.Add(record);
            FlatButton clear = Ui.Button("清除", delegate
            {
                _recX = null;
                _recY = null;
                _coordBound = false;
                UpdateCoordLabel();
            });
            clear.Width = 54;
            clear.Height = 26;
            clear.Location = new Point(214, 42);
            row.Controls.Add(clear);
            row.Height = 76;

            imageBtn.Click += delegate
            {
                _coordMode = "image";
                _coordBound = false;
                UpdateCoordLabel();
                if (_templateRowPanel != null) _templateRowPanel.Visible = true;
            };
            fixedBtn.Click += delegate
            {
                _coordMode = "recorded";
                _coordBound = false;
                UpdateCoordLabel();
            };
            _modeButtons = new FlatButton[] { fixedBtn, imageBtn };

            flow.Controls.Add(row);
            _coordRow = row;
            UpdateCoordLabel();
        }

        private FlatButton[] _modeButtons = new FlatButton[0];

        private void UpdateCoordLabel()
        {
            if (_modeButtons != null && _modeButtons.Length == 2)
            {
                bool img = _coordMode == "image" || _coordBound;
                _modeButtons[0].BackColor = img ? Theme.PanelAlt : Theme.Mix(Theme.Panel, Theme.Accent, 0.16f);
                _modeButtons[0].ForeColor = img ? Theme.Text : Theme.Accent;
                _modeButtons[1].BackColor = img ? Theme.Mix(Theme.Panel, Theme.Accent, 0.16f) : Theme.PanelAlt;
                _modeButtons[1].ForeColor = img ? Theme.Accent : Theme.Text;
            }
            if (_coordLabel == null) return;
            if (_coordBound)
            {
                _coordLabel.Text = "旧版：用识图积木的坐标";
                _coordLabel.ForeColor = Theme.Warning;
            }
            else if (_coordMode == "image")
            {
                _coordLabel.Text = "运行时自动找图并点击";
                _coordLabel.ForeColor = Theme.Accent;
            }
            else if (_recX.HasValue)
            {
                _coordLabel.Text = "(" + _recX.Value + ", " + _recY.GetValueOrDefault() + ")";
                _coordLabel.ForeColor = Theme.Success;
            }
            else
            {
                _coordLabel.Text = "未记录";
                _coordLabel.ForeColor = Theme.TextDim;
            }
        }

        private void RecordCoordinate()
        {
            using (CoordPickerForm f = new CoordPickerForm())
            {
                if (f.ShowDialog(this) != DialogResult.OK) return;
                _recX = f.PickedPoint.X;
                _recY = f.PickedPoint.Y;
                _coordBound = false;
                UpdateCoordLabel();
            }
        }

        // ---------------------------- 模板缩略图 ----------------------------

        private void AddTemplateRow(FlowLayoutPanel flow, BlockInfo info)
        {
            _templateValue = Json.GetString(_block.Params, "template", "");

            Panel row = new Panel();
            row.Width = 520;
            row.Height = 96;
            row.Margin = new Padding(0, 3, 0, 3);
            _templateRowPanel = row;
            Label label = new Label();
            label.Text = "识别模板";
            label.ForeColor = Theme.TextDim;
            label.AutoSize = false;
            label.Width = 108;
            label.Height = 24;
            label.Location = new Point(4, 4);
            row.Controls.Add(label);

            _templateLabel = new Label();
            _templateLabel.Text = string.IsNullOrEmpty(_templateValue) ? "未选择" : _templateValue;
            _templateLabel.ForeColor = Theme.Text;
            _templateLabel.AutoSize = false;
            _templateLabel.Width = 280;
            _templateLabel.Height = 24;
            _templateLabel.TextAlign = ContentAlignment.MiddleLeft;
            _templateLabel.Location = new Point(116, 4);
            row.Controls.Add(_templateLabel);

            FlatButton lib = Ui.Button("模板库…", delegate
            {
                using (TemplateManagerForm t = new TemplateManagerForm(_doc, true))
                {
                    if (t.ShowDialog(this) == DialogResult.OK && !string.IsNullOrEmpty(t.SelectedTemplate))
                    {
                        _templateValue = t.SelectedTemplate;
                        _templateLabel.Text = _templateValue;
                        ReloadThumbs();
                    }
                }
            });
            lib.Width = 84;
            lib.Height = 24;
            lib.Location = new Point(400, 3);
            row.Controls.Add(lib);

            FlatButton grab = Ui.Button("截图取图", delegate
            {
                string saved = TemplateCapture.PickAndSave(this, _doc, "截图取图");
                if (saved == null) return;
                _templateValue = saved;
                _templateLabel.Text = _templateValue;
                ReloadThumbs();
                if (_block.Type == "mouse_click")
                {
                    _coordMode = "image";
                    UpdateCoordLabel();
                }
            });
            grab.Width = 84;
            grab.Height = 24;
            grab.Location = new Point(400, 30);
            row.Controls.Add(grab);

            FlowLayoutPanel strip = new FlowLayoutPanel();
            strip.Location = new Point(116, 34);
            strip.Size = new Size(278, 56);
            strip.AutoScroll = true;
            strip.FlowDirection = FlowDirection.LeftToRight;
            strip.WrapContents = false;
            strip.Tag = "thumbstrip";
            row.Controls.Add(strip);
            _controls["__thumbs"] = strip;

            if (_block.Type == "mouse_click") row.Visible = (_coordMode == "image" || _coordBound);
            flow.Controls.Add(row);
            ReloadThumbs();
        }

        private void ReloadThumbs()
        {
            Control stripCtl;
            if (!_controls.TryGetValue("__thumbs", out stripCtl)) return;
            FlowLayoutPanel strip = stripCtl as FlowLayoutPanel;
            if (strip == null) return;
            foreach (Image img in _thumbs) img.Dispose();
            _thumbs.Clear();
            strip.Controls.Clear();

            string dir = Path.Combine(_doc.Directory, "templates");
            if (!Directory.Exists(dir)) return;
            string[] files = Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            int shown = 0;
            foreach (string f in files)
            {
                string ext = Path.GetExtension(f).ToLowerInvariant();
                if (ext != ".png" && ext != ".jpg" && ext != ".jpeg" && ext != ".bmp") continue;
                if (shown++ >= 12) break;
                try
                {
                    Image img = Image.FromFile(f);
                    _thumbs.Add(img);
                    PictureBox pb = new PictureBox();
                    pb.Size = new Size(48, 48);
                    pb.SizeMode = PictureBoxSizeMode.Zoom;
                    pb.Image = img;
                    pb.Cursor = Cursors.Hand;
                    pb.Margin = new Padding(2);
                    pb.BorderStyle = BorderStyle.FixedSingle;
                    pb.BackColor = Theme.PanelAlt;
                    string rel = "templates/" + f.Substring(dir.Length).TrimStart(Path.DirectorySeparatorChar).Replace('\\', '/');
                    pb.Tag = rel;
                    pb.Click += delegate(object s, EventArgs e)
                    {
                        _templateValue = Convert.ToString(((PictureBox)s).Tag);
                        _templateLabel.Text = _templateValue;
                    };
                    ToolTip tip = new ToolTip();
                    tip.SetToolTip(pb, rel);
                    strip.Controls.Add(pb);
                }
                catch { }
            }
        }

        private static decimal ToDecimal(object v)
        {
            double d = Json.ToNumber(v, 0);
            if (d > 100000000d) d = 100000000d;
            if (d < -100000000d) d = -100000000d;
            return (decimal)d;
        }

        private void Apply()
        {
            try
            {
                foreach (KeyValuePair<string, Control> kv in _controls)
                {
                    if (kv.Key == "__thumbs") continue;
                    string name = kv.Key;
                    Control c = kv.Value;
                    string kind;
                    _kinds.TryGetValue(name, out kind);
                    object value = null;
                    if (kind == "key")
                    {
                        KeyPicker picker = c as KeyPicker;
                        string keySpec = picker != null ? picker.Value : Convert.ToString(((TextBox)c).Text);
                        if (!KeyCatalog.IsValid(keySpec))
                        {
                            MessageBox.Show(this, "按键写法无效：" + keySpec + Environment.NewLine
                                + "请用下拉框选择，或手填 ctrl+c / f5 / enter 这类写法。",
                                "BlockAuto", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            return;
                        }
                        _block.Params[name] = keySpec;
                        continue;
                    }
                    if (kind == "number") value = (double)((NumericUpDown)c).Value;
                    else if (kind == "bool") value = ((CheckBox)c).Checked;
                    else if (kind == "choice") value = Convert.ToString(((ComboBox)c).SelectedItem);
                    else if (kind == "offset")
                    {
                        string s = ((TextBox)c).Text.Replace("，", ",");
                        string[] parts = s.Split(',');
                        List<object> arr = new List<object>();
                        arr.Add(Json.ToNumber(parts.Length > 0 ? parts[0].Trim() : "0", 0));
                        arr.Add(Json.ToNumber(parts.Length > 1 ? parts[1].Trim() : "0", 0));
                        value = arr;
                    }
                    else value = ((TextBox)c).Text;

                    if (_block.Type == "create_var" && name == "init")
                    {
                        string type = Convert.ToString(_block.Params.ContainsKey("type") ? _block.Params["type"] : "num");
                        Control typeCtl;
                        if (_controls.TryGetValue("type", out typeCtl))
                        {
                            ComboBox cb = typeCtl as ComboBox;
                            if (cb != null && cb.SelectedItem != null) type = Convert.ToString(cb.SelectedItem);
                        }
                        if (type == "num") value = Json.ToNumber(value, 0);
                        else if (type == "bool") value = Json.ToBool(value, false);
                    }
                    _block.Params[name] = value;
                }

                if (_block.Type == "find_image")
                {
                    _block.Params["template"] = _templateValue;
                    if (!_block.Params.ContainsKey("region") || string.IsNullOrEmpty(Convert.ToString(_block.Params["region"])))
                        _block.Params["region"] = "active";
                }
                if (_block.Type == "mouse_click")
                {
                    _block.Params["coord_mode"] = _coordBound ? "bound" : _coordMode;
                    if (_coordMode == "image") _block.Params["template"] = _templateValue;
                    bool useRecorded = !_coordBound && _coordMode != "image";
                    if (useRecorded && _recX.HasValue)
                    {
                        _block.Params["x"] = (double)_recX.Value;
                        _block.Params["y"] = (double)_recY.GetValueOrDefault();
                    }
                    else if (useRecorded)
                    {
                        _block.Params["x"] = "";
                        _block.Params["y"] = "";
                    }
                }

                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "参数保存失败：" + ex.Message, "BlockAuto", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (Image img in _thumbs) { try { img.Dispose(); } catch { } }
                _thumbs.Clear();
            }
            base.Dispose(disposing);
        }
    }
}