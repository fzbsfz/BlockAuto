using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace BlockAuto
{
    public enum ScheduleType { Once, Interval, Daily, Weekly }

    public sealed class ScheduledTask
    {
        public string Id = Guid.NewGuid().ToString("N").Substring(0, 8);
        public string Name = "定时任务";
        public string ScriptPath = "";
        public ScheduleType Type = ScheduleType.Daily;
        public DateTime OnceAt = DateTime.Now.AddHours(1);
        public int IntervalMinutes = 60;
        public string TimeOfDay = "09:00";
        public int[] Weekdays = new int[] { 1, 2, 3, 4, 5 };
        public bool Enabled = true;
        public DateTime? LastRun;
        public bool LastSuccess = true;
        public string LastMessage = "";

        public string TypeText
        {
            get
            {
                switch (Type)
                {
                    case ScheduleType.Once: return "一次性";
                    case ScheduleType.Interval: return "间隔";
                    case ScheduleType.Daily: return "每日";
                    case ScheduleType.Weekly: return "每周";
                    default: return "每日";
                }
            }
        }

        public string ScheduleText
        {
            get
            {
                switch (Type)
                {
                    case ScheduleType.Once:
                        return OnceAt.ToString("yyyy-MM-dd HH:mm");
                    case ScheduleType.Interval:
                        return "每 " + IntervalMinutes + " 分钟";
                    case ScheduleType.Daily:
                        return "每天 " + TimeOfDay;
                    case ScheduleType.Weekly:
                        return "每周 " + WeekdayText() + " " + TimeOfDay;
                    default:
                        return "";
                }
            }
        }

        public string WeekdayText()
        {
            string[] names = { "一", "二", "三", "四", "五", "六", "日" };
            StringBuilder sb = new StringBuilder();
            foreach (int d in Weekdays)
            {
                if (d >= 1 && d <= 7) sb.Append(names[d - 1]);
            }
            return sb.Length == 0 ? "一" : sb.ToString();
        }

        public DateTime? ComputeNext(DateTime now)
        {
            if (!Enabled) return null;
            switch (Type)
            {
                case ScheduleType.Once:
                    return OnceAt > now ? OnceAt : (DateTime?)null;
                case ScheduleType.Interval:
                    {
                        DateTime baseline = LastRun ?? now;
                        DateTime next = baseline.AddMinutes(Math.Max(1, IntervalMinutes));
                        while (next <= now) next = next.AddMinutes(Math.Max(1, IntervalMinutes));
                        return next;
                    }
                case ScheduleType.Daily:
                    {
                        TimeSpan t;
                        if (!TimeSpan.TryParseExact(TimeOfDay, "hh\\:mm", CultureInfo.InvariantCulture, out t))
                            t = TimeSpan.FromHours(9);
                        DateTime next = now.Date + t;
                        if (next <= now) next = next.AddDays(1);
                        return next;
                    }
                case ScheduleType.Weekly:
                    {
                        TimeSpan t;
                        if (!TimeSpan.TryParseExact(TimeOfDay, "hh\\:mm", CultureInfo.InvariantCulture, out t))
                            t = TimeSpan.FromHours(9);
                        int[] days = Weekdays != null && Weekdays.Length > 0 ? Weekdays : new int[] { 1 };
                        for (int i = 0; i < 8; i++)
                        {
                            DateTime day = now.Date.AddDays(i);
                            int dow = ((int)day.DayOfWeek + 6) % 7 + 1; // 周一=1 ... 周日=7
                            bool match = false;
                            foreach (int d in days) if (d == dow) { match = true; break; }
                            if (!match) continue;
                            DateTime next = day + t;
                            if (next > now) return next;
                        }
                        return null;
                    }
                default:
                    return null;
            }
        }
    }

    /// <summary>编辑器内置定时调度：依附主程序进程，主程序关闭即失效。</summary>
    public sealed class Scheduler
    {
        private readonly List<ScheduledTask> _tasks = new List<ScheduledTask>();
        private readonly object _lock = new object();

        public event Action<ScheduledTask> TaskDue;

        public List<ScheduledTask> Tasks
        {
            get { lock (_lock) return new List<ScheduledTask>(_tasks); }
        }

        public static Scheduler Load()
        {
            Scheduler s = new Scheduler();
            try
            {
                if (!File.Exists(AppPaths.SchedulesFile)) return s;
                Dictionary<string, object> root = Json.AsObject(Json.Parse(File.ReadAllText(AppPaths.SchedulesFile, Encoding.UTF8)));
                List<object> arr = Json.ArrayAt(root, "tasks");
                if (arr == null) return s;
                foreach (object it in arr)
                {
                    Dictionary<string, object> o = Json.AsObject(it);
                    ScheduledTask t = new ScheduledTask();
                    t.Id = Json.GetString(o, "id", t.Id);
                    t.Name = Json.GetString(o, "name", t.Name);
                    t.ScriptPath = Json.GetString(o, "script", "");
                    t.Type = (ScheduleType)(int)Json.GetNumber(o, "type", 2);
                    string once = Json.GetString(o, "once_at", "");
                    DateTime dt;
                    if (DateTime.TryParseExact(once, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out dt)) t.OnceAt = dt;
                    t.IntervalMinutes = (int)Json.GetNumber(o, "interval_minutes", 60);
                    t.TimeOfDay = Json.GetString(o, "time_of_day", "09:00");
                    List<object> wd = Json.ArrayAt(o, "weekdays");
                    if (wd != null)
                    {
                        List<int> list = new List<int>();
                        foreach (object x in wd) list.Add((int)Json.ToNumber(x, 1));
                        t.Weekdays = list.ToArray();
                    }
                    t.Enabled = Json.GetBool(o, "enabled", true);
                    string lr = Json.GetString(o, "last_run", "");
                    if (DateTime.TryParse(lr, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt)) t.LastRun = dt;
                    t.LastSuccess = Json.GetBool(o, "last_success", true);
                    t.LastMessage = Json.GetString(o, "last_message", "");
                    lock (s._lock) s._tasks.Add(t);
                }
            }
            catch { }
            return s;
        }

        public void Save()
        {
            lock (_lock)
            {
                List<object> arr = new List<object>();
                foreach (ScheduledTask t in _tasks)
                {
                    Dictionary<string, object> o = new Dictionary<string, object>();
                    o["id"] = t.Id;
                    o["name"] = t.Name;
                    o["script"] = t.ScriptPath;
                    o["type"] = (double)(int)t.Type;
                    o["once_at"] = t.OnceAt.ToString("yyyy-MM-dd HH:mm");
                    o["interval_minutes"] = (double)t.IntervalMinutes;
                    o["time_of_day"] = t.TimeOfDay;
                    List<object> wd = new List<object>();
                    foreach (int d in t.Weekdays) wd.Add((double)d);
                    o["weekdays"] = wd;
                    o["enabled"] = t.Enabled;
                    o["last_run"] = t.LastRun.HasValue ? t.LastRun.Value.ToString("o") : "";
                    o["last_success"] = t.LastSuccess;
                    o["last_message"] = t.LastMessage;
                    arr.Add(o);
                }
                Dictionary<string, object> root = new Dictionary<string, object>();
                root["tasks"] = arr;
                try { File.WriteAllText(AppPaths.SchedulesFile, Json.Stringify(root, true), new UTF8Encoding(false)); }
                catch { }
            }
        }

        public void Add(ScheduledTask t)
        {
            lock (_lock) _tasks.Add(t);
            Save();
        }

        public void Remove(ScheduledTask t)
        {
            lock (_lock) _tasks.Remove(t);
            Save();
        }

        public void Update(ScheduledTask t, Action<ScheduledTask> mutate)
        {
            mutate(t);
            Save();
        }

        /// <summary>由 UI 定时器每秒调用；到点则触发事件。</summary>
        public void Tick(DateTime now)
        {
            List<ScheduledTask> due = new List<ScheduledTask>();
            lock (_lock)
            {
                foreach (ScheduledTask t in _tasks)
                {
                    if (!t.Enabled) continue;
                    DateTime? next = t.ComputeNext(now);
                    if (next.HasValue && next.Value <= now)
                    {
                        if (t.Type == ScheduleType.Once) t.Enabled = false; // 一次性任务执行后禁用
                        due.Add(t);
                    }
                }
            }
            if (due.Count == 0) return;
            Save();
            foreach (ScheduledTask t in due)
            {
                Action<ScheduledTask> h = TaskDue;
                if (h != null) { try { h(t); } catch { } }
            }
        }
    }
}
