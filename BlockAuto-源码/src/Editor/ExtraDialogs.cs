using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace BlockAuto
{
    /// <summary>操作录制窗口：开始/暂停/停止录制，实时预览录制步骤，结束后转成积木插入画布。</summary>
    public sealed class RecorderForm : DarkDialog
    {
        private readonly ScriptDocument _doc;
        private readonly List<Block> _target;
        private readonly InputRecorder _recorder = new InputRecorder();
        private readonly ListBox _list;
        private readonly Label _status;
        private readonly FlatButton _start;
        private readonly FlatButton _pause;
        private readonly FlatButton _stop;
        private readonly CheckBox _withDelay;
        private readonly NumericUpDown _delay;
        private readonly CheckBox _includeMove;
        private readonly Timer _refresh;

        public int AppliedCount { get; private set; }

        public RecorderForm(ScriptDocument doc, List<Block> target)
        {
            _doc = doc;
            _target = target;
            Text = "操作录制";
            Size = new Size(720, 560);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;

            Label title = new Label();
            title.Text = "录制键鼠操作 → 自动生成积木";
            title.ForeColor = Theme.Text;
            title.Font = Theme.UiFont(12f, FontStyle.Bold);
            title.Location = new Point(16, 14);
            title.AutoSize = true;
            Controls.Add(title);

            Label hint = Ui.Label("支持录制：鼠标点击 / 双击 / 右键 / 滚轮 / 移动，以及键盘输入和快捷键（例如 Ctrl+C、F5）。"
                + "只记录键鼠时序，不识别画面、不自动加判断循环；坐标是屏幕绝对坐标，换分辨率会错位，跨机器请改用识图积木。");
            hint.Location = new Point(16, 44);
            hint.MaximumSize = new Size(670, 0);
            hint.AutoSize = true;
            Controls.Add(hint);

            _status = new Label();
            _status.Text = "未开始";
            _status.ForeColor = Theme.TextDim;
            _status.Font = Theme.UiFont(9.5f, FontStyle.Bold);
            _status.Location = new Point(16, 92);
            _status.AutoSize = true;
            Controls.Add(_status);

            _list = new ListBox();
            _list.Location = new Point(16, 116);
            _list.Size = new Size(676, 250);
            _list.BackColor = Theme.PanelAlt;
            _list.ForeColor = Theme.Text;
            _list.BorderStyle = BorderStyle.FixedSingle;
            _list.Font = Theme.MonoFont(9f, FontStyle.Regular);
            _list.HorizontalScrollbar = true;
            Controls.Add(_list);

            _start = Ui.Button("开始录制", delegate { StartRecording(); });
            _start.Width = 110;
            _start.Location = new Point(16, 378);
            _pause = Ui.Button("暂停", delegate { TogglePause(); });
            _pause.Width = 80;
            _pause.Location = new Point(132, 378);
            _pause.Enabled = false;
            _stop = Ui.Button("停止录制", delegate { StopRecording(); });
            _stop.Width = 80;
            _stop.Location = new Point(218, 378);
            _stop.Enabled = false;
            Controls.Add(_start);
            Controls.Add(_pause);
            Controls.Add(_stop);

            _withDelay = new CheckBox();
            _withDelay.Text = "每个动作后加延时";
            _withDelay.ForeColor = Theme.Text;
            _withDelay.Checked = true;
            _withDelay.Location = new Point(320, 380);
            _withDelay.Width = 150;
            Controls.Add(_withDelay);
            _delay = new NumericUpDown();
            _delay.Minimum = 0;
            _delay.Maximum = 10000;
            _delay.Increment = 50;
            _delay.Value = 200;
            _delay.Location = new Point(470, 380);
            _delay.Width = 80;
            _delay.BackColor = Theme.PanelAlt;
            _delay.ForeColor = Theme.Text;
            _delay.BorderStyle = BorderStyle.FixedSingle;
            Controls.Add(_delay);
            Label ms = Ui.Label("ms");
            ms.Location = new Point(556, 384);
            Controls.Add(ms);

            _includeMove = new CheckBox();
            _includeMove.Text = "包含鼠标移动";
            _includeMove.ForeColor = Theme.Text;
            _includeMove.Location = new Point(320, 410);
            _includeMove.Width = 150;
            Controls.Add(_includeMove);

            FlatButton removeLast = Ui.Button("删除上一步", delegate { _recorder.RemoveLast(); RefreshList(); });
            removeLast.Location = new Point(470, 404);
            removeLast.Width = 90;
            Controls.Add(removeLast);
            FlatButton clear = Ui.Button("清空录制", delegate { _recorder.Clear(); });
            clear.Location = new Point(566, 404);
            clear.Width = 90;
            Controls.Add(clear);

            Label note = Ui.Label("提示：开始录制后本窗口不会拦截你的操作，鼠标和键盘都会被全局记录（键盘动作会以「输入文本 / 按键」显示在列表里，"
                + "录制中实时刷新）；按「停止」或本窗口的停止按钮结束。");
            note.Location = new Point(16, 442);
            note.MaximumSize = new Size(670, 0);
            note.AutoSize = true;
            Controls.Add(note);

            FlatButton ok = Ui.Button("插入到画布", delegate { ApplyRecording(); });
            ok.Width = 110;
            ok.BackColor = Theme.Accent;
            ok.ForeColor = Color.White;
            ok.Location = new Point(472, 486);
            ok.Enabled = false;
            Controls.Add(ok);
            FlatButton cancel = Ui.Button("关闭", delegate { Close(); });
            cancel.Location = new Point(592, 486);
            Controls.Add(cancel);
            _ok = ok;

            _elapsedTimer = new Timer();
            _elapsedTimer.Interval = 500;
            _elapsedTimer.Tick += delegate { UpdateElapsed(); };
            _elapsedTimer.Start();
            _recorder.Changed += delegate { RefreshList(); };
            _refresh = new Timer();
            _refresh.Interval = 300;
            _refresh.Tick += delegate { RefreshList(); };
            _refresh.Start();

            FormClosing += delegate
            {
                _refresh.Stop();
                try { _recorder.Stop(); } catch { }
                _recorder.Dispose();
            };
        }

        private readonly FlatButton _ok;
        private readonly Timer _elapsedTimer;
        private DateTime _recordStarted;

        private void UpdateElapsed()
        {
            if (!_recorder.IsRecording) return;
            TimeSpan ts = DateTime.Now - _recordStarted;
            int sec = (int)ts.TotalSeconds;
            string state = _recorder.IsPaused ? "已暂停" : "录制中";
            // 状态行同时显示步数与耗时，录制过程中每 0.5 秒刷新
            if (_status != null && !_status.IsDisposed)
                _status.Text = state + " · 已录 " + _recorder.Count + " 步（键盘 " + _recorder.KeyActionCount
                    + " 步）· " + sec + " 秒";
        }

        private void StartRecording()
        {
            try
            {
                _recorder.DelayMs = (int)_delay.Value;
                _recorder.Start();
                _recordStarted = DateTime.Now;
                _start.Enabled = false;
                _pause.Enabled = true;
                _stop.Enabled = true;
                _pause.Text = "暂停";
                _status.Text = "录制中…";
                _status.ForeColor = Theme.Danger;
                _ok.Enabled = false;
                RefreshList();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "无法开始录制：" + ex.Message, "BlockAuto", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void TogglePause()
        {
            if (!_recorder.IsRecording) return;
            if (_recorder.IsPaused)
            {
                _recorder.Resume();
                _pause.Text = "暂停";
                _status.Text = "录制中…";
                _status.ForeColor = Theme.Danger;
            }
            else
            {
                _recorder.Pause();
                _pause.Text = "继续";
                _status.Text = "已暂停（点“继续”恢复）";
                _status.ForeColor = Theme.Warning;
            }
        }

        private void StopRecording()
        {
            if (_recorder.IsRecording) _recorder.Stop();
            _start.Enabled = true;
            _pause.Enabled = false;
            _stop.Enabled = false;
            _status.Text = "已停止，共 " + _recorder.Count + " 步";
            _status.ForeColor = Theme.Text;
            _ok.Enabled = _recorder.Count > 0;
            RefreshList();
        }

        private void RefreshList()
        {
            int count = _recorder.Count;
            int top = _list.TopIndex;
            _list.BeginUpdate();
            _list.Items.Clear();
            int i = 1;
            foreach (RecordedAction a in _recorder.Actions)
            {
                if (a.Kind == RecordedKind.MouseMove && !_includeMove.Checked) { i++; continue; }
                _list.Items.Add(i + ". " + a.Describe());
                i++;
            }
            _list.EndUpdate();
            if (top < _list.Items.Count) _list.TopIndex = top;
            if (_recorder.IsRecording) _status.Text = (_pause.Text == "继续" ? "已暂停（点“继续”恢复）" : "录制中…") + "  已记录 " + count + " 步";
        }

        private void ApplyRecording()
        {
            if (_recorder.IsRecording) _recorder.Stop();
            List<RecordedAction> src = _recorder.Actions;
            if (src.Count == 0) return;

            // 允许过滤掉纯移动
            List<RecordedAction> effective = new List<RecordedAction>();
            foreach (RecordedAction a in src)
            {
                if (a.Kind == RecordedKind.MouseMove && !_includeMove.Checked) continue;
                effective.Add(a);
            }
            if (effective.Count == 0)
            {
                MessageBox.Show(this, "没有可插入的动作（可勾选“包含鼠标移动”）。", "BlockAuto", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            int skipped;
            List<Block> blocks = BuildFromActions(effective, _withDelay.Checked, (int)_delay.Value, out skipped);
            _doc.AssignNewIds(blocks);   // 批量创建时 id 会重复，必须先重新编号
            foreach (Block b in blocks) _target.Add(b);
            AppliedCount = blocks.Count;
            DialogResult = DialogResult.OK;
            Close();
        }

        private List<Block> BuildFromActions(List<RecordedAction> actions, bool withDelay, int delayMs, out int skipped)
        {
            skipped = 0;
            List<Block> result = new List<Block>();
            foreach (RecordedAction a in actions)
            {
                Block b = null;
                switch (a.Kind)
                {
                    case RecordedKind.MouseClick:
                    case RecordedKind.MouseDoubleClick:
                    case RecordedKind.MouseRightClick:
                        b = _doc.NewBlock("mouse_click");
                        b.Params["button"] = a.Kind == RecordedKind.MouseRightClick ? "right" : "left";
                        b.Params["action"] = a.Kind == RecordedKind.MouseDoubleClick ? "dblclick" : "click";
                        b.Params["offset"] = new List<object> { 0d, 0d };
                        // 绝对坐标通过参数 x/y 提供，并额外写出注释便于排查
                        b.Params["x"] = (double)a.X;
                        b.Params["y"] = (double)a.Y;
                        b.Comment = "录制坐标(" + a.X + "," + a.Y + ")";
                        break;
                    case RecordedKind.MouseWheel:
                        b = _doc.NewBlock("mouse_wheel");
                        b.Params["dir"] = a.WheelDelta > 0 ? "up" : "down";
                        b.Params["amount"] = (double)Math.Max(1, Math.Abs(a.WheelDelta) / 120);
                        b.Comment = "录制";
                        break;
                    case RecordedKind.MouseMove:
                        b = _doc.NewBlock("mouse_move");
                        b.Params["x"] = (double)a.X;
                        b.Params["y"] = (double)a.Y;
                        b.Comment = "录制";
                        break;
                    case RecordedKind.KeyText:
                        b = _doc.NewBlock("key_type");
                        b.Params["text"] = a.Text;
                        b.Comment = "录制";
                        break;
                    case RecordedKind.KeyPress:
                        b = _doc.NewBlock("key_press");
                        b.Params["key"] = a.Key;
                        b.Comment = "录制";
                        break;
                }
                if (b == null) { skipped++; continue; }
                result.Add(b);
                if (withDelay && delayMs > 0)
                {
                    Block d = _doc.NewBlock("sleep");
                    d.Params["seconds"] = Math.Round(delayMs / 1000.0, 3);
                    d.Comment = "录制延时";
                    result.Add(d);
                }
            }
            return result;
        }
    }

    /// <summary>脚本打包窗口：生成 zip 分发包。</summary>
    public sealed class ScriptPackDialog : DarkDialog
    {
        private readonly ScriptDocument _doc;
        private readonly string _runnerPath;
        private readonly TextBox _output;
        private readonly TextBox _preview;
        private readonly CheckBox _withRunner;
        private readonly CheckBox _silentRun;

        public ScriptPackDialog(ScriptDocument doc, string runnerPath)
        {
            _doc = doc;
            _runnerPath = runnerPath;
            Text = "打包脚本";
            Size = new Size(640, 430);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;

            Label title = new Label();
            title.Text = "把 .block 与模板打包给他人运行";
            title.ForeColor = Theme.Text;
            title.Font = Theme.UiFont(12f, FontStyle.Bold);
            title.Location = new Point(16, 14);
            title.AutoSize = true;
            Controls.Add(title);

            Label l1 = Ui.Label("输出 zip");
            l1.Location = new Point(16, 52);
            Controls.Add(l1);
            _output = Ui.TextBox(DefaultZipPath(), false);
            _output.Location = new Point(16, 74);
            _output.Width = 490;
            Controls.Add(_output);
            FlatButton browse = Ui.Button("浏览…", delegate
            {
                SaveFileDialog dlg = new SaveFileDialog();
                dlg.Filter = "压缩包 (*.zip)|*.zip";
                dlg.FileName = Path.GetFileName(_output.Text);
                if (dlg.ShowDialog(this) == DialogResult.OK) _output.Text = dlg.FileName;
            });
            browse.Location = new Point(514, 74);
            Controls.Add(browse);

            _withRunner = new CheckBox();
            _withRunner.Text = "包含 BlockRunner.exe（接收方无需安装编辑器）";
            _withRunner.ForeColor = Theme.Text;
            _withRunner.Checked = File.Exists(_runnerPath);
            _withRunner.Location = new Point(16, 110);
            _withRunner.Width = 520;
            Controls.Add(_withRunner);

            _silentRun = new CheckBox();
            _silentRun.Text = "双击运行时静默（不弹黑窗口，推荐）";
            _silentRun.ForeColor = Theme.Text;
            _silentRun.Checked = true;
            _silentRun.Location = new Point(316, 110);
            _silentRun.Width = 300;
            Controls.Add(_silentRun);

            Label l2 = Ui.Label("将包含以下内容：");
            l2.Location = new Point(16, 142);
            Controls.Add(l2);
            _preview = Ui.TextBox("", true);
            _preview.Location = new Point(16, 166);
            _preview.Height = 150;
            _preview.Width = 600;
            _preview.ReadOnly = true;
            _preview.ScrollBars = ScrollBars.Vertical;
            Controls.Add(_preview);
            FillPreview();

            FlatButton ok = Ui.Button("开始打包", delegate { DoPack(); });
            ok.Width = 100;
            ok.BackColor = Theme.Accent;
            ok.ForeColor = Color.White;
            ok.Location = new Point(410, 336);
            Controls.Add(ok);
            FlatButton cancel = Ui.Button("取消", delegate { Close(); });
            cancel.Location = new Point(518, 336);
            Controls.Add(cancel);
        }

        private string DefaultZipPath()
        {
            string dir = string.IsNullOrEmpty(_doc.FilePath) ? Environment.CurrentDirectory : Path.GetDirectoryName(_doc.FilePath);
            string name = string.IsNullOrEmpty(_doc.FilePath) ? "script" : Path.GetFileNameWithoutExtension(_doc.FilePath);
            return Path.Combine(dir, name + "-打包.zip");
        }

        private void FillPreview()
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine("· " + (string.IsNullOrEmpty(_doc.FilePath) ? "script.block" : Path.GetFileName(_doc.FilePath)));
            List<string> tpls = ScriptPackager.CollectTemplates(_doc);
            foreach (string t in tpls)
            {
                string full = _doc.ResolveTemplatePath(t);
                sb.AppendLine("· " + t + (full != null && File.Exists(full) ? "" : "   （缺失）"));
            }
            if (tpls.Count == 0) sb.AppendLine("· （脚本未使用识图模板）");
            sb.AppendLine("· BlockRunner.exe" + (_withRunner.Checked ? "" : "（未勾选）"));
            sb.AppendLine("· 双击运行.cmd / 使用说明.txt");
            _preview.Text = sb.ToString();
            _withRunner.CheckedChanged += delegate { FillPreview(); };
        }

        private void DoPack()
        {
            try
            {
                ScriptPackager.PackResult r = ScriptPackager.Pack(_doc.FilePath, _runnerPath, _output.Text.Trim(), _withRunner.Checked, _silentRun.Checked);
                string msg = "打包完成：" + r.ZipPath + "\n模板 " + r.TemplateCount + " 个，大小 " + Math.Round(r.SizeBytes / 1024.0, 1) + " KB";
                if (r.Warnings.Count > 0) msg += "\n\n注意：\n" + string.Join("\n", r.Warnings.ToArray());
                MessageBox.Show(this, msg, "BlockAuto", MessageBoxButtons.OK, MessageBoxIcon.Information);
                string dir = Path.GetDirectoryName(r.ZipPath);
                try { Process.Start("explorer.exe", "\"" + dir + "\""); } catch { }
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "打包失败：" + ex.Message, "BlockAuto", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}