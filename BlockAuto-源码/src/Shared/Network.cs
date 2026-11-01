using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;

namespace BlockAuto
{
    /// <summary>
    /// 可选扩展：联网通信（版本更新检测 / 云端脚本库 / 日志上报）。
    /// 全部为“手动触发”，默认关闭，不发任何后台请求；失败不影响本地功能。
    /// </summary>
    public static class NetworkService
    {
        public sealed class UpdateInfo
        {
            public bool HasUpdate;
            public string LatestVersion = "";
            public string CurrentVersion = EngineVersion.Current;
            public string Notes = "";
            public string DownloadUrl = "";
            public string Error = "";
        }

        public sealed class CloudScript
        {
            public string Name = "";
            public string Url = "";
            public string Description = "";
        }

        public static string HttpGet(string url, int timeoutMs)
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Timeout = timeoutMs;
            req.ReadWriteTimeout = timeoutMs;
            req.UserAgent = "BlockAuto/" + EngineVersion.Current;
            using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
            using (StreamReader reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                return reader.ReadToEnd();
        }

        public static string HttpPostJson(string url, string json, int timeoutMs, string token)
        {
            byte[] data = Encoding.UTF8.GetBytes(json ?? "");
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "POST";
            req.ContentType = "application/json; charset=utf-8";
            req.Timeout = timeoutMs;
            req.ReadWriteTimeout = timeoutMs;
            req.UserAgent = "BlockAuto/" + EngineVersion.Current;
            if (!string.IsNullOrEmpty(token)) req.Headers["X-Auth-Token"] = token;
            req.ContentLength = data.Length;
            using (Stream s = req.GetRequestStream()) s.Write(data, 0, data.Length);
            using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
            using (StreamReader reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                return reader.ReadToEnd();
        }

        /// <summary>检查更新：接口返回 JSON {"version":"1.2.0","notes":"...","url":"..."}。</summary>
        public static UpdateInfo CheckUpdate(string url)
        {
            UpdateInfo info = new UpdateInfo();
            if (string.IsNullOrEmpty(url))
            {
                info.Error = "未配置更新检查地址";
                return info;
            }
            try
            {
                string body = HttpGet(url, 8000);
                Dictionary<string, object> o = Json.AsObject(Json.Parse(body));
                info.LatestVersion = Json.GetString(o, "version", "");
                info.Notes = Json.GetString(o, "notes", "");
                info.DownloadUrl = Json.GetString(o, "url", "");
                info.HasUpdate = CompareVersion(info.LatestVersion, EngineVersion.Current) > 0;
                if (string.IsNullOrEmpty(info.LatestVersion)) info.Error = "返回内容缺少 version 字段";
            }
            catch (Exception ex)
            {
                info.Error = ex.Message;
            }
            return info;
        }

        /// <summary>版本号比较：返回 1 表示 a 比 b 新。</summary>
        public static int CompareVersion(string a, string b)
        {
            string[] pa = (a ?? "").TrimStart('v', 'V').Split('.');
            string[] pb = (b ?? "").TrimStart('v', 'V').Split('.');
            int n = Math.Max(pa.Length, pb.Length);
            for (int i = 0; i < n; i++)
            {
                int va, vb;
                if (!int.TryParse(i < pa.Length ? pa[i] : "0", out va)) va = 0;
                if (!int.TryParse(i < pb.Length ? pb[i] : "0", out vb)) vb = 0;
                if (va != vb) return va > vb ? 1 : -1;
            }
            return 0;
        }

        /// <summary>云端脚本库：接口返回 [{"name","url","description"}]。</summary>
        public static List<CloudScript> ListCloudScripts(string url)
        {
            List<CloudScript> list = new List<CloudScript>();
            if (string.IsNullOrEmpty(url)) throw new InvalidOperationException("未配置云脚本库地址");
            string body = HttpGet(url, 8000);
            object parsed;
            try { parsed = Json.Parse(body); }
            catch (JsonException ex) { throw new InvalidOperationException("云脚本库返回的不是合法 JSON：" + ex.Message); }
            Dictionary<string, object> root = parsed as Dictionary<string, object>;
            List<object> arr = parsed as List<object>;
            if (root != null) arr = Json.ArrayAt(root, "scripts");
            if (arr == null) throw new InvalidOperationException("云脚本库返回格式不正确（期望数组或 {scripts:[...]}）");
            foreach (object it in arr)
            {
                Dictionary<string, object> o = it as Dictionary<string, object>;
                if (o == null) continue;
                CloudScript s = new CloudScript();
                s.Name = Json.GetString(o, "name", "");
                s.Url = Json.GetString(o, "url", "");
                s.Description = Json.GetString(o, "description", "");
                if (s.Url.Length > 0) list.Add(s);
            }
            return list;
        }

        public static void DownloadTo(string url, string targetPath, int timeoutMs)
        {
            using (WebClient wc = new WebClient())
            {
                wc.Headers["User-Agent"] = "BlockAuto/" + EngineVersion.Current;
                string dir = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                wc.DownloadFile(url, targetPath);
            }
        }

        /// <summary>日志上报：只在用户勾选“允许上传日志”时由用户手动触发。</summary>
        public static string UploadLog(string url, string logDir, bool enabled, string token)
        {
            if (!enabled) return "未开启“允许上传日志”，已取消";
            if (string.IsNullOrEmpty(url)) return "未配置上报地址";
            try
            {
                StringBuilder sb = new StringBuilder();
                if (Directory.Exists(logDir))
                {
                    string[] files = Directory.GetFiles(logDir, "*.log");
                    Array.Sort(files);
                    int start = Math.Max(0, files.Length - 3);
                    for (int i = start; i < files.Length; i++)
                        sb.AppendLine("===== " + Path.GetFileName(files[i]) + " =====").AppendLine(File.ReadAllText(files[i], Encoding.UTF8));
                }
                Dictionary<string, object> payload = new Dictionary<string, object>();
                payload["app"] = "BlockAuto";
                payload["version"] = EngineVersion.Current;
                payload["machine"] = Environment.MachineName;
                payload["time"] = DateTime.Now.ToString("o");
                payload["logs"] = sb.ToString();
                string body = Json.Stringify(payload, false);
                HttpPostJson(url, body, 10000, token);
                return "日志已上报";
            }
            catch (Exception ex)
            {
                return "上报失败：" + ex.Message;
            }
        }
    }

    /// <summary>
    /// 版本号唯一来源：联网更新检查、联网/关于窗口、运行器 banner、打包使用说明、状态栏
    /// 全部引用这里，避免各处写死后互相不一致。
    /// </summary>
    public static class EngineVersion
    {
        /// <summary>用于版本比较的完整版本号（三段式）。</summary>
        public const string Current = "1.0.0";
        /// <summary>对外展示的短版本号。</summary>
        public const string Display = "1.0";
        /// <summary>发布阶段说明。</summary>
        public const string Stage = "未发行";
        /// <summary>形如 “1.0（未发行）”。</summary>
        public const string Full = Display + "（" + Stage + "）";
    }
}