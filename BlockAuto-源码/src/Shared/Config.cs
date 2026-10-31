using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace BlockAuto
{
    public static class AppPaths
    {
        private static string _dataDir;

        /// <summary>数据目录：优先 %APPDATA%\BlockAuto，不可写时依次回退，保证受限环境也能启动。</summary>
        public static string DataDir
        {
            get
            {
                if (_dataDir != null) return _dataDir;
                List<string> candidates = new List<string>();
                try { candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BlockAuto")); }
                catch { }
                try { candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BlockAuto")); }
                catch { }
                try { candidates.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data")); }
                catch { }
                try { candidates.Add(Path.Combine(Path.GetTempPath(), "BlockAuto")); }
                catch { }

                foreach (string dir in candidates)
                {
                    try
                    {
                        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                        string probe = Path.Combine(dir, ".write-test");
                        File.WriteAllText(probe, "ok");
                        File.Delete(probe);
                        _dataDir = dir;
                        return _dataDir;
                    }
                    catch { }
                }
                _dataDir = Path.GetTempPath();
                return _dataDir;
            }
        }

        public static string ConfigFile { get { return Path.Combine(DataDir, "config.json"); } }
        public static string HotkeysFile { get { return Path.Combine(DataDir, "hotkeys.json"); } }
        public static string SchedulesFile { get { return Path.Combine(DataDir, "schedules.json"); } }
        public static string LogDir { get { return Path.Combine(DataDir, "logs"); } }
        public static string TemplatesDir { get { return Path.Combine(DataDir, "templates"); } }
        public static string SamplesDir { get { return Path.Combine(DataDir, "samples"); } }
    }

    /// <summary>全局配置，持久化到 %APPDATA%\BlockAuto\config.json。</summary>
    public sealed class AppConfig
    {
        public double DefaultThreshold = 0.8;
        public int ActionDelayMs = 80;
        public int MaxRecursion = 100;
        public int LogRetentionDays = 14;
        public int ScreenshotCacheMs = 250;
        public string LogDirectory = "";
        public bool EnableAi = false;
        public int AiPort = 8765;
        public string AiToken = "";
        public bool ActionWhitelist = false;
        public bool ConfirmDangerousActions = false;
        public List<string> AllowedActions = new List<string>();
        public string UpdateCheckUrl = "";
        public bool UploadLogs = false;
        public string CloudRepoUrl = "";
        public int MatchScalePercent = 0;
        public List<string> RecentScripts = new List<string>();
        public int CanvasZoomPercent = 100;

        // ---- v2.0 键鼠优先 ----
        /// <summary>"simple" = 键鼠优先简洁界面（默认）；"pro" = 专业模式（代码视图/断点/高级积木）</summary>
        public string UiMode = "simple";
        /// <summary>界面主题：light（默认）/ dark</summary>
        public string ThemeMode = "light";
        /// <summary>关闭窗口时最小化到系统托盘</summary>
        public bool HideToTray = true;
        /// <summary>首次运行引导是否已看过</summary>
        public bool FirstRunDone = false;
        /// <summary>保存时保留的历史版本数量（0 = 不备份）</summary>
        public int BackupVersions = 5;
        /// <summary>录制后自动补延时</summary>
        public bool RecordAutoDelay = true;
        public int RecordDelayMs = 200;
        /// <summary>托盘热键：一键唤起窗口</summary>
        public string TrayHotkey = "Ctrl+Alt+B";
        /// <summary>是否已提示过“让 .block 双击运行”</summary>
        public bool AssocPrompted = false;

        public bool IsSimpleMode { get { return !string.Equals(UiMode, "pro", StringComparison.OrdinalIgnoreCase); } }

        public string EffectiveLogDir
        {
            get { return string.IsNullOrEmpty(LogDirectory) ? AppPaths.LogDir : LogDirectory; }
        }

        public static AppConfig Load()
        {
            try
            {
                if (!File.Exists(AppPaths.ConfigFile)) return new AppConfig();
                Dictionary<string, object> o = Json.AsObject(Json.Parse(File.ReadAllText(AppPaths.ConfigFile, Encoding.UTF8)));
                AppConfig c = new AppConfig();
                c.DefaultThreshold = Json.GetNumber(o, "default_threshold", 0.8);
                c.ActionDelayMs = (int)Json.GetNumber(o, "action_delay_ms", 80);
                c.MaxRecursion = (int)Json.GetNumber(o, "max_recursion", 100);
                c.LogRetentionDays = (int)Json.GetNumber(o, "log_retention_days", 14);
                c.ScreenshotCacheMs = (int)Json.GetNumber(o, "screenshot_cache_ms", 250);
                c.LogDirectory = Json.GetString(o, "log_directory", "");
                c.EnableAi = Json.GetBool(o, "enable_ai", false);
                c.AiPort = (int)Json.GetNumber(o, "ai_port", 8765);
                c.AiToken = Json.GetString(o, "ai_token", "");
                c.ActionWhitelist = Json.GetBool(o, "action_whitelist", false);
                c.ConfirmDangerousActions = Json.GetBool(o, "confirm_dangerous_actions", false);
                c.UpdateCheckUrl = Json.GetString(o, "update_check_url", "");
                c.UploadLogs = Json.GetBool(o, "upload_logs", false);
                c.CloudRepoUrl = Json.GetString(o, "cloud_repo_url", "");
                c.MatchScalePercent = (int)Json.GetNumber(o, "match_scale_percent", 0);
                List<object> allowed = Json.ArrayAt(o, "allowed_actions");
                if (allowed != null) foreach (object a in allowed) if (a != null) c.AllowedActions.Add(Convert.ToString(a));
                if (c.AllowedActions.Count == 0)
                {
                    c.AllowedActions.Add("script.start");
                    c.AllowedActions.Add("script.stop");
                    c.AllowedActions.Add("cap.findImage");
                    c.AllowedActions.Add("cap.mouseClick");
                    c.AllowedActions.Add("cap.keyType");
                }
                List<object> rec = Json.ArrayAt(o, "recent_scripts");
                if (rec != null)
                    foreach (object x in rec) if (x != null) c.RecentScripts.Add(Convert.ToString(x));
                c.CanvasZoomPercent = (int)Json.GetNumber(o, "canvas_zoom_percent", 100);
                c.UiMode = Json.GetString(o, "ui_mode", "simple");
                c.ThemeMode = Json.GetString(o, "theme_mode", "light");
                c.HideToTray = Json.GetBool(o, "hide_to_tray", true);
                c.FirstRunDone = Json.GetBool(o, "first_run_done", false);
                c.BackupVersions = (int)Json.GetNumber(o, "backup_versions", 5);
                c.RecordAutoDelay = Json.GetBool(o, "record_auto_delay", true);
                c.RecordDelayMs = (int)Json.GetNumber(o, "record_delay_ms", 200);
                c.TrayHotkey = Json.GetString(o, "tray_hotkey", "Ctrl+Alt+B");
                c.AssocPrompted = Json.GetBool(o, "assoc_prompted", false);
                if (c.DefaultThreshold <= 0 || c.DefaultThreshold > 1) c.DefaultThreshold = 0.8;
                if (c.MaxRecursion <= 0) c.MaxRecursion = 100;
                return c;
            }
            catch
            {
                return new AppConfig();
            }
        }

        public void Save()
        {
            try
            {
                Dictionary<string, object> o = new Dictionary<string, object>();
                o["default_threshold"] = DefaultThreshold;
                o["action_delay_ms"] = (double)ActionDelayMs;
                o["max_recursion"] = (double)MaxRecursion;
                o["log_retention_days"] = (double)LogRetentionDays;
                o["screenshot_cache_ms"] = (double)ScreenshotCacheMs;
                o["log_directory"] = LogDirectory;
                o["enable_ai"] = EnableAi;
                o["ai_port"] = (double)AiPort;
                o["ai_token"] = AiToken;
                o["action_whitelist"] = ActionWhitelist;
                o["confirm_dangerous_actions"] = ConfirmDangerousActions;
                o["update_check_url"] = UpdateCheckUrl;
                o["upload_logs"] = UploadLogs;
                o["cloud_repo_url"] = CloudRepoUrl;
                o["match_scale_percent"] = (double)MatchScalePercent;
                List<object> allowed = new List<object>();
                foreach (string a in AllowedActions) allowed.Add(a);
                o["allowed_actions"] = allowed;
                List<object> rec = new List<object>();
                foreach (string s in RecentScripts) rec.Add(s);
                o["recent_scripts"] = rec;
                o["canvas_zoom_percent"] = (double)CanvasZoomPercent;
                o["ui_mode"] = UiMode;
                o["theme_mode"] = ThemeMode;
                o["hide_to_tray"] = HideToTray;
                o["first_run_done"] = FirstRunDone;
                o["backup_versions"] = (double)BackupVersions;
                o["record_auto_delay"] = RecordAutoDelay;
                o["record_delay_ms"] = (double)RecordDelayMs;
                o["tray_hotkey"] = TrayHotkey;
                o["assoc_prompted"] = AssocPrompted;
                File.WriteAllText(AppPaths.ConfigFile, Json.Stringify(o, true), new UTF8Encoding(false));
            }
            catch { }
        }

        public void AddRecent(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            RecentScripts.RemoveAll(delegate(string p) { return string.Equals(p, path, StringComparison.OrdinalIgnoreCase); });
            RecentScripts.Insert(0, path);
            while (RecentScripts.Count > 8) RecentScripts.RemoveAt(RecentScripts.Count - 1);
            Save();
        }
    }
}
