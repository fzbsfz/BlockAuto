using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace BlockAuto
{
    /// <summary>
    /// 右侧常驻参数栏：点中列表里的某一步，就在右边直接改参数，不弹窗。
    /// 只放常用项，坐标/记录位置等键鼠专属操作做成一行按钮。
    /// </summary>
    public sealed class SideParamPanel : Panel
    {
        private readonly Label _title;
        private readonly Label _sub;
        private readonly FlowLayoutPanel _flow;
        private readonly FlatButton _close;
        private ScriptDocument _doc;
        private Block _block;
        private Action _pushUndo;
        private Action _changed;
        private bool _applying;

        /// <summary>点右上角 × 收起参数抽屉。</summary>
        public event Action CloseRequested;

        public SideParamPanel()
        {
            Dock = DockStyle.Right;
            Width = Theme.Px(300);
            BackColor = Theme.Panel;
            BorderStyle = BorderStyle.FixedSingle;
            Padding = new Padding(Theme.Px(10), Theme.Px(6), Theme.Px(8), Theme.Px(8));

            TableLayoutPanel grid = new TableLayoutPanel();
            grid.Dock = DockStyle.Fill;
            grid.ColumnCount = 1;
            grid.RowCount = 3;
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.Px(30)));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.Px(22)));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            grid.BackColor = Theme.Panel;
            Controls.Add(grid);

            Panel head = new Panel();
            head.Dock = DockStyle.Fill;
            head.BackColor = Theme.Panel;

            _close = Ui.Button("×", delegate
            {
                Action h = CloseRequested;
                if (h != null) h();
            });
            _close.Width = Theme.Px(24);
            _close.Height = Theme.Px(22);
            _close.Dock = DockStyle.Right;
            _close.Font = Theme.UiFont(11f, FontStyle.Bold);
            _close.ForeColor = Theme.TextDim;
            head.Controls.Add(_close);

            _title = new Label();
            _title.Text = "参数";
            _title.Font = Theme.UiFont(10.5f, FontStyle.Bold);
            _title.ForeColor = Theme.Text;
            _title.Dock = DockStyle.Fill;
            _title.TextAlign = ContentAlignment.MiddleLeft;
            head.Controls.Add(_title);
            _title.BringToFront();
            grid.Controls.Add(head, 0, 0);

            _sub = new Label();
            _sub.Text = "在左侧点一个步骤";
            _sub.Font = Theme.UiFont(8.5f, FontStyle.Regular);
            _sub.ForeColor = Theme.TextDim;
            _sub.Dock = DockStyle.Fill;
            _sub.TextAlign = ContentAlignment.MiddleLeft;
            grid.Controls.Add(_sub, 0, 1);

            _flow = new FlowLayoutPanel();
            _flow.Dock = DockStyle.Fill;
            _flow.FlowDirection = FlowDirection.TopDown;
            _flow.WrapContents = false;
            _flow.AutoScroll = true;
            _flow.BackColor = Theme.Panel;
            grid.Controls.Add(_flow, 0, 2);
        }

        public void Bind(ScriptDocument doc, Block b, Action pushUndo, Action changed)
        {
            _doc = doc;
            _block = b;
            _pushUndo = pushUndo;
            _changed = changed;
            Rebuild();
        }

        public void Rebuild()
        {
            _flow.SuspendLayout();
            _flow.Controls.Clear();
            _applying = true;
            try
            {
                if (_block == null)
                {
                    _title.Text = "参数";
                    _sub.Text = "在左侧点一个步骤，这里显示它的参数";
                    Label hint = new Label();
                    hint.Text = "提示：双击步骤可以打开完整参数窗口。";
                    hint.ForeColor = Theme.TextDim;
                    hint.Font = Theme.UiFont(8.5f, FontStyle.Regular);
                    hint.MaximumSize = new Size(Theme.Px(214), 0);
                    hint.AutoSize = true;
                    hint.Margin = new Padding(2, 8, 0, 0);
                    _flow.Controls.Add(hint);
                    return;
                }

                _title.Text = Catalog.TitleOf(_block.Type);
                _sub.Text = "第 " + (StepNumberOf(_block) + 1) + " 步" + (_block.Disabled ? " · 已禁用" : "");

                if (_block.Type == "mouse_click") BuildClickPanel();
                else BuildGenericPanel();
            }
            finally
            {
                _applying = false;
                _flow.ResumeLayout();
            }
        }

        private int StepNumberOf(Block b)
        {
            if (_doc == null) return 0;
            List<Block> list = _doc.ParentListOf(b);
            return list == null ? 0 : list.IndexOf(b);
        }

        private void BuildClickPanel()
        {
            bool imageMode = ParamText("coord_mode") == "image" || ParamText("coord_mode") == "bound";

            AddSection("坐标来源（二选一）");
            _flow.Controls.Add(RadioTile("固定坐标", "用「记录位置」记下的屏幕坐标", !imageMode, delegate { SetMode("recorded"); }));
            _flow.Controls.Add(RadioTile("自动识图", "截图取图，运行时自动找这个位置", imageMode, delegate { SetMode("image"); }));

            if (imageMode)
            {
                AddSection("识别模板（运行时自动找到并点击）");
                string tpl = ParamText("template");
                Label tplLabel = new Label();
                tplLabel.Text = tpl.Length > 0 ? Path.GetFileName(tpl) : "还没截图，点下面的「截图取图」";
                tplLabel.ForeColor = tpl.Length > 0 ? Theme.Text : Theme.Danger;
                tplLabel.Font = Theme.UiFont(9.5f, FontStyle.Bold);
                tplLabel.AutoSize = true;
                tplLabel.MaximumSize = new Size(Theme.Px(210), 0);
                tplLabel.Margin = new Padding(2, 2, 0, 4);
                _flow.Controls.Add(tplLabel);

                FlowLayoutPanel rowT = HRow();
                rowT.Controls.Add(WideButton("📷 截图取图", delegate { CaptureTemplate(); }));
                rowT.Controls.Add(WideButton("模板库…", delegate { PickTemplate(); }));
                _flow.Controls.Add(rowT);

                AddSection("识别区域");
                FlowLayoutPanel rowR = HRow();
                TextBox tbRegion = MakeText(ParamText("region"));
                tbRegion.Width = Theme.Px(140);
                tbRegion.Leave += delegate { ApplyText("region", tbRegion.Text); };
                rowR.Controls.Add(tbRegion);
                rowR.Controls.Add(WideButton("当前窗口", delegate
                {
                    tbRegion.Text = "active";
                    ApplyText("region", "active");
                }));
                _flow.Controls.Add(rowR);

                AddSection("相似度（0~1，越大越严格）");
                _flow.Controls.Add(NumberBox("threshold", 0.8, 0, 1, 0.05, 3));

                AddSection("找不到图时");
                _flow.Controls.Add(ChoiceBox("on_fail", new string[] { "retry", "skip", "error" },
                    new string[] { "重试几次", "跳过这一步", "报错停止" }, "retry"));
                AddSection("重试次数 / 间隔（秒）");
                FlowLayoutPanel rowRetry = HRow();
                NumericUpDown retries = new NumericUpDown();
                retries.Minimum = 0;
                retries.Maximum = 200;
                retries.Value = ToDecimal(ParamText("retries").Length > 0 ? ParamText("retries") : "3");
                retries.Width = Theme.Px(90);
                retries.BackColor = Theme.IsLight ? Color.White : Theme.PanelAlt;
                retries.ForeColor = Theme.Text;
                retries.ValueChanged += delegate
                {
                    if (_applying) return;
                    SetParam("retries", (double)retries.Value);
                };
                rowRetry.Controls.Add(retries);
                NumericUpDown wait = new NumericUpDown();
                wait.DecimalPlaces = 1;
                wait.Minimum = 0;
                wait.Maximum = 3600;
                wait.Increment = 0.5m;
                wait.Value = ToDecimal(ParamText("retry_delay").Length > 0 ? ParamText("retry_delay") : "0.3");
                wait.Width = Theme.Px(90);
                wait.BackColor = Theme.IsLight ? Color.White : Theme.PanelAlt;
                wait.ForeColor = Theme.Text;
                wait.ValueChanged += delegate
                {
                    if (_applying) return;
                    SetParam("retry_delay", (double)wait.Value);
                };
                rowRetry.Controls.Add(wait);
                _flow.Controls.Add(rowRetry);
            }
            else
            {
                AddSection("点击位置");
                string pos = ParamText("x").Length > 0 ? "(" + ParamText("x") + ", " + ParamText("y") + ")" : "未记录";
                Label posLabel = new Label();
                posLabel.Text = pos;
                posLabel.ForeColor = ParamText("x").Length > 0 ? Theme.Success : Theme.TextDim;
                posLabel.Font = Theme.UiFont(10f, FontStyle.Bold);
                posLabel.AutoSize = true;
                posLabel.Margin = new Padding(2, 2, 0, 4);
                _flow.Controls.Add(posLabel);

                FlowLayoutPanel row3 = HRow();
                row3.Controls.Add(WideButton("◎ 记录位置", delegate
                {
                    Action<Block> h = RecordRequested;
                    if (h != null) h(_block);
                }));
                row3.Controls.Add(WideButton("清除坐标", delegate { ClearPoint(); }));
                _flow.Controls.Add(row3);
                AddHint("想要“点某个按钮”更稳？改用上面的「自动识图」，截图后自动找位置。");
            }

            AddSection("按键 / 动作");
            FlowLayoutPanel row1 = HRow();
            row1.Controls.Add(Toggle("左键", ParamText("button") != "right", delegate { SetParam("button", "left"); }));
            row1.Controls.Add(Toggle("右键", ParamText("button") == "right", delegate { SetParam("button", "right"); }));
            row1.Controls.Add(Toggle("单击", ParamText("action") != "dblclick", delegate { SetParam("action", "click"); }));
            row1.Controls.Add(Toggle("双击", ParamText("action") == "dblclick", delegate { SetParam("action", "dblclick"); }));
            _flow.Controls.Add(row1);

            AddSection("点击后延时（秒，0 = 用全局间隔）");
            _flow.Controls.Add(NumberBox("delay_after", 0.2, 0, 3600, 0.1, 1));

            AddTextRow("偏移 (dx,dy)", "offset", true);

            FlowLayoutPanel row4 = HRow();
            row4.Controls.Add(WideButton("全部参数…", delegate { EditRequested(); }));
            row4.Controls.Add(WideButton(_block.Disabled ? "启用" : "禁用", delegate { ToggleDisable(); }));
            _flow.Controls.Add(row4);
        }

        private void SetMode(string mode)
        {
            if (_pushUndo != null) _pushUndo();
            _block.Params["coord_mode"] = mode;
            if (mode == "image")
            {
                if (!_block.Params.ContainsKey("template")) _block.Params["template"] = "";
                if (!_block.Params.ContainsKey("threshold")) _block.Params["threshold"] = 0.8d;
                if (!_block.Params.ContainsKey("region")) _block.Params["region"] = "active";
                if (!_block.Params.ContainsKey("on_fail")) _block.Params["on_fail"] = "retry";
            }
            if (_changed != null) _changed();
            Rebuild();
        }

        private void ClearPoint()
        {
            if (_pushUndo != null) _pushUndo();
            _block.Params["x"] = "";
            _block.Params["y"] = "";
            if (_changed != null) _changed();
            Rebuild();
        }

        private void PickTemplate()
        {
            using (TemplateManagerForm f = new TemplateManagerForm(_doc, true))
            {
                if (f.ShowDialog(FindForm()) != DialogResult.OK || string.IsNullOrEmpty(f.SelectedTemplate)) return;
                if (_pushUndo != null) _pushUndo();
                _block.Params["coord_mode"] = "image";
                _block.Params["template"] = f.SelectedTemplate;
                if (_changed != null) _changed();
                Rebuild();
            }
        }

        private NumericUpDown NumberBox(string key, double def, double min, double max, double step, int decimals)
        {
            NumericUpDown num = new NumericUpDown();
            num.DecimalPlaces = decimals;
            num.Minimum = (decimal)min;
            num.Maximum = (decimal)max;
            num.Increment = (decimal)step;
            string cur = ParamText(key);
            num.Value = ToDecimal(cur.Length > 0 ? cur : def.ToString("0.###"));
            num.Width = Theme.Px(216);
            num.BackColor = Theme.IsLight ? Color.White : Theme.PanelAlt;
            num.ForeColor = Theme.Text;
            num.BorderStyle = BorderStyle.FixedSingle;
            num.Margin = new Padding(2, 0, 0, 6);
            num.ValueChanged += delegate
            {
                if (_applying) return;
                SetParam(key, (double)num.Value);
            };
            return num;
        }

        private sealed class ComboItem
        {
            public string Key;
            public string Text;
            public ComboItem(string key, string text) { Key = key; Text = text; }
            public override string ToString() { return Text; }
        }

        private ComboBox ChoiceBox(string key, string[] values, string[] labels, string def)
        {
            ComboBox cb = new ComboBox();
            cb.DropDownStyle = ComboBoxStyle.DropDownList;
            Theme.ApplyDarkCombo(cb);
            cb.Width = Theme.Px(216);
            cb.Margin = new Padding(2, 0, 0, 6);
            string cur = ParamText(key);
            if (cur.Length == 0) cur = def;
            int idx = 0;
            for (int i = 0; i < values.Length; i++)
            {
                cb.Items.Add(new ComboItem(values[i], labels[i]));
                if (values[i] == cur) idx = i;
            }
            cb.SelectedIndex = idx;
            cb.SelectedIndexChanged += delegate
            {
                if (_applying || cb.SelectedItem == null) return;
                SetParam(key, ((ComboItem)cb.SelectedItem).Key);
            };
            return cb;
        }

        private bool IsImageIf(Block b)
        {
            object v;
            return b != null && b.Params.TryGetValue("cond_type", out v)
                && string.Equals(Convert.ToString(v), "image", StringComparison.OrdinalIgnoreCase);
        }

        private void BuildGenericPanel()
        {
            if (_block.Type == "if")
            {
                BuildIfPanel();
                return;
            }
            BlockInfo info = Catalog.Get(_block.Type);
            if (info == null)
            {
                AddHint("该积木没有可编辑参数。");
            }
            else
            {
                foreach (ParamSpec spec in info.Params)
                {
                    if (spec.Name == "branches") continue;
                    AddParamRow(spec);
                }
                if (info.Params.Count == 0) AddHint("该积木没有可编辑参数。");
            }

            FlowLayoutPanel row = HRow();
            row.Controls.Add(WideButton("全部参数…", delegate { EditRequested(); }));
            row.Controls.Add(WideButton(_block.Disabled ? "启用" : "禁用", delegate { ToggleDisable(); }));
            _flow.Controls.Add(row);
        }

        private void BuildIfPanel()
        {
            AddSection("判断方式");
            FlowLayoutPanel row = HRow();
            row.Controls.Add(Toggle("条件判断", !IsImageIf(_block), delegate
            {
                if (_pushUndo != null) _pushUndo();
                _block.Params["cond_type"] = "cond";
                if (_changed != null) _changed();
                Rebuild();
            }));
            row.Controls.Add(Toggle("有图片", IsImageIf(_block), delegate
            {
                if (_pushUndo != null) _pushUndo();
                _block.Params["cond_type"] = "image";
                if (!_block.Params.ContainsKey("template")) _block.Params["template"] = "";
                if (_changed != null) _changed();
                Rebuild();
            }));
            _flow.Controls.Add(row);

            if (IsImageIf(_block))
            {
                AddSection("模板图片");
                Label tpl = new Label();
                tpl.Text = ParamText("template").Length > 0 ? System.IO.Path.GetFileName(ParamText("template")) : "未选择";
                tpl.ForeColor = ParamText("template").Length > 0 ? Theme.Text : Theme.Danger;
                tpl.AutoSize = true;
                _flow.Controls.Add(tpl);
                FlatButton pick = WideButton("从模板库选择…", delegate
                {
                    using (TemplateManagerForm f = new TemplateManagerForm(_doc, true))
                    {
                        if (f.ShowDialog(FindForm()) == DialogResult.OK && !string.IsNullOrEmpty(f.SelectedTemplate))
                        {
                            if (_pushUndo != null) _pushUndo();
                            _block.Params["template"] = f.SelectedTemplate;
                            if (_changed != null) _changed();
                            Rebuild();
                        }
                    }
                });
                _flow.Controls.Add(pick);
                _flow.Controls.Add(WideButton("截图取图…", delegate { CaptureTemplate(); }));
                AddTextRow("相似度(0~1)", "threshold", false);
                AddTextRow("识别区域", "region", false);
            }
            else
            {
                AddHint("选择“有图片”后：找到图片就执行内部步骤，找不到就跳过。");
            }

            FlowLayoutPanel row2 = HRow();
            row2.Controls.Add(WideButton("全部参数…", delegate { EditRequested(); }));
            row2.Controls.Add(WideButton(_block.Disabled ? "启用" : "禁用", delegate { ToggleDisable(); }));
            _flow.Controls.Add(row2);
        }

/// <summary>框选屏幕区域，直接存为模板并填入当前判断/识图。</summary>
        private void CaptureTemplate()
        {
            Rectangle r = RoiPickerForm.Pick(FindForm(), "截图取图");
            if (r.Width < 4 || r.Height < 4) return;
            string saved = TemplateCapture.SaveRegionToTemplates(_doc, r);
            if (saved == null) return;
            if (_pushUndo != null) _pushUndo();
            if (_block.Type == "mouse_click") _block.Params["coord_mode"] = "image";
            _block.Params["template"] = saved;
            if (_block.Type == "mouse_click")
            {
                if (!_block.Params.ContainsKey("threshold")) _block.Params["threshold"] = 0.8d;
                if (!_block.Params.ContainsKey("region")) _block.Params["region"] = "active";
                if (!_block.Params.ContainsKey("on_fail")) _block.Params["on_fail"] = "retry";
            }
            if (_changed != null) _changed();
            Rebuild();
        }

        private void AddParamRow(ParamSpec spec)
        {
            object cur = _block.Param(spec.Name, spec.Default);

            if (spec.Kind == "choice" && spec.Options != null)
            {
                AddSection(spec.Label);
                ComboBox cb = new ComboBox();
                cb.DropDownStyle = ComboBoxStyle.DropDownList;
                Theme.ApplyDarkCombo(cb);
                cb.Width = Theme.Px(216);
                foreach (string opt in spec.Options)
                {
                    if (_block.Type == "create_var" && spec.Name == "type" && (opt == "dict")) continue;
                    cb.Items.Add(opt);
                }
                string curText = Convert.ToString(cur);
                int idx = cb.Items.IndexOf(curText);
                if (idx < 0) idx = 0;
                if (cb.Items.Count > 0) cb.SelectedIndex = idx;
                cb.SelectedIndexChanged += delegate
                {
                    if (_applying || cb.SelectedItem == null) return;
                    SetParam(spec.Name, Convert.ToString(cb.SelectedItem));
                };
                cb.Margin = new Padding(2, 0, 0, 6);
                _flow.Controls.Add(cb);
                return;
            }

            if (spec.Kind == "bool")
            {
                CheckBox ck = new CheckBox();
                ck.Text = spec.Label;
                ck.ForeColor = Theme.Text;
                ck.Checked = Json.ToBool(cur, false);
                ck.AutoSize = true;
                ck.Margin = new Padding(2, 4, 0, 6);
                ck.CheckedChanged += delegate
                {
                    if (_applying) return;
                    SetParam(spec.Name, ck.Checked);
                };
                _flow.Controls.Add(ck);
                return;
            }

            if (spec.Kind == "number")
            {
                AddSection(spec.Label);
                NumericUpDown num = new NumericUpDown();
                num.DecimalPlaces = 3;
                num.Minimum = -100000000m;
                num.Maximum = 100000000m;
                num.Increment = 0.1m;
                num.Value = ToDecimal(cur);
                num.Width = Theme.Px(216);
                num.BackColor = Theme.IsLight ? Color.White : Theme.PanelAlt;
                num.ForeColor = Theme.Text;
                num.BorderStyle = BorderStyle.FixedSingle;
                num.ValueChanged += delegate
                {
                    if (_applying) return;
                    SetParam(spec.Name, (double)num.Value);
                };
                _flow.Controls.Add(num);
                return;
            }

            if (spec.Kind == "region")
            {
                AddSection(spec.Label);
                TextBox tb = MakeText(Convert.ToString(cur));
                FlowLayoutPanel row = HRow();
                row.Controls.Add(tb);
                row.Controls.Add(WideButton("当前窗口", delegate { tb.Text = "active"; ApplyText(spec.Name, tb.Text); }));
                _flow.Controls.Add(row);
                return;
            }

            if (spec.Kind == "template")
            {
                AddSection(spec.Label);
                Label tpl = new Label();
                tpl.Text = Convert.ToString(cur);
                tpl.ForeColor = Theme.Text;
                tpl.Font = Theme.UiFont(8.5f, FontStyle.Regular);
                tpl.AutoSize = false;
                tpl.Width = Theme.Px(216);
                tpl.Height = Theme.Px(34);
                _flow.Controls.Add(tpl);
                FlatButton pick = WideButton("从模板库选择…", delegate
                {
                    using (TemplateManagerForm f = new TemplateManagerForm(_doc, true))
                    {
                        if (f.ShowDialog(FindForm()) == DialogResult.OK && !string.IsNullOrEmpty(f.SelectedTemplate))
                        {
                            tpl.Text = f.SelectedTemplate;
                            SetParam("template", f.SelectedTemplate);
                        }
                    }
                });
                _flow.Controls.Add(pick);
                _flow.Controls.Add(WideButton("截图取图…", delegate { CaptureTemplate(); }));
                return;
            }

            AddTextRow(spec.Label, spec.Name, spec.Kind == "offset");
        }

        private void AddTextRow(string label, string key, bool offset)
        {
            AddSection(label);
            TextBox tb = MakeText(offset ? OffsetText() : ParamText(key));
            tb.Leave += delegate { ApplyText(key, tb.Text); };
            tb.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { ApplyText(key, tb.Text); e.SuppressKeyPress = true; }
            };
            _flow.Controls.Add(tb);
        }

        /// <summary>偏移量按 “dx,dy” 显示，而不是列表的 [0, 0]。</summary>
        private string OffsetText()
        {
            object v;
            if (_block == null || !_block.Params.TryGetValue("offset", out v)) return "0,0";
            List<object> list = v as List<object>;
            if (list == null) return string.IsNullOrEmpty(Convert.ToString(v)) ? "0,0" : Convert.ToString(v);
            if (list.Count == 0) return "0,0";
            return Engine.FormatValue(list[0]) + "," + Engine.FormatValue(list.Count > 1 ? list[1] : 0d);
        }

        private void ApplyText(string key, string text)
        {
            if (_applying) return;
            string cur = key == "offset" ? OffsetText() : ParamText(key);
            if (cur == text) return;
            if (key == "offset")
            {
                string[] parts = text.Replace("，", ",").Split(',');
                List<object> arr = new List<object>();
                arr.Add(Json.ToNumber(parts.Length > 0 ? parts[0].Trim() : "0", 0));
                arr.Add(Json.ToNumber(parts.Length > 1 ? parts[1].Trim() : "0", 0));
                SetParam(key, arr);
            }
            else SetParam(key, text);
        }

        private void SetParam(string key, object value)
        {
            if (_block == null || _applying) return;
            if (_pushUndo != null) _pushUndo();
            _block.Params[key] = value;
            if (_changed != null) _changed();
            RefreshSub();
        }

        private void ResetClick()
        {
            if (_pushUndo != null) _pushUndo();
            _block.Params["button"] = "left";
            _block.Params["action"] = "click";
            if (_changed != null) _changed();
            Rebuild();
        }

        private void ToggleDisable()
        {
            if (_pushUndo != null) _pushUndo();
            _block.Disabled = !_block.Disabled;
            if (_changed != null) _changed();
            Rebuild();
        }

        private void EditRequested()
        {
            Action<Block> h = EditBlockRequested;
            if (h != null && _block != null) h(_block);
        }

        public event Action<Block> RecordRequested;
        public event Action<Block> EditBlockRequested;

        private void RefreshSub()
        {
            if (_block == null) return;
            _sub.Text = "第 " + (StepNumberOf(_block) + 1) + " 步";
        }

        private string ParamText(string key)
        {
            object v;
            if (_block != null && _block.Params.TryGetValue(key, out v)) return Engine.FormatValue(v);
            return "";
        }

        private static decimal ToDecimal(object v)
        {
            double d = Json.ToNumber(v, 0);
            if (d > 100000000d) d = 100000000d;
            if (d < -100000000d) d = -100000000d;
            return (decimal)d;
        }

        private void AddSection(string text)
        {
            Label l = new Label();
            l.Text = text;
            l.ForeColor = Theme.TextDim;
            l.Font = Theme.UiFont(8f, FontStyle.Regular);
            l.AutoSize = true;
            l.Margin = new Padding(2, 8, 0, 2);
            _flow.Controls.Add(l);
        }

        private void AddHint(string text)
        {
            Label l = new Label();
            l.Text = text;
            l.ForeColor = Theme.TextDim;
            l.Font = Theme.UiFont(8.5f, FontStyle.Regular);
            l.MaximumSize = new Size(Theme.Px(214), 0);
            l.AutoSize = true;
            l.Margin = new Padding(2, 6, 0, 0);
            _flow.Controls.Add(l);
        }

        private FlowLayoutPanel HRow()
        {
            FlowLayoutPanel row = new FlowLayoutPanel();
            row.FlowDirection = FlowDirection.LeftToRight;
            row.WrapContents = false;
            row.AutoSize = true;
            row.Margin = new Padding(0, 0, 0, 4);
            return row;
        }

        private TextBox MakeText(string value)
        {
            TextBox tb = new TextBox();
            tb.Text = value ?? "";
            tb.Width = Theme.Px(216);
            Theme.ApplyDarkTextBox(tb);
            tb.Margin = new Padding(2, 0, 0, 6);
            return tb;
        }

        private FlatButton Toggle(string text, bool active, EventHandler onClick)
        {
            FlatButton b = Ui.Button(text, onClick);
            b.Width = Theme.Px(62);
            b.Height = Theme.Px(26);
            b.Margin = new Padding(2, 0, 4, 0);
            if (active)
            {
                b.BackColor = Theme.Mix(Theme.Panel, Theme.Accent, 0.16f);
                b.ForeColor = Theme.Accent;
                b.Outline = true;
            }
            return b;
        }

        private FlatButton WideButton(string text, EventHandler onClick)
        {
            FlatButton b = Ui.Button(text, onClick);
            b.Width = Theme.Px(100);
            b.Height = Theme.Px(26);
            b.Margin = new Padding(2, 0, 4, 4);
            b.Outline = true;
            return b;
        }

        /// <summary>
        /// 「单选小卡片」：圆点 + 标题 + 说明两行排布。原来的两个小按钮挤在同一行，
        /// 和下面的「记录位置」语义也分不清，改成卡片后一眼看出是二选一。
        /// </summary>
        private Control RadioTile(string title, string hint, bool active, EventHandler onClick)
        {
            RadioTileControl tile = new RadioTileControl();
            tile.Text = title;
            tile.Hint = hint;
            tile.Active = active;
            tile.Width = Theme.Px(216);
            tile.Height = Theme.Px(46);
            tile.Margin = new Padding(2, 0, 0, 6);
            tile.Click += onClick;
            return tile;
        }

        private sealed class RadioTileControl : Control
        {
            public string Hint = "";
            public bool Active;
            private bool _hover;

            public RadioTileControl()
            {
                Cursor = Cursors.Hand;
                Font = Theme.UiFont(9.5f, FontStyle.Bold);
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            }

            protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                Color back = Active ? Theme.Mix(Theme.Panel, Theme.Accent, 0.10f)
                    : (_hover ? Theme.Mix(Theme.Panel, Theme.Accent, 0.04f) : Theme.Panel);
                using (System.Drawing.Drawing2D.GraphicsPath p =
                    Theme.RoundedRect(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 5f))
                {
                    using (SolidBrush b = new SolidBrush(back)) g.FillPath(b, p);
                    using (Pen pen = new Pen(Active ? Theme.Accent : Theme.Border)) g.DrawPath(pen, p);
                }

                // 单选圆点
                float d = Theme.Px(13);
                float cx = Theme.Px(10);
                float cy = Height / 2f - d / 2f;
                using (Pen ring = new Pen(Active ? Theme.Accent : Theme.TextDim, 1.4f))
                    g.DrawEllipse(ring, cx, cy, d, d);
                if (Active)
                {
                    using (SolidBrush dot = new SolidBrush(Theme.Accent))
                        g.FillEllipse(dot, cx + d / 2.6f, cy + d / 2.6f, d / 3.2f, d / 3.2f);
                }

                int textX = (int)(cx + d + Theme.Px(8));
                TextRenderer.DrawText(g, Text, Font,
                    new Rectangle(textX, Theme.Px(4), Width - textX - Theme.Px(6), Theme.Px(20)),
                    Active ? Theme.Accent : Theme.Text,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(g, Hint, Theme.UiFontCached(8.5f, FontStyle.Regular),
                    new Rectangle(textX, Theme.Px(23), Width - textX - Theme.Px(6), Theme.Px(19)),
                    Theme.TextDim,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
        }
    }
}