using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace BlockAuto
{
    internal sealed class PaletteChip : Control
    {
        public string BlockType;
        public string Category;
        private bool _hover;
        private Point _down;
        private bool _maybeDrag;

        public event Action<string> AddRequested;

        public PaletteChip(string type, string title, string category)
        {
            BlockType = type;
            Category = category;
            Text = title;
            Height = Theme.Px(30);
            Width = Theme.Px(190);
            Margin = new Padding(Theme.Px(8), Theme.Px(2), Theme.Px(4), Theme.Px(2));
            Cursor = Cursors.Hand;
            Font = Theme.UiFont(9f, FontStyle.Regular);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                _down = e.Location;
                _maybeDrag = true;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_maybeDrag || e.Button != MouseButtons.Left) return;
            if (Math.Abs(e.X - _down.X) < 4 && Math.Abs(e.Y - _down.Y) < 4) return;
            _maybeDrag = false;
            DataObject data = new DataObject("BlockAuto.BlockType", BlockType);
            DoDragDrop(data, DragDropEffects.Copy);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            bool dragged = !_maybeDrag;
            _maybeDrag = false;
            if (dragged || e.Button != MouseButtons.Left) return;
            // 点右侧「+」直接添加（很多人第一反应就是点加号）
            Rectangle plus = new Rectangle(Width - Theme.Px(28), 0, Theme.Px(28), Height);
            if (plus.Contains(e.Location))
            {
                Action<string> h = AddRequested;
                if (h != null) h(BlockType);
                return;
            }
            // 点其它位置：提示一下怎么加，避免“点了没反应”
            ToolTip tip = new ToolTip();
            tip.SetToolTip(this, "双击积木，或点右侧 + 添加");
        }

        protected override void OnDoubleClick(EventArgs e)
        {
            base.OnDoubleClick(e);
            Action<string> h = AddRequested;
            if (h != null) h(BlockType);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color accent = Theme.CategoryAccent(Category);
            Color fill = _hover ? Theme.Mix(Theme.Panel, accent, 0.10f) : Theme.Panel;
            using (GraphicsPath p = Theme.RoundedRect(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 4f))
            {
                using (SolidBrush b = new SolidBrush(fill)) g.FillPath(b, p);
                using (Pen pen = new Pen(Theme.Border)) g.DrawPath(pen, p);
            }
            using (SolidBrush bar = new SolidBrush(accent))
                g.FillRectangle(bar, Theme.Px(3), Theme.Px(5), Theme.Px(3), Height - Theme.Px(10));
            BlockIcons.Draw(g, BlockType, new RectangleF(Theme.Px(11), (Height - Theme.Px(16)) / 2f, Theme.Px(16), Theme.Px(16)), accent);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(Theme.Px(34), 0, Width - Theme.Px(56), Height), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            // 「+」做成一个小方钮，hover 时填充，明确表示“点这里添加”
            Rectangle plus = new Rectangle(Width - Theme.Px(26), (Height - Theme.Px(18)) / 2, Theme.Px(18), Theme.Px(18));
            using (System.Drawing.Drawing2D.GraphicsPath pp = Theme.RoundedRect(plus, 4))
            {
                using (SolidBrush pb = new SolidBrush(_hover ? Theme.Mix(Theme.Panel, accent, 0.18f) : Theme.PanelAlt))
                    g.FillPath(pb, pp);
                using (Pen pen = new Pen(_hover ? accent : Theme.Border)) g.DrawPath(pen, pp);
            }
            TextRenderer.DrawText(g, "+", Theme.UiFontCached(10f, FontStyle.Bold), plus,
                _hover ? accent : Theme.TextDim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

    /// <summary>积木库分组标题：明显的三角箭头 + 分类色块，一眼看出能点开。</summary>
    internal sealed class PaletteGroupHeader : Control
    {
        private readonly string _group;
        private readonly Color _color;
        private readonly bool _collapsed;
        private bool _hover;

        public event Action ToggleRequested;

        public PaletteGroupHeader(string group, Color color, bool collapsed)
        {
            _group = group;
            _color = color;
            _collapsed = collapsed;
            Cursor = Cursors.Hand;
            Font = Theme.UiFont(9.5f, FontStyle.Bold);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            Action h = ToggleRequested;
            if (h != null) h();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (_hover)
            {
                using (SolidBrush bg = new SolidBrush(Theme.PanelAlt))
                    g.FillRectangle(bg, 0, 0, Width, Height);
            }
            // 分类色竖条
            using (SolidBrush bar = new SolidBrush(_color))
                g.FillRectangle(bar, Theme.Px(2), Theme.Px(6), Theme.Px(3), Height - Theme.Px(12));
            // 三角箭头（收起 ▶ / 展开 ▼），用分类色，比原来的小字更醒目
            float cx = Theme.Px(16);
            float cy = Height / 2f;
            float s = Theme.Px(5);
            using (SolidBrush ab = new SolidBrush(_collapsed ? _color : Theme.Mix(_color, Theme.Text, 0.2f)))
            using (GraphicsPath tri = new GraphicsPath())
            {
                if (_collapsed)
                    tri.AddPolygon(new PointF[] { new PointF(cx - s / 2, cy - s), new PointF(cx + s / 2 + 1, cy), new PointF(cx - s / 2, cy + s) });
                else
                    tri.AddPolygon(new PointF[] { new PointF(cx - s, cy - s / 2), new PointF(cx + s, cy - s / 2), new PointF(cx, cy + s / 2 + 1) });
                g.FillPath(ab, tri);
            }
            TextRenderer.DrawText(g, _group, Font,
                new Rectangle(Theme.Px(28), 0, Width - Theme.Px(32), Height),
                _hover ? Theme.Text : Theme.Mix(Theme.Text, _color, 0.35f),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

    /// <summary>积木库折叠态的窄条按钮：图标 + 分类名，点一下就展开该分类。</summary>
    internal sealed class PaletteRailChip : Control
    {
        private readonly string _group;
        private readonly string _icon;
        private readonly Color _color;
        private bool _hover;

        public event Action OpenRequested;

        public PaletteRailChip(string group, string icon, Color color)
        {
            _group = group;
            _icon = icon;
            _color = color;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            Action h = OpenRequested;
            if (h != null) h();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color fill = _hover ? Theme.Mix(Theme.Panel, _color, 0.14f) : Theme.Panel;
            using (GraphicsPath p = Theme.RoundedRect(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 6f))
            {
                using (SolidBrush b = new SolidBrush(fill)) g.FillPath(b, p);
                using (Pen pen = new Pen(Theme.Mix(Theme.Border, _color, _hover ? 0.5f : 0.2f))) g.DrawPath(pen, p);
            }
            float iconSize = Theme.Px(18);
            BlockIcons.Draw(g, _icon, new RectangleF((Width - iconSize) / 2f, Theme.Px(7), iconSize, iconSize), _color);
            TextRenderer.DrawText(g, _group, Theme.UiFontCached(8.5f, FontStyle.Regular),
                new Rectangle(0, Theme.Px(27), Width, Height - Theme.Px(28)),
                _hover ? Theme.Text : Theme.TextDim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak);
        }
    }

    public sealed class MainForm : Form
    {
        private sealed class RunSession
        {
            public Engine Engine;
            public Thread Thread;
            public ScriptDocument Doc;
            public string Key;
            public bool Primary;
            public string Source;
        }

        private readonly AppConfig _config;
        private readonly Logger _logger;
        private readonly DesktopAutomation _auto;
        private readonly Scheduler _scheduler;
        private readonly HotkeyManager _hotkeys;
        private readonly List<RunSession> _sessions = new List<RunSession>();

        private ScriptDocument _doc;
        private string _functionName;
        private bool _dirty;

        private BlockCanvas _canvas;
        private RichTextBox _codeView;
        private RichTextBox _logBox;
        private Label _contextLabel;
        private FlatButton _backButton;
        private FlatButton _canvasTab;
        private FlatButton _codeTab;
        private FlatButton _runButton;
        private FlatButton _pauseButton;
        private FlatButton _stopButton;
        private FlatButton _stepButton;
        private ToolStripStatusLabel _statusLeft;
        private ToolStripStatusLabel _statusZoom;
        private ToolStripStatusLabel _statusBlocks;
        private ToolStripStatusLabel _statusScript;
        private ToolStripStatusLabel _statusAi;
        private ToolStripStatusLabel _statusVersion;
        private FlowLayoutPanel _paletteFlow;
        private TextBox _search;
        private ListBox _functionList;
        private Panel _leftPanel;
        private System.Windows.Forms.Timer _timer;
        private AiHttpServer _aiServer;
        private string _recorderSnapshot = "";
        private FlatButton _recordButton;
        private Panel _funcPanel;
        private TableLayoutPanel _leftGrid;
        private TableLayoutPanel _middleGrid;
        private NotifyIcon _tray;
        private readonly Dictionary<string, bool> _groupCollapsed = new Dictionary<string, bool>();
        private bool _realExit;
        private bool _logCollapsed = true;
        private Panel _logPanel;
        private TableLayoutPanel _rootGrid;
        private Label _logToggle;
        private SideParamPanel _sidePanel;
        private Panel _contentPanel;
        private Panel _railPanel;
        private Panel _palettePanel;
        private System.Windows.Forms.Timer _drawerTimer;
        private int _drawerTargetLeft;
        private bool _drawerOpen;
        private bool _drawerManualHide;
        private FlowLayoutPanel _railFlow;
        private FlatButton _railToggle;
        private int _trayHotkeyId = 0xBEEF;
        private bool _codeVisible;

        public MainForm()
        {
            AutoScaleMode = AutoScaleMode.None;
            _config = AppConfig.Load();
            Theme.ApplyTheme(_config.ThemeMode);
            _logger = new Logger(_config.EffectiveLogDir, _config.LogRetentionDays);
            _logger.EntryWritten += OnLogEntry;
            _auto = new DesktopAutomation();
            _auto.CacheMilliseconds = _config.ScreenshotCacheMs;
            _scheduler = Scheduler.Load();
            _scheduler.TaskDue += OnScheduleDue;
            _hotkeys = new HotkeyManager();
            _hotkeys.Triggered += OnHotkey;

            BuildUi();
            ApplyDoc(CreateNewDocument(), null, false);
            ApplyMode();
            SetPaletteExpanded(false);   // v2.5：积木库默认折叠成窄条，把宽度留给主流程

            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = 1000;
            _timer.Tick += delegate
            {
                try { _scheduler.Tick(DateTime.Now); }
                catch (Exception ex) { _logger.Write(new LogEntry { Level = LogLevel.Error, Source = "定时", Message = ex.Message }); }
            };
            _timer.Start();

            StartAiServerIfEnabled();
            Shown += delegate
            {
                PerformLayout();
                SyncLayoutWidths();
                _canvas.FitToView();
                _canvas.Focus();
                InitializeTray();
                if (!_config.FirstRunDone && !Program.SuppressWelcome) ShowWelcome();
            };
            FormClosing += OnClosing;
        }

        // ---------------------------- 界面搭建 ----------------------------

        private void BuildUi()
        {
            Text = "BlockAuto 积木自动化平台";
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            int wantW = Math.Min(Theme.Px(1120), Math.Max(Theme.Px(720), wa.Width - Theme.Px(60)));
            int wantH = Math.Min(Theme.Px(760), Math.Max(Theme.Px(500), wa.Height - Theme.Px(60)));
            Size = new Size(wantW, wantH);
            MinimumSize = new Size(Math.Min(Theme.Px(720), wa.Width), Math.Min(Theme.Px(460), wa.Height));
            StartPosition = FormStartPosition.CenterScreen;
            Theme.ApplyDarkForm(this);

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 5;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            root.BackColor = Theme.Background;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.Px(44)));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.Px(30)));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.Px(24)));
            _rootGrid = root;
            Controls.Add(root);

            root.Controls.Add(BuildToolbar(), 0, 0);

            // 中心区域：左侧积木库（默认收成窄条）+ 主流程列表（占满宽度，参数栏改成抽屉悬浮）
            TableLayoutPanel middle = new TableLayoutPanel();
            middle.Dock = DockStyle.Fill;
            middle.ColumnCount = 2;
            middle.RowCount = 1;
            middle.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Theme.Px(70)));
            middle.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            middle.Margin = new Padding(0);
            root.Controls.Add(middle, 0, 2);

            _leftPanel = BuildLeftPanel();
            _middleGrid = middle;
            middle.Controls.Add(_leftPanel, 0, 0);
            middle.Controls.Add(BuildCenter(), 1, 0);

            root.Controls.Add(BuildLogPanel(), 0, 3);
            root.Controls.Add(BuildStatus(), 0, 4);

            _drawerTimer = new System.Windows.Forms.Timer();
            _drawerTimer.Interval = 15;
            _drawerTimer.Tick += delegate { StepDrawer(); };

            WireShortcuts();
        }

        private Control BuildToolbar()
        {
            Panel top = new Panel();
            top.Dock = DockStyle.Fill;
            top.BackColor = Theme.Panel;
            top.Padding = new Padding(0, 0, 0, 1);

            TableLayoutPanel grid = new TableLayoutPanel();
            grid.Dock = DockStyle.Fill;
            grid.ColumnCount = 2;
            grid.RowCount = 1;
            grid.Padding = new Padding(8, 5, 8, 5);
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Theme.Px(164)));
            top.Controls.Add(grid);

            FlowLayoutPanel flow = new FlowLayoutPanel();
            flow.Dock = DockStyle.Fill;
            flow.FlowDirection = FlowDirection.LeftToRight;
            flow.WrapContents = false;
            flow.BackColor = Theme.Panel;
            grid.Controls.Add(flow, 0, 0);

            _recordButton = BigButton("录制", Theme.Danger, delegate { OpenRecorder(); }, 84);
            _recordButton.IconKind = "record";
            _runButton = BigButton("运行", Theme.Success, delegate { RunPrimary(false); }, 78);
            _runButton.IconKind = "run";
            _stopButton = BigButton("停止", Theme.Danger, delegate { StopAll(); }, 78);
            _stopButton.IconKind = "stop";
            _stopButton.BackColor = Theme.Mix(Theme.Panel, Theme.Danger, 0.08f);
            _stopButton.ForeColor = Theme.Mix(Theme.Danger, Theme.TextDim, 0.25f);
            _stopButton.Enabled = false;

            _pauseButton = SmallButton("暂停", delegate { TogglePause(); });
            _pauseButton.Enabled = false;
            _stepButton = SmallButton("单步", delegate { StepPrimary(); });

            FlatButton shot = SmallButton("◎ 截图位置", delegate { CapturePositionForSelection(); });
            shot.Width = Theme.Px(100);
            shot.ForeColor = Theme.Accent;
            shot.BackColor = Theme.Mix(Theme.Panel, Theme.Accent, 0.10f);

            flow.Controls.Add(_recordButton);
            flow.Controls.Add(_runButton);
            flow.Controls.Add(_stopButton);
            flow.Controls.Add(_pauseButton);
            flow.Controls.Add(_stepButton);
            flow.Controls.Add(Gap(6));
            flow.Controls.Add(shot);

            FlowLayoutPanel rightFlow = new FlowLayoutPanel();
            rightFlow.Dock = DockStyle.Fill;
            rightFlow.FlowDirection = FlowDirection.RightToLeft;
            rightFlow.WrapContents = false;
            rightFlow.BackColor = Theme.Panel;
            grid.Controls.Add(rightFlow, 1, 0);

            FlatButton more = Ui.Button("更多 ▾", null);
            more.Width = Theme.Px(74);
            more.Height = Theme.Px(32);
            more.Outline = true;
            more.Margin = new Padding(4, 0, 0, 0);
            more.Click += delegate { ShowMoreMenu(more); };
            FlatButton file = Ui.Button("文件 ▾", null);
            file.Width = Theme.Px(74);
            file.Height = Theme.Px(32);
            file.Outline = true;
            file.Margin = new Padding(4, 0, 0, 0);
            file.Click += delegate { ShowFileMenu(file); };
            rightFlow.Controls.Add(more);
            rightFlow.Controls.Add(file);
            return top;
        }

        private static Panel Gap(int width)
        {
            Panel p = new Panel();
            p.Width = Theme.Px(width);
            p.Height = 1;
            p.Margin = new Padding(0);
            return p;
        }

        private static FlatButton SmallButton(string text, EventHandler h)
        {
            FlatButton b = Ui.Button(text, h);
            b.Width = Theme.Px(56);
            b.Height = Theme.Px(32);
            b.Outline = true;
            b.Margin = new Padding(4, 0, 0, 0);
            return b;
        }

        private void ShowFileMenu(Control anchor)
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.BackColor = Theme.Panel;
            menu.ForeColor = Theme.Text;
            menu.Renderer = new DarkMenuRenderer();
            menu.ShowImageMargin = false;
            AddMenu(menu, "新建脚本", delegate { NewDocument(); });
            AddMenu(menu, "打开脚本…", delegate { OpenDocument(); });
            AddMenu(menu, "保存", delegate { SaveDocument(false); });
            AddMenu(menu, "另存为副本…", delegate { SaveDocument(true); });
            menu.Items.Add(new ToolStripSeparator());
            AddMenu(menu, "让 .block 双击直接运行（关联运行器）", delegate { AssociateBlockFiles(false); });
            AddMenu(menu, "取消 .block 关联", delegate { AssociateBlockFiles(true); });
            menu.Items.Add(new ToolStripSeparator());
            AddMenu(menu, "导出可运行包（zip，含运行器）…", delegate { OpenPackager(); });
            menu.Items.Add(new ToolStripSeparator());
            AddMenu(menu, "在资源管理器中显示", delegate
            {
                try
                {
                    string p = string.IsNullOrEmpty(_doc.FilePath) ? Environment.CurrentDirectory : Path.GetDirectoryName(_doc.FilePath);
                    Process.Start("explorer.exe", "\"" + p + "\"");
                }
                catch { }
            });
            AddMenu(menu, "退出", delegate { _realExit = true; Close(); });
            menu.Show(anchor, new Point(0, anchor.Height + 2));
        }
        private static FlatButton BigButton(string text, Color color, EventHandler onClick, int width)
        {
            FlatButton b = Ui.Button(text, onClick);
            b.Width = Theme.Px(width);
            b.Height = Theme.Px(32);
            // v2.5：色块调淡，只在文字和图标上用强调色，不再抢视觉
            b.BackColor = Theme.Mix(Theme.Panel, color, 0.13f);
            b.ForeColor = Theme.Darken(color, Theme.IsLight ? 0.92f : 1.05f);
            b.HoverColor = Theme.Mix(Theme.Panel, color, 0.22f);
            b.PressColor = color;
            b.Outline = true;
            b.Margin = new Padding(0, 0, 6, 0);
            b.Font = Theme.UiFont(10f, FontStyle.Bold);
            return b;
        }

        private void ShowAddActionMenu(Control anchor)
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.BackColor = Theme.Panel;
            menu.ForeColor = Theme.Text;
            menu.Renderer = new DarkMenuRenderer();
            menu.ShowImageMargin = false;
            AddMenu(menu, "鼠标点击", delegate { _canvas.AddBlockType("mouse_click"); });
            AddMenu(menu, "鼠标移动", delegate { _canvas.AddBlockType("mouse_move"); });
            AddMenu(menu, "鼠标滚轮", delegate { _canvas.AddBlockType("mouse_wheel"); });
            AddMenu(menu, "输入文本", delegate { _canvas.AddBlockType("key_type"); });
            AddMenu(menu, "按键 / 快捷键", delegate { _canvas.AddBlockType("key_press"); });
            AddMenu(menu, "等待（延时）", delegate { _canvas.AddBlockType("sleep"); });
            menu.Items.Add(new ToolStripSeparator());
            AddMenu(menu, "找色", delegate { _canvas.AddBlockType("find_color"); });
            AddMenu(menu, "日志", delegate { _canvas.AddBlockType("log"); });
            menu.Items.Add(new ToolStripSeparator());
            AddMenu(menu, "如果 / 否则（可判断“有图片”）", delegate { _canvas.AddBlockType("if"); });
            AddMenu(menu, "当…时循环", delegate { _canvas.AddBlockType("while"); });
            AddMenu(menu, "循环 N 次", delegate { _canvas.AddBlockType("for_count"); });
            menu.Items.Add(new ToolStripSeparator());
            AddMenu(menu, "◎ 截图位置（框选取点）", delegate { CapturePositionForSelection(); });
            AddMenu(menu, "📷 截图取图（存为识图模板，高级）", delegate { CaptureTemplateForSelection(); });
            menu.Show(anchor, new Point(0, anchor.Height + 2));
        }

        private void ShowMoreMenu(Control anchor)
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.BackColor = Theme.PanelAlt;
            menu.ForeColor = Theme.Text;
            menu.Renderer = new DarkMenuRenderer();
            menu.ShowImageMargin = false;
            AddMenu(menu, "另存为…", delegate { SaveDocument(true); });
            AddMenu(menu, _sidePanel != null && _sidePanel.Visible ? "隐藏右侧参数栏" : "显示右侧参数栏", delegate { ToggleSidePanel(); });
            menu.Items.Add(new ToolStripSeparator());
            AddMenu(menu, "模板库", delegate { OpenTemplates(); });
            AddMenu(menu, "全局热键", delegate { OpenHotkeys(); });
            AddMenu(menu, "定时任务", delegate { OpenSchedules(); });
            AddMenu(menu, "打包脚本…", delegate { OpenPackager(); });
            AddMenu(menu, "联网功能…", delegate { OpenNetwork(); });
            menu.Items.Add(new ToolStripSeparator());
            AddMenu(menu, _config.IsSimpleMode ? "切换到专业模式" : "切换到简洁模式", delegate { ToggleMode(); });
            AddMenu(menu, Theme.IsLight ? "切换到深色主题" : "切换到浅色主题", delegate
            {
                _config.ThemeMode = Theme.IsLight ? "dark" : "light";
                _config.Save();
                MessageBox.Show(this, "主题将在下次启动时生效。", "BlockAuto", MessageBoxButtons.OK, MessageBoxIcon.Information);
            });
            AddMenu(menu, "设置", delegate { OpenSettings(); });
            menu.Items.Add(new ToolStripSeparator());
            AddMenu(menu, "退出", delegate { _realExit = true; Close(); });
            menu.Show(anchor, new Point(0, anchor.Height + 2));
        }

        private static void AddMenu(ContextMenuStrip menu, string text, EventHandler h)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text);
            item.Click += h;
            menu.Items.Add(item);
        }
        private static FlatButton ToolButton(string text, EventHandler onClick)
        {
            FlatButton b = Ui.Button(text, onClick);
            b.Width = 62;
            return b;
        }

        private static Panel Separator()
        {
            Panel p = new Panel();
            p.Width = 9;
            p.Height = 26;
            p.Margin = new Padding(4, 3, 4, 3);
            Label line = new Label();
            line.BackColor = Theme.Border;
            line.Width = 1;
            line.Height = 22;
            line.Location = new Point(4, 2);
            p.Controls.Add(line);
            return p;
        }

        private Panel BuildLeftPanel()
        {
            Panel left = new Panel();
            left.Dock = DockStyle.Fill;
            left.BackColor = Theme.Panel;

            _palettePanel = BuildPalettePanel();
            _railPanel = BuildRail();
            left.Controls.Add(_palettePanel);
            left.Controls.Add(_railPanel);
            _palettePanel.Visible = false;

            RebuildPalette();
            RebuildRail();
            return left;
        }

        /// <summary>折叠态：一条窄栏，图标+名称直接点开对应分类（v2.5 默认状态）。</summary>
        private Panel BuildRail()
        {
            Panel rail = new Panel();
            rail.Dock = DockStyle.Fill;
            rail.BackColor = Theme.PanelAlt;

            TableLayoutPanel grid = new TableLayoutPanel();
            grid.Dock = DockStyle.Fill;
            grid.ColumnCount = 1;
            grid.RowCount = 2;
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.Px(34)));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            rail.Controls.Add(grid);

            FlatButton open = Ui.Button("»", null);
            open.Dock = DockStyle.Fill;
            open.Font = Theme.UiFont(11f, FontStyle.Bold);
            open.ForeColor = Theme.Accent;
            open.BackColor = Theme.Panel;
            open.Click += delegate { SetPaletteExpanded(true); };
            ToolTip tip = new ToolTip();
            tip.SetToolTip(open, "展开积木库");
            _railToggle = open;
            grid.Controls.Add(open, 0, 0);

            _railFlow = new FlowLayoutPanel();
            _railFlow.Dock = DockStyle.Fill;
            _railFlow.FlowDirection = FlowDirection.TopDown;
            _railFlow.WrapContents = false;
            _railFlow.AutoScroll = true;
            _railFlow.BackColor = Theme.PanelAlt;
            _railFlow.Padding = new Padding(Theme.Px(2), Theme.Px(4), Theme.Px(2), Theme.Px(4));
            grid.Controls.Add(_railFlow, 0, 1);
            return rail;
        }

        private void RebuildRail()
        {
            if (_railFlow == null) return;
            _railFlow.SuspendLayout();
            _railFlow.Controls.Clear();
            foreach (string group in Catalog.GroupOrder)
            {
                string icon = RailIconFor(group);
                PaletteRailChip chip = new PaletteRailChip(RailLabelFor(group), icon, RailColorFor(group));
                chip.Width = Theme.Px(60);
                chip.Height = Theme.Px(52);
                chip.Margin = new Padding(Theme.Px(3), Theme.Px(3), Theme.Px(3), Theme.Px(3));
                string g = group;
                chip.OpenRequested += delegate { OpenGroupFromRail(g); };
                _railFlow.Controls.Add(chip);
            }
            _railFlow.ResumeLayout();
        }

        private static string RailIconFor(string group)
        {
            switch (group)
            {
                case "键鼠动作": return "mouse_click";
                case "识别": return "find_color";
                case "流程控制": return "if";
                case "变量与条件": return "create_var";
                case "函数": return "call_func";
                default: return "log";
            }
        }

        /// <summary>窄条上放两个字就够（完整名字展开后能看到），避免文字换行被裁掉。</summary>
        private static string RailLabelFor(string group)
        {
            switch (group)
            {
                case "键鼠动作": return "键鼠";
                case "识别": return "识别";
                case "流程控制": return "流程";
                case "变量与条件": return "变量";
                case "函数": return "函数";
                default: return "其他";
            }
        }

        private static Color RailColorFor(string group)
        {
            switch (group)
            {
                case "键鼠动作": return Theme.CategoryAccent(BlockCategory.Action);
                case "识别": return Theme.CategoryAccent(BlockCategory.Action);
                case "流程控制": return Theme.CategoryAccent(BlockCategory.Logic);
                case "变量与条件": return Theme.CategoryAccent(BlockCategory.Var);
                case "函数": return Theme.CategoryAccent(BlockCategory.Func);
                default: return Theme.CategoryAccent(BlockCategory.Tool);
            }
        }

        /// <summary>点窄栏里的分类：展开积木库并只展开这一组。</summary>
        private void OpenGroupFromRail(string group)
        {
            foreach (string g in Catalog.GroupOrder) _groupCollapsed[g] = g != group;
            _groupCollapsed[group] = false;
            if (_search != null) _search.Text = "";
            SetPaletteExpanded(true);
        }

        /// <summary>积木库展开/收起（窄条 ↔ 完整列表）。</summary>
        private void SetPaletteExpanded(bool expand)
        {
            if (_middleGrid == null || _leftPanel == null) return;
            if (_railPanel != null) _railPanel.Visible = !expand;
            if (_palettePanel != null) _palettePanel.Visible = expand;
            _middleGrid.ColumnStyles[0].Width = expand ? Theme.Px(212) : Theme.Px(70);
            if (expand) RebuildPalette();
        }

        private bool IsPaletteExpanded
        {
            get { return _palettePanel != null && _palettePanel.Visible; }
        }

        private Panel BuildPalettePanel()
        {
            Panel panel = new Panel();
            panel.Dock = DockStyle.Fill;
            panel.BackColor = Theme.Panel;

            TableLayoutPanel grid = new TableLayoutPanel();
            grid.Dock = DockStyle.Fill;
            grid.ColumnCount = 1;
            grid.RowCount = 3;
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.Px(76)));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.Px(168)));
            _leftGrid = grid;
            panel.Controls.Add(grid);

            Panel head = new Panel();
            head.Dock = DockStyle.Fill;
            head.BackColor = Theme.Panel;
            FlatButton collapse = Ui.Button("«", null);
            collapse.Width = Theme.Px(26);
            collapse.Height = Theme.Px(24);
            collapse.Location = new Point(Theme.Px(2), Theme.Px(4));
            collapse.Font = Theme.UiFont(11f, FontStyle.Bold);
            collapse.ForeColor = Theme.TextDim;
            collapse.Click += delegate { SetPaletteExpanded(false); };
            head.Controls.Add(collapse);
            Label title = new Label();
            title.Text = "积木库";
            title.Font = Theme.UiFont(10.5f, FontStyle.Bold);
            title.ForeColor = Theme.Text;
            title.AutoSize = true;
            title.Location = new Point(Theme.Px(32), Theme.Px(8));
            head.Controls.Add(title);
            _search = new TextBox();
            _search.Location = new Point(Theme.Px(8), Theme.Px(38));
            _search.Width = Theme.Px(186);
            _search.Text = "";
            Theme.ApplyDarkTextBox(_search);
            head.Controls.Add(_search);
            Label ph = new Label();
            ph.Text = "搜索积木…";
            ph.ForeColor = Theme.TextDim;
            ph.Font = Theme.UiFont(8.5f, FontStyle.Regular);
            ph.Location = new Point(Theme.Px(12), Theme.Px(41));
            ph.AutoSize = true;
            ph.BackColor = Theme.PanelAlt;
            ph.Cursor = Cursors.IBeam;
            ph.Click += delegate { _search.Focus(); };
            head.Controls.Add(ph);
            _search.TextChanged += delegate
            {
                ph.Visible = _search.Text.Length == 0;
                RebuildPalette();
            };
            head.Resize += delegate { _search.Width = Math.Max(Theme.Px(80), head.Width - Theme.Px(18)); };
            grid.Controls.Add(head, 0, 0);

            _paletteFlow = new FlowLayoutPanel();
            _paletteFlow.Dock = DockStyle.Fill;
            _paletteFlow.FlowDirection = FlowDirection.TopDown;
            _paletteFlow.WrapContents = false;
            _paletteFlow.AutoScroll = true;
            _paletteFlow.BackColor = Theme.Panel;
            _paletteFlow.Padding = new Padding(0, 4, 0, 4);
            grid.Controls.Add(_paletteFlow, 0, 1);

            Panel funcs = new Panel();
            funcs.Dock = DockStyle.Fill;
            funcs.BackColor = Theme.Panel;
            _funcPanel = funcs;
            Label fl = new Label();
            fl.Text = "函数";
            fl.Font = Theme.UiFont(10f, FontStyle.Bold);
            fl.ForeColor = Theme.Text;
            fl.Location = new Point(Theme.Px(12), Theme.Px(6));
            fl.AutoSize = true;
            funcs.Controls.Add(fl);

            _functionList = new ListBox();
            _functionList.Location = new Point(Theme.Px(12), Theme.Px(30));
            _functionList.Size = new Size(Theme.Px(186), Theme.Px(92));
            _functionList.BackColor = Theme.PanelAlt;
            _functionList.ForeColor = Theme.Text;
            _functionList.BorderStyle = BorderStyle.FixedSingle;
            _functionList.SelectedIndexChanged += delegate
            {
                if (_functionList.SelectedItem == null) return;
                FunctionDef f = _functionList.SelectedItem as FunctionDef;
                if (f != null) EnterFunction(f.Name);
            };
            funcs.Controls.Add(_functionList);

            FlowLayoutPanel fbtn = new FlowLayoutPanel();
            fbtn.Location = new Point(Theme.Px(8), Theme.Px(130));
            fbtn.Size = new Size(Theme.Px(200), Theme.Px(34));
            fbtn.WrapContents = false;
            fbtn.FlowDirection = FlowDirection.LeftToRight;
            FlatButton add = Ui.Button("新建函数", delegate { NewFunction(); });
            add.Width = Theme.Px(96);
            add.Height = Theme.Px(28);
            add.Margin = new Padding(2, 0, 4, 0);
            FlatButton edit = Ui.Button("编辑", delegate { EditFunction(); });
            edit.Width = Theme.Px(48);
            edit.Height = Theme.Px(28);
            edit.Margin = new Padding(2, 0, 4, 0);
            FlatButton del = Ui.Button("删除", delegate { DeleteFunction(); });
            del.Width = Theme.Px(48);
            del.Height = Theme.Px(28);
            del.Margin = new Padding(2, 0, 0, 0);
            fbtn.Controls.Add(add);
            fbtn.Controls.Add(edit);
            fbtn.Controls.Add(del);
            funcs.Controls.Add(fbtn);
            grid.Controls.Add(funcs, 0, 2);
            return panel;
        }
        private Control BuildCenter()
        {
            Panel center = new Panel();
            center.Dock = DockStyle.Fill;
            center.BackColor = Theme.Canvas;

            TableLayoutPanel grid = new TableLayoutPanel();
            grid.Dock = DockStyle.Fill;
            grid.ColumnCount = 1;
            grid.RowCount = 2;
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.Px(40)));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            center.Controls.Add(grid);

            Panel head = new Panel();
            head.Dock = DockStyle.Fill;
            head.BackColor = Theme.Panel;
            FlatButton sidebar = Ui.Button("≡", delegate { ToggleSidebar(); });
            sidebar.Width = Theme.Px(30);
            sidebar.Height = Theme.Px(28);
            sidebar.Location = new Point(Theme.Px(6), Theme.Px(6));
            sidebar.Font = Theme.UiFont(11f, FontStyle.Bold);
            ToolTip tip = new ToolTip();
            tip.SetToolTip(sidebar, "展开 / 收起积木库");
            head.Controls.Add(sidebar);
            _contextLabel = new Label();
            _contextLabel.Text = "主流程";
            _contextLabel.ForeColor = Theme.Text;
            _contextLabel.Font = Theme.UiFont(10f, FontStyle.Bold);
            _contextLabel.Location = new Point(Theme.Px(46), Theme.Px(11));
            _contextLabel.AutoSize = true;
            head.Controls.Add(_contextLabel);
            _backButton = Ui.Button("← 主流程", delegate { EnterFunction(null); });
            _backButton.Width = Theme.Px(92);
            _backButton.Height = Theme.Px(26);
            _backButton.Location = new Point(Theme.Px(160), Theme.Px(6));
            _backButton.Visible = false;
            head.Controls.Add(_backButton);

            // 右上角三个按钮按右对齐依次排布，避免互相压住
            FlowLayoutPanel right = new FlowLayoutPanel();
            right.Dock = DockStyle.Right;
            right.Width = Theme.Px(246);
            right.FlowDirection = FlowDirection.RightToLeft;
            right.WrapContents = false;
            right.BackColor = Theme.Panel;
            right.Padding = new Padding(0, Theme.Px(6), Theme.Px(6), 0);
            _codeTab = Ui.Button("代码", delegate { SetCodeView(true); });
            _codeTab.Width = Theme.Px(60);
            _codeTab.Height = Theme.Px(28);
            _codeTab.Margin = new Padding(4, 0, 0, 0);
            _canvasTab = Ui.Button("画布", delegate { SetCodeView(false); });
            _canvasTab.Width = Theme.Px(60);
            _canvasTab.Height = Theme.Px(28);
            _canvasTab.Margin = new Padding(4, 0, 0, 0);
            _canvasTab.BackColor = Theme.Accent;
            _canvasTab.ForeColor = Color.White;
            FlatButton addAction = Ui.Button("＋ 添加动作", null);
            addAction.Width = Theme.Px(94);
            addAction.Height = Theme.Px(28);
            addAction.Margin = new Padding(8, 0, 0, 0);
            addAction.BackColor = Theme.Mix(Theme.Panel, Theme.Accent, 0.12f);
            addAction.ForeColor = Theme.Accent;
            addAction.Outline = true;
            addAction.Click += delegate { ShowAddActionMenu(addAction); };
            right.Controls.Add(_codeTab);
            right.Controls.Add(_canvasTab);
            right.Controls.Add(addAction);
            head.Controls.Add(right);
            grid.Controls.Add(head, 0, 0);

            // 主流程列表占满宽度；参数栏是浮在右侧的抽屉，不占位
            _contentPanel = new Panel();
            _contentPanel.Dock = DockStyle.Fill;
            _contentPanel.BackColor = Theme.Canvas;
            grid.Controls.Add(_contentPanel, 0, 1);

            _canvas = new BlockCanvas();
            _canvas.Dock = DockStyle.Fill;
            _canvas.DocumentChanged += delegate
            {
                _dirty = true;
                UpdateTitle();
                RefreshCode();
                UpdateCounts();
                RefreshSidePanel();
            };
            _canvas.ViewChanged += delegate { _statusZoom.Text = "步骤 " + Math.Max(0, _canvas.ItemCount - 1); };
            _canvas.SelectionChanged += delegate(Block b)
            {
                if (b == null)
                {
                    if (_sidePanel != null) _sidePanel.Bind(_doc, null, null, null);
                    ShowDrawer(false);
                    return;
                }
                _statusLeft.Text = "选中：" + Catalog.TitleOf(b.Type) + "（" + b.Id + "）";
                _drawerManualHide = false;
                RefreshSidePanel();
                ShowDrawer(true);
            };
            _canvas.BlockEditRequested += OnEditBlock;
            _canvas.RecordClickRequested += OnRecordClick;
            _canvas.CaptureTemplateRequested += OnCaptureTemplate;
            _canvas.CapturePositionRequested += OnCapturePosition;
            _contentPanel.Controls.Add(_canvas);

            _codeView = new RichTextBox();
            _codeView.Dock = DockStyle.Fill;
            _codeView.ReadOnly = true;
            _codeView.BackColor = Theme.Canvas;
            _codeView.ForeColor = Theme.IsLight ? Theme.Text : Color.FromArgb(0xD8, 0xDE, 0xE9);
            _codeView.Font = Theme.MonoFont(10f, FontStyle.Regular);
            _codeView.BorderStyle = BorderStyle.None;
            _codeView.WordWrap = false;
            _codeView.Visible = false;
            _codeView.DetectUrls = false;
            _codeView.ScrollBars = RichTextBoxScrollBars.Both;
            _contentPanel.Controls.Add(_codeView);

            _sidePanel = new SideParamPanel();
            _sidePanel.Dock = DockStyle.None;
            _sidePanel.Visible = false;
            _sidePanel.RecordRequested += OnRecordClick;
            _sidePanel.CapturePositionRequested += OnCapturePosition;
            _sidePanel.EditBlockRequested += OnEditBlock;
            _sidePanel.CloseRequested += delegate { _drawerManualHide = true; ShowDrawer(false); };
            _contentPanel.Controls.Add(_sidePanel);
            _contentPanel.SizeChanged += delegate { LayoutDrawer(); };

            // 抽屉浮在最上层；代码视图打开时自己再提到最前
            _contentPanel.Controls.SetChildIndex(_sidePanel, _contentPanel.Controls.Count - 1);
            return center;
        }

        // ---------------------------- 参数抽屉（悬浮在右侧，选中步骤才滑出） ----------------------------

        private void ShowDrawer(bool show)
        {
            if (_sidePanel == null || _contentPanel == null || _drawerTimer == null) return;
            if (show && (_codeVisible || _drawerManualHide)) return;
            if (show == _drawerOpen && _sidePanel.Visible == show) return;
            _drawerOpen = show;
            int w = Theme.Px(300);
            if (show)
            {
                _sidePanel.Width = w;
                _sidePanel.Height = _contentPanel.ClientSize.Height;
                _sidePanel.Top = 0;
                if (!_sidePanel.Visible)
                {
                    _sidePanel.Left = _contentPanel.ClientSize.Width;
                    _sidePanel.Visible = true;
                }
                _contentPanel.Controls.SetChildIndex(_sidePanel, _contentPanel.Controls.Count - 1);
                _drawerTargetLeft = _contentPanel.ClientSize.Width - w;
            }
            else
            {
                _drawerTargetLeft = _contentPanel.ClientSize.Width;
            }
            _drawerTimer.Start();
        }

        private void StepDrawer()
        {
            if (_sidePanel == null || _contentPanel == null)
            {
                _drawerTimer.Stop();
                return;
            }
            int dx = _drawerTargetLeft - _sidePanel.Left;
            if (Math.Abs(dx) <= Theme.Px(2))
            {
                _sidePanel.Left = _drawerTargetLeft;
                _drawerTimer.Stop();
                if (!_drawerOpen) _sidePanel.Visible = false;
                return;
            }
            int step = Math.Max(Theme.Px(16), Math.Abs(dx) / 2);
            _sidePanel.Left += dx > 0 ? Math.Min(step, dx) : -Math.Min(step, -dx);
        }

        /// <summary>把抽屉贴到当前内容区的右边缘（内容区尺寸一变就重新摆一次，避免跑到窗口外）。</summary>
        private void LayoutDrawer()
        {
            if (_sidePanel == null || _contentPanel == null) return;
            int w = Theme.Px(300);
            int h = Math.Max(0, _contentPanel.ClientSize.Height);

            _sidePanel.Size = new Size(w, h);
            _sidePanel.Top = 0;
            int openLeft = Math.Max(0, _contentPanel.ClientSize.Width - w);
            if (!_drawerOpen)
            {
                _sidePanel.Left = _contentPanel.ClientSize.Width;
                _drawerTargetLeft = _sidePanel.Left;
                return;
            }
            _sidePanel.Left = openLeft;
            _drawerTargetLeft = openLeft;
        }


        /// <summary>「截图位置」：框选屏幕上要点的目标，把选区中心写成点击坐标。</summary>
        private void OnCapturePosition(Block b)
        {
            if (b == null) return;
            Rectangle r = RoiPickerForm.Pick(this, "截图位置：框选要点击的目标（取选区中心）");
            if (r.Width < 2 || r.Height < 2) return;
            int cx = r.Left + r.Width / 2;
            int cy = r.Top + r.Height / 2;
            _canvas.SetRecordedPoint(b, cx, cy);
            _dirty = true;
            _logger.Write(new LogEntry
            {
                Level = LogLevel.Info,
                Source = "截图位置",
                Message = "第 " + (BlockIndex(b) + 1) + " 步取到坐标 (" + cx + ", " + cy + ")"
            });
            _statusLeft.Text = "已取到坐标 (" + cx + ", " + cy + ")";
            _canvas.NotifyDocumentChanged();
            RefreshSidePanel();
            ShowDrawer(true);
        }

        /// <summary>工具栏「◎ 截图位置」：给当前选中的鼠标点击步骤取坐标。</summary>
        private void CapturePositionForSelection()
        {
            if (_doc == null) return;
            Block b = _canvas != null ? _canvas.SelectedBlock : null;
            if (b == null || b.Type != "mouse_click")
            {
                _statusLeft.Text = "先在左边选中一个「鼠标点击」步骤，再点截图位置";
                MessageBox.Show(this,
                    "请先选中一个「鼠标点击」步骤，再点「◎ 截图位置」。"
                    + Environment.NewLine + Environment.NewLine
                    + "框选屏幕上要点击的目标，程序会把选区中心记成点击坐标。",
                    "截图位置", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            OnCapturePosition(b);
        }

        private void OnCaptureTemplate(Block b)
        {
            string saved = TemplateCapture.PickAndSave(this, _doc, "截图取图");
            if (saved == null) return;
            _canvas.ApplyCapturedTemplate(b, saved);
            _dirty = true;
            _logger.Write(new LogEntry
            {
                Level = LogLevel.Info,
                Source = "截图取图",
                Message = "第 " + (BlockIndex(b) + 1) + " 步已用新模板：" + saved + "（运行时自动识图并点击）"
            });
            _statusLeft.Text = "已截图取图：" + Path.GetFileName(saved);
            RefreshSidePanel();
            ShowDrawer(true);
        }

        /// <summary>工具栏「📷 截图取图」：截一块屏幕存成模板，选中鼠标点击时直接填进去。</summary>
        private void CaptureTemplateForSelection()
        {
            if (_doc == null) return;
            Block b = _canvas != null ? _canvas.SelectedBlock : null;
            string saved = TemplateCapture.PickAndSave(this, _doc, "截图取图");
            if (saved == null) return;
            if (b != null && b.Type == "mouse_click")
            {
                _canvas.ApplyCapturedTemplate(b, saved);
                _dirty = true;
                RefreshSidePanel();
                ShowDrawer(true);
                _statusLeft.Text = "已把截图填进第 " + (BlockIndex(b) + 1) + " 步：" + Path.GetFileName(saved);
            }
            else
            {
                _statusLeft.Text = "已保存模板：" + saved;
                MessageBox.Show(this,
                    "截图已保存为模板：" + saved + Environment.NewLine + Environment.NewLine +
                    "选中一个「鼠标点击」步骤后再点这个按钮，就会直接填进那一步（自动识图 + 点击）。",
                    "截图取图", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            _logger.Write(new LogEntry { Level = LogLevel.Info, Source = "截图取图", Message = "保存模板：" + saved });
        }

        private int BlockIndex(Block b)
        {
            if (_doc == null || b == null) return 0;
            List<Block> list = _doc.ParentListOf(b);
            return list == null ? 0 : list.IndexOf(b);
        }
        private Control BuildLogPanel()
        {
            Panel panel = new Panel();
            panel.Dock = DockStyle.Fill;
            panel.BackColor = Theme.Panel;
            panel.Padding = new Padding(8, 0, 8, 2);
            _logPanel = panel;

            TableLayoutPanel grid = new TableLayoutPanel();
            grid.Dock = DockStyle.Fill;
            grid.ColumnCount = 1;
            grid.RowCount = 2;
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.Px(30)));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            grid.BackColor = Theme.Panel;
            panel.Controls.Add(grid);

            Panel head = new Panel();
            head.Dock = DockStyle.Fill;
            head.Cursor = Cursors.Hand;
            head.Click += delegate { SetLogCollapsed(!_logCollapsed); };

            // v2.5：日志开关、日志目录、清空统一左对齐排一行
            FlowLayoutPanel headFlow = new FlowLayoutPanel();
            headFlow.Dock = DockStyle.Fill;
            headFlow.FlowDirection = FlowDirection.LeftToRight;
            headFlow.WrapContents = false;
            headFlow.BackColor = Theme.Panel;
            headFlow.Padding = new Padding(0, 2, 0, 2);
            head.Controls.Add(headFlow);

            Label l = new Label();
            l.Text = "日志 ▸";
            l.ForeColor = Theme.Text;
            l.Font = Theme.UiFont(9.5f, FontStyle.Bold);
            l.AutoSize = true;
            l.Margin = new Padding(2, 5, 10, 0);
            l.Cursor = Cursors.Hand;
            l.Click += delegate { SetLogCollapsed(!_logCollapsed); };
            _logToggle = l;
            headFlow.Controls.Add(l);

            FlatButton openDir = Ui.Button("日志目录", delegate
            {
                try { Process.Start("explorer.exe", "\"" + _config.EffectiveLogDir + "\""); }
                catch { }
            });
            openDir.Width = Theme.Px(76);
            openDir.Height = Theme.Px(22);
            openDir.Outline = true;
            openDir.Margin = new Padding(0, 1, 6, 0);
            headFlow.Controls.Add(openDir);

            FlatButton clear = Ui.Button("清空", delegate { _logBox.Clear(); });
            clear.Width = Theme.Px(52);
            clear.Height = Theme.Px(22);
            clear.Outline = true;
            clear.Margin = new Padding(0, 1, 0, 0);
            headFlow.Controls.Add(clear);
            grid.Controls.Add(head, 0, 0);

            _logBox = new RichTextBox();
            _logBox.Dock = DockStyle.Fill;
            _logBox.ReadOnly = true;
            _logBox.BackColor = Theme.IsLight ? Color.FromArgb(0xFA, 0xFB, 0xFC) : Color.FromArgb(0x14, 0x16, 0x1B);
            _logBox.ForeColor = Theme.Text;
            _logBox.Font = Theme.MonoFont(8.5f, FontStyle.Regular);
            _logBox.BorderStyle = BorderStyle.None;
            _logBox.WordWrap = true;
            _logBox.DetectUrls = false;
            _logBox.HideSelection = false;
            _logBox.Visible = false;
            grid.Controls.Add(_logBox, 0, 1);
            return panel;
        }

        private void SetLogCollapsed(bool collapsed)
        {
            _logCollapsed = collapsed;
            if (_logBox != null) _logBox.Visible = !collapsed;
            if (_logToggle != null) _logToggle.Text = collapsed ? "日志 ▸" : "日志 ▾";
            if (_rootGrid != null && _rootGrid.RowStyles.Count > 3)
                _rootGrid.RowStyles[3].Height = collapsed ? Theme.Px(30) : Theme.Px(158);
            if (_logPanel != null) _logPanel.Invalidate();
        }
        private Control BuildStatus()
        {
            StatusStrip strip = new StatusStrip();
            strip.BackColor = Theme.Panel;
            strip.ForeColor = Theme.TextDim;
            strip.SizingGrip = false;
            strip.Font = Theme.UiFont(8.5f, FontStyle.Regular);

            _statusLeft = new ToolStripStatusLabel("就绪");
            _statusScript = new ToolStripStatusLabel("");
            _statusBlocks = new ToolStripStatusLabel("");
            _statusZoom = new ToolStripStatusLabel("步骤 0");
            _statusAi = new ToolStripStatusLabel("AI 接口关闭");
            _statusVersion = new ToolStripStatusLabel("v" + EngineVersion.Display);
            _statusVersion.ForeColor = Theme.TextDim;
            _statusLeft.Spring = false;
            _statusScript.Spring = true;
            _statusScript.TextAlign = ContentAlignment.MiddleRight;
            strip.Items.Add(_statusLeft);
            strip.Items.Add(_statusScript);
            strip.Items.Add(_statusBlocks);
            strip.Items.Add(_statusZoom);
            strip.Items.Add(_statusAi);
            strip.Items.Add(_statusVersion);
            return strip;
        }

        private void WireShortcuts()
        {
            KeyPreview = true;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.S))
            {
                SaveDocument(false);
                return true;
            }
            if (keyData == (Keys.Control | Keys.Shift | Keys.S))
            {
                SaveDocument(true);
                return true;
            }
            if (keyData == (Keys.Control | Keys.O))
            {
                OpenDocument();
                return true;
            }
            if (keyData == (Keys.Control | Keys.N))
            {
                NewDocument();
                return true;
            }
            if (keyData == (Keys.Control | Keys.Z))
            {
                if (_canvas.Undo()) _dirty = true;
                return true;
            }
            if (keyData == (Keys.Control | Keys.Y))
            {
                if (_canvas.Redo()) _dirty = true;
                return true;
            }
            if (keyData == (Keys.Control | Keys.C))
            {
                _canvas.CopySelected();
                return true;
            }
            if (keyData == (Keys.Control | Keys.V))
            {
                _canvas.PasteClipboard();
                return true;
            }
            if (keyData == Keys.F5)
            {
                RunPrimary(false);
                return true;
            }
            if (keyData == Keys.F10)
            {
                StepPrimary();
                return true;
            }
            if (keyData == (Keys.Shift | Keys.F5))
            {
                StopAll();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ---------------------------- 文档管理 ----------------------------

        private void ApplyDoc(ScriptDocument doc, string path, bool dirty)
        {
            _doc = doc;
            _doc.FilePath = path ?? "";
            if (!_doc.Settings.ContainsKey("max_recursion"))
                _doc.Settings["max_recursion"] = (double)_config.MaxRecursion;
            if (!_doc.Settings.ContainsKey("default_threshold"))
                _doc.Settings["default_threshold"] = _config.DefaultThreshold;
            _functionName = null;
            _dirty = dirty;
            _canvas.SetDocument(_doc, null);
            RefreshFunctionList();
            UpdateContext();
            UpdateTitle();
            RefreshCode();
            UpdateCounts();
            _statusLeft.Text = "就绪";
            // 换脚本时把参数抽屉收回去，避免还显示上一个脚本的步骤
            _drawerManualHide = false;
            if (_sidePanel != null) _sidePanel.Bind(_doc, null, null, null);
            ShowDrawer(false);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.EnableDarkTitleBar(this);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            SyncLayoutWidths();
        }

        private void SyncLayoutWidths()
        {
            if (_rootGrid == null) return;
            _rootGrid.Size = ClientSize;
            if (_middleGrid != null) _middleGrid.Width = Math.Max(200, _rootGrid.ClientSize.Width);
            LayoutDrawer();
        }

        internal ScriptDocument CurrentDocument { get { return _doc; } }

        /// <summary>自检用：把参数抽屉直接摆到位（跳过滑出动画），方便截图核对。</summary>
        internal void SnapSidePanelForCheck()
        {
            RefreshSidePanel();
            _drawerManualHide = false;
            _drawerOpen = true;
            _drawerTimer.Stop();
            if (_sidePanel != null)
            {
                _sidePanel.Visible = true;
                LayoutDrawer();
                _contentPanel.Controls.SetChildIndex(_sidePanel, _contentPanel.Controls.Count - 1);
                _sidePanel.PerformLayout();
                _sidePanel.Refresh();
            }
        }

        /// <summary>立即收起参数抽屉（不走滑出动画）。</summary>
        private void HideDrawerNow()
        {
            if (_sidePanel == null) return;
            if (_drawerTimer != null) _drawerTimer.Stop();
            _drawerOpen = false;
            _sidePanel.Visible = false;
        }

        internal void HideSidePanelForCheck()
        {
            _drawerTimer.Stop();
            _drawerOpen = false;
            if (_sidePanel != null) _sidePanel.Visible = false;
        }

        /// <summary>自检：简洁模式下切代码视图必须被挡住（画布仍在、给出状态提示）。</summary>
        /// <summary>自检：容器拖放（把积木放进最后一个分支）。</summary>
        internal string DragDropCheckForCheck()
        {
            return _canvas != null ? _canvas.DropIntoBranchForCheck() : "nocanvas";
        }

        /// <summary>自检：走真实的“点选中”路径，看参数抽屉是否真的弹出并停在可视区内。</summary>
        internal string DrawerFlowForCheck()
        {
            if (_canvas == null || _doc == null || _doc.Blocks.Count == 0) return "skip 无积木";
            _drawerManualHide = false;
            _canvas.SelectBlock(_doc.Blocks[0]);
            DateTime until = DateTime.Now.AddMilliseconds(700);
            while (DateTime.Now < until) { Application.DoEvents(); System.Threading.Thread.Sleep(10); }
            bool vis = _sidePanel != null && _sidePanel.Visible;
            Rectangle b = _sidePanel != null ? _sidePanel.Bounds : Rectangle.Empty;
            bool inside = _contentPanel != null && b.Left >= 0 && b.Right <= _contentPanel.ClientSize.Width + 2 && b.Width > 100;
            return (vis && inside ? "OK" : "FAIL") + " visible=" + vis + " bounds=" + b
                + " content=" + (_contentPanel != null ? _contentPanel.ClientSize.ToString() : "-");
        }

        /// <summary>自检：按键积木的「全部参数…」弹窗能否正常打开（防止渲染卡死）。</summary>
        internal string KeyDialogForCheck()
        {
            if (_doc == null) return "skip";
            Block key = null;
            foreach (Block b in _doc.AllBlocks())
                if (b.Type == "key_press") { key = b; break; }
            if (key == null) return "skip 没有按键积木";
            DateTime t0 = DateTime.Now;
            try
            {
                using (ParamDialog d = new ParamDialog(_doc, key))
                {
                    d.StartPosition = FormStartPosition.Manual;
                    d.Location = new Point(-6000, -6000);
                    d.Show();
                    Application.DoEvents();
                    int controls = d.Controls.Count;
                    d.Close();
                    return "OK " + (int)(DateTime.Now - t0).TotalMilliseconds + "ms controls=" + controls;
                }
            }
            catch (Exception ex)
            {
                return "FAIL " + (int)(DateTime.Now - t0).TotalMilliseconds + "ms " + ex.GetType().Name + " " + ex.Message;
            }
        }

        internal string SimpleModeCodeGuardForCheck()
        {
            string oldMode = _config.UiMode;
            _config.UiMode = "simple";
            ApplyMode();
            SetCodeView(true);
            bool canvasStill = _canvas.Visible && !_codeView.Visible;
            string status = _statusLeft.Text;
            _config.UiMode = oldMode;
            ApplyMode();
            return (canvasStill ? "OK" : "FAIL") + " 画布仍可见=" + canvasStill + " 提示=" + status;
        }
        internal void ExpandPaletteForCheck() { SetPaletteExpanded(true); }

        internal void CollapsePaletteForCheck() { SetPaletteExpanded(false); }

        internal void ExpandStepForCheck(int index)
        {
            if (_doc == null || _doc.Blocks.Count == 0) return;
            int i = Math.Max(0, Math.Min(index, _doc.Blocks.Count - 1));
            _canvas.SelectAndExpand(_doc.Blocks[i]);
        }

        internal void ShowCodeForCheck()
        {
            // 自检要能截到代码视图：先切到专业模式再切页签
            _config.UiMode = "pro";
            ApplyMode();
            SetCodeView(true);
        }

        internal void RunForCheck()
        {
            RunPrimary(false);
        }

        internal string RunStateForCheck()
        {
            return "status=" + _statusLeft.Text + " sessions=" + _sessions.Count + " logLen=" + _logBox.TextLength
                + " canvasEnabled=" + _canvas.Enabled;
        }

        internal string CodeTextForCheck()
        {
            RefreshCode();
            return _codeView.Text;
        }

        internal void LoadCanvasTabForCheck()
        {
            SetCodeView(false);
        }

        internal string LayoutInfo()
        {
            return string.Format("form={0}x{1} client={10} root={11} middle={12} content={13} side={14} canvas={5}x{6} left={4} items={8} dpi={15} sideBounds={16} sideZ={17} parent={18}",
                Width, Height, DeviceDpi, Screen.PrimaryScreen.Bounds, _leftPanel.Width,
                _canvas.Width, _canvas.Height, _canvas.Zoom, _canvas.ItemCount, _canvas.PanInfo,
                ClientSize.Width, _rootGrid.Width, _middleGrid.Width, _contentPanel != null ? _contentPanel.Width : 0,
                _sidePanel != null && _sidePanel.Visible ? _sidePanel.Width : 0, DeviceDpi,
                _sidePanel != null ? _sidePanel.Bounds.ToString() : "null",
                _sidePanel != null && _sidePanel.Parent != null ? _sidePanel.Parent.Controls.GetChildIndex(_sidePanel).ToString() : "-",
                _sidePanel != null && _sidePanel.Parent != null ? "content" : "noparent") + StructureInfo();
        }

        private string StructureInfo()
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            if (_contentPanel != null)
            {
                sb.Append(" kids=");
                for (int i = 0; i < _contentPanel.Controls.Count; i++)
                {
                    Control c = _contentPanel.Controls[i];
                    sb.Append(i).Append(':').Append(c.GetType().Name).Append(c.Visible ? "+" : "-").Append(' ');
                }
                sb.Append(" contentSize=").Append(_contentPanel.Size).Append(" client=").Append(_contentPanel.ClientSize);
            }
            foreach (Block b in _doc.Blocks)
            {
                sb.Append(" | ").Append(b.Type).Append('(').Append(b.Id).Append(')');
                foreach (BranchBody br in b.Branches) sb.Append('[').Append(br.Name).Append('=').Append(br.Blocks.Count).Append(']');
            }
            return sb.ToString();
        }

        internal void LoadDocumentForCheck(string path)
        {
            ScriptDocument d = ScriptDocument.Load(path);
            ApplyDoc(d, path, false);
            _canvas.FitToView();
        }

        /// <summary>命令行走 .block 直接打开（双击脚本 / 快捷方式 / 文件关联）。</summary>
        internal void OpenStartupFile(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                ScriptDocument d = ScriptDocument.Load(path);
                ApplyDoc(d, path, false);
                _config.AddRecent(path);
                _canvas.FitToView();
                _statusLeft.Text = "已打开 " + Path.GetFileName(path);
                _logger.Write(new LogEntry
                {
                    Level = LogLevel.Info,
                    Source = "编辑器",
                    Message = "命令行打开脚本：" + path
                });
            }
            catch (Exception ex)
            {
                _logger.Write(new LogEntry
                {
                    Level = LogLevel.Error,
                    Source = "编辑器",
                    Message = "命令行打开脚本失败：" + path + "，" + ex.Message
                });
                MessageBox.Show(this,
                    "命令行指定的脚本打不开：" + Environment.NewLine + path + Environment.NewLine + Environment.NewLine + ex.Message,
                    "BlockAuto", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void NewDocument()
        {
            if (!ConfirmDiscard()) return;
            ApplyDoc(CreateNewDocument(), null, false);
            _canvas.FitToView();
        }

        private ScriptDocument CreateNewDocument()
        {
            ScriptDocument d = ScriptDocument.CreateNew();
            d.Settings["max_recursion"] = (double)_config.MaxRecursion;
            d.Settings["default_threshold"] = _config.DefaultThreshold;
            return d;
        }

        private void OpenDocument()
        {
            if (!ConfirmDiscard()) return;
            OpenFileDialog dlg = new OpenFileDialog();
            dlg.Filter = "BlockAuto 脚本 (*.block)|*.block|JSON 文件 (*.json)|*.json|所有文件 (*.*)|*.*";
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                ScriptDocument d = ScriptDocument.Load(dlg.FileName);
                ApplyDoc(d, dlg.FileName, false);
                _config.AddRecent(dlg.FileName);
                _logger.Write(new LogEntry { Level = LogLevel.Info, Source = "编辑器", Message = "打开脚本：" + dlg.FileName });
                _statusLeft.Text = "已打开 " + Path.GetFileName(dlg.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "打开失败：" + ex.Message, "BlockAuto", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private bool SaveDocument(bool saveAs)
        {
            string path = _doc.FilePath;
            if (saveAs || string.IsNullOrEmpty(path))
            {
                SaveFileDialog dlg = new SaveFileDialog();
                dlg.Filter = "BlockAuto 脚本 (*.block)|*.block";
                dlg.DefaultExt = "block";
                dlg.FileName = string.IsNullOrEmpty(path) ? "script.block" : Path.GetFileName(path);
                if (dlg.ShowDialog(this) != DialogResult.OK) return false;
                path = dlg.FileName;
            }
            try
            {
                _doc.EnsureNextLinks();
                List<string> errors = _doc.Validate();
                if (errors.Count > 0)
                {
                    string msg = "脚本存在以下问题：" + Environment.NewLine + string.Join(Environment.NewLine, errors.ToArray());
                    msg += Environment.NewLine + Environment.NewLine + "仍然保存吗？（不影响后续修复）";
                    if (MessageBox.Show(this, msg, "保存检查", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                        return false;
                }
                _doc.Name = Path.GetFileNameWithoutExtension(path);
                BackupFile(path);
                _doc.Save(path);
                _doc.FilePath = path;
                _dirty = false;
                UpdateTitle();
                _config.AddRecent(path);
                _statusScript.Text = path;
                _logger.Write(new LogEntry { Level = LogLevel.Info, Source = "编辑器", Message = "保存脚本：" + path });
                OfferAssociation(path);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "保存失败：" + ex.Message, "BlockAuto", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        private void AssociateBlockFiles(bool remove)
        {
            string err = remove ? FileAssoc.Unassociate() : FileAssoc.Associate();
            if (err != null)
            {
                MessageBox.Show(this, (remove ? "取消关联失败：" : "关联失败：") + err, "BlockAuto",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            _logger.Write(new LogEntry
            {
                Level = LogLevel.Info,
                Source = "文件关联",
                Message = remove ? "已取消 .block 关联" : "已把 .block 关联到 BlockRunner（双击即可运行）"
            });
            MessageBox.Show(this,
                remove ? "已取消关联，重新双击 .block 会回到用默认程序打开。"
                       : "设置完成：以后双击 .block 就会直接运行脚本，不再当成文本打开。\n\n脚本旁边需要有 templates/ 文件夹（用到识图时）。",
                "BlockAuto", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void OfferAssociation(string path)
        {
            if (_config.AssocPrompted || FileAssoc.IsAssociated()) return;
            _config.AssocPrompted = true;
            _config.Save();
            if (MessageBox.Show(this,
                "脚本已保存为可运行的 .block 文件。\n\n是否让 .block 双击就直接运行（关联 BlockRunner）？\n选择“否”以后也可以在 文件 ▾ 菜单里设置。",
                "让脚本双击即可运行", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                string err = FileAssoc.Associate();
                if (err != null)
                    MessageBox.Show(this, "关联失败：" + err + "\n\n可以改用 文件 ▾ → 导出可运行包。", "BlockAuto",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private bool ConfirmDiscard()
        {
            if (!_dirty || _doc == null) return true;
            DialogResult r = MessageBox.Show(this, "当前脚本有未保存的修改，是否保存？", "BlockAuto",
                MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (r == DialogResult.Cancel) return false;
            if (r == DialogResult.Yes) return SaveDocument(false);
            return true;
        }

        private void UpdateTitle()
        {
            string name = string.IsNullOrEmpty(_doc.FilePath) ? "未命名脚本" : Path.GetFileName(_doc.FilePath);
            Text = "BlockAuto 积木自动化平台 - " + name + (_dirty ? " *" : "");
        }

        private void UpdateCounts()
        {
            int n = 0;
            if (_doc != null) foreach (Block b in _doc.AllBlocks()) n++;
            _statusBlocks.Text = n + " 个积木 · " + _doc.Functions.Count + " 个函数";
            if (!string.IsNullOrEmpty(_doc.FilePath)) _statusScript.Text = _doc.FilePath;
        }

        private void RefreshCode()
        {
            if (_doc == null || _codeView == null) return;
            string text = CodeGen.Generate(_doc);
            if (string.IsNullOrEmpty(text) || text.Trim().Length == 0)
                text = "// 这个脚本还没有可预览的步骤。" + Environment.NewLine
                    + "// 先在动作列表里加几步（或点「录制」录一段），这里会实时生成只读伪代码。";
            _codeView.Text = text;
        }

        private void SetCodeView(bool code)
        {
            // 简洁模式没有「代码」页签：直接拒绝并提示，避免出现一块什么都没有的白板
            if (code && _config.IsSimpleMode)
            {
                _statusLeft.Text = "代码视图需在专业模式启用：更多 ▾ → 切换到专业模式";
                return;
            }
            _codeVisible = code;
            _canvas.Visible = !code;
            _codeView.Visible = code;
            if (code)
            {
                _codeView.BringToFront();
                HideDrawerNow();
            }
            if (code)
            {
                RefreshCode();
                _codeTab.BackColor = Theme.Accent;
                _codeTab.ForeColor = Color.White;
                _canvasTab.BackColor = Theme.PanelAlt;
                _canvasTab.ForeColor = Theme.Text;
            }
            else
            {
                _canvasTab.BackColor = Theme.Accent;
                _canvasTab.ForeColor = Color.White;
                _codeTab.BackColor = Theme.PanelAlt;
                _codeTab.ForeColor = Theme.Text;
                _canvas.Focus();
            }
        }

        // ---------------------------- 积木库 ----------------------------

        private void RebuildPalette()
        {
            if (_paletteFlow == null) return;
            string query = _search != null ? _search.Text.Trim() : "";
            bool simple = _config.IsSimpleMode;
            int inner = Math.Max(Theme.Px(140), _paletteFlow.ClientSize.Width - Theme.Px(22));
            _paletteFlow.SuspendLayout();
            _paletteFlow.Controls.Clear();

            foreach (string group in Catalog.GroupOrder)
            {
                List<BlockInfo> infos = new List<BlockInfo>();
                foreach (BlockInfo info in Catalog.All)
                {
                    if (info.Hidden) continue;
                    if (Catalog.GroupOf(info.Type) != group) continue;
                    if (!Catalog.VisibleInMode(info, simple)) continue;
                    if (query.Length > 0 &&
                        info.Title.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0 &&
                        info.Type.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    infos.Add(info);
                }
                if (infos.Count == 0) continue;
                infos.Sort(delegate(BlockInfo a, BlockInfo b) { return a.Sort.CompareTo(b.Sort); });

                string groupKey = group;
                bool collapsed = _groupCollapsed.ContainsKey(groupKey) && _groupCollapsed[groupKey];
                PaletteGroupHeader header = new PaletteGroupHeader(group, BlockCategory.CategoryColor(infos[0].Category), collapsed);
                header.Width = inner;
                header.Height = Theme.Px(28);
                header.Margin = new Padding(Theme.Px(4), Theme.Px(6), Theme.Px(2), Theme.Px(2));
                header.ToggleRequested += delegate
                {
                    bool now = _groupCollapsed.ContainsKey(groupKey) && _groupCollapsed[groupKey];
                    _groupCollapsed[groupKey] = !now;
                    RebuildPalette();
                };
                _paletteFlow.Controls.Add(header);

                if (collapsed) continue;
                foreach (BlockInfo info in infos)
                {
                    PaletteChip chip = new PaletteChip(info.Type, info.Title, info.Category);
                    chip.Width = inner;
                    chip.Height = Theme.Px(30);
                    chip.Margin = new Padding(Theme.Px(6), Theme.Px(2), Theme.Px(2), Theme.Px(2));
                    chip.AddRequested += delegate(string t) { _canvas.AddBlockType(t); };
                    _paletteFlow.Controls.Add(chip);
                }
            }
            if (_paletteFlow.Controls.Count == 0)
            {
                Label none = new Label();
                none.Text = "未找到匹配的积木";
                none.ForeColor = Theme.TextDim;
                none.AutoSize = true;
                none.Margin = new Padding(14, 10, 0, 0);
                _paletteFlow.Controls.Add(none);
            }
            _paletteFlow.ResumeLayout();
        }
        // ---------------------------- 函数管理 ----------------------------

        private void RefreshFunctionList()
        {
            if (_functionList == null) return;
            _functionList.Items.Clear();
            foreach (FunctionDef f in _doc.Functions)
            {
                _functionList.Items.Add(f);
            }
            _functionList.DisplayMember = "Name";
            if (_functionName != null)
            {
                for (int i = 0; i < _functionList.Items.Count; i++)
                {
                    FunctionDef f = _functionList.Items[i] as FunctionDef;
                    if (f != null && f.Name == _functionName) { _functionList.SelectedIndex = i; break; }
                }
            }
        }

        private void EnterFunction(string name)
        {
            _functionName = name;
            _canvas.SetFunction(name);
            UpdateContext();
            _canvas.FitToView();
        }

        private void UpdateContext()
        {
            if (_functionName == null)
            {
                _contextLabel.Text = "主流程";
                _backButton.Visible = false;
            }
            else
            {
                FunctionDef f = _doc.FindFunction(_functionName);
                string ps = "";
                if (f != null)
                {
                    List<string> parts = new List<string>();
                    foreach (VarDef v in f.Params) parts.Add(v.Name + ":" + v.Type);
                    ps = string.Join(", ", parts.ToArray());
                }
                _contextLabel.Text = "函数：" + _functionName + "(" + ps + ")";
                _backButton.Visible = true;
            }
        }

        private void NewFunction()
        {
            string before = _doc.ToJson();
            using (FunctionDialog dlg = new FunctionDialog(_doc, null))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
            }
            _canvas.PushUndoSnapshot(before);
            _dirty = true;
            RefreshFunctionList();
            UpdateCounts();
            RefreshCode();
            UpdateTitle();
            // 进入新函数
            if (_doc.Functions.Count > 0) EnterFunction(_doc.Functions[_doc.Functions.Count - 1].Name);
        }

        private void EditFunction()
        {
            FunctionDef f = null;
            if (_functionList.SelectedItem != null) f = _functionList.SelectedItem as FunctionDef;
            if (f == null && _functionName != null) f = _doc.FindFunction(_functionName);
            if (f == null) { MessageBox.Show(this, "请先在左侧选择一个函数", "BlockAuto", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            string before = _doc.ToJson();
            string oldName = f.Name;
            using (FunctionDialog dlg = new FunctionDialog(_doc, f))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
            }
            _canvas.PushUndoSnapshot(before);
            _doc.NormalizeBranches();
            if (_functionName != null && _functionName == oldName) _functionName = f.Name;
            RefreshFunctionList();
            _canvas.Rebuild();
            _canvas.NotifyDocumentChanged();
            UpdateContext();
            UpdateCounts();
        }

        private void DeleteFunction()
        {
            FunctionDef f = null;
            if (_functionList.SelectedItem != null) f = _functionList.SelectedItem as FunctionDef;
            if (f == null && _functionName != null) f = _doc.FindFunction(_functionName);
            if (f == null) return;
            if (MessageBox.Show(this, "删除函数 " + f.Name + " 会同时删除所有调用它的积木，确定继续？", "BlockAuto",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            string before = _doc.ToJson();
            string name = f.Name;
            _doc.Functions.Remove(f);
            RemoveCallers(_doc.Blocks, name);
            foreach (FunctionDef other in _doc.Functions) RemoveCallers(other.Body, name);
            _doc.NormalizeBranches();
            _canvas.PushUndoSnapshot(before);
            if (_functionName == name) _functionName = null;
            RefreshFunctionList();
            _canvas.SetFunction(_functionName);
            _dirty = true;
            UpdateContext();
            UpdateCounts();
            RefreshCode();
            UpdateTitle();
        }

        private static void RemoveCallers(List<Block> list, string funcName)
        {
            if (list == null) return;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                Block b = list[i];
                if (b.Type == "call_func" && Json.GetString(b.Params, "func", "") == funcName) list.RemoveAt(i);
                else foreach (BranchBody br in b.Branches) RemoveCallers(br.Blocks, funcName);
            }
        }

        // ---------------------------- 块编辑 ----------------------------

        private void OnEditBlock(Block b)
        {
            if (b == null || _doc == null) return;
            string before = _doc.ToJson();
            try
            {
                using (ParamDialog dlg = new ParamDialog(_doc, b))
                {
                    if (dlg.ShowDialog(this) != DialogResult.OK) return;
                }
            }
            catch (Exception ex)
            {
                // 弹窗构造/渲染出错时给明确提示，别让用户以为“卡死”
                _logger.Write(new LogEntry
                {
                    Level = LogLevel.Error,
                    Source = "参数",
                    Message = "打开参数窗口失败：" + Catalog.TitleOf(b.Type) + "，" + ex.Message,
                    Detail = ex.StackTrace
                });
                MessageBox.Show(this,
                    "打开的「" + Catalog.TitleOf(b.Type) + "」参数窗口出错了：" + Environment.NewLine + ex.Message
                    + Environment.NewLine + Environment.NewLine + "可以先用右侧参数抽屉改常用项，详细日志见日志面板。",
                    "BlockAuto", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            _doc.NormalizeBranches();
            _canvas.PushUndoSnapshot(before);
            _canvas.Rebuild();
            _canvas.NotifyDocumentChanged();
            RefreshSidePanel();
        }

        // ---------------------------- 运行 ----------------------------

        private bool AnyPrimaryRunning()
        {
            foreach (RunSession s in _sessions) if (s.Primary) return true;
            return false;
        }

        private RunSession PrimarySession()
        {
            foreach (RunSession s in _sessions) if (s.Primary) return s;
            return null;
        }

        private void RunPrimary(bool startPaused)
        {
            if (_doc == null) return;
            if (AnyPrimaryRunning()) return;
            string key = string.IsNullOrEmpty(_doc.FilePath) ? "<unsaved>" : _doc.FilePath;
            if (Engine.IsScriptRunning(key))
            {
                _logger.Write(new LogEntry { Level = LogLevel.Warn, Source = "引擎", Message = "脚本正在运行，已跳过本次触发：" + key });
                return;
            }
            List<string> errors = _doc.Validate();
            if (errors.Count > 0)
            {
                _logger.Write(new LogEntry { Level = LogLevel.Error, Source = "结构", Message = "脚本结构错误，无法运行：" + errors[0] });
                for (int i = 1; i < errors.Count; i++)
                    _logger.Write(new LogEntry { Level = LogLevel.Error, Source = "结构", Message = errors[i] });
                MessageBox.Show(this, "脚本结构错误：\n" + string.Join("\n", errors.ToArray()), "BlockAuto", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            StartSession(_doc, key, true, startPaused, "画布调试");
        }

        private void StartSession(ScriptDocument doc, string key, bool primary, bool startPaused, string source)
        {
            StartSession(doc, key, primary, startPaused, source, null);
        }

        private void StartSession(ScriptDocument doc, string key, bool primary, bool startPaused, string source, EngineOptions options)
        {
            if (!Engine.TryAcquire(key))
            {
                _logger.Write(new LogEntry { Level = LogLevel.Warn, Source = "引擎", Message = "脚本已在运行，触发被跳过：" + key });
                return;
            }
            EngineOptions opt = options ?? new EngineOptions();
            if (options == null)
            {
                opt.ActionDelayMs = _config.ActionDelayMs;
                opt.MaxRecursion = _config.MaxRecursion;
                opt.BaseDirectory = doc.Directory;
                opt.EnableEscapeStop = true;
            }
            Engine engine = new Engine(doc, opt, _auto, _logger);
            engine.StartPaused = startPaused;

            RunSession session = new RunSession();
            session.Engine = engine;
            session.Doc = doc;
            session.Key = key;
            session.Primary = primary;
            session.Source = source;

            engine.BlockEntering += delegate(Block b)
            {
                if (!primary) return;
                SafeInvoke(delegate { _canvas.HighlightBlock(b.Id); });
            };
            engine.Paused += delegate
            {
                SafeInvoke(delegate
                {
                    UpdateRunUi();
                    if (primary) _statusLeft.Text = "已暂停";
                });
            };

            Thread t = new Thread(delegate()
            {
                EngineResult result = null;
                try
                {
                    result = engine.Run();
                }
                catch (Exception ex)
                {
                    _logger.Write(new LogEntry { Level = LogLevel.Error, Source = "引擎", Message = "运行线程异常：" + ex.Message, Detail = ex.StackTrace });
                    result = new EngineResult { Status = EngineStatus.Failed, Message = ex.Message };
                }
                finally
                {
                    Engine.Release(key);
                }
                SafeInvoke(delegate { OnSessionFinished(session, result); });
            });
            t.IsBackground = true;
            t.Name = "BlockAuto-Engine";
            session.Thread = t;
            _sessions.Add(session);
            t.Start();
            _logger.Write(new LogEntry { Level = LogLevel.Info, Source = "引擎", Message = "开始运行（" + source + "）：" + key });
            UpdateRunUi();
            if (primary) _statusLeft.Text = "运行中…";
        }

        private void OnSessionFinished(RunSession session, EngineResult result)
        {
            _sessions.Remove(session);
            if (session.Primary)
            {
                _canvas.ClearHighlight();
                switch (result != null ? result.Status : EngineStatus.Failed)
                {
                    case EngineStatus.Completed: _statusLeft.Text = "运行完成"; break;
                    case EngineStatus.Stopped: _statusLeft.Text = "已停止"; break;
                    case EngineStatus.StructureError: _statusLeft.Text = "结构错误"; break;
                    default: _statusLeft.Text = "运行失败"; break;
                }
                if (result != null && result.Status == EngineStatus.Failed)
                {
                    Block fb = string.IsNullOrEmpty(result.FailedBlockId) ? null : _doc.FindBlock(result.FailedBlockId);
                    if (fb != null)
                    {
                        _canvas.SelectBlock(fb);
                        _canvas.MarkError(fb.Id);
                    }
                    string title = string.IsNullOrEmpty(result.FailedBlockTitle) ? "某个积木" : result.FailedBlockTitle;
                    string msg = "运行到第 " + (result.FailedStep + 1) + " 步时失败。" + Environment.NewLine + Environment.NewLine
                        + "积木：" + title + Environment.NewLine
                        + "原因：" + result.Message + Environment.NewLine + Environment.NewLine
                        + "已为你选中并高亮该积木，修改后可以再运行一次。";
                    _logger.Write(new LogEntry { Level = LogLevel.Error, Source = "运行", Message = msg.Replace(Environment.NewLine, " ") });
                    MessageBox.Show(this, msg, "运行失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            UpdateRunUi();
        }

        private void TogglePause()
        {
            RunSession s = PrimarySession();
            if (s == null) return;
            if (s.Engine.IsPaused) s.Engine.Resume();
            else s.Engine.Pause();
            UpdateRunUi();
        }

        private void StepPrimary()
        {
            RunSession s = PrimarySession();
            if (s == null)
            {
                RunPrimary(true);
                return;
            }
            if (!s.Engine.IsPaused) s.Engine.Pause();
            s.Engine.StepOnce();
            UpdateRunUi();
        }

        private void StopAll()
        {
            bool any = false;
            foreach (RunSession s in new List<RunSession>(_sessions))
            {
                try { s.Engine.Stop(); any = true; } catch { }
            }
            if (any) _statusLeft.Text = "正在停止…";
            UpdateRunUi();
        }

        private void UpdateRunUi()
        {
            bool running = AnyPrimaryRunning();
            RunSession s = PrimarySession();
            _canvas.Enabled = !running;
            _runButton.Enabled = !running;
            _pauseButton.Enabled = running;
            _stopButton.Enabled = _sessions.Count > 0;
            _stepButton.Enabled = !running || (s != null);
            _pauseButton.Text = (s != null && s.Engine.IsPaused) ? "继续" : "暂停";
            _statusZoom.Text = "步骤 " + Math.Max(0, _canvas.ItemCount - 1);
            if (!running && _sessions.Count == 0 && _statusLeft.Text == "运行中…") _statusLeft.Text = "就绪";
        }

        // ---------------------------- 后台运行（热键 / 定时） ----------------------------

        private void RunBackground(string path, string source)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                _logger.Write(new LogEntry { Level = LogLevel.Error, Source = "引擎", Message = "脚本不存在：" + path });
                return;
            }
            if (Engine.IsScriptRunning(path))
            {
                _logger.Write(new LogEntry { Level = LogLevel.Warn, Source = "引擎", Message = "脚本正在运行，已跳过（" + source + "）：" + Path.GetFileName(path) });
                return;
            }
            ScriptDocument doc;
            try
            {
                doc = ScriptDocument.Load(path);
            }
            catch (Exception ex)
            {
                _logger.Write(new LogEntry { Level = LogLevel.Error, Source = "结构", Message = "加载脚本失败：" + path + "，" + ex.Message });
                return;
            }
            StartSession(doc, path, false, false, source);
        }

        private void OnHotkey(HotkeyBinding b)
        {
            SafeInvoke(delegate
            {
                _logger.Write(new LogEntry { Level = LogLevel.Info, Source = "热键", Message = b.Keys + " 触发：" + Path.GetFileName(b.ScriptPath) });
                RunBackground(b.ScriptPath, "热键 " + b.Keys);
            });
        }

        private void OnScheduleDue(ScheduledTask t)
        {
            SafeInvoke(delegate
            {
                t.LastRun = DateTime.Now;
                t.LastMessage = "已触发";
                _scheduler.Save();
                _logger.Write(new LogEntry { Level = LogLevel.Info, Source = "定时", Message = "到达执行时间：" + t.Name + " → " + Path.GetFileName(t.ScriptPath) });
                RunBackground(t.ScriptPath, "定时任务 " + t.Name);
            });
        }

        // ---------------------------- 弹窗 ----------------------------

        private void OpenTemplates()
        {
            using (TemplateManagerForm f = new TemplateManagerForm(_doc, false))
                f.ShowDialog(this);
        }

        private void OpenHotkeys()
        {
            using (HotkeyForm f = new HotkeyForm(_hotkeys))
                f.ShowDialog(this);
        }

        private void OpenSchedules()
        {
            using (ScheduleForm f = new ScheduleForm(_scheduler))
            {
                f.TestRequested += delegate(ScheduledTask t)
                {
                    _logger.Write(new LogEntry { Level = LogLevel.Info, Source = "定时", Message = "手动测试：" + t.Name });
                    RunBackground(t.ScriptPath, "手动测试");
                };
                f.ShowDialog(this);
            }
        }

        // ---------------------------- AI 接口 / 打包 / 录制 ----------------------------

        internal string CurrentFilePath
        {
            get { return _doc == null ? "" : _doc.FilePath; }
        }

        internal int ActiveRunCount
        {
            get { return _sessions.Count; }
        }

        internal string SnapshotCurrentJson()
        {
            if (_doc == null) return "";
            _doc.EnsureNextLinks();
            return _doc.ToJson();
        }

        internal List<string> SnapshotRecentLog(int count)
        {
            List<string> lines = new List<string>();
            string[] all = _logBox.Lines;
            int start = Math.Max(0, all.Length - count);
            for (int i = start; i < all.Length; i++)
                if (all[i].Trim().Length > 0) lines.Add(all[i]);
            return lines;
        }

        internal void SafeRunFromApi(string path, double threshold)
        {
            SafeInvoke(delegate
            {
                if (!File.Exists(path)) return;
                ScriptDocument d;
                try { d = ScriptDocument.Load(path); }
                catch (Exception ex)
                {
                    _logger.Write(new LogEntry { Level = LogLevel.Error, Source = "结构", Message = "加载脚本失败：" + ex.Message });
                    return;
                }
                EngineOptions opt = new EngineOptions();
                opt.ActionDelayMs = _config.ActionDelayMs;
                opt.MaxRecursion = _config.MaxRecursion;
                opt.EnableEscapeStop = true;
                opt.ThresholdOverride = threshold > 0 ? (double?)threshold : null;
                StartSession(d, path, false, false, "AI接口", opt);
            });
        }

        internal void SafeStopAllFromApi()
        {
            SafeInvoke(delegate { StopAll(); });
        }

        internal void SafeReloadFromDisk(string path)
        {
            SafeInvoke(delegate
            {
                if (_doc == null || !string.Equals(path, _doc.FilePath, StringComparison.OrdinalIgnoreCase)) return;
                if (_dirty)
                {
                    _logger.Write(new LogEntry { Level = LogLevel.Warn, Source = "AI接口", Message = "当前脚本有未保存修改，已保留本地编辑内容" });
                    return;
                }
                try
                {
                    ScriptDocument d = ScriptDocument.Load(path);
                    ApplyDoc(d, path, false);
                }
                catch (Exception ex)
                {
                    _logger.Write(new LogEntry { Level = LogLevel.Error, Source = "结构", Message = "重新加载失败：" + ex.Message });
                }
            });
        }

        private void StartAiServerIfEnabled()
        {
            string err = null;
            if (_config.EnableAi)
            {
                _aiServer = new AiHttpServer(new EditorAiHost(this, _config, _auto, _logger));
                err = _aiServer.Start(_config.AiPort, _config.AiToken);
            }
            if (_config.EnableAi)
            {
                if (err == null)
                {
                    _logger.Write(new LogEntry { Level = LogLevel.Info, Source = "AI接口", Message = "已监听 http://127.0.0.1:" + _config.AiPort + "/ （Token 鉴权）" });
                    _statusAi.Text = "AI 接口运行中";
                }
                else
                {
                    _logger.Write(new LogEntry { Level = LogLevel.Error, Source = "AI接口", Message = "启动失败：" + err });
                    _statusAi.Text = "AI 接口启动失败";
                }
            }
            else
            {
                _statusAi.Text = "AI 接口关闭";
            }
        }

        private void RestartAiServer()
        {
            if (_aiServer != null)
            {
                try { _aiServer.Dispose(); } catch { }
                _aiServer = null;
            }
            StartAiServerIfEnabled();
        }

        private void OpenPackager()
        {
            if (_doc == null) return;
            if (string.IsNullOrEmpty(_doc.FilePath))
            {
                MessageBox.Show(this, "请先保存当前脚本，再打包。", "BlockAuto", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using (ScriptPackDialog dlg = new ScriptPackDialog(_doc, Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BlockRunner.exe")))
            {
                dlg.ShowDialog(this);
            }
        }

        private void OpenRecorder()
        {
            _recorderSnapshot = _doc.ToJson();
            using (RecorderForm dlg = new RecorderForm(_doc, CurrentListForRecord()))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK && dlg.AppliedCount > 0)
                {
                    _canvas.PushUndoSnapshot(_recorderSnapshot);
                    _doc.EnsureNextLinks();
                    _canvas.Rebuild();
                    _canvas.NotifyDocumentChanged();
                    _dirty = true;
                    _logger.Write(new LogEntry { Level = LogLevel.Info, Source = "录制", Message = "已插入 " + dlg.AppliedCount + " 个积木" });
                }
            }
        }

        private List<Block> CurrentListForRecord()
        {
            if (!string.IsNullOrEmpty(_functionName))
            {
                FunctionDef f = _doc.FindFunction(_functionName);
                if (f != null) return f.Body;
            }
            return _doc.Blocks;
        }

        private void RefreshSidePanel()
        {
            if (_sidePanel == null) return;
            _sidePanel.Bind(_doc, _canvas != null ? _canvas.SelectedBlock : null,
                delegate { if (_canvas != null) _canvas.PushUndo(); },
                delegate { if (_canvas != null) { _canvas.Rebuild(); _canvas.NotifyDocumentChanged(); } });
        }

        private void ToggleSidePanel()
        {
            if (_sidePanel == null) return;
            if (_sidePanel.Visible || _drawerOpen)
            {
                _drawerManualHide = true;
                ShowDrawer(false);
            }
            else
            {
                _drawerManualHide = false;
                if (_canvas != null && _canvas.SelectedBlock == null)
                {
                    _statusLeft.Text = "先在左边点一个步骤，再打开参数栏";
                    return;
                }
                RefreshSidePanel();
                ShowDrawer(true);
            }
        }

        private void OpenNetwork()
        {
            using (NetworkForm f = new NetworkForm(_config, OpenScriptFromOutside))
                f.ShowDialog(this);
        }

        internal void OpenScriptFromOutside(string path)
        {
            SafeInvoke(delegate
            {
                if (!ConfirmDiscard()) return;
                try
                {
                    ScriptDocument d = ScriptDocument.Load(path);
                    ApplyDoc(d, path, false);
                    _config.AddRecent(path);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "打开失败：" + ex.Message, "BlockAuto", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            });
        }

        // ---------------------------- v2.0 键鼠优先：模式 / 托盘 / 记录坐标 ----------------------------

        /// <summary>v2.5：≡ 按钮在「窄条积木库」和「完整积木库」之间切换，不再整个隐藏。</summary>
        private void ToggleSidebar()
        {
            if (_middleGrid == null || _leftPanel == null) return;
            SetPaletteExpanded(!IsPaletteExpanded);
        }

        private void ToggleMode()
        {
            _config.UiMode = _config.IsSimpleMode ? "pro" : "simple";
            _config.Save();
            ApplyMode();
            _logger.Write(new LogEntry
            {
                Level = LogLevel.Info,
                Source = "界面",
                Message = _config.IsSimpleMode ? "已切换到简洁模式（键鼠优先）" : "已切换到专业模式（显示代码视图/断点/高级积木）"
            });
        }

        private void ApplyMode()
        {
            bool simple = _config.IsSimpleMode;
            if (_codeTab != null) _codeTab.Visible = !simple;
            if (simple && _codeVisible) SetCodeView(false);
            if (_funcPanel != null) _funcPanel.Visible = !simple;
            if (_leftGrid != null && _leftGrid.RowStyles.Count > 2) _leftGrid.RowStyles[2].Height = simple ? 0f : Theme.Px(168);
            if (_canvas != null) _canvas.SimpleMode = simple;
            if (_pauseButton != null) _pauseButton.Visible = !simple;
            if (_stepButton != null) _stepButton.Visible = !simple;
            if (simple)
            {
                // 键鼠优先：流程/变量/函数分组默认收起，键鼠动作与识别常驻
                _groupCollapsed["流程控制"] = true;
                _groupCollapsed["变量与条件"] = true;
                _groupCollapsed["函数"] = true;
            }
            RebuildPalette();
        }

        private void OnRecordClick(Block b)
        {
            if (b == null) return;
            using (CoordPickerForm f = new CoordPickerForm())
            {
                if (f.ShowDialog(this) != DialogResult.OK) return;
                _canvas.SetRecordedPoint(b, f.PickedPoint.X, f.PickedPoint.Y);
                _logger.Write(new LogEntry
                {
                    Level = LogLevel.Info,
                    Source = "坐标记录",
                    Message = "已记录点击坐标 (" + f.PickedPoint.X + ", " + f.PickedPoint.Y + ")"
                });
                _statusLeft.Text = "已记录坐标 (" + f.PickedPoint.X + ", " + f.PickedPoint.Y + ")";
            }
        }

        private void InitializeTray()
        {
            if (_tray != null) return;
            _tray = new NotifyIcon();
            _tray.Icon = MakeTrayIcon();
            _tray.Text = "BlockAuto 键鼠自动化";
            _tray.Visible = true;

            ContextMenuStrip m = new ContextMenuStrip();
            m.BackColor = Theme.PanelAlt;
            m.ForeColor = Theme.Text;
            m.Renderer = new DarkMenuRenderer();
            m.ShowImageMargin = false;
            AddMenu(m, "显示主窗口", delegate { RestoreFromTray(); });
            AddMenu(m, "运行当前脚本", delegate { RestoreFromTray(); RunPrimary(false); });
            AddMenu(m, "停止全部", delegate { StopAll(); });
            m.Items.Add(new ToolStripSeparator());
            AddMenu(m, "隐藏到托盘", delegate { HideToTray(); });
            AddMenu(m, "退出", delegate { _realExit = true; Close(); });
            _tray.ContextMenuStrip = m;
            _tray.DoubleClick += delegate { RestoreFromTray(); };

            try { Win32.RegisterHotKey(Handle, _trayHotkeyId, HotkeyParser.MOD_CONTROL | HotkeyParser.MOD_ALT | HotkeyParser.MOD_NOREPEAT, 0x42); }
            catch { }
        }

        private static Icon MakeTrayIcon()
        {
            Bitmap bmp = new Bitmap(16, 16);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (SolidBrush b = new SolidBrush(Theme.Success)) g.FillEllipse(b, 1, 1, 14, 14);
                using (Font f = new Font("Arial", 8.5f, FontStyle.Bold, GraphicsUnit.Pixel))
                    g.DrawString("B", f, Brushes.White, new PointF(5, 3));
            }
            return Icon.FromHandle(bmp.GetHicon());
        }

        private void HideToTray()
        {
            if (_tray == null) InitializeTray();
            Hide();
            ShowInTaskbar = false;
            if (_tray != null)
            {
                _tray.Visible = true;
                try { _tray.ShowBalloonTip(1500, "BlockAuto 已在后台运行", "双击托盘图标或按 Ctrl+Alt+B 可重新打开", ToolTipIcon.Info); } catch { }
            }
        }

        private void RestoreFromTray()
        {
            ShowInTaskbar = true;
            Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
            BringToFront();
        }

        private void ShowWelcome()
        {
            using (WelcomeForm f = new WelcomeForm(_config))
            {
                f.ShowDialog(this);
                _config.FirstRunDone = true;
                _config.Save();
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0312 && m.WParam.ToInt32() == _trayHotkeyId)
            {
                RestoreFromTray();
                return;
            }
            base.WndProc(ref m);
        }

        private void BackupFile(string path)
        {
            if (_config.BackupVersions <= 0 || !File.Exists(path)) return;
            try
            {
                string dir = Path.Combine(Path.GetDirectoryName(path), ".backup");
                Directory.CreateDirectory(dir);
                string baseName = Path.GetFileNameWithoutExtension(path);
                string target = Path.Combine(dir, baseName + "." + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".block");
                File.Copy(path, target, true);
                FileInfo[] files = new DirectoryInfo(dir).GetFiles(baseName + ".*.block");
                Array.Sort(files, delegate(FileInfo a, FileInfo b) { return b.LastWriteTime.CompareTo(a.LastWriteTime); });
                for (int i = _config.BackupVersions; i < files.Length; i++)
                {
                    try { files[i].Delete(); } catch { }
                }
            }
            catch { }
        }
        private void OpenSettings()
        {
            using (SettingsForm f = new SettingsForm(_config))
            {
                if (f.ShowDialog(this) == DialogResult.OK)
                {
                    _auto.CacheMilliseconds = _config.ScreenshotCacheMs;
                    _auto.MatchScales = _config.MatchScalePercent > 0
                        ? new double[] { 1.0 - _config.MatchScalePercent / 100.0, 1.0 + _config.MatchScalePercent / 100.0 }
                        : null;
                    RestartAiServer();
                    _logger.Write(new LogEntry { Level = LogLevel.Info, Source = "设置", Message = "全局设置已保存" });
                }
            }
        }

        // ---------------------------- 日志与关闭 ----------------------------

        private void OnLogEntry(LogEntry entry)
        {
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action<LogEntry>(OnLogEntry), entry); } catch { }
                return;
            }
            AppendLog(entry);
        }

        private void AppendLog(LogEntry entry)
        {
            if (_logBox == null || _logBox.IsDisposed) return;
            try
            {
                if (_logBox.TextLength > 300000) _logBox.Clear();
                Color c = entry.Level == LogLevel.Error ? Theme.Danger : (entry.Level == LogLevel.Warn ? Theme.Warning : Color.FromArgb(0xC9, 0xD1, 0xDD));
                _logBox.SelectionStart = _logBox.TextLength;
                _logBox.SelectionColor = Color.FromArgb(0x77, 0x80, 0x92);
                _logBox.AppendText("[" + entry.Time.ToString("HH:mm:ss") + "] ");
                _logBox.SelectionColor = c;
                _logBox.AppendText("[" + entry.LevelText + "] ");
                _logBox.SelectionColor = Color.FromArgb(0x9F, 0xB0, 0xC8);
                _logBox.AppendText("[" + entry.Source + "] ");
                _logBox.SelectionColor = c;
                _logBox.AppendText(entry.Message + Environment.NewLine);
                _logBox.ScrollToCaret();
            }
            catch { }
        }

        private void SafeInvoke(MethodInvoker action)
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                try { BeginInvoke(action); } catch { }
            }
            else
            {
                try { action(); } catch { }
            }
        }

        private void OnClosing(object sender, FormClosingEventArgs e)
        {
            if (!_realExit && _config.HideToTray && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                HideToTray();
                return;
            }
            if (_sessions.Count > 0)
            {
                if (MessageBox.Show(this, "脚本正在运行，确定退出吗？", "BlockAuto",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
                foreach (RunSession s in new List<RunSession>(_sessions))
                {
                    try { s.Engine.Stop(); } catch { }
                }
                Thread.Sleep(200);
            }
            if (!ConfirmDiscard())
            {
                e.Cancel = true;
                return;
            }
            if (_timer != null) _timer.Stop();
            _config.Save();
            try { _hotkeys.Dispose(); } catch { }
            if (_tray != null) { try { _tray.Visible = false; _tray.Dispose(); } catch { } }
            try { Win32.UnregisterHotKey(Handle, _trayHotkeyId); } catch { }
            if (_aiServer != null) { try { _aiServer.Dispose(); } catch { } }
            _auto.Dispose();
        }
    }
}
