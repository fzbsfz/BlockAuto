using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Windows.Forms;

namespace BlockAuto
{
    /// <summary>
    /// v2.1 动作列表画布：从上到下就是执行顺序，一行一步。
    /// 键鼠工具的主界面不是节点图——用户要能一眼看完「第几步做什么」，上下拖动即可改顺序。
    /// </summary>
    public sealed class BlockCanvas : Control
    {
        private const int RowHeight = 34;
        private const int BranchRowHeight = 22;
        private const int Indent = 22;
        /// <summary>展开详情区的固定留白（不含按钮行）；实际高度按按钮换行数动态计算。</summary>
        private const int DetailPad = 12;
        private const int PillHeight = 22;
        private const int ChevronSize = 16;
        private const int PillGap = 6;

        /// <summary>画布按设计坐标绘制，Paint 时整体缩放；这里返回设计区尺寸。</summary>
        private int W { get { return (int)(ClientSize.Width / Theme.Scale); } }
        private int H { get { return (int)(ClientSize.Height / Theme.Scale); } }
        private static Point ToDesign(Point p)
        {
            return new Point((int)(p.X / Theme.Scale), (int)(p.Y / Theme.Scale));
        }

        private sealed class Row
        {
            public Block Block;
            public bool IsStart;
            public bool IsBranch;
            public string Label = "";
            public List<Block> Owner;
            public int IndexInOwner;
            public int Depth;
            public Rectangle Bounds;
            public Rectangle Chevron;
            public Rectangle DetailBounds;
            public bool Expanded;
        }

        private ScriptDocument _doc;
        private string _functionName;
        private readonly List<Row> _rows = new List<Row>();
        private readonly HashSet<string> _expanded = new HashSet<string>();
        private readonly List<string> _undo = new List<string>();
        private readonly List<string> _redo = new List<string>();
        private readonly List<Block> _clipboard = new List<Block>();

        private Block _selected;
        private string _highlightId;
        private string _errorId;
        private int _scrollY;
        private int _contentHeight;
        private int _hoverRow = -1;
        private bool _dragging;
        private Block _dragBlock;
        private int _dragFromY;
        private bool _dragArmed;
        private Point _dragStart;
        private int _dropRowIndex = -1;
        private Point _dragPoint;

        private bool _simpleMode = true;
        private int _pendingToggle = -1;
        private string _lastToggleId = "";
        private DateTime _lastToggleAt = DateTime.MinValue;

        public event EventHandler DocumentChanged;
        public event EventHandler ViewChanged;
        public event Action<Block> SelectionChanged;
        public event Action<Block> BlockEditRequested;
        public event Action<Block> RecordClickRequested;
        /// <summary>要求为这一步“截图取图”（由主窗口弹框选截图并写回模板）。</summary>
        public event Action<Block> CaptureTemplateRequested;

        public BlockCanvas()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Canvas;
            AllowDrop = true;
            TabStop = true;
            DoubleBuffered = true;
            Font = Theme.ScaledFont(9f, FontStyle.Regular);
        }

        // ---------------------------- 对外属性 ----------------------------

        public ScriptDocument Document { get { return _doc; } }
        public string FunctionName { get { return _functionName; } }
        public float Zoom { get { return 1f; } }
        public Block SelectedBlock { get { return _selected; } }
        public string HighlightId { get { return _highlightId; } }
        public int ItemCount { get { return _rows.Count; } }
        public string PanInfo { get { return "scroll=" + _scrollY; } }
        public bool FreeLayout = false;
        public bool SimpleMode
        {
            get { return _simpleMode; }
            set { _simpleMode = value; Invalidate(); }
        }

        public List<Block> CurrentList
        {
            get
            {
                if (_doc == null) return new List<Block>();
                if (!string.IsNullOrEmpty(_functionName))
                {
                    FunctionDef f = _doc.FindFunction(_functionName);
                    if (f != null) return f.Body;
                    _functionName = null;
                }
                return _doc.Blocks;
            }
        }

        // ---------------------------- 生命周期 ----------------------------

        public void SetDocument(ScriptDocument doc, string functionName)
        {
            _doc = doc;
            _functionName = functionName;
            _selected = null;
            _expanded.Clear();
            _undo.Clear();
            _redo.Clear();
            _clipboard.Clear();
            _scrollY = 0;
            Rebuild();
            Invalidate();
            FireViewChanged();
        }

        public void SetFunction(string functionName)
        {
            _functionName = functionName;
            _selected = null;
            _scrollY = 0;
            Rebuild();
            Invalidate();
        }

        public void Rebuild()
        {
            _rows.Clear();
            if (_doc == null) return;

            Row start = new Row();
            start.IsStart = true;
            start.Label = "开始";
            start.Depth = 0;
            _rows.Add(start);

            Flatten(CurrentList, 0);
            LayoutRows();
            Invalidate();
        }

        private void Flatten(List<Block> list, int depth)
        {
            if (list == null) return;
            for (int i = 0; i < list.Count; i++)
            {
                Block b = list[i];
                Row row = new Row();
                row.Block = b;
                row.Owner = list;
                row.IndexInOwner = i;
                row.Depth = depth;
                row.Expanded = _expanded.Contains(b.Id);
                _rows.Add(row);

                if (b.IsContainer && row.Expanded)
                {
                    List<BranchSpec> specs = Catalog.Branches(b);
                    for (int bi = 0; bi < b.Branches.Count; bi++)
                    {
                        Row label = new Row();
                        label.IsBranch = true;
                        label.Label = bi < specs.Count ? specs[bi].Label : b.Branches[bi].Name;
                        label.Depth = depth + 1;
                        _rows.Add(label);
                        Flatten(b.Branches[bi].Blocks, depth + 1);
                    }
                }
            }
        }

        private void LayoutRows()
        {
            int y = 8 - _scrollY;
            int width = W;
            for (int i = 0; i < _rows.Count; i++)
            {
                Row r = _rows[i];
                int x = 10 + r.Depth * Indent;
                int w = Math.Max(160, width - x - 14);
                int detail = 0;
                if (r.Expanded && r.Block != null && !r.Block.IsContainer) detail = DetailHeightFor(r.Block, w);
                int h;
                if (r.IsStart) h = RowHeight;
                else if (r.IsBranch) h = BranchRowHeight;
                else h = RowHeight + detail;

                r.Bounds = new Rectangle(x, y, w, h);
                r.Chevron = new Rectangle(x + 4, y + (RowHeight - ChevronSize) / 2, ChevronSize, ChevronSize);
                if (detail > 0)
                    r.DetailBounds = new Rectangle(x + 26, y + RowHeight, w - 32, detail);
                y += h + 3;
            }
            _contentHeight = y + _scrollY + 12;
        }

        public void FitToView() { Rebuild(); }

        // ---------------------------- 绘制 ----------------------------

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Canvas);
            g.ScaleTransform(Theme.Scale, Theme.Scale);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            if (_doc == null)
            {
                GdiText(g, "未打开脚本", 10f, FontStyle.Regular, new RectangleF(16, 16, W - 32, 24),
                    Theme.TextDim, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                return;
            }

            // 左侧层级引导线
            using (Pen guide = new Pen(Theme.Border, 1f))
            {
                for (int i = 0; i < _rows.Count; i++)
                {
                    Row r = _rows[i];
                    if (r.Depth <= 0) continue;
                    int gx = 10 + r.Depth * Indent - Indent / 2;
                    g.DrawLine(guide, gx, r.Bounds.Top + 4, gx, r.Bounds.Bottom - 4);
                }
            }

            DrawContainerBoxes(g);

            int visibleTop = 0, visibleBottom = H;
            for (int i = 0; i < _rows.Count; i++)
            {
                Row r = _rows[i];
                if (r.Bounds.Bottom < visibleTop || r.Bounds.Top > visibleBottom) continue;
                if (r.IsBranch) DrawBranchRow(g, r, i);
                else DrawStepRow(g, r, i);
            }

            if (_rows.Count > 0 && _contentHeight > H)
                DrawScrollBar(g);

            if (_dragging && _dragBlock != null) DrawDragIndicator(g);
        }

        private void DrawDragIndicator(Graphics g)
        {
            List<Block> owner = _doc != null ? _doc.ParentListOf(_dragBlock) : null;
            if (owner == null) return;
            int from = owner.IndexOf(_dragBlock);
            int to = _dropRowIndex;
            if (to < 0) to = from;
            if (to > owner.Count) to = owner.Count;

            int lineY = -1;
            if (to < owner.Count)
            {
                foreach (Row r in _rows)
                {
                    if (r.Block == owner[to]) { lineY = r.Bounds.Top - 1; break; }
                }
            }
            if (lineY < 0)
            {
                for (int i = _rows.Count - 1; i >= 0; i--)
                {
                    if (_rows[i].Block != null && owner.Contains(_rows[i].Block)) { lineY = _rows[i].Bounds.Bottom; break; }
                }
            }
            if (lineY < 0) lineY = 40;

            using (Pen pen = new Pen(Theme.Accent, 3f))
                g.DrawLine(pen, 12, lineY, W - 16, lineY);
            using (SolidBrush dot = new SolidBrush(Theme.Accent))
                g.FillEllipse(dot, 8, lineY - 4, 8, 8);

            string hint = "拖动中：把「" + Catalog.TitleOf(_dragBlock.Type) + "」放到第 " + Math.Min(to + 2, owner.Count + 1) + " 步";
            Size sz = TextRenderer.MeasureText(g, hint, Theme.UiFontCached(8.5f, FontStyle.Regular),
                new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPrefix);
            int w = sz.Width / Math.Max(1, Theme.Px(1)) + Theme.Px(20);
            Rectangle box = new Rectangle(W - w - 16, 8, w, 24);
            using (SolidBrush bg = new SolidBrush(Theme.Accent)) g.FillRectangle(bg, box);
            GdiText(g, hint, 8.5f, FontStyle.Regular,
                new RectangleF(box.X + 10, box.Y, box.Width - 16, box.Height), Color.White,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }

        /// <summary>把容器内部步骤用左侧竖线和底色“包”起来，直观体现包含关系。</summary>
        private void DrawContainerBoxes(Graphics g)
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                Row r = _rows[i];
                if (r.Block == null || !r.Block.IsContainer || !r.Expanded) continue;
                int end = i;
                for (int j = i + 1; j < _rows.Count; j++)
                {
                    if (_rows[j].Depth <= r.Depth) break;
                    end = j;
                }
                if (end <= i) continue;
                Rectangle top = r.Bounds;
                Rectangle bottom = _rows[end].Bounds;
                Rectangle box = new Rectangle(top.X + 11, top.Bottom - 1, top.Width - 10, bottom.Bottom - top.Bottom + 4);
                Color accent = Theme.CategoryAccent(r.Block.Category);
                using (SolidBrush fill = new SolidBrush(Theme.Mix(Theme.Canvas, accent, 0.05f)))
                    g.FillRectangle(fill, box);
                using (Pen pen = new Pen(accent, 2f))
                    g.DrawLine(pen, box.X, box.Y, box.X, box.Bottom);
                using (Pen hair = new Pen(Theme.Mix(Theme.Canvas, accent, 0.30f), 1f))
                {
                    g.DrawLine(hair, box.X, box.Bottom, box.Right, box.Bottom);
                    g.DrawLine(hair, box.Right - 1, box.Y, box.Right - 1, box.Bottom);
                }
            }
        }

        private void DrawScrollBar(Graphics g)
        {
            int trackTop = 4;
            int trackHeight = H - 8;
            if (trackHeight <= 20) return;
            float ratio = (float)H / Math.Max(H, _contentHeight);
            int thumbH = Math.Max(24, (int)(trackHeight * ratio));
            int maxScroll = Math.Max(1, _contentHeight - H);
            int thumbY = trackTop + (int)((trackHeight - thumbH) * ((float)_scrollY / maxScroll));
            using (SolidBrush b = new SolidBrush(Theme.Mix(Theme.Border, Theme.Panel, 0.2f)))
                g.FillRectangle(b, W - 8, thumbY, 5, thumbH);
        }

        private void DrawBranchRow(Graphics g, Row r, int index)
        {
            Rectangle b = r.Bounds;
            Color accent = Theme.Mix(Theme.Warning, Theme.Text, 0.25f);
            // 分支名做成一枚小标签，不再整行铺色块
            int w = Math.Min(b.Width - 8, Theme.Px(58));
            Rectangle tag = new Rectangle(b.X + 4, b.Y + 2, w, b.Height - 4);
            using (SolidBrush fill = new SolidBrush(Theme.IsLight ? Color.FromArgb(0xFD, 0xF4, 0xE3) : Theme.Mix(Theme.Panel, Theme.Warning, 0.14f)))
            using (GraphicsPath p = Theme.RoundedRect(tag, 4))
                g.FillPath(fill, p);
            GdiText(g, r.Label, 8.5f, FontStyle.Bold,
                new RectangleF(tag.X + 8, tag.Y, tag.Width - 10, tag.Height),
                accent, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }

        private void DrawStepRow(Graphics g, Row r, int index)
        {
            Rectangle b = r.Bounds;
            bool hover = index == _hoverRow;
            bool selected = _selected != null && r.Block != null && _selected == r.Block;
            bool running = r.Block != null && !string.IsNullOrEmpty(_highlightId) && _highlightId == r.Block.Id;
            bool error = r.Block != null && !string.IsNullOrEmpty(_errorId) && _errorId == r.Block.Id;
            bool disabled = r.Block != null && r.Block.Disabled;

            // v2.5：开始行与普通步骤保持同样的白底样式，只用绿色 ▶ 图标区分
            Color back = Theme.Canvas;
            if (error) back = Theme.Mix(Theme.Canvas, Theme.Danger, 0.10f);
            else if (running) back = Theme.Mix(Theme.Canvas, Theme.Accent, 0.12f);
            else if (selected) back = Theme.Mix(Theme.Canvas, Theme.Accent, 0.07f);
            else if (hover) back = Theme.PanelAlt;

            using (SolidBrush fill = new SolidBrush(back)) g.FillRectangle(fill, b.X, b.Y, b.Width, b.Height);
            using (Pen line = new Pen(Theme.Border, 1f))
                g.DrawLine(line, b.X, b.Bottom, b.Right, b.Bottom);

            Color accent = r.IsStart ? Theme.Success : Theme.CategoryAccent(r.Block != null ? r.Block.Category : BlockCategory.Tool);

            // 左侧类别竖条
            using (SolidBrush bar = new SolidBrush(disabled ? Theme.Border : accent))
                g.FillRectangle(bar, b.X, b.Y + 5, 3, RowHeight - 11);

            // 行首展开箭头：整行可点，箭头只负责表达状态
            if (!r.IsStart)
                DrawChevron(g, r, hover, selected, accent);

            // 序号（加深颜色，一眼看清第几步）
            int numX = b.X + 28;
            if (r.Depth == 0)
            {
                GdiText(g, NumberText(index), 9.5f, FontStyle.Bold,
                    new RectangleF(numX, b.Y + (RowHeight - 20) / 2f, 22, 20),
                    disabled ? Theme.TextDim : Theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            }
            else
            {
                using (SolidBrush nb = new SolidBrush(Theme.Mix(Theme.TextDim, Theme.Canvas, 0.25f)))
                    g.FillEllipse(nb, numX + 7, b.Y + 15, 4, 4);
            }

            // 图标
            int iconX = b.X + 52;
            BlockIcons.Draw(g, r.IsStart ? "start" : (r.Block != null ? r.Block.Type : "comment"),
                new RectangleF(iconX, b.Y + 9, 16, 16), disabled ? Theme.TextDim : accent);

            // 标题 + 摘要
            string title = r.IsStart ? "开始" : Catalog.TitleOf(r.Block.Type);
            string summary = r.IsStart ? "" : Summary(r.Block);
            int titleX = iconX + 24;
            int rightReserve = 16;
            if (disabled) rightReserve += 52;
            if (r.Block != null && r.Block.IsContainer) rightReserve += 66;
            int titleW = Math.Max(40, b.Width - (titleX - b.X) - rightReserve);
            GdiText(g, title, 10f, FontStyle.Regular,
                new RectangleF(titleX, b.Y + (RowHeight - 20) / 2f, titleW, 20),
                disabled ? Theme.TextDim : Theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (!string.IsNullOrEmpty(summary))
            {
                Size ts = TextRenderer.MeasureText(g, title, Theme.UiFontCached(10f, FontStyle.Regular),
                    new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPrefix);
                int sx = titleX + Math.Min(ts.Width + 10, Math.Max(30, titleW - 60));
                int sumW = Math.Max(30, b.Right - sx - rightReserve);
                GdiText(g, summary, 9f, FontStyle.Regular,
                    new RectangleF(sx, b.Y + (RowHeight - 20) / 2f, sumW, 20),
                    disabled ? Theme.Mix(Theme.TextDim, Theme.Canvas, 0.35f) : Theme.TextDim,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }

            if (r.Block != null && r.Block.IsContainer)
            {
                int count = 0;
                foreach (BranchBody br in r.Block.Branches) count += br.Blocks.Count;
                GdiText(g, "共 " + count + " 步", 8.5f, FontStyle.Regular,
                    new RectangleF(b.Right - 74, b.Y + (RowHeight - 20) / 2f, 58, 20), Theme.TextDim,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
            }
            if (disabled)
            {
                GdiText(g, "已禁用", 8f, FontStyle.Regular,
                    new RectangleF(b.Right - 60, b.Y + (RowHeight - 20) / 2f, 50, 20), Theme.TextDim,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
            }

            if (r.Expanded && r.Block != null && !r.Block.IsContainer) DrawDetail(g, r);
        }

        /// <summary>行首的展开/收起箭头（▶/▼）：整行可点，箭头只提示当前状态。</summary>
        private void DrawChevron(Graphics g, Row r, bool hover, bool selected, Color accent)
        {
            Rectangle c = r.Chevron;
            bool expanded = r.Expanded;
            if (hover || selected)
            {
                using (SolidBrush bg = new SolidBrush(Theme.Mix(Theme.Canvas, accent, expanded ? 0.16f : 0.10f)))
                using (GraphicsPath p = Theme.RoundedRect(new RectangleF(c.X, c.Y, c.Width, c.Height), 4))
                    g.FillPath(bg, p);
            }
            Color col = expanded || hover || selected ? accent : Theme.Mix(accent, Theme.TextDim, 0.45f);
            using (SolidBrush b = new SolidBrush(col))
            using (GraphicsPath tri = new GraphicsPath())
            {
                float cx = c.X + c.Width / 2f;
                float cy = c.Y + c.Height / 2f;
                if (expanded)
                    tri.AddPolygon(new PointF[] { new PointF(cx - 4.5f, cy - 2.5f), new PointF(cx + 4.5f, cy - 2.5f), new PointF(cx, cy + 3.5f) });
                else
                    tri.AddPolygon(new PointF[] { new PointF(cx - 2.5f, cy - 4.5f), new PointF(cx + 3.5f, cy), new PointF(cx - 2.5f, cy + 4.5f) });
                g.FillPath(b, tri);
            }
        }

        private void DrawDetail(Graphics g, Row r)
        {
            Rectangle d = r.DetailBounds;
            using (SolidBrush fill = new SolidBrush(Theme.PanelAlt))
                g.FillRectangle(fill, d);
            using (Pen p = new Pen(Theme.Border))
                g.DrawRectangle(p, d);

            Block b = r.Block;
            List<KeyValuePair<Rectangle, string>> hits = BuildDetailHits(b, d);
            int pillTop = d.Bottom - DetailPad / 2 - PillRows(b, InnerPillWidth(d)) * (PillHeight + PillGap);
            float y = d.Y + 6;
            foreach (string line in DetailLines(b))
            {
                if (y + 15 > pillTop - 2) break;
                GdiText(g, line, 8.5f, FontStyle.Regular,
                    new RectangleF(d.X + 8, y, d.Width - 16, 15), Theme.TextDim,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                y += 15;
            }

            foreach (KeyValuePair<Rectangle, string> hit in hits)
            {
                string label = DetailLabel(hit.Value);
                bool active = IsDetailActive(b, hit.Value);
                Color color = active ? Theme.Accent : Theme.TextDim;
                DrawPill(g, hit.Key, label, color, active);
            }
        }

        private static string DetailLabel(string action)
        {
            switch (action)
            {
                case "left": return "左键";
                case "right": return "右键";
                case "click": return "单击";
                case "dblclick": return "双击";
                case "mode_fixed": return "固定坐标";
                case "mode_image": return "自动识图";
                case "record": return "◎ 记录位置";
                case "shot": return "📷 截图取图";
                case "templates": return "模板库";
                case "edit": return "编辑参数";
                case "disable": return "禁用";
                case "enable": return "启用";
                default: return action;
            }
        }

        private static bool IsDetailActive(Block b, string action)
        {
            string mode = ParamText(b, "coord_mode");
            switch (action)
            {
                case "right": return ParamText(b, "button") == "right";
                case "left": return ParamText(b, "button") != "right";
                case "dblclick": return ParamText(b, "action") == "dblclick";
                case "click": return ParamText(b, "action") != "dblclick";
                case "mode_image": return mode == "image" || mode == "bound";
                case "mode_fixed": return mode != "image" && mode != "bound";
                case "enable": return true;
                default: return false;
            }
        }

        /// <summary>详情区按钮的宽度与顺序（绘制与点击共用，避免位置不一致）。</summary>
        private static List<KeyValuePair<string, int>> DetailButtonSpec(Block b)
        {
            List<KeyValuePair<string, int>> items = new List<KeyValuePair<string, int>>();
            if (b.Type == "mouse_click")
            {
                items.Add(new KeyValuePair<string, int>("mode_fixed", 80));
                items.Add(new KeyValuePair<string, int>("mode_image", 80));
                items.Add(new KeyValuePair<string, int>("record", 96));
                items.Add(new KeyValuePair<string, int>("shot", 100));
                items.Add(new KeyValuePair<string, int>("templates", 68));
                items.Add(new KeyValuePair<string, int>("left", 48));
                items.Add(new KeyValuePair<string, int>("right", 48));
                items.Add(new KeyValuePair<string, int>("click", 48));
                items.Add(new KeyValuePair<string, int>("dblclick", 48));
                items.Add(new KeyValuePair<string, int>("edit", 76));
                items.Add(new KeyValuePair<string, int>(b.Disabled ? "enable" : "disable", 56));
            }
            else
            {
                items.Add(new KeyValuePair<string, int>("edit", 76));
                items.Add(new KeyValuePair<string, int>(b.Disabled ? "enable" : "disable", 56));
            }
            return items;
        }

        private static int InnerPillWidth(Rectangle d)
        {
            return Math.Max(120, d.Width - 16);
        }

        /// <summary>按钮按可用宽度自动换行，行数决定详情区高度。</summary>
        private static int PillRows(Block b, int innerWidth)
        {
            return WrapDetailButtons(b, innerWidth).Count;
        }

        private static List<List<KeyValuePair<string, int>>> WrapDetailButtons(Block b, int innerWidth)
        {
            List<List<KeyValuePair<string, int>>> rows = new List<List<KeyValuePair<string, int>>>();
            List<KeyValuePair<string, int>> cur = new List<KeyValuePair<string, int>>();
            int x = 0;
            foreach (KeyValuePair<string, int> item in DetailButtonSpec(b))
            {
                if (cur.Count > 0 && x + item.Value > innerWidth)
                {
                    rows.Add(cur);
                    cur = new List<KeyValuePair<string, int>>();
                    x = 0;
                }
                cur.Add(item);
                x += item.Value + PillGap;
            }
            if (cur.Count > 0) rows.Add(cur);
            return rows;
        }

        private static int DetailHeightFor(Block b, int rowWidth)
        {
            Rectangle d = new Rectangle(0, 0, Math.Max(120, rowWidth - 32), 0);
            int inner = InnerPillWidth(d);
            int rows = WrapDetailButtons(b, inner).Count;
            int textLines = Math.Min(DetailLines(b).Count, 2);
            return DetailPad + textLines * 15 + rows * (PillHeight + PillGap) + DetailPad;
        }

        private static List<KeyValuePair<Rectangle, string>> BuildDetailHits(Block b, Rectangle d)
        {
            return LayoutDetailHits(b, d, InnerPillWidth(d), true);
        }

        private static List<KeyValuePair<Rectangle, string>> LayoutDetailHits(Block b, Rectangle d, int innerWidth, bool withRects)
        {
            List<List<KeyValuePair<string, int>>> rows = WrapDetailButtons(b, innerWidth);
            int totalH = rows.Count * (PillHeight + PillGap);
            int y = withRects ? d.Bottom - DetailPad / 2 - totalH : 0;
            List<KeyValuePair<Rectangle, string>> list = new List<KeyValuePair<Rectangle, string>>();
            foreach (List<KeyValuePair<string, int>> row in rows)
            {
                int x = withRects ? d.X + 8 : 0;
                foreach (KeyValuePair<string, int> item in row)
                {
                    if (withRects) list.Add(new KeyValuePair<Rectangle, string>(new Rectangle(x, y, item.Value, PillHeight), item.Key));
                    x += item.Value + PillGap;
                }
                y += PillHeight + PillGap;
            }
            return list;
        }

        private static void DrawPill(Graphics g, Rectangle rect, string text, Color color, bool filled)
        {
            using (GraphicsPath p = Theme.RoundedRect(rect, 4))
            {
                if (filled)
                {
                    using (SolidBrush b = new SolidBrush(color)) g.FillPath(b, p);
                }
                else
                {
                    using (SolidBrush b = new SolidBrush(Theme.Panel)) g.FillPath(b, p);
                    using (Pen pen = new Pen(Theme.Border)) g.DrawPath(pen, p);
                }
            }
            TextRenderer.DrawText(g, text, Theme.UiFontCached(8.5f, FontStyle.Regular), Theme.Device(rect),
                filled ? Color.White : Theme.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        /// <summary>在“设计坐标”里排版、按设备像素绘制文字：不受缩放变换影响，高 DPI 下清晰不糊。</summary>
        private static void GdiText(Graphics g, string text, float size, FontStyle style, RectangleF designRect, Color color, TextFormatFlags flags)
        {
            if (string.IsNullOrEmpty(text)) return;
            TextRenderer.DrawText(g, text, Theme.UiFontCached(size, style), Theme.Device(designRect), color,
                flags | TextFormatFlags.NoPrefix);
        }

        /// <summary>“找不到图时”的中文描述。</summary>
        internal static string FailText(Block b)
        {
            string mode = ParamText(b, "on_fail");
            if (mode == "skip") return "跳过这一步";
            if (mode == "error") return "报错停止";
            return "重试 " + ParamText(b, "retries") + " 次 / 每 " + ParamText(b, "retry_delay") + " 秒";
        }

        private static string NumberText(int index)
        {
            // 行号：开始=①，之后 ②③…
            if (index < 20) return ((char)(0x2460 + index)).ToString();
            return (index + 1) + ".";
        }

        // ---------------------------- 摘要文案 ----------------------------

        private static string ParamText(Block b, string key)
        {
            object v;
            if (b.Params.TryGetValue(key, out v)) return Engine.FormatValue(v);
            return "";
        }

        private string Summary(Block b)
        {
            switch (b.Type)
            {
                case "mouse_click":
                    {
                        string btn = ParamText(b, "button") == "right" ? "右键" : "左键";
                        string act = ParamText(b, "action") == "dblclick" ? "双击" : "单击";
                        string mode = ParamText(b, "coord_mode");
                        if (mode == "image")
                        {
                            string tpl = ParamText(b, "template");
                            string file = tpl.Length > 0 ? System.IO.Path.GetFileName(tpl) : "未截图";
                            return "自动识图 " + file + " · " + btn + act;
                        }
                        string pos = ParamText(b, "x").Length > 0
                            ? "(" + ParamText(b, "x") + ", " + ParamText(b, "y") + ")"
                            : (mode == "bound" ? "旧版识图坐标" : "未记录坐标");
                        return pos + " · " + btn + act;
                    }
                case "mouse_move":
                    return "(" + ParamText(b, "x") + ", " + ParamText(b, "y") + ")";
                case "mouse_wheel":
                    return (ParamText(b, "dir") == "up" ? "向上" : "向下") + " " + ParamText(b, "amount") + " 格";
                case "key_type":
                    return "\"" + Shorten(ParamText(b, "text"), 24) + "\"";
                case "key_press":
                    return ParamText(b, "key");
                case "sleep":
                    return ParamText(b, "seconds") + " 秒";
                case "find_image":
                    return System.IO.Path.GetFileName(ParamText(b, "template")) + " · 阈值" + ParamText(b, "threshold");
                case "find_color":
                    return ParamText(b, "rgb");
                case "log":
                    return "[" + ParamText(b, "level") + "] " + Shorten(ParamText(b, "text"), 26);
                case "if":
                    return IsImageCondition(b)
                        ? "如果有图片 " + System.IO.Path.GetFileName(ParamText(b, "template"))
                        : CondText(b);
                case "while":
                    return "当 " + CondText(b);
                case "for_count":
                    return ParamText(b, "times") + " 次";
                case "set_var":
                case "get_var":
                case "create_var":
                    return ParamText(b, "name");
                case "call_func":
                    return ParamText(b, "func");
                case "comment":
                    return Shorten(ParamText(b, "text"), 30);
                default:
                    return "";
            }
        }

        private static bool IsImageCondition(Block b)
        {
            object v;
            if (b.Params.TryGetValue("cond_type", out v))
                return string.Equals(Convert.ToString(v), "image", StringComparison.OrdinalIgnoreCase);
            return false;
        }

        private string CondText(Block b)
        {
            PinLink link = b.Input("cond");
            if (link != null && link.HasSource)
            {
                Block src = _doc != null ? _doc.FindBlock(link.SourceBlockId) : null;
                if (src != null) return "条件来自「" + Catalog.TitleOf(src.Type) + "」";
                return "条件";
            }
            return "条件";
        }

        private static string Shorten(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\r", "").Replace("\n", " ");
            return s.Length <= max ? s : s.Substring(0, max) + "…";
        }

        private static List<string> DetailLines(Block b)
        {
            List<string> lines = new List<string>();
            switch (b.Type)
            {
                case "mouse_click":
                    // 展开行只放一行摘要，详细参数统一在右侧「参数」抽屉里改，避免两处重复显示
                    if (ParamText(b, "coord_mode") == "image")
                    {
                        string tpl = ParamText(b, "template");
                        lines.Add("自动识图 " + (tpl.Length > 0 ? System.IO.Path.GetFileName(tpl) : "（还没截图）")
                            + " · 阈值 " + ParamText(b, "threshold") + " · " + FailText(b));
                    }
                    else
                    {
                        lines.Add((ParamText(b, "x").Length > 0
                                ? "固定坐标 (" + ParamText(b, "x") + ", " + ParamText(b, "y") + ")"
                                : "固定坐标（还没记录位置）")
                            + " · 偏移 " + ParamText(b, "offset"));
                    }
                    break;
                case "find_image":
                    lines.Add("模板：" + ParamText(b, "template"));
                    lines.Add("区域：" + ParamText(b, "region") + "    阈值：" + ParamText(b, "threshold"));
                    break;
                case "if":
                    lines.Add(IsImageCondition(b)
                        ? "判断方式：有图片则执行，没有则跳过"
                        : "判断方式：条件表达式");
                    if (IsImageCondition(b))
                        lines.Add("模板：" + ParamText(b, "template") + "    区域：" + ParamText(b, "region")
                            + "    阈值：" + ParamText(b, "threshold"));
                    break;
                case "sleep":
                    lines.Add("等待 " + ParamText(b, "seconds") + " 秒（支持 0.5 这样的小数）");
                    break;
                case "key_type":
                    lines.Add("文本：" + ParamText(b, "text"));
                    break;
                case "key_press":
                    lines.Add("按键：" + ParamText(b, "key"));
                    break;
                default:
                    {
                        BlockInfo info = Catalog.Get(b.Type);
                        if (info != null)
                        {
                            List<string> parts = new List<string>();
                            foreach (ParamSpec spec in info.Params)
                            {
                                if (spec.Name == "branches") continue;
                                string v = ParamText(b, spec.Name);
                                if (v.Length > 0) parts.Add(spec.Label + "：" + v);
                            }
                            lines.Add(parts.Count > 0 ? string.Join("    ", parts.ToArray()) : info.Summary);
                        }
                        break;
                    }
            }
            return lines;
        }

        // ---------------------------- 交互 ----------------------------

        private Row RowAt(Point p)
        {
            for (int i = 0; i < _rows.Count; i++)
                if (_rows[i].Bounds.Contains(p)) return _rows[i];
            return null;
        }

        private int RowIndexAt(Point p)
        {
            for (int i = 0; i < _rows.Count; i++)
                if (_rows[i].Bounds.Contains(p)) return i;
            return -1;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (_doc == null) return;
            Focus();
            _pendingToggle = -1;
            Point p = ToDesign(e.Location);
            Row row = RowAt(p);
            if (row == null)
            {
                _selected = null;
                FireSelection();
                Invalidate();
                return;
            }
            if (row.IsStart)
            {
                _selected = null;
                FireSelection();
                Invalidate();
                return;
            }

            _selected = row.Block;
            FireSelection();
            Invalidate();

            if (e.Button == MouseButtons.Right)
            {
                ShowContextMenu(Cursor.Position, row.Block);
                return;
            }

            // 展开行里的快捷按钮：先匹配按钮，命中就不算“整行点击”
            if (row.Expanded && row.DetailBounds.Contains(p))
            {
                foreach (KeyValuePair<Rectangle, string> hit in BuildDetailHits(row.Block, row.DetailBounds))
                {
                    if (!hit.Key.Contains(p)) continue;
                    HandleDetailAction(row.Block, hit.Value);
                    return;
                }
            }

            // 整行都是展开/收起热区（箭头不再要求精确点中）
            _pendingToggle = _rows.IndexOf(row);
            _dragArmed = true;
            _dragBlock = row.Block;
            _dragStart = p;
            _dragFromY = row.Bounds.Top;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int idx = RowIndexAt(ToDesign(e.Location));
            if (idx != _hoverRow) { _hoverRow = idx; Invalidate(); }

            if (_dragArmed && e.Button == MouseButtons.Left && _dragBlock != null)
            {
                if (Math.Abs(ToDesign(e.Location).Y - _dragStart.Y) > 6) _dragging = true;
                if (_dragging)
                {
                    _dragPoint = ToDesign(e.Location);
                    Cursor = Cursors.SizeAll;
                    _dropRowIndex = DropIndexAt(ToDesign(e.Location).Y, _dragBlock);
                    Invalidate();
                }
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            bool wasDragging = _dragging && _dragBlock != null;
            if (wasDragging) CompleteDrag();
            else if (_pendingToggle >= 0 && _pendingToggle < _rows.Count)
            {
                // 整行单击＝展开/收起。双击（同一行连点两次）留给“打开完整参数窗口”，这里不重复切换。
                Row row = _rows[_pendingToggle];
                if (row.Block != null)
                {
                    DateTime now = DateTime.Now;
                    bool doubleClick = row.Block.Id == _lastToggleId &&
                        (now - _lastToggleAt).TotalMilliseconds < SystemInformation.DoubleClickTime;
                    if (!doubleClick)
                    {
                        _lastToggleId = row.Block.Id;
                        _lastToggleAt = now;
                        ToggleExpanded(row.Block);
                    }
                }
            }
            _pendingToggle = -1;
            _dragging = false;
            _dragArmed = false;
            _dropRowIndex = -1;
            Cursor = Cursors.Default;
            Invalidate();
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            Row row = RowAt(ToDesign(e.Location));
            if (row == null || row.IsStart || row.Block == null) return;
            if (row.Block.IsContainer) return;   // 容器双击只做展开/收起，避免来回切换
            Action<Block> h = BlockEditRequested;
            if (h != null) h(row.Block);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            int max = Math.Max(0, _contentHeight - H + 12);
            _scrollY = Math.Max(0, Math.Min(max, _scrollY - (int)(e.Delta / Theme.Scale)));
            Rebuild();
            FireViewChanged();
        }

        private int DropIndexAt(int mouseY, Block dragged)
        {
            Row target = RowAt(new Point(10, mouseY));
            if (target == null || target.IsStart || target.IsBranch || target.Block == null) return -1;
            if (target.Owner == null) return -1;
            if (target.Owner != _doc.ParentListOf(dragged)) return -1;
            bool after = mouseY > target.Bounds.Top + target.Bounds.Height / 2;
            int idx = target.Owner.IndexOf(target.Block);
            return after ? idx + 1 : idx;
        }

        private void CompleteDrag()
        {
            if (_dropRowIndex < 0) return;
            List<Block> owner = _doc.ParentListOf(_dragBlock);
            if (owner == null) return;
            int oldIndex = owner.IndexOf(_dragBlock);
            int insert = _dropRowIndex;
            if (insert < 0 || insert > owner.Count) return;
            if (insert == oldIndex || insert == oldIndex + 1) return;

            PushUndo();
            owner.RemoveAt(oldIndex);
            if (oldIndex < insert) insert--;
            owner.Insert(Math.Max(0, Math.Min(insert, owner.Count)), _dragBlock);
            _doc.EnsureNextLinks();
            Rebuild();
            FireChanged();
        }

        private void ToggleExpanded(Block b)
        {
            if (b == null) return;
            if (_expanded.Contains(b.Id))
            {
                _expanded.Remove(b.Id);
            }
            else
            {
                _expanded.Clear();          // 手风琴：一次只展开一个
                _expanded.Add(b.Id);
            }
            Rebuild();
            EnsureVisible(b);
        }

        private void ToggleDisabled(Block b)
        {
            if (b == null) return;
            PushUndo();
            b.Disabled = !b.Disabled;
            Rebuild();
            FireChanged();
        }

        private void HandleDetailAction(Block b, string action)
        {
            if (b == null) return;
            switch (action)
            {
                case "left":
                case "right":
                    PushUndo();
                    b.Params["button"] = action;
                    Rebuild();
                    FireChanged();
                    break;
                case "click":
                case "dblclick":
                    PushUndo();
                    b.Params["action"] = action;
                    Rebuild();
                    FireChanged();
                    break;
                case "record":
                    {
                        Action<Block> h = RecordClickRequested;
                        if (h != null) h(b);
                        break;
                    }
                case "shot":
                case "templates":
                    {
                        Action<Block> h = CaptureTemplateRequested;
                        if (h != null) h(b);
                        else
                        {
                            using (TemplateManagerForm f = new TemplateManagerForm(_doc, true))
                            {
                                if (f.ShowDialog(FindForm()) == DialogResult.OK && !string.IsNullOrEmpty(f.SelectedTemplate))
                                {
                                    PushUndo();
                                    b.Params["coord_mode"] = "image";
                                    b.Params["template"] = f.SelectedTemplate;
                                    Rebuild();
                                    FireChanged();
                                }
                            }
                        }
                        break;
                    }
                case "mode_fixed":
                    PushUndo();
                    b.Params["coord_mode"] = "recorded";
                    Rebuild();
                    FireChanged();
                    break;
                case "mode_image":
                    PushUndo();
                    b.Params["coord_mode"] = "image";
                    if (!b.Params.ContainsKey("template")) b.Params["template"] = "";
                    Rebuild();
                    FireChanged();
                    break;
                case "edit":
                    {
                        Action<Block> h = BlockEditRequested;
                        if (h != null) h(b);
                        break;
                    }
                case "disable":
                case "enable":
                    ToggleDisabled(b);
                    break;
            }
        }

        /// <summary>截图取图完成后由主窗口回调：把模板写进这一步并切到自动识图。</summary>
        public void ApplyCapturedTemplate(Block b, string template)
        {
            if (b == null || string.IsNullOrEmpty(template)) return;
            PushUndo();
            b.Params["coord_mode"] = "image";
            b.Params["template"] = template;
            if (!b.Params.ContainsKey("threshold")) b.Params["threshold"] = 0.8d;
            if (!b.Params.ContainsKey("region")) b.Params["region"] = "active";
            _expanded.Clear();
            _expanded.Add(b.Id);
            Rebuild();
            EnsureVisible(b);
            FireChanged();
        }

        /// <summary>把这一步的点击位置绑定到前面最近的识图积木（旧脚本兼容，仅专业模式右键菜单使用）。</summary>
        private void BindToVision(Block b)
        {
            List<Block> list = _doc != null ? _doc.ParentListOf(b) : null;
            if (list == null) return;
            int idx = list.IndexOf(b);
            Block src = null;
            for (int i = idx - 1; i >= 0; i--)
            {
                if (list[i].Type == "find_image") { src = list[i]; break; }
                if (list[i].Type == "if" && IsImageCondition(list[i])) { src = list[i]; break; }
            }
            if (src == null)
            {
                MessageBox.Show(FindForm(),
                    "这一步前面没有可绑定的识图来源。\n\n新版推荐直接用这一步自带的「📷 截图取图」，一步完成识图+点击。",
                    "BlockAuto", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            PushUndo();
            b.Params["coord_mode"] = "bound";
            PinLink lx = b.EnsureInput("x");
            PinLink ly = b.EnsureInput("y");
            lx.From = src.Id + ".x";
            ly.From = src.Id + ".y";
            Rebuild();
            FireChanged();
        }


        private void EnsureVisible(Block b)
        {
            foreach (Row r in _rows)
            {
                if (r.Block != b) continue;
                if (r.Bounds.Top < 0) _scrollY = Math.Max(0, _scrollY + r.Bounds.Top - 8);
                else if (r.Bounds.Bottom > H) _scrollY += r.Bounds.Bottom - H + 8;
                Rebuild();
                return;
            }
        }

        // ---------------------------- 增删改 ----------------------------

        public void AddBlockType(string type)
        {
            if (_doc == null) return;
            List<Block> list = CurrentList;
            int index = list.Count;
            if (_selected != null)
            {
                List<Block> selList = _doc.ParentListOf(_selected);
                if (selList == list) index = list.IndexOf(_selected) + 1;
            }
            Block b = _doc.NewBlock(type);
            if (type == "sleep" && !b.Params.ContainsKey("seconds")) b.Params["seconds"] = 1d;
            PushUndo();
            list.Insert(Math.Max(0, Math.Min(index, list.Count)), b);
            _selected = b;
            _doc.EnsureNextLinks();
            Rebuild();
            EnsureVisible(b);
            FireChanged();
            FireSelection();
        }

        public void DeleteSelected()
        {
            if (_selected == null || _doc == null) return;
            List<Block> list = _doc.ParentListOf(_selected);
            if (list == null) return;
            PushUndo();
            RemoveBlockRecursive(_selected);
            list.Remove(_selected);
            _selected = null;
            _doc.EnsureNextLinks();
            Rebuild();
            FireChanged();
            FireSelection();
        }

        private void RemoveBlockRecursive(Block b)
        {
            foreach (Block x in _doc.AllBlocks())
            {
                foreach (PinLink p in x.Inputs)
                    if (p.HasSource && p.SourceBlockId == b.Id) p.From = "";
            }
            foreach (BranchBody br in b.Branches)
                foreach (Block c in new List<Block>(br.Blocks)) RemoveBlockRecursive(c);
        }

        public void CopySelected()
        {
            if (_selected == null) return;
            _clipboard.Clear();
            _clipboard.Add(_selected);
        }

        public void PasteClipboard()
        {
            if (_doc == null || _clipboard.Count == 0) return;
            List<Block> list = CurrentList;
            int index = list.Count;
            if (_selected != null)
            {
                List<Block> selList = _doc.ParentListOf(_selected);
                if (selList == list) index = list.IndexOf(_selected) + 1;
            }
            PushUndo();
            Block last = null;
            foreach (Block src in _clipboard)
            {
                Block copy = _doc.CloneBlock(src, null);
                list.Insert(Math.Min(index, list.Count), copy);
                index++;
                last = copy;
            }
            _selected = last;
            _doc.EnsureNextLinks();
            Rebuild();
            EnsureVisible(last);
            FireChanged();
        }

        private void MoveSelected(int delta)
        {
            if (_selected == null) return;
            List<Block> list = _doc.ParentListOf(_selected);
            if (list == null) return;
            int i = list.IndexOf(_selected);
            int j = i + delta;
            if (i < 0 || j < 0 || j >= list.Count) return;
            PushUndo();
            list.RemoveAt(i);
            list.Insert(j, _selected);
            _doc.EnsureNextLinks();
            Rebuild();
            FireChanged();
        }

        private void MoveSelectedEdge(bool top)
        {
            if (_selected == null) return;
            List<Block> list = _doc.ParentListOf(_selected);
            if (list == null) return;
            int i = list.IndexOf(_selected);
            if (i < 0) return;
            PushUndo();
            list.RemoveAt(i);
            list.Insert(top ? 0 : list.Count, _selected);
            _doc.EnsureNextLinks();
            Rebuild();
            FireChanged();
        }

        public void ToggleBreakpoint(Block b)
        {
            if (b == null) return;
            PushUndo();
            b.Breakpoint = !b.Breakpoint;
            Rebuild();
            FireChanged();
        }

        public void AddElifBranch(Block b)
        {
            if (b == null || b.Type != "if") return;
            PushUndo();
            List<object> names = b.Params.ContainsKey("branches") ? b.Params["branches"] as List<object> : null;
            if (names == null)
            {
                names = new List<object>();
                names.Add("main");
                names.Add("else");
                b.Params["branches"] = names;
            }
            int insertAt = names.Count - 1;
            if (insertAt < 1) insertAt = 1;
            string name = "elif" + DateTime.Now.Ticks.ToString().Substring(10);
            names.Insert(insertAt, name);
            b.Branches.Insert(insertAt, new BranchBody(name));
            _doc.NormalizeBranches();
            Rebuild();
            FireChanged();
        }

        public void SetRecordedPoint(Block b, int x, int y)
        {
            if (b == null) return;
            PushUndo();
            b.Params["x"] = (double)x;
            b.Params["y"] = (double)y;
            b.Params["coord_mode"] = "recorded";
            _expanded.Clear();
            _expanded.Add(b.Id);
            Rebuild();
            FireChanged();
        }

        public void ArrangeAll()
        {
            // 列表模式不需要整理，保留接口并确保顺序与 next 一致
            if (_doc == null) return;
            _doc.EnsureNextLinks();
            Rebuild();
            FireChanged();
        }

        public void PushUndo()
        {
            if (_doc == null) return;
            PushUndoSnapshot(_doc.ToJson());
        }

        public void PushUndoSnapshot(string json)
        {
            if (_doc == null || json == null) return;
            _undo.Add(json);
            if (_undo.Count > 80) _undo.RemoveAt(0);
            _redo.Clear();
        }

        public bool Undo()
        {
            if (_undo.Count == 0 || _doc == null) return false;
            string current = _doc.ToJson();
            string prev = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);
            _redo.Add(current);
            Restore(prev);
            return true;
        }

        public bool Redo()
        {
            if (_redo.Count == 0 || _doc == null) return false;
            string current = _doc.ToJson();
            string next = _redo[_redo.Count - 1];
            _redo.RemoveAt(_redo.Count - 1);
            _undo.Add(current);
            Restore(next);
            return true;
        }

        private void Restore(string json)
        {
            ScriptDocument restored = ScriptDocument.FromJson(json, _doc.FilePath);
            _doc.Blocks = restored.Blocks;
            _doc.Functions = restored.Functions;
            _doc.GlobalVars = restored.GlobalVars;
            _doc.Settings = restored.Settings;
            if (!string.IsNullOrEmpty(_functionName) && _doc.FindFunction(_functionName) == null) _functionName = null;
            _selected = null;
            _expanded.Clear();
            Rebuild();
            Invalidate();
            FireChanged();
            FireSelection();
        }

        // ---------------------------- 高亮 / 选择 ----------------------------

        public void HighlightBlock(string id)
        {
            _highlightId = id;
            _errorId = null;
            if (!string.IsNullOrEmpty(id)) EnsureVisibleById(id);
            Invalidate();
        }

        public void ClearHighlight()
        {
            _highlightId = null;
            _errorId = null;
            Invalidate();
        }

        public void MarkError(string id)
        {
            _errorId = id;
            _highlightId = null;
            if (!string.IsNullOrEmpty(id)) EnsureVisibleById(id);
            Invalidate();
        }

        private void EnsureVisibleById(string id)
        {
            foreach (Row r in _rows)
            {
                if (r.Block == null || r.Block.Id != id) continue;
                if (r.Bounds.Top < 0) _scrollY = Math.Max(0, _scrollY + r.Bounds.Top - 8);
                else if (r.Bounds.Bottom > H) _scrollY += r.Bounds.Bottom - H + 8;
                Rebuild();
                return;
            }
        }

        public void SelectBlock(Block b)
        {
            _selected = b;
            if (b != null) EnsureVisibleById(b.Id);
            FireSelection();
            Invalidate();
        }

        public void SelectAndExpand(Block b)
        {
            _selected = b;
            if (b != null)
            {
                _expanded.Clear();
                _expanded.Add(b.Id);
                Rebuild();
                EnsureVisibleById(b.Id);
            }
            FireSelection();
            Invalidate();
        }

        public void NotifyDocumentChanged()
        {
            FireChanged();
        }

        // ---------------------------- 右键菜单 ----------------------------

        public void ShowContextMenu(Point screenPoint, Block block)
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.BackColor = Theme.Panel;
            menu.ForeColor = Theme.Text;
            menu.ShowImageMargin = false;

            if (block == null)
            {
                AddMenu(menu, "向脚本末尾添加等待 1 秒", delegate { AddBlockType("sleep"); });
                AddMenu(menu, "粘贴", delegate { PasteClipboard(); });
                menu.Show(screenPoint);
                return;
            }

            AddMenu(menu, "编辑参数…", delegate
            {
                _selected = block;
                Action<Block> h = BlockEditRequested;
                if (h != null) h(block);
            });
            AddMenu(menu, block.IsContainer ? "展开/收起内部步骤" : "展开/收起详情", delegate { ToggleExpanded(block); });
            menu.Items.Add(new ToolStripSeparator());
            AddMenu(menu, "复制这一步", delegate { _selected = block; CopySelected(); });
            AddMenu(menu, "粘贴到下面", delegate { _selected = block; PasteClipboard(); });
            AddMenu(menu, "在下面插入等待 0.5 秒", delegate
            {
                _selected = block;
                Block wait = _doc.NewBlock("sleep");
                wait.Params["seconds"] = 0.5d;
                List<Block> list = _doc.ParentListOf(block);
                if (list != null)
                {
                    PushUndo();
                    list.Insert(list.IndexOf(block) + 1, wait);
                    _selected = wait;
                    _doc.EnsureNextLinks();
                    Rebuild();
                    FireChanged();
                    FireSelection();
                }
            });
            menu.Items.Add(new ToolStripSeparator());
            AddMenu(menu, "上移一步", delegate { _selected = block; MoveSelected(-1); });
            AddMenu(menu, "下移一步", delegate { _selected = block; MoveSelected(1); });
            AddMenu(menu, "移到最上面", delegate { _selected = block; MoveSelectedEdge(true); });
            AddMenu(menu, "移到最下面", delegate { _selected = block; MoveSelectedEdge(false); });
            menu.Items.Add(new ToolStripSeparator());
            AddMenu(menu, block.Disabled ? "启用这一步" : "禁用这一步（运行时跳过）", delegate { ToggleDisabled(block); });
            AddMenu(menu, "删除这一步", delegate { _selected = block; DeleteSelected(); });
            if (!_simpleMode && block.Type != "comment")
                AddMenu(menu, "设为断点（专业）", delegate { ToggleBreakpoint(block); });
            menu.Show(screenPoint);
        }

        private static void AddMenu(ContextMenuStrip menu, string text, EventHandler h)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text);
            item.Click += h;
            menu.Items.Add(item);
        }

        // ---------------------------- 拖放新增 ----------------------------

        protected override void OnDragEnter(DragEventArgs drgevent)
        {
            if (drgevent.Data.GetDataPresent("BlockAuto.BlockType")) drgevent.Effect = DragDropEffects.Copy;
            else base.OnDragEnter(drgevent);
        }

        protected override void OnDragOver(DragEventArgs drgevent)
        {
            if (drgevent.Data.GetDataPresent("BlockAuto.BlockType"))
            {
                drgevent.Effect = DragDropEffects.Copy;
                Point client = ToDesign(PointToClient(new Point(drgevent.X, drgevent.Y)));
                _dropRowIndex = DropIndexAt(client.Y, null);
                Invalidate();
            }
            else base.OnDragOver(drgevent);
        }

        protected override void OnDragDrop(DragEventArgs drgevent)
        {
            if (drgevent.Data.GetDataPresent("BlockAuto.BlockType"))
            {
                string type = Convert.ToString(drgevent.Data.GetData("BlockAuto.BlockType"));
                Point client = ToDesign(PointToClient(new Point(drgevent.X, drgevent.Y)));
                List<Block> list = CurrentList;
                int index = list.Count;
                Row target = RowAt(client);
                if (target != null && target.Block != null && target.Owner == list)
                    index = list.IndexOf(target.Block) + (client.Y > target.Bounds.Top + target.Bounds.Height / 2 ? 1 : 0);

                Block b = _doc.NewBlock(type);
                PushUndo();
                list.Insert(Math.Max(0, Math.Min(index, list.Count)), b);
                _selected = b;
                _doc.EnsureNextLinks();
                Rebuild();
                EnsureVisibleById(b.Id);
                FireChanged();
                FireSelection();
                _dropRowIndex = -1;
                Invalidate();
            }
            else base.OnDragDrop(drgevent);
        }

        protected override void OnDragLeave(EventArgs e)
        {
            _dropRowIndex = -1;
            Invalidate();
            base.OnDragLeave(e);
        }

        // ---------------------------- 事件 ----------------------------

        private void FireChanged()
        {
            EventHandler h = DocumentChanged;
            if (h != null) h(this, EventArgs.Empty);
        }

        private void FireViewChanged()
        {
            EventHandler h = ViewChanged;
            if (h != null) h(this, EventArgs.Empty);
        }

        private void FireSelection()
        {
            Action<Block> h = SelectionChanged;
            if (h != null) h(_selected);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Delete || e.KeyCode == Keys.Back)
            {
                DeleteSelected();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Up && e.Control)
            {
                MoveSelected(-1);
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Down && e.Control)
            {
                MoveSelected(1);
                e.Handled = true;
            }
        }
    }
}