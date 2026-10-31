using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace BlockAuto
{
    /// <summary>
    /// 把编辑器能力暴露给 AI HTTP 接口：脚本控制复用同一引擎，原子能力复用 DesktopAutomation。
    /// 支持动作白名单与“人工确认”开关。
    /// </summary>
    public sealed class EditorAiHost : AiHttpHost
    {
        private readonly MainForm _form;
        private readonly AppConfig _config;
        private readonly DesktopAutomation _auto;
        private readonly Logger _logger;
        private readonly Dictionary<string, DateTime> _lastRun = new Dictionary<string, DateTime>();
        private readonly Dictionary<string, bool> _lastSuccess = new Dictionary<string, bool>();

        public EditorAiHost(MainForm form, AppConfig config, DesktopAutomation auto, Logger logger)
        {
            _form = form;
            _config = config;
            _auto = auto;
            _logger = logger;
        }

        public string ExpectedToken { get { return _config.AiToken; } }

        private static Dictionary<string, object> Ok()
        {
            return new Dictionary<string, object> { { "ok", true } };
        }

        private static Dictionary<string, object> Err(string message)
        {
            return new Dictionary<string, object> { { "ok", false }, { "error", message } };
        }

        private string ResolveScript(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            path = path.Trim().Trim('"');
            try
            {
                if (Path.IsPathRooted(path)) return Path.GetFullPath(path);
                string current = _form.CurrentFilePath;
                string baseDir = string.IsNullOrEmpty(current) ? Environment.CurrentDirectory : Path.GetDirectoryName(current);
                return Path.GetFullPath(Path.Combine(baseDir, path));
            }
            catch (Exception)
            {
                return null;
            }
        }

        private bool NeedConfirm(string action)
        {
            if (!_config.ConfirmDangerousActions) return true;
            DialogResult r = MessageBox.Show("AI 接口请求执行：" + action + "\n\n是否允许？", "BlockAuto 安全确认",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            return r == DialogResult.Yes;
        }

        private bool Allowed(string action)
        {
            if (!_config.ActionWhitelist) return true;
            foreach (string a in _config.AllowedActions)
            {
                if (string.Equals(a, action, StringComparison.OrdinalIgnoreCase)) return true;
            }
            _logger.Write(new LogEntry { Level = LogLevel.Warn, Source = "AI接口", Message = "动作 " + action + " 不在白名单内，已拒绝" });
            return false;
        }

        // ---------------------------- 脚本控制 ----------------------------

        public Dictionary<string, object> StartScript(string scriptPath, double threshold)
        {
            string path = ResolveScript(scriptPath);
            if (path == null || !File.Exists(path)) return Err("脚本不存在：" + scriptPath);
            if (!Allowed("script.start")) return Err("动作 script.start 被白名单禁用");
            if (Engine.IsScriptRunning(path)) return Err("脚本正在运行，已跳过（同一脚本只允许一个实例）");
            _form.SafeRunFromApi(path, threshold);
            _lastRun[path] = DateTime.Now;
            _logger.Write(new LogEntry { Level = LogLevel.Info, Source = "AI接口", Message = "启动脚本：" + path });
            return Ok();
        }

        public Dictionary<string, object> StopScript(string scriptPath)
        {
            if (!Allowed("script.stop")) return Err("动作 script.stop 被白名单禁用");
            _form.SafeStopAllFromApi();
            _logger.Write(new LogEntry { Level = LogLevel.Info, Source = "AI接口", Message = "停止脚本：" + scriptPath });
            return Ok();
        }

        public Dictionary<string, object> ScriptStatus(string scriptPath)
        {
            string path = ResolveScript(scriptPath);
            Dictionary<string, object> d = new Dictionary<string, object>();
            d["ok"] = true;
            d["running"] = path != null && Engine.IsScriptRunning(path);
            DateTime last;
            if (path != null && _lastRun.TryGetValue(path, out last)) d["last_run"] = last.ToString("o");
            bool ok;
            if (path != null && _lastSuccess.TryGetValue(path, out ok)) d["last_success"] = ok;
            d["active_runs"] = _form.ActiveRunCount;
            return d;
        }

        // ---------------------------- 原子能力 ----------------------------

        public Dictionary<string, object> Capture(string region)
        {
            try
            {
                Rectangle r = RegionParser.Parse(string.IsNullOrEmpty(region) ? "full" : region);
                using (Bitmap bmp = ScreenCapture.Capture(r))
                using (MemoryStream ms = new MemoryStream())
                {
                    bmp.Save(ms, ImageFormat.Png);
                    Dictionary<string, object> d = Ok();
                    d["x"] = (double)r.X;
                    d["y"] = (double)r.Y;
                    d["width"] = (double)r.Width;
                    d["height"] = (double)r.Height;
                    d["image"] = Convert.ToBase64String(ms.ToArray());
                    return d;
                }
            }
            catch (Exception ex)
            {
                return Err(ex.Message);
            }
        }

        public Dictionary<string, object> FindImage(Dictionary<string, object> req)
        {
            if (!Allowed("cap.findImage")) return Err("动作 cap.findImage 被白名单禁用");
            string template = Json.GetString(req, "template", "");
            string path = ResolveScript(template);
            if (path == null || !File.Exists(path)) return Err("模板不存在：" + template);
            double threshold = Json.GetNumber(req, "threshold", _config.DefaultThreshold);
            string region = Json.GetString(req, "region", "full");
            FindImageResult r = _auto.FindImage(path, region, threshold);
            Dictionary<string, object> d = Ok();
            d["found"] = r.Found;
            d["x"] = r.X;
            d["y"] = r.Y;
            d["confidence"] = r.Confidence;
            if (!string.IsNullOrEmpty(_auto.LastError)) d["error"] = _auto.LastError;
            return d;
        }

        public Dictionary<string, object> MouseClick(Dictionary<string, object> req)
        {
            if (!Allowed("cap.mouseClick")) return Err("动作 cap.mouseClick 被白名单禁用");
            double x = Json.GetNumber(req, "x", -1);
            double y = Json.GetNumber(req, "y", -1);
            string button = Json.GetString(req, "button", "left");
            string action = Json.GetString(req, "action", "click");
            if (x < 0 || y < 0) return Err("缺少坐标 x/y");
            if (!NeedConfirm(string.Format("鼠标{0} ({1:0}, {2:0})", button == "right" ? "右键" : "左键", x, y)))
                return Err("用户拒绝了该动作");
            bool ok = _auto.MouseClick(true, x, true, y, button, action, 0, 0);
            _logger.Write(new LogEntry { Level = LogLevel.Info, Source = "AI接口", Message = string.Format("鼠标点击 ({0:0},{1:0}) {2}", x, y, ok ? "成功" : "失败") });
            return ok ? Ok() : Err(_auto.LastError);
        }

        public Dictionary<string, object> KeyType(Dictionary<string, object> req)
        {
            if (!Allowed("cap.keyType")) return Err("动作 cap.keyType 被白名单禁用");
            string text = Json.GetString(req, "text", "");
            if (text.Length == 0) return Err("缺少 text");
            if (!NeedConfirm("键盘输入 " + text)) return Err("用户拒绝了该动作");
            bool ok = _auto.KeyType(text);
            _logger.Write(new LogEntry { Level = LogLevel.Info, Source = "AI接口", Message = "键盘输入：" + text });
            return ok ? Ok() : Err(_auto.LastError);
        }

        // ---------------------------- 脚本树 ----------------------------

        public Dictionary<string, object> GetTree(string scriptPath)
        {
            string path = ResolveScript(scriptPath);
            if (path == null) return Err("缺少 script 参数");
            if (File.Exists(path))
            {
                try
                {
                    ScriptDocument d = ScriptDocument.Load(path);
                    return new Dictionary<string, object> { { "ok", true }, { "tree", d.ToJson() } };
                }
                catch (Exception ex)
                {
                    return Err("读取失败：" + ex.Message);
                }
            }
            if (_form.CurrentFilePath == path) return new Dictionary<string, object> { { "ok", true }, { "tree", _form.SnapshotCurrentJson() } };
            return Err("脚本不存在：" + scriptPath);
        }

        public Dictionary<string, object> PutTree(string scriptPath, Dictionary<string, object> req)
        {
            if (!Allowed("script.tree")) return Err("动作 script.tree 被白名单禁用");
            string path = ResolveScript(scriptPath);
            if (path == null) return Err("缺少 script 参数");
            object treeObj = null;
            if (req.ContainsKey("tree")) treeObj = req["tree"];
            else treeObj = req;
            string json = treeObj is string ? (string)treeObj : Json.Stringify(treeObj, true);
            ScriptDocument doc;
            try
            {
                doc = ScriptDocument.FromJson(json, path);
            }
            catch (Exception ex)
            {
                return Err("节点树解析失败：" + ex.Message);
            }
            List<string> errors = doc.Validate();
            if (errors.Count > 0) return Err("节点树校验失败：" + string.Join("; ", errors.ToArray()));
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                doc.EnsureNextLinks();
                doc.Save(path);
            }
            catch (Exception ex)
            {
                return Err("写入失败：" + ex.Message);
            }
            _logger.Write(new LogEntry { Level = LogLevel.Info, Source = "AI接口", Message = "写入脚本节点树：" + path });
            if (string.Equals(path, _form.CurrentFilePath, StringComparison.OrdinalIgnoreCase))
                _form.SafeReloadFromDisk(path);
            return new Dictionary<string, object> { { "ok", true }, { "path", path } };
        }

        public Dictionary<string, object> GetLog(string scriptPath)
        {
            List<object> lines = new List<object>();
            foreach (string line in _form.SnapshotRecentLog(300))
                lines.Add(line);
            Dictionary<string, object> d = Ok();
            d["lines"] = lines;
            return d;
        }
    }
}