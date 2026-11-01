using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace BlockAuto
{
    internal static class Ui
    {
        public static Label Label(string text)
        {
            Label l = new Label();
            l.Text = text;
            l.ForeColor = Theme.TextDim;
            l.AutoSize = true;
            l.Font = Theme.UiFont(9f, FontStyle.Regular);
            return l;
        }

        public static Label Section(string text)
        {
            Label l = new Label();
            l.Text = text;
            l.ForeColor = Theme.Text;
            l.AutoSize = true;
            l.Font = Theme.UiFont(10f, FontStyle.Bold);
            l.Margin = new Padding(0, 8, 0, 4);
            return l;
        }

        public static TextBox TextBox(string value, bool multiline)
        {
            TextBox t = new TextBox();
            t.Text = value ?? "";
            t.Width = 380;
            if (multiline) { t.Multiline = true; t.Height = 64; t.ScrollBars = ScrollBars.Vertical; }
            Theme.ApplyDarkTextBox(t);
            return t;
        }

        public static FlatButton Button(string text, EventHandler onClick)
        {
            FlatButton b = new FlatButton();
            b.Text = text;
            b.Width = 92;
            b.Height = 28;
            b.Click += onClick;
            return b;
        }

        public static Panel Row(Label label, Control control, int controlWidth)
        {
            Panel p = new Panel();
            p.Width = 470;
            p.Height = Math.Max(30, control.Height + 8);
            p.Margin = new Padding(0, 2, 0, 2);
            label.Location = new Point(8, 6);
            label.AutoSize = false;
            label.Width = 110;
            label.Height = 24;
            label.TextAlign = ContentAlignment.MiddleLeft;
            control.Location = new Point(122, 4);
            if (controlWidth > 0) control.Width = controlWidth;
            p.Controls.Add(label);
            p.Controls.Add(control);
            return p;
        }
    }

    public class DarkDialog : Form
    {
        private bool _dpiScaled;

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.EnableDarkTitleBar(this);
        }

        public DarkDialog()
        {
            // 弹窗按 96dpi 设计尺寸书写；Theme.Scale 是当前系统缩放倍率，
            // 打开时整体放大一次，避免 125%/200% 下内容挤成一团。
            AutoScaleMode = AutoScaleMode.None;
            Theme.ApplyDarkForm(this);
            BackColor = Theme.Panel;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
        }

        protected override void OnLoad(EventArgs e)
        {
            if (!_dpiScaled)
            {
                _dpiScaled = true;
                float s = Theme.Scale;
                if (s > 1.01f)
                {
                    try { Scale(new SizeF(s, s)); }
                    catch { }
                }
                // 高缩放下弹窗可能比屏幕还高（200% 时参数窗约 1209px），
                // 内容会顶出屏幕像“卡死”一样。这里统一夹到工作区内，内容区自己滚动。
                try
                {
                    Rectangle wa = Screen.FromControl(this).WorkingArea;
                    int maxW = Math.Max(320, wa.Width - 60);
                    int maxH = Math.Max(240, wa.Height - 60);
                    int w = Math.Min(Width, maxW);
                    int h = Math.Min(Height, maxH);
                    if (w != Width || h != Height) ClientSize = new Size(w, h);
                    if (Left + Width > wa.Right) Left = Math.Max(wa.Left, wa.Right - Width - 10);
                    if (Top + Height > wa.Bottom) Top = Math.Max(wa.Top, wa.Bottom - Height - 10);
                }
                catch { }
            }
            base.OnLoad(e);
        }
    }

    // ---------------------------- 参数编辑 ----------------------------

    // ---------------------------- 文本输入 ----------------------------

    public sealed class TextInputDialog : DarkDialog
    {
        private readonly TextBox _box;

        public TextInputDialog(string title, string label, string value)
        {
            Text = title;
            Size = new Size(420, 190);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;

            Label l = Ui.Label(label);
            l.Location = new Point(16, 18);
            Controls.Add(l);

            _box = Ui.TextBox(value, false);
            _box.Location = new Point(16, 46);
            _box.Width = 370;
            Controls.Add(_box);

            FlatButton ok = Ui.Button("确定", delegate { DialogResult = DialogResult.OK; Close(); });
            ok.BackColor = Theme.Accent;
            ok.ForeColor = Color.White;
            ok.Location = new Point(196, 96);
            FlatButton cancel = Ui.Button("取消", delegate { DialogResult = DialogResult.Cancel; Close(); });
            cancel.Location = new Point(294, 96);
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;
        }

        public string Value { get { return _box.Text.Trim(); } }
    }

    // ---------------------------- 函数定义 ----------------------------

    public sealed class FunctionDialog : DarkDialog
    {
        private readonly TextBox _name;
        private readonly TextBox _params;
        private readonly CheckBox _hasReturn;
        private readonly ScriptDocument _doc;
        private readonly string _original;

        public FunctionDialog(ScriptDocument doc, FunctionDef existing)
        {
            _doc = doc;
            _original = existing != null ? existing.Name : null;
            Text = existing == null ? "新建函数" : "编辑函数";
            Size = new Size(470, 330);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;

            Label l1 = Ui.Label("函数名");
            l1.Location = new Point(18, 22);
            Controls.Add(l1);
            _name = Ui.TextBox(existing != null ? existing.Name : "my_func", false);
            _name.Location = new Point(18, 44);
            _name.Width = 410;
            Controls.Add(_name);

            Label l2 = Ui.Label("参数（格式 name:type，逗号分隔；type 可为 num/str/bool/array/dict）");
            l2.Location = new Point(18, 82);
            Controls.Add(l2);
            _params = Ui.TextBox(ParamsText(existing), true);
            _params.Location = new Point(18, 104);
            _params.Width = 410;
            _params.Height = 70;
            Controls.Add(_params);

            _hasReturn = new CheckBox();
            _hasReturn.Text = "有返回值";
            _hasReturn.ForeColor = Theme.Text;
            _hasReturn.Checked = existing != null && existing.HasReturn;
            _hasReturn.Location = new Point(18, 184);
            _hasReturn.Width = 200;
            Controls.Add(_hasReturn);

            FlatButton ok = Ui.Button("确定", delegate { Apply(); });
            ok.BackColor = Theme.Accent;
            ok.ForeColor = Color.White;
            ok.Location = new Point(240, 230);
            FlatButton cancel = Ui.Button("取消", delegate { DialogResult = DialogResult.Cancel; Close(); });
            cancel.Location = new Point(338, 230);
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;
        }

        private static string ParamsText(FunctionDef f)
        {
            if (f == null) return "arg:num";
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < f.Params.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(f.Params[i].Name).Append(':').Append(f.Params[i].Type);
            }
            return sb.ToString();
        }

        private void Apply()
        {
            string name = _name.Text.Trim();
            if (name.Length == 0)
            {
                MessageBox.Show(this, "函数名不能为空", "BlockAuto", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            foreach (char c in name)
            {
                if (!char.IsLetterOrDigit(c) && c != '_')
                {
                    MessageBox.Show(this, "函数名只能包含字母、数字、下划线", "BlockAuto", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }
            if (!string.Equals(name, _original, StringComparison.Ordinal))
            {
                foreach (FunctionDef f in _doc.Functions)
                {
                    if (string.Equals(f.Name, name, StringComparison.Ordinal))
                    {
                        MessageBox.Show(this, "已存在同名函数", "BlockAuto", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                }
            }
            List<VarDef> ps = new List<VarDef>();
            string raw = _params.Text.Replace("，", ",");
            if (raw.Trim().Length > 0)
            {
                foreach (string part in raw.Split(','))
                {
                    string p = part.Trim();
                    if (p.Length == 0) continue;
                    string[] kv = p.Split(':');
                    string pn = kv[0].Trim();
                    string pt = kv.Length > 1 ? kv[1].Trim() : "any";
                    if (pn.Length == 0) continue;
                    ps.Add(new VarDef(pn, pt, null));
                }
            }
            if (_doc.FindFunction(name) != null && !string.Equals(name, _original, StringComparison.Ordinal))
            {
                MessageBox.Show(this, "函数名重复", "BlockAuto", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            FunctionDef fn = new FunctionDef();
            fn.Name = name;
            fn.Params = ps;
            fn.HasReturn = _hasReturn.Checked;
            if (_original != null)
            {
                foreach (FunctionDef f in _doc.Functions)
                {
                    if (f.Name == _original)
                    {
                        f.Name = name;
                        f.Params = ps;
                        f.HasReturn = fn.HasReturn;
                        break;
                    }
                }
            }
            else
            {
                _doc.Functions.Add(fn);
            }
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    // ---------------------------- 屏幕框选 ----------------------------

    public sealed class RoiPickerForm : Form
    {
        private Point _start;
        private Point _current;
        private bool _selecting;
        private Rectangle _selected;
        private Bitmap _screen;

        public RoiPickerForm()
        {
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = System.Windows.Forms.SystemInformation.VirtualScreen;
            TopMost = true;
            ShowInTaskbar = false;
            Cursor = Cursors.Cross;
            DoubleBuffered = true;
            KeyPreview = true;
            BackColor = Color.Black;
            Opacity = 1;
        }

        public Rectangle SelectedRect { get { return _selected; } }

        public static Rectangle Pick(IWin32Window owner, string title)
        {
            using (RoiPickerForm f = new RoiPickerForm())
            {
                f.Text = title;
                f.ShowDialog(owner);
                return f.SelectedRect;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            try
            {
                Rectangle vs = System.Windows.Forms.SystemInformation.VirtualScreen;
                Bounds = vs;
                _screen = ScreenCapture.Capture(vs);
            }
            catch { _screen = null; }
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Rectangle vs = System.Windows.Forms.SystemInformation.VirtualScreen;
            Rectangle local = new Rectangle(0, 0, ClientSize.Width, ClientSize.Height);
            g.Clear(Color.Black);
            // 1:1 绘制，不做任何拉伸，保证“看到的”与“截到的”像素一致
            if (_screen != null) g.DrawImageUnscaled(_screen, 0, 0);
            using (SolidBrush dim = new SolidBrush(Color.FromArgb(150, 0, 0, 0))) g.FillRectangle(dim, local);

            Rectangle sel = _selecting ? Normalize(_start, _current) : _selected;
            if (sel.Width > 0 && sel.Height > 0)
            {
                if (_screen != null)
                {
                    Rectangle src = new Rectangle(sel.X - vs.X, sel.Y - vs.Y, sel.Width, sel.Height);
                    src.Intersect(new Rectangle(0, 0, _screen.Width, _screen.Height));
                    if (src.Width > 0 && src.Height > 0)
                        g.DrawImage(_screen, sel, src, GraphicsUnit.Pixel);
                }
                using (Pen p = new Pen(Theme.AccentHover, 2f)) g.DrawRectangle(p, sel);
                string text = "选区 " + sel.Width + " × " + sel.Height + "    坐标 " + sel.Left + "," + sel.Top
                    + "    屏幕 " + ClientSize.Width + "×" + ClientSize.Height;
                Size ts = TextRenderer.MeasureText(text, Theme.UiFont(9f, FontStyle.Regular));
                using (SolidBrush bb = new SolidBrush(Color.FromArgb(200, 0, 0, 0)))
                    g.FillRectangle(bb, sel.Left, Math.Max(vs.Top, sel.Top - 26), ts.Width + 16, 24);
                g.DrawString(text, Theme.UiFont(9f, FontStyle.Regular), Brushes.White, sel.Left + 8, Math.Max(vs.Top, sel.Top - 24));
            }

            if (_selecting)
            {
                using (Pen p = new Pen(Color.FromArgb(120, 255, 255, 255), 1f))
                {
                    g.DrawLine(p, _current.X, 0, _current.X, ClientSize.Height);
                    g.DrawLine(p, 0, _current.Y, ClientSize.Width, _current.Y);
                }
            }

            string hint = "拖动鼠标框选区域，松开完成；按 ESC 取消";
            using (SolidBrush bb = new SolidBrush(Color.FromArgb(190, 0, 0, 0)))
                g.FillRectangle(bb, 16, 16, 360, 30);
            g.DrawString(hint, Theme.UiFont(9.5f, FontStyle.Regular), Brushes.White, 26, 23);
        }

        private static Rectangle Normalize(Point a, Point b)
        {
            return Rectangle.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                _start = e.Location;
                _current = e.Location;
                _selecting = true;
                Invalidate();
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_selecting)
            {
                _current = e.Location;
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!_selecting) return;
            _selecting = false;
            Rectangle local = Normalize(_start, e.Location);
            if (local.Width < 4 || local.Height < 4)
            {
                Invalidate();
                return;
            }
            Rectangle vs = System.Windows.Forms.SystemInformation.VirtualScreen;
            Rectangle abs = new Rectangle(local.X + vs.X, local.Y + vs.Y, local.Width, local.Height);
            abs.Intersect(vs);
            if (abs.Width < 4 || abs.Height < 4)
            {
                Invalidate();
                return;
            }
            _selected = abs;
            DialogResult = DialogResult.OK;
            Close();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _screen != null) _screen.Dispose();
            base.Dispose(disposing);
        }
    }

    // ---------------------------- 模板库 ----------------------------

    public sealed class TemplateManagerForm : DarkDialog
    {
        private readonly ScriptDocument _doc;
        private readonly bool _pickMode;
        private readonly string _templatesDir;
        private readonly ListView _list;
        private readonly PictureBox _preview;
        private string _selectedTemplate = "";

        public TemplateManagerForm(ScriptDocument doc, bool pickMode)
        {
            _doc = doc;
            _pickMode = pickMode;
            string baseDir = string.IsNullOrEmpty(doc.FilePath) ? Environment.CurrentDirectory : doc.Directory;
            _templatesDir = Path.Combine(baseDir, "templates");
            if (!Directory.Exists(_templatesDir)) Directory.CreateDirectory(_templatesDir);
            Text = "模板管理" + (pickMode ? " - 选择模板" : "");
            Size = new Size(820, 560);
            MaximizeBox = false;
            MinimizeBox = false;

            _list = new ListView();
            _list.View = View.Details;
            _list.FullRowSelect = true;
            _list.MultiSelect = false;
            _list.Columns.Add("模板", 330);
            _list.Columns.Add("大小", 90);
            _list.Columns.Add("修改时间", 150);
            _list.Location = new Point(12, 12);
            _list.Size = new Size(470, 470);
            Theme.StyleListView(_list);
            _list.SelectedIndexChanged += delegate { ShowPreview(); };
            _list.DoubleClick += delegate
            {
                if (_pickMode) Accept();
                else ShowPreview();
            };
            Controls.Add(_list);

            _preview = new PictureBox();
            _preview.Location = new Point(496, 12);
            _preview.Size = new Size(300, 260);
            _preview.BackColor = Theme.PanelAlt;
            _preview.BorderStyle = BorderStyle.FixedSingle;
            _preview.SizeMode = PictureBoxSizeMode.CenterImage;
            Controls.Add(_preview);

            Label info = Ui.Label("提示：模板必须以相对路径保存，放在脚本目录的 templates 文件夹内，方便跨电脑迁移。");
            info.Location = new Point(496, 286);
            info.MaximumSize = new Size(300, 0);
            info.AutoSize = true;
            Controls.Add(info);

            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.Location = new Point(12, 490);
            buttons.Size = new Size(780, 40);
            buttons.FlowDirection = FlowDirection.LeftToRight;
            buttons.WrapContents = false;
            buttons.Controls.Add(Ui.Button("导入图片", delegate { ImportImage(); }));
            buttons.Controls.Add(Ui.Button("截图取图", delegate { CaptureTemplate(); }));
            buttons.Controls.Add(Ui.Button("新建分组", delegate { NewGroup(); }));
            buttons.Controls.Add(Ui.Button("重命名", delegate { Rename(); }));
            buttons.Controls.Add(Ui.Button("删除", delegate { DeleteTemplate(); }));
            buttons.Controls.Add(Ui.Button("刷新", delegate { Reload(); }));
            FlatButton ok = Ui.Button(_pickMode ? "使用此模板" : "关闭", delegate
            {
                if (_pickMode) Accept();
                else Close();
            });
            ok.BackColor = Theme.Accent;
            ok.ForeColor = Color.White;
            buttons.Controls.Add(ok);
            Controls.Add(buttons);
            AcceptButton = ok;

            Reload();
        }

        public string SelectedTemplate { get { return _selectedTemplate; } }

        private void Reload()
        {
            _list.Items.Clear();
            if (!Directory.Exists(_templatesDir)) return;
            string[] files = Directory.GetFiles(_templatesDir, "*.*", SearchOption.AllDirectories);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            foreach (string f in files)
            {
                string ext = Path.GetExtension(f).ToLowerInvariant();
                if (ext != ".png" && ext != ".jpg" && ext != ".jpeg" && ext != ".bmp") continue;
                string rel = f.Substring(_templatesDir.Length).TrimStart(Path.DirectorySeparatorChar);
                FileInfo fi = new FileInfo(f);
                ListViewItem it = new ListViewItem(rel);
                it.SubItems.Add(fi.Length < 1024 ? fi.Length + " B" : (fi.Length / 1024) + " KB");
                it.SubItems.Add(fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm"));
                it.Tag = f;
                _list.Items.Add(it);
            }
        }

        private string SelectedPath(out string relative)
        {
            relative = "";
            if (_list.SelectedItems.Count == 0) return null;
            string full = Convert.ToString(_list.SelectedItems[0].Tag);
            relative = "templates/" + _list.SelectedItems[0].Text.Replace('\\', '/');
            return full;
        }

        private void ShowPreview()
        {
            string rel;
            string full = SelectedPath(out rel);
            if (_preview.Image != null)
            {
                Image old = _preview.Image;
                _preview.Image = null;
                old.Dispose();
            }
            if (full == null) return;
            try
            {
                using (FileStream fs = File.OpenRead(full))
                using (Image img = Image.FromStream(fs))
                {
                    _preview.Image = new Bitmap(img);
                }
            }
            catch { }
        }

        private void Accept()
        {
            string rel;
            if (SelectedPath(out rel) == null)
            {
                MessageBox.Show(this, "请先选择一个模板图片", "BlockAuto", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            _selectedTemplate = rel;
            DialogResult = DialogResult.OK;
            Close();
        }

        private string CurrentGroup()
        {
            string rel;
            string full = SelectedPath(out rel);
            if (full == null) return _templatesDir;
            return Path.GetDirectoryName(full);
        }

        private void ImportImage()
        {
            OpenFileDialog dlg = new OpenFileDialog();
            dlg.Filter = "图片 (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|所有文件 (*.*)|*.*";
            dlg.Multiselect = true;
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            string dir = CurrentGroup();
            foreach (string f in dlg.FileNames)
            {
                try
                {
                    string target = Path.Combine(dir, Path.GetFileName(f));
                    if (File.Exists(target))
                        target = Path.Combine(dir, Path.GetFileNameWithoutExtension(f) + "_" + DateTime.Now.ToString("HHmmss") + Path.GetExtension(f));
                    File.Copy(f, target, true);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "导入失败：" + ex.Message, "BlockAuto", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            Reload();
        }

        private void CaptureTemplate()
        {
            Rectangle r = RoiPickerForm.Pick(this, "截取模板");
            if (r.Width < 4 || r.Height < 4) return;
            using (TextInputDialog name = new TextInputDialog("保存模板", "模板文件名", "template.png"))
            {
                if (name.ShowDialog(this) != DialogResult.OK) return;
                string fileName = name.Value;
                if (!fileName.ToLowerInvariant().EndsWith(".png")) fileName += ".png";
                try
                {
                    using (Bitmap bmp = ScreenCapture.Capture(r))
                    {
                        string target = Path.Combine(_templatesDir, fileName);
                        bmp.Save(target, System.Drawing.Imaging.ImageFormat.Png);
                    }
                    Reload();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "保存失败：" + ex.Message, "BlockAuto", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private void NewGroup()
        {
            using (TextInputDialog dlg = new TextInputDialog("新建分组", "分组名", "group1"))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                string dir = Path.Combine(_templatesDir, dlg.Value);
                try
                {
                    Directory.CreateDirectory(dir);
                    Reload();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "创建失败：" + ex.Message, "BlockAuto", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private void Rename()
        {
            string rel;
            string full = SelectedPath(out rel);
            if (full == null) return;
            using (TextInputDialog dlg = new TextInputDialog("重命名模板", "新文件名", Path.GetFileName(full)))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    string target = Path.Combine(Path.GetDirectoryName(full), dlg.Value);
                    if (!Path.HasExtension(target)) target += Path.GetExtension(full);
                    File.Move(full, target);
                    Reload();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "重命名失败：" + ex.Message, "BlockAuto", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private void DeleteTemplate()
        {
            string rel;
            string full = SelectedPath(out rel);
            if (full == null) return;
            if (MessageBox.Show(this, "确定删除模板 " + Path.GetFileName(full) + " ？", "BlockAuto",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try
            {
                File.Delete(full);
                Reload();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "删除失败：" + ex.Message, "BlockAuto", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }

    // ---------------------------- 设置 ----------------------------

    public sealed class SettingsForm : DarkDialog
    {
        private readonly AppConfig _config;
        private readonly NumericUpDown _threshold;
        private readonly NumericUpDown _delay;
        private readonly NumericUpDown _recursion;
        private readonly NumericUpDown _retention;
        private readonly NumericUpDown _cache;
        private readonly TextBox _logDir;
        private readonly CheckBox _enableAi;
        private readonly NumericUpDown _aiPort;
        private readonly TextBox _aiToken;
        private readonly CheckBox _whitelist;
        private readonly TextBox _allowed;
        private readonly CheckBox _confirm;
        private readonly NumericUpDown _matchScale;
        private readonly TextBox _updateUrl;
        private readonly TextBox _cloudRepo;
        private readonly CheckBox _uploadLogs;

        public SettingsForm(AppConfig config)
        {
            _config = config;
            Text = "全局设置";
            Size = new Size(600, 720);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;

            FlowLayoutPanel flow = new FlowLayoutPanel();
            flow.Dock = DockStyle.Fill;
            flow.FlowDirection = FlowDirection.TopDown;
            flow.WrapContents = false;
            flow.AutoScroll = true;
            flow.Padding = new Padding(16);
            flow.BackColor = Theme.Panel;
            Controls.Add(flow);

            flow.Controls.Add(Ui.Section("识别与动作"));
            _threshold = Num(0.05m, 1m, 0.05m, 2, (decimal)config.DefaultThreshold);
            flow.Controls.Add(Ui.Row(Ui.Label("默认识图阈值"), _threshold, 120));
            _delay = Num(0m, 5000m, 10m, 0, config.ActionDelayMs);
            flow.Controls.Add(Ui.Row(Ui.Label("动作后延时(ms)"), _delay, 120));
            _cache = Num(0m, 5000m, 50m, 0, config.ScreenshotCacheMs);
            flow.Controls.Add(Ui.Row(Ui.Label("截图缓存(ms)"), _cache, 120));
            _recursion = Num(1m, 1000m, 1m, 0, config.MaxRecursion);
            flow.Controls.Add(Ui.Row(Ui.Label("最大递归深度"), _recursion, 120));

            flow.Controls.Add(Ui.Section("日志"));
            _retention = Num(1m, 365m, 1m, 0, config.LogRetentionDays);
            flow.Controls.Add(Ui.Row(Ui.Label("日志保留天数"), _retention, 120));
            _logDir = Ui.TextBox(config.LogDirectory, false);
            flow.Controls.Add(Ui.Row(Ui.Label("日志目录(空=默认)"), _logDir, 300));

            flow.Controls.Add(Ui.Section("AI 控制接口（预留，重启生效）"));
            _enableAi = new CheckBox();
            _enableAi.Text = "启用 HTTP 接口";
            _enableAi.ForeColor = Theme.Text;
            _enableAi.Checked = config.EnableAi;
            flow.Controls.Add(Ui.Row(Ui.Label(""), _enableAi, 200));
            _aiPort = Num(1024m, 65535m, 1m, 0, config.AiPort);
            flow.Controls.Add(Ui.Row(Ui.Label("监听端口"), _aiPort, 120));
            _aiToken = Ui.TextBox(config.AiToken, false);
            flow.Controls.Add(Ui.Row(Ui.Label("访问 Token"), _aiToken, 300));
            _whitelist = new CheckBox();
            _whitelist.Text = "启用动作白名单";
            _whitelist.ForeColor = Theme.Text;
            _whitelist.Checked = config.ActionWhitelist;
            flow.Controls.Add(Ui.Row(Ui.Label(""), _whitelist, 200));
            _allowed = Ui.TextBox(string.Join(",", config.AllowedActions.ToArray()), false);
            flow.Controls.Add(Ui.Row(Ui.Label("白名单动作"), _allowed, 300));
            Label actionHint = Ui.Label("动作名用点号+驼峰，例如 cap.findImage；对应接口路径 /cap/findImage。"
                + "全部动作名可在 GET /health 的 actions 字段里查到，直接照抄即可，别按 URL 猜。");
            actionHint.ForeColor = Theme.TextDim;
            actionHint.MaximumSize = new Size(520, 0);
            flow.Controls.Add(actionHint);
            _confirm = new CheckBox();
            _confirm.Text = "高危动作需人工确认";
            _confirm.ForeColor = Theme.Text;
            _confirm.Checked = config.ConfirmDangerousActions;
            flow.Controls.Add(Ui.Row(Ui.Label(""), _confirm, 200));

            flow.Controls.Add(Ui.Section("识图适配"));
            _matchScale = Num(0m, 30m, 5m, 0, config.MatchScalePercent);
            flow.Controls.Add(Ui.Row(Ui.Label("缩放容差(%)"), _matchScale, 120));

            flow.Controls.Add(Ui.Section("联网（默认关闭，手动开启）"));
            _updateUrl = Ui.TextBox(config.UpdateCheckUrl, false);
            flow.Controls.Add(Ui.Row(Ui.Label("更新检查地址"), _updateUrl, 300));
            _cloudRepo = Ui.TextBox(config.CloudRepoUrl, false);
            flow.Controls.Add(Ui.Row(Ui.Label("云脚本库地址"), _cloudRepo, 300));
            _uploadLogs = new CheckBox();
            _uploadLogs.Text = "允许上传日志";
            _uploadLogs.ForeColor = Theme.Text;
            _uploadLogs.Checked = config.UploadLogs;
            flow.Controls.Add(Ui.Row(Ui.Label(""), _uploadLogs, 200));

            Label note = Ui.Label("注：AI 接口保存后立即重启；更新检查/云脚本库为手动触发，不会后台自动联网。");
            note.MaximumSize = new Size(520, 0);
            flow.Controls.Add(note);

            Panel bottom = new Panel();
            bottom.Dock = DockStyle.Bottom;
            bottom.Height = 54;
            FlatButton ok = Ui.Button("保存", delegate { Apply(); });
            ok.BackColor = Theme.Accent;
            ok.ForeColor = Color.White;
            ok.Location = new Point(380, 12);
            FlatButton cancel = Ui.Button("取消", delegate { DialogResult = DialogResult.Cancel; Close(); });
            cancel.Location = new Point(480, 12);
            bottom.Controls.Add(ok);
            bottom.Controls.Add(cancel);
            Controls.Add(bottom);
            AcceptButton = ok;
            CancelButton = cancel;
        }

        private static NumericUpDown Num(decimal min, decimal max, decimal step, int decimals, decimal value)
        {
            NumericUpDown n = new NumericUpDown();
            n.Minimum = min;
            n.Maximum = max;
            n.Increment = step;
            n.DecimalPlaces = decimals;
            n.Value = Math.Max(min, Math.Min(max, value));
            n.Width = 120;
            n.BackColor = Theme.PanelAlt;
            n.ForeColor = Theme.Text;
            n.BorderStyle = BorderStyle.FixedSingle;
            return n;
        }

        private void Apply()
        {
            _config.DefaultThreshold = (double)_threshold.Value;
            _config.ActionDelayMs = (int)_delay.Value;
            _config.ScreenshotCacheMs = (int)_cache.Value;
            _config.MaxRecursion = (int)_recursion.Value;
            _config.LogRetentionDays = (int)_retention.Value;
            _config.LogDirectory = _logDir.Text.Trim();
            _config.EnableAi = _enableAi.Checked;
            _config.AiPort = (int)_aiPort.Value;
            _config.AiToken = _aiToken.Text.Trim();
            _config.ActionWhitelist = _whitelist.Checked;
            _config.ConfirmDangerousActions = _confirm.Checked;
            _config.MatchScalePercent = (int)_matchScale.Value;
            _config.UpdateCheckUrl = _updateUrl.Text.Trim();
            _config.CloudRepoUrl = _cloudRepo.Text.Trim();
            _config.UploadLogs = _uploadLogs.Checked;
            List<string> allowed = new List<string>();
            foreach (string raw in _allowed.Text.Replace("，", ",").Split(','))
            {
                string a = raw.Trim();
                if (a.Length > 0 && !allowed.Contains(a)) allowed.Add(a);
            }
            _config.AllowedActions = allowed;
            _config.Save();
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    // ---------------------------- 热键 ----------------------------

    public sealed class HotkeyForm : DarkDialog
    {
        private readonly HotkeyManager _manager;
        private readonly ListView _list;

        public HotkeyForm(HotkeyManager manager)
        {
            _manager = manager;
            Text = "全局热键管理";
            Size = new Size(760, 480);
            MaximizeBox = false;
            MinimizeBox = false;

            _list = new ListView();
            _list.View = View.Details;
            _list.FullRowSelect = true;
            _list.MultiSelect = false;
            _list.Columns.Add("快捷键", 170);
            _list.Columns.Add("脚本", 380);
            _list.Columns.Add("状态", 120);
            _list.Location = new Point(14, 14);
            _list.Size = new Size(710, 350);
            Theme.StyleListView(_list);
            _list.DoubleClick += delegate { Edit(); };
            Controls.Add(_list);

            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.Location = new Point(14, 376);
            buttons.Size = new Size(710, 40);
            buttons.WrapContents = false;
            buttons.Controls.Add(Ui.Button("添加", delegate { Add(); }));
            buttons.Controls.Add(Ui.Button("编辑", delegate { Edit(); }));
            buttons.Controls.Add(Ui.Button("删除", delegate { Remove(); }));
            buttons.Controls.Add(Ui.Button("刷新注册", delegate { _manager.Refresh(); Reload(); }));
            FlatButton close = Ui.Button("关闭", delegate { Close(); });
            close.BackColor = Theme.Accent;
            close.ForeColor = Color.White;
            buttons.Controls.Add(close);
            Controls.Add(buttons);

            Label note = Ui.Label("热键全局生效：脚本运行中重复触发会被跳过并记录 WARN 日志。");
            note.Location = new Point(14, 420);
            Controls.Add(note);

            Reload();
        }

        private void Reload()
        {
            _list.Items.Clear();
            foreach (HotkeyBinding b in _manager.Bindings)
            {
                ListViewItem it = new ListViewItem(b.Keys);
                it.SubItems.Add(string.IsNullOrEmpty(b.ScriptPath) ? "（未选择）" : b.ScriptPath);
                it.SubItems.Add(string.IsNullOrEmpty(b.LastError) ? (b.Enabled ? "已启用" : "已禁用") : b.LastError);
                it.Tag = b;
                _list.Items.Add(it);
            }
        }

        private void Add()
        {
            using (HotkeyEditDialog dlg = new HotkeyEditDialog(null))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                HotkeyBinding b = new HotkeyBinding();
                b.Keys = dlg.KeysSpec;
                b.ScriptPath = dlg.ScriptPath;
                b.Enabled = true;
                string error;
                if (!_manager.Add(b, out error))
                {
                    MessageBox.Show(this, error, "BlockAuto", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                Reload();
            }
        }

        private void Edit()
        {
            if (_list.SelectedItems.Count == 0) return;
            HotkeyBinding old = (HotkeyBinding)_list.SelectedItems[0].Tag;
            using (HotkeyEditDialog dlg = new HotkeyEditDialog(old))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                HotkeyBinding b = new HotkeyBinding();
                b.Keys = dlg.KeysSpec;
                b.ScriptPath = dlg.ScriptPath;
                b.Enabled = true;
                _manager.ReplaceAt(_list.SelectedItems[0].Index, b);
                Reload();
            }
        }

        private void Remove()
        {
            if (_list.SelectedItems.Count == 0) return;
            _manager.RemoveAt(_list.SelectedItems[0].Index);
            Reload();
        }
    }

    public sealed class HotkeyEditDialog : DarkDialog
    {
        private readonly TextBox _keys;
        private readonly TextBox _script;

        public HotkeyEditDialog(HotkeyBinding existing)
        {
            Text = existing == null ? "添加热键" : "编辑热键";
            Size = new Size(520, 260);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;

            Label l1 = Ui.Label("快捷键（例如 Ctrl+Alt+F1）");
            l1.Location = new Point(16, 16);
            Controls.Add(l1);
            _keys = Ui.TextBox(existing != null ? existing.Keys : "Ctrl+Alt+F1", false);
            _keys.Location = new Point(16, 40);
            _keys.Width = 470;
            Controls.Add(_keys);

            Label l2 = Ui.Label("脚本路径");
            l2.Location = new Point(16, 78);
            Controls.Add(l2);
            _script = Ui.TextBox(existing != null ? existing.ScriptPath : "", false);
            _script.Location = new Point(16, 102);
            _script.Width = 370;
            Controls.Add(_script);
            FlatButton browse = Ui.Button("浏览…", delegate
            {
                OpenFileDialog dlg = new OpenFileDialog();
                dlg.Filter = "BlockAuto 脚本 (*.block)|*.block";
                if (dlg.ShowDialog(this) == DialogResult.OK) _script.Text = dlg.FileName;
            });
            browse.Location = new Point(394, 102);
            browse.Width = 90;
            Controls.Add(browse);

            FlatButton ok = Ui.Button("确定", delegate { Apply(); });
            ok.BackColor = Theme.Accent;
            ok.ForeColor = Color.White;
            ok.Location = new Point(290, 168);
            FlatButton cancel = Ui.Button("取消", delegate { DialogResult = DialogResult.Cancel; Close(); });
            cancel.Location = new Point(388, 168);
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;
        }

        public string KeysSpec { get { return _keys.Text.Trim(); } }
        public string ScriptPath { get { return _script.Text.Trim(); } }

        private void Apply()
        {
            uint mods, vk;
            string error;
            if (!HotkeyParser.TryParse(KeysSpec, out mods, out vk, out error))
            {
                MessageBox.Show(this, error, "BlockAuto", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (ScriptPath.Length == 0 || !File.Exists(ScriptPath))
            {
                MessageBox.Show(this, "请选择一个存在的 .block 脚本", "BlockAuto", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    // ---------------------------- 定时任务 ----------------------------

    public sealed class ScheduleForm : DarkDialog
    {
        private readonly Scheduler _scheduler;
        private readonly ListView _list;

        public ScheduleForm(Scheduler scheduler)
        {
            _scheduler = scheduler;
            Text = "编辑器定时任务";
            Size = new Size(860, 520);
            MaximizeBox = false;
            MinimizeBox = false;

            _list = new ListView();
            _list.View = View.Details;
            _list.FullRowSelect = true;
            _list.MultiSelect = false;
            _list.Columns.Add("名称", 150);
            _list.Columns.Add("脚本", 230);
            _list.Columns.Add("计划", 160);
            _list.Columns.Add("下次执行", 150);
            _list.Columns.Add("状态", 100);
            _list.Location = new Point(14, 14);
            _list.Size = new Size(820, 380);
            Theme.StyleListView(_list);
            _list.DoubleClick += delegate { Edit(); };
            Controls.Add(_list);

            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.Location = new Point(14, 404);
            buttons.Size = new Size(820, 40);
            buttons.WrapContents = false;
            buttons.Controls.Add(Ui.Button("添加", delegate { Add(); }));
            buttons.Controls.Add(Ui.Button("编辑", delegate { Edit(); }));
            buttons.Controls.Add(Ui.Button("删除", delegate { Remove(); }));
            buttons.Controls.Add(Ui.Button("立即测试", delegate { TestRun(); }));
            FlatButton close = Ui.Button("关闭", delegate { Close(); });
            close.BackColor = Theme.Accent;
            close.ForeColor = Color.White;
            buttons.Controls.Add(close);
            Controls.Add(buttons);

            Label note = Ui.Label("定时任务依附主程序进程，关闭编辑器后全部失效；如需关机后仍能执行，请用 Windows 任务计划程序调用 BlockRunner.exe。");
            note.Location = new Point(14, 450);
            note.MaximumSize = new Size(820, 0);
            Controls.Add(note);

            Reload();
        }

        public event Action<ScheduledTask> TestRequested;

        private void Reload()
        {
            _list.Items.Clear();
            DateTime now = DateTime.Now;
            foreach (ScheduledTask t in _scheduler.Tasks)
            {
                ListViewItem it = new ListViewItem(t.Name);
                it.SubItems.Add(t.ScriptPath);
                it.SubItems.Add(t.ScheduleText);
                DateTime? next = t.ComputeNext(now);
                it.SubItems.Add(next.HasValue ? next.Value.ToString("MM-dd HH:mm:ss") : "—");
                it.SubItems.Add(t.Enabled ? "启用" : "禁用");
                it.Tag = t;
                _list.Items.Add(it);
            }
        }

        private void Add()
        {
            using (ScheduleEditDialog dlg = new ScheduleEditDialog(null))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                ScheduledTask t = new ScheduledTask();
                dlg.ApplyTo(t);
                _scheduler.Add(t);
                Reload();
            }
        }

        private void Edit()
        {
            if (_list.SelectedItems.Count == 0) return;
            ScheduledTask t = (ScheduledTask)_list.SelectedItems[0].Tag;
            using (ScheduleEditDialog dlg = new ScheduleEditDialog(t))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                dlg.ApplyTo(t);
                _scheduler.Save();
                Reload();
            }
        }

        private void Remove()
        {
            if (_list.SelectedItems.Count == 0) return;
            ScheduledTask t = (ScheduledTask)_list.SelectedItems[0].Tag;
            _scheduler.Remove(t);
            Reload();
        }

        private void TestRun()
        {
            if (_list.SelectedItems.Count == 0) return;
            ScheduledTask t = (ScheduledTask)_list.SelectedItems[0].Tag;
            Action<ScheduledTask> h = TestRequested;
            if (h != null) h(t);
        }
    }

    public sealed class ScheduleEditDialog : DarkDialog
    {
        private readonly TextBox _name;
        private readonly TextBox _script;
        private readonly ComboBox _type;
        private readonly DateTimePicker _once;
        private readonly NumericUpDown _interval;
        private readonly TextBox _time;
        private readonly CheckBox[] _days;
        private readonly CheckBox _enabled;

        public ScheduleEditDialog(ScheduledTask existing)
        {
            Text = existing == null ? "添加定时任务" : "编辑定时任务";
            Size = new Size(560, 470);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;

            Label l1 = Ui.Label("任务名称");
            l1.Location = new Point(16, 16);
            Controls.Add(l1);
            _name = Ui.TextBox(existing != null ? existing.Name : "定时任务", false);
            _name.Location = new Point(16, 38);
            _name.Width = 500;
            Controls.Add(_name);

            Label l2 = Ui.Label("脚本");
            l2.Location = new Point(16, 72);
            Controls.Add(l2);
            _script = Ui.TextBox(existing != null ? existing.ScriptPath : "", false);
            _script.Location = new Point(16, 94);
            _script.Width = 400;
            Controls.Add(_script);
            FlatButton browse = Ui.Button("浏览…", delegate
            {
                OpenFileDialog dlg = new OpenFileDialog();
                dlg.Filter = "BlockAuto 脚本 (*.block)|*.block";
                if (dlg.ShowDialog(this) == DialogResult.OK) _script.Text = dlg.FileName;
            });
            browse.Location = new Point(424, 94);
            browse.Width = 92;
            Controls.Add(browse);

            Label l3 = Ui.Label("触发方式");
            l3.Location = new Point(16, 132);
            Controls.Add(l3);
            _type = new ComboBox();
            _type.DropDownStyle = ComboBoxStyle.DropDownList;
            Theme.ApplyDarkCombo(_type);
            _type.Items.Add("一次性");
            _type.Items.Add("间隔周期");
            _type.Items.Add("每日定时");
            _type.Items.Add("每周定时");
            _type.Location = new Point(110, 130);
            _type.Width = 160;
            _type.SelectedIndexChanged += delegate { UpdateEnabled(); };
            Controls.Add(_type);

            Label l4 = Ui.Label("一次性时间");
            l4.Location = new Point(16, 172);
            Controls.Add(l4);
            _once = new DateTimePicker();
            _once.Format = DateTimePickerFormat.Custom;
            _once.CustomFormat = "yyyy-MM-dd HH:mm";
            _once.Location = new Point(110, 170);
            _once.Width = 200;
            Controls.Add(_once);

            Label l5 = Ui.Label("间隔分钟");
            l5.Location = new Point(330, 172);
            Controls.Add(l5);
            _interval = new NumericUpDown();
            _interval.Minimum = 1;
            _interval.Maximum = 100000;
            _interval.Value = 60;
            _interval.Location = new Point(410, 170);
            _interval.Width = 90;
            _interval.BackColor = Theme.PanelAlt;
            _interval.ForeColor = Theme.Text;
            Controls.Add(_interval);

            Label l6 = Ui.Label("时间 (HH:mm)");
            l6.Location = new Point(16, 208);
            Controls.Add(l6);
            _time = Ui.TextBox("09:00", false);
            _time.Location = new Point(110, 206);
            _time.Width = 90;
            Controls.Add(_time);

            Label l7 = Ui.Label("星期");
            l7.Location = new Point(216, 208);
            Controls.Add(l7);
            _days = new CheckBox[7];
            string[] names = { "一", "二", "三", "四", "五", "六", "日" };
            for (int i = 0; i < 7; i++)
            {
                CheckBox c = new CheckBox();
                c.Text = names[i];
                c.ForeColor = Theme.Text;
                c.Width = 40;
                c.Location = new Point(256 + i * 36, 207);
                c.Checked = i < 5;
                Controls.Add(c);
                _days[i] = c;
            }

            _enabled = new CheckBox();
            _enabled.Text = "启用";
            _enabled.ForeColor = Theme.Text;
            _enabled.Checked = existing == null || existing.Enabled;
            _enabled.Location = new Point(16, 248);
            _enabled.Width = 120;
            Controls.Add(_enabled);

            Label note = Ui.Label("说明：编辑器定时是「到点新启动一次脚本」，与脚本内部的 while+sleep 循环不是一回事。");
            note.Location = new Point(16, 288);
            note.MaximumSize = new Size(500, 0);
            Controls.Add(note);

            FlatButton ok = Ui.Button("确定", delegate { ApplyAndClose(); });
            ok.BackColor = Theme.Accent;
            ok.ForeColor = Color.White;
            ok.Location = new Point(330, 380);
            FlatButton cancel = Ui.Button("取消", delegate { DialogResult = DialogResult.Cancel; Close(); });
            cancel.Location = new Point(428, 380);
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;

            if (existing != null)
            {
                _name.Text = existing.Name;
                _script.Text = existing.ScriptPath;
                _type.SelectedIndex = (int)existing.Type;
                _once.Value = existing.OnceAt;
                _interval.Value = Math.Max(1, Math.Min(100000, existing.IntervalMinutes));
                _time.Text = existing.TimeOfDay;
                for (int i = 0; i < 7; i++)
                {
                    bool on = false;
                    foreach (int d in existing.Weekdays) if (d == i + 1) on = true;
                    _days[i].Checked = on;
                }
                _enabled.Checked = existing.Enabled;
            }
            else
            {
                _type.SelectedIndex = 2;
            }
            UpdateEnabled();
        }

        private void UpdateEnabled()
        {
            int t = _type.SelectedIndex;
            _once.Enabled = t == 0;
            _interval.Enabled = t == 1;
            _time.Enabled = t >= 2;
            foreach (CheckBox c in _days) c.Enabled = t == 3;
        }

        private void ApplyAndClose()
        {
            if (_script.Text.Trim().Length == 0 || !File.Exists(_script.Text.Trim()))
            {
                MessageBox.Show(this, "请选择存在的 .block 脚本", "BlockAuto", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            TimeSpan ts;
            if (_type.SelectedIndex >= 2 && !TimeSpan.TryParseExact(_time.Text.Trim(), "hh\\:mm", CultureInfo.InvariantCulture, out ts))
            {
                MessageBox.Show(this, "时间格式应为 HH:mm，例如 09:30", "BlockAuto", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        }

        public void ApplyTo(ScheduledTask t)
        {
            t.Name = _name.Text.Trim();
            t.ScriptPath = _script.Text.Trim();
            t.Type = (ScheduleType)_type.SelectedIndex;
            t.OnceAt = _once.Value;
            t.IntervalMinutes = (int)_interval.Value;
            t.TimeOfDay = _time.Text.Trim();
            List<int> days = new List<int>();
            for (int i = 0; i < 7; i++) if (_days[i].Checked) days.Add(i + 1);
            t.Weekdays = days.Count == 0 ? new int[] { 1 } : days.ToArray();
            t.Enabled = _enabled.Checked;
        }
    }
}

