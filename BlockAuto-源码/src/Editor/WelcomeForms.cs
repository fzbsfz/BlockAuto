using System;
using System.Drawing;
using System.Windows.Forms;

namespace BlockAuto
{
    /// <summary>记录鼠标坐标：倒计时结束后取当前光标位置，F2 立即记录，ESC 取消。</summary>
    public sealed class CoordPickerForm : DarkDialog
    {
        private readonly Timer _timer;
        private readonly Label _count;
        private readonly Label _hint;
        private int _remainMs = 3000;
        private Point _picked;

        public CoordPickerForm()
        {
            Text = "记录鼠标位置";
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(380, 150);
            Rectangle screen = Screen.FromPoint(Cursor.Position).WorkingArea;
            Location = new Point(screen.Left + (screen.Width - Width) / 2, screen.Top + 60);

            Label title = new Label();
            title.Text = "把鼠标移到目标位置";
            title.ForeColor = Theme.Text;
            title.Font = Theme.UiFont(11f, FontStyle.Bold);
            title.Location = new Point(16, 14);
            title.AutoSize = true;
            Controls.Add(title);

            _count = new Label();
            _count.Text = "3 秒后自动记录…";
            _count.ForeColor = Theme.Warning;
            _count.Font = Theme.UiFont(10f, FontStyle.Regular);
            _count.Location = new Point(16, 42);
            _count.AutoSize = true;
            Controls.Add(_count);

            _hint = new Label();
            _hint.Text = "按 F2 立即记录，按 ESC 取消";
            _hint.ForeColor = Theme.TextDim;
            _hint.Font = Theme.UiFont(8.5f, FontStyle.Regular);
            _hint.Location = new Point(16, 66);
            _hint.AutoSize = true;
            Controls.Add(_hint);

            FlatButton now = Ui.Button("立即记录", delegate { CapturePoint(); });
            now.Width = 90;
            now.Location = new Point(160, 92);
            Controls.Add(now);
            FlatButton cancel = Ui.Button("取消", delegate { DialogResult = DialogResult.Cancel; Close(); });
            cancel.Width = 80;
            cancel.Location = new Point(258, 92);
            Controls.Add(cancel);

            _timer = new Timer();
            _timer.Interval = 100;
            _timer.Tick += delegate { Tick(); };
            _timer.Start();
            Shown += delegate { Activate(); };
            FormClosing += delegate { _timer.Stop(); };
        }

        public Point PickedPoint { get { return _picked; } }

        private void Tick()
        {
            if ((Win32.GetAsyncKeyState(0x71) & 0x8000) != 0) // F2
            {
                CapturePoint();
                return;
            }
            if ((Win32.GetAsyncKeyState(0x1B) & 0x8000) != 0) // ESC
            {
                _timer.Stop();
                DialogResult = DialogResult.Cancel;
                Close();
                return;
            }
            _remainMs -= _timer.Interval;
            if (_remainMs <= 0)
            {
                CapturePoint();
                return;
            }
            _count.Text = ((_remainMs + 999) / 1000) + " 秒后自动记录（当前位置 " + Cursor.Position.X + ", " + Cursor.Position.Y + "）";
        }

        private void CapturePoint()
        {
            _timer.Stop();
            _picked = Cursor.Position;
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    /// <summary>首次打开的三步引导。</summary>
    public sealed class WelcomeForm : DarkDialog
    {
        private readonly CheckBox _dontShow;

        public WelcomeForm(AppConfig config)
        {
            Text = "欢迎使用 BlockAuto";
            Size = new Size(560, 400);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;

            Label title = new Label();
            title.Text = "三步把手动操作变成能重复执行的脚本";
            title.ForeColor = Theme.Text;
            title.Font = Theme.UiFont(13f, FontStyle.Bold);
            title.Location = new Point(20, 18);
            title.AutoSize = true;
            Controls.Add(title);

            string[] steps = new string[]
            {
                "① 点左上角【● 录制】，手动做一遍要重复的操作，再点【停止】",
                "② 停止后会自动生成鼠标/键盘积木，出现在画布上",
                "③ 点【▶ 运行】即可重放；想加判断循环，再从左侧拖积木"
            };
            int y = 64;
            foreach (string s in steps)
            {
                Label l = new Label();
                l.Text = s;
                l.ForeColor = Theme.Text;
                l.Font = Theme.UiFont(10f, FontStyle.Regular);
                l.Location = new Point(24, y);
                l.MaximumSize = new Size(500, 0);
                l.AutoSize = true;
                Controls.Add(l);
                y += 52;
            }

            Label tip = new Label();
            tip.Text = "提示：鼠标点击积木上的「◎ 记录位置」，可随时按 F2 抓取当前光标坐标，不用手填数字。";
            tip.ForeColor = Theme.TextDim;
            tip.Font = Theme.UiFont(9f, FontStyle.Regular);
            tip.Location = new Point(24, y + 6);
            tip.MaximumSize = new Size(500, 0);
            tip.AutoSize = true;
            Controls.Add(tip);

            _dontShow = new CheckBox();
            _dontShow.Text = "以后不再显示";
            _dontShow.ForeColor = Theme.TextDim;
            _dontShow.Checked = true;
            _dontShow.Location = new Point(24, 300);
            _dontShow.Width = 180;
            Controls.Add(_dontShow);

            FlatButton ok = Ui.Button("开始使用", delegate { Apply(); });
            ok.Width = 100;
            ok.BackColor = Theme.Accent;
            ok.ForeColor = Color.White;
            ok.Location = new Point(432, 318);
            Controls.Add(ok);
            AcceptButton = ok;
        }

        private void Apply()
        {
            if (_dontShow.Checked)
            {
                // 由主窗体统一保存 FirstRunDone
            }
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}