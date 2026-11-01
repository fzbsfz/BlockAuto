using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BlockAuto
{
    /// <summary>联网功能窗口：版本更新检测 / 云端脚本库 / 日志上报（全部手动触发）。</summary>
    public sealed class NetworkForm : DarkDialog
    {
        private readonly AppConfig _config;
        private readonly Action<string> _openScript;
        private readonly Label _updateStatus;
        private readonly ListBox _cloudList;
        private readonly Label _cloudStatus;
        private readonly TextBox _logBox;
        private readonly List<NetworkService.CloudScript> _scripts = new List<NetworkService.CloudScript>();

        public NetworkForm(AppConfig config, Action<string> openScript)
        {
            _config = config;
            _openScript = openScript;
            Text = "联网功能（手动触发，默认关闭）";
            Size = new Size(760, 620);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;

            Label note = Ui.Label("本页所有请求仅在点击按钮时发出；关闭本窗口后程序不会联网。日志上报需在设置中勾选“允许上传日志”。");
            note.Location = new Point(16, 12);
            note.MaximumSize = new Size(720, 0);
            AutoSize = false;
            Controls.Add(note);

            // ---- 更新检测 ----
            Label t1 = Ui.Section("版本更新检测");
            t1.Location = new Point(16, 48);
            Controls.Add(t1);
            Label cur = Ui.Label("当前版本：" + EngineVersion.Full + "    检查地址：" + (string.IsNullOrEmpty(config.UpdateCheckUrl) ? "（未配置，请在设置中填写）" : config.UpdateCheckUrl));
            cur.Location = new Point(16, 76);
            cur.MaximumSize = new Size(720, 0);
            Controls.Add(cur);
            FlatButton check = Ui.Button("检查更新", delegate { CheckUpdate(); });
            check.Location = new Point(16, 100);
            Controls.Add(check);
            FlatButton openUrl = Ui.Button("打开下载页", delegate
            {
                string url = _config.UpdateCheckUrl;
                if (!string.IsNullOrEmpty(url)) { try { Process.Start(url); } catch { } }
            });
            openUrl.Location = new Point(124, 100);
            openUrl.Width = 100;
            Controls.Add(openUrl);
            _updateStatus = Ui.Label("尚未检查");
            _updateStatus.Location = new Point(236, 106);
            _updateStatus.MaximumSize = new Size(500, 40);
            Controls.Add(_updateStatus);

            // ---- 云脚本库 ----
            Label t2 = Ui.Section("云端脚本库");
            t2.Location = new Point(16, 148);
            Controls.Add(t2);
            _cloudList = new ListBox();
            _cloudList.Location = new Point(16, 176);
            _cloudList.Size = new Size(560, 180);
            _cloudList.BackColor = Theme.PanelAlt;
            _cloudList.ForeColor = Theme.Text;
            _cloudList.BorderStyle = BorderStyle.FixedSingle;
            _cloudList.DoubleClick += delegate { DownloadSelected(); };
            Controls.Add(_cloudList);
            FlatButton list = Ui.Button("获取列表", delegate { LoadCloud(); });
            list.Location = new Point(592, 176);
            list.Width = 140;
            Controls.Add(list);
            FlatButton download = Ui.Button("下载到…", delegate { DownloadSelected(); });
            download.Location = new Point(592, 212);
            download.Width = 140;
            Controls.Add(download);
            FlatButton runIt = Ui.Button("打开运行", delegate
            {
                if (_cloudList.SelectedIndex < 0) return;
                NetworkService.CloudScript s = _scripts[_cloudList.SelectedIndex];
                MessageBox.Show(this, "请先下载到本地，再用编辑器打开脚本。\n地址：" + s.Url, "BlockAuto", MessageBoxButtons.OK, MessageBoxIcon.Information);
            });
            runIt.Location = new Point(592, 248);
            runIt.Width = 140;
            Controls.Add(runIt);
            _cloudStatus = Ui.Label("");
            _cloudStatus.Location = new Point(16, 362);
            _cloudStatus.MaximumSize = new Size(720, 20);
            Controls.Add(_cloudStatus);

            // ---- 日志上报 ----
            Label t3 = Ui.Section("日志上报");
            t3.Location = new Point(16, 392);
            Controls.Add(t3);
            _logBox = Ui.TextBox("上报地址：" + (_config.UploadLogs ? "（使用更新检查地址的同域上报接口，见设置）" : "（未开启“允许上传日志”）"), false);
            _logBox.Location = new Point(16, 420);
            _logBox.Width = 560;
            _logBox.ReadOnly = true;
            Controls.Add(_logBox);
            FlatButton upload = Ui.Button("上报最近日志", delegate { UploadLogs(); });
            upload.Location = new Point(592, 419);
            upload.Width = 140;
            Controls.Add(upload);

            FlatButton close = Ui.Button("关闭", delegate { Close(); });
            close.Location = new Point(592, 470);
            close.Width = 90;
            close.BackColor = Theme.Accent;
            close.ForeColor = Color.White;
            Controls.Add(close);
            CancelButton = close;
        }

        private void CheckUpdate()
        {
            _updateStatus.Text = "检查中…";
            _updateStatus.ForeColor = Theme.TextDim;
            string url = _config.UpdateCheckUrl;
            Task.Factory.StartNew(delegate { return NetworkService.CheckUpdate(url); })
                .ContinueWith(delegate(Task<NetworkService.UpdateInfo> t)
                {
                    SafeInvoke(delegate
                    {
                        NetworkService.UpdateInfo i = t.Result;
                        if (!string.IsNullOrEmpty(i.Error))
                        {
                            _updateStatus.Text = "检查失败：" + i.Error;
                            _updateStatus.ForeColor = Theme.Danger;
                            return;
                        }
                        if (i.HasUpdate)
                        {
                            _updateStatus.Text = "发现新版本 " + i.LatestVersion + (string.IsNullOrEmpty(i.Notes) ? "" : "：" + i.Notes);
                            _updateStatus.ForeColor = Theme.Success;
                        }
                        else
                        {
                            _updateStatus.Text = "已是最新版本（服务器最新 " + i.LatestVersion + "）";
                            _updateStatus.ForeColor = Theme.Text;
                        }
                    });
                });
        }

        private void LoadCloud()
        {
            _cloudStatus.Text = "获取中…";
            string url = _config.CloudRepoUrl;
            Task.Factory.StartNew(delegate { return NetworkService.ListCloudScripts(url); })
                .ContinueWith(delegate(Task<List<NetworkService.CloudScript>> t)
                {
                    SafeInvoke(delegate
                    {
                        if (t.IsFaulted)
                        {
                            _cloudStatus.Text = "获取失败：" + (t.Exception != null ? t.Exception.GetBaseException().Message : "未知错误");
                            _cloudStatus.ForeColor = Theme.Danger;
                            return;
                        }
                        _scripts.Clear();
                        _scripts.AddRange(t.Result);
                        _cloudList.Items.Clear();
                        foreach (NetworkService.CloudScript s in _scripts)
                            _cloudList.Items.Add(s.Name + (string.IsNullOrEmpty(s.Description) ? "" : "  —  " + s.Description));
                        _cloudStatus.Text = _scripts.Count == 0 ? "云脚本库为空" : "共 " + _scripts.Count + " 个脚本（双击下载）";
                        _cloudStatus.ForeColor = Theme.Text;
                    });
                });
        }

        private void DownloadSelected()
        {
            if (_cloudList.SelectedIndex < 0)
            {
                _cloudStatus.Text = "请先选择一个脚本";
                return;
            }
            NetworkService.CloudScript s = _scripts[_cloudList.SelectedIndex];
            SaveFileDialog dlg = new SaveFileDialog();
            dlg.Filter = "BlockAuto 脚本 (*.block)|*.block";
            dlg.FileName = s.Name.EndsWith(".block", StringComparison.OrdinalIgnoreCase) ? s.Name : s.Name + ".block";
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            string target = dlg.FileName;
            _cloudStatus.Text = "下载中…";
            Task.Factory.StartNew(delegate { NetworkService.DownloadTo(s.Url, target, 15000); return target; })
                .ContinueWith(delegate(Task<string> t)
                {
                    SafeInvoke(delegate
                    {
                        if (t.IsFaulted)
                        {
                            _cloudStatus.Text = "下载失败：" + (t.Exception != null ? t.Exception.GetBaseException().Message : "");
                            _cloudStatus.ForeColor = Theme.Danger;
                            return;
                        }
                        _cloudStatus.Text = "已下载：" + target;
                        _cloudStatus.ForeColor = Theme.Success;
                        if (MessageBox.Show(this, "是否立即在编辑器中打开？", "BlockAuto", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                        {
                            DialogResult = DialogResult.OK;
                            Close();
                            Action<string> h = _openScript;
                            if (h != null) h(target);
                        }
                    });
                });
        }

        private void UploadLogs()
        {
            if (!_config.UploadLogs)
            {
                MessageBox.Show(this, "未开启“允许上传日志”。\n请到 设置 → 联网 中勾选后再试。", "BlockAuto", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (MessageBox.Show(this, "将上报最近 3 天的日志到：" + _config.UpdateCheckUrl + "\n\n确定继续？", "BlockAuto",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            string url = _config.UpdateCheckUrl;
            string dir = _config.EffectiveLogDir;
            Task.Factory.StartNew(delegate { return NetworkService.UploadLog(url, dir, true, _config.AiToken); })
                .ContinueWith(delegate(Task<string> t)
                {
                    SafeInvoke(delegate
                    {
                        string msg = t.IsFaulted ? "上报失败：" + (t.Exception != null ? t.Exception.GetBaseException().Message : "") : t.Result;
                        MessageBox.Show(this, msg, "BlockAuto", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    });
                });
        }

        private void SafeInvoke(MethodInvoker action)
        {
            if (IsDisposed) return;
            if (InvokeRequired) { try { BeginInvoke(action); } catch { } }
            else action();
        }
    }
}