using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

namespace BlockAuto
{
    /// <summary>
    /// 可选扩展：AI/Agent 控制用的 RESTful JSON 接口。默认关闭，由全局设置开启。
    /// 仅监听本机回环地址，必须携带 Token（Header "X-BlockAuto-Token" 或 ?token=）。
    /// </summary>
    public sealed class AiHttpServer : IDisposable
    {
        private HttpListener _listener;
        private Thread _thread;
        private volatile bool _running;
        private readonly object _sync = new object();
        private readonly AiHttpHost _host;

        public AiHttpServer(AiHttpHost host)
        {
            _host = host;
        }

        public bool IsRunning { get { return _running; } }
        public int Port { get; private set; }

        public string Start(int port, string token)
        {
            if (_running) return "已在运行";
            if (port <= 0 || port > 65535) return "端口无效";
            if (string.IsNullOrEmpty(token)) return "必须配置访问 Token";
            try
            {
                HttpListener l = new HttpListener();
                l.Prefixes.Add("http://127.0.0.1:" + port + "/");
                l.Start();
                _listener = l;
            }
            catch (Exception ex)
            {
                return "监听失败：" + ex.Message + "（端口可能被占用，或需要管理员权限）";
            }
            Port = port;
            _running = true;
            _thread = new Thread(Loop);
            _thread.IsBackground = true;
            _thread.Name = "BlockAuto-AiHttp";
            _thread.Start();
            return null;
        }

        private void Loop()
        {
            while (_running)
            {
                HttpListenerContext ctx;
                try
                {
                    ctx = _listener.GetContext();
                }
                catch
                {
                    if (!_running) break;
                    Thread.Sleep(50);
                    continue;
                }
                ThreadPool.QueueUserWorkItem(delegate(object state) { Handle((HttpListenerContext)state); }, ctx);
            }
        }

        private void Handle(HttpListenerContext ctx)
        {
            string body = "";
            try
            {
                body = ReadBody(ctx.Request);
                string path = ctx.Request.Url.AbsolutePath.ToLowerInvariant();
                string method = ctx.Request.HttpMethod.ToUpperInvariant();
                string token = ctx.Request.Headers["X-BlockAuto-Token"];
                if (string.IsNullOrEmpty(token)) token = ctx.Request.QueryString["token"];
                if (!string.Equals(token, _host.ExpectedToken, StringComparison.Ordinal))
                {
                    WriteJson(ctx, 401, new Dictionary<string, object> { { "ok", false }, { "error", "Token 无效" } });
                    return;
                }

                Dictionary<string, object> req = null;
                if (!string.IsNullOrEmpty(body))
                {
                    try
                    {
                        object parsed = Json.Parse(body);
                        req = parsed as Dictionary<string, object>;
                    }
                    catch (JsonException ex)
                    {
                        WriteJson(ctx, 400, Fail("JSON 解析失败：" + ex.Message));
                        return;
                    }
                }
                if (req == null) req = new Dictionary<string, object>();
                // HttpListener 在非 UTF-8 系统区域下会把 URL 按本地代码页解码，导致中文路径乱码；
                // 这里直接从 RawUrl 用 UTF-8 重新解码一次。
                foreach (string key in ctx.Request.QueryString.AllKeys)
                {
                    if (key == null) continue;
                    if (!req.ContainsKey(key)) req[key] = ctx.Request.QueryString[key];
                }
                // 再用 UTF-8 解码结果覆盖：HttpListener 在非 UTF-8 区域会把中文参数按本地代码页解码成乱码
                foreach (KeyValuePair<string, string> kv in ParseQueryUtf8(ctx.Request.RawUrl))
                {
                    req[kv.Key] = kv.Value;
                }

                Dictionary<string, object> result = Route(method, path, req);
                bool ok = Json.GetBool(result, "ok", true);
                int status = ok ? 200 : (Json.GetNumber(result, "status", 400) == 401 ? 401 : 400);
                if (result.ContainsKey("status")) result.Remove("status");
                WriteJson(ctx, status, result);
            }
            catch (Exception ex)
            {
                try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "blockauto-ai-error.txt"), DateTime.Now + " " + ctx.Request.RawUrl + "\n" + ex + "\n\n"); } catch { }
                try { WriteJson(ctx, 500, Fail(ex.Message)); } catch { }
            }
        }

        private Dictionary<string, object> Route(string method, string path, Dictionary<string, object> req)
        {
            switch (path.ToLowerInvariant())
            {
                case "/script/start":
                    if (method != "POST") return Fail("请使用 POST");
                    return _host.StartScript(Json.GetString(req, "script", ""), Json.GetNumber(req, "threshold", 0));
                case "/script/stop":
                    if (method != "POST") return Fail("请使用 POST");
                    return _host.StopScript(Json.GetString(req, "script", ""));
                case "/script/status":
                    return _host.ScriptStatus(Json.GetString(req, "script", ""));
                case "/cap/screenshot":
                    if (method != "POST") return Fail("请使用 POST");
                    return _host.Capture(Json.GetString(req, "region", "full"));
                case "/cap/findimage":
                    if (method != "POST") return Fail("请使用 POST");
                    return _host.FindImage(req);
                case "/cap/mouseclick":
                    if (method != "POST") return Fail("请使用 POST");
                    return _host.MouseClick(req);
                case "/cap/keytype":
                    if (method != "POST") return Fail("请使用 POST");
                    return _host.KeyType(req);
                case "/script/tree":
                    if (method == "GET") return _host.GetTree(Json.GetString(req, "script", ""));
                    if (method == "PUT") return _host.PutTree(Json.GetString(req, "script", ""), req);
                    return Fail("请使用 GET 或 PUT");
                case "/log":
                    return _host.GetLog(Json.GetString(req, "script", ""));
                case "/health":
                    return Health();

                default:
                    return Fail("未知接口：" + path);
            }
        }

        private static Dictionary<string, object> Health()
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            d["ok"] = true;
            d["name"] = "BlockAuto";
            d["version"] = EngineVersion.Current;
            d["time"] = DateTime.Now.ToString("o");
            // 接口路径 ↔ 白名单动作名对照：接 API 时直接照抄，不用按 URL 猜动作名
            List<object> actions = new List<object>();
            actions.Add(ActionItem("/script/start", "POST", "script.start"));
            actions.Add(ActionItem("/script/stop", "POST", "script.stop"));
            actions.Add(ActionItem("/script/status", "GET", "script.status"));
            actions.Add(ActionItem("/script/tree", "GET/PUT", "script.tree"));
            actions.Add(ActionItem("/cap/screenshot", "POST", "cap.screenshot"));
            actions.Add(ActionItem("/cap/findImage", "POST", "cap.findImage"));
            actions.Add(ActionItem("/cap/mouseClick", "POST", "cap.mouseClick"));
            actions.Add(ActionItem("/cap/keyType", "POST", "cap.keyType"));
            actions.Add(ActionItem("/log", "GET", "log.read"));
            actions.Add(ActionItem("/health", "GET", "health"));
            d["actions"] = actions;
            return d;
        }

        private static Dictionary<string, object> ActionItem(string path, string method, string action)
        {
            Dictionary<string, object> m = new Dictionary<string, object>();
            m["path"] = path;
            m["method"] = method;
            m["action"] = action;
            return m;
        }
        private static List<KeyValuePair<string, string>> ParseQueryUtf8(string rawUrl)
        {
            List<KeyValuePair<string, string>> list = new List<KeyValuePair<string, string>>();
            if (string.IsNullOrEmpty(rawUrl)) return list;
            int q = rawUrl.IndexOf('?');
            if (q < 0 || q + 1 >= rawUrl.Length) return list;
            string query = rawUrl.Substring(q + 1);
            foreach (string part in query.Split('&'))
            {
                if (part.Length == 0) continue;
                int eq = part.IndexOf('=');
                string key = eq < 0 ? part : part.Substring(0, eq);
                string val = eq < 0 ? "" : part.Substring(eq + 1);
                list.Add(new KeyValuePair<string, string>(Uri.UnescapeDataString(key), Uri.UnescapeDataString(val)));
            }
            return list;
        }

        private static string ReadBody(HttpListenerRequest request)
        {
            using (StreamReader reader = new StreamReader(request.InputStream, Encoding.UTF8))
                return reader.ReadToEnd();
        }

        private static Dictionary<string, object> Fail(string message)
        {
            return new Dictionary<string, object> { { "ok", false }, { "error", message } };
        }

        private static void WriteJson(HttpListenerContext ctx, int status, Dictionary<string, object> payload)
        {
            byte[] data = Encoding.UTF8.GetBytes(Json.Stringify(payload, false));
            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = "application/json; charset=utf-8";
            ctx.Response.ContentLength64 = data.Length;
            ctx.Response.OutputStream.Write(data, 0, data.Length);
            ctx.Response.OutputStream.Close();
        }

        public void Dispose()
        {
            _running = false;
            try { if (_listener != null) _listener.Stop(); } catch { }
            try { if (_listener != null) _listener.Close(); } catch { }
            _listener = null;
        }
    }

    /// <summary>HTTP 接口与编辑器/引擎之间的适配层，由编辑器实现。</summary>
    public interface AiHttpHost
    {
        string ExpectedToken { get; }
        Dictionary<string, object> StartScript(string scriptPath, double threshold);
        Dictionary<string, object> StopScript(string scriptPath);
        Dictionary<string, object> ScriptStatus(string scriptPath);
        Dictionary<string, object> Capture(string region);
        Dictionary<string, object> FindImage(Dictionary<string, object> req);
        Dictionary<string, object> MouseClick(Dictionary<string, object> req);
        Dictionary<string, object> KeyType(Dictionary<string, object> req);
        Dictionary<string, object> GetTree(string scriptPath);
        Dictionary<string, object> PutTree(string scriptPath, Dictionary<string, object> req);
        Dictionary<string, object> GetLog(string scriptPath);
    }

    /// <summary>用于自测的简单宿主实现。</summary>
    public sealed class MockAiHost : AiHttpHost
    {
        public string Token = "test-token";
        public readonly List<string> Calls = new List<string>();
        public string TreeJson = "{\"schema\":\"1.0\"}";

        public string ExpectedToken { get { return Token; } }

        public Dictionary<string, object> StartScript(string scriptPath, double threshold)
        {
            Calls.Add("start:" + scriptPath + ":" + threshold.ToString(CultureInfo.InvariantCulture));
            return new Dictionary<string, object> { { "ok", true }, { "message", "已启动" } };
        }

        public Dictionary<string, object> StopScript(string scriptPath)
        {
            Calls.Add("stop:" + scriptPath);
            return new Dictionary<string, object> { { "ok", true } };
        }

        public Dictionary<string, object> ScriptStatus(string scriptPath)
        {
            Calls.Add("status:" + scriptPath);
            return new Dictionary<string, object> { { "ok", true }, { "running", false } };
        }

        public Dictionary<string, object> Capture(string region)
        {
            Calls.Add("capture:" + region);
            return new Dictionary<string, object> { { "ok", true }, { "image", "iVBORw0KGgo=" } };
        }

        public Dictionary<string, object> FindImage(Dictionary<string, object> req)
        {
            Calls.Add("findImage");
            return new Dictionary<string, object> { { "ok", true }, { "found", false } };
        }

        public Dictionary<string, object> MouseClick(Dictionary<string, object> req)
        {
            Calls.Add("mouseClick");
            return new Dictionary<string, object> { { "ok", true } };
        }

        public Dictionary<string, object> KeyType(Dictionary<string, object> req)
        {
            Calls.Add("keyType");
            return new Dictionary<string, object> { { "ok", true } };
        }

        public Dictionary<string, object> GetTree(string scriptPath)
        {
            Calls.Add("getTree:" + scriptPath);
            return new Dictionary<string, object> { { "ok", true }, { "tree", TreeJson } };
        }

        public Dictionary<string, object> PutTree(string scriptPath, Dictionary<string, object> req)
        {
            Calls.Add("putTree:" + scriptPath);
            return new Dictionary<string, object> { { "ok", true } };
        }

        public Dictionary<string, object> GetLog(string scriptPath)
        {
            Calls.Add("log:" + scriptPath);
            return new Dictionary<string, object> { { "ok", true }, { "lines", new List<object>() } };
        }
    }
}