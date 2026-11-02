using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace LabelPlus
{
    public sealed class QcCanvas : Control
    {
        public QcPage Page;
        public Image DisplayImage;
        public bool OriginalVisible;
        public int SelectedIndex = -1;
        public event EventHandler SelectionChanged;
        public event EventHandler BeforeEdit;
        public event EventHandler IssuesChanged;
        public event EventHandler IssueCreated;
        public event EventHandler ViewChanged;
        float zoom = 1;
        PointF origin;
        Point mouseStart;
        PointF oldOrigin;
        PointF arrowStart, arrowEnd;
        QcIssue beforeDrag;
        int dragKind; // 1 new arrow, 2 head, 3 tail/note, 4 note, 5 pan
        bool edited;
        bool handMode;
        public float Zoom { get { return zoom; } }
        public PointF ViewOrigin { get { return origin; } }
        static readonly Color Red = Color.FromArgb(222, 44, 56);

        public QcCanvas()
        {
            DoubleBuffered = true; TabStop = true;
            BackColor = Color.FromArgb(54, 57, 62);
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        }

        public void SetPage(QcPage page, Image image)
        {
            CancelDrag(); Page = page; DisplayImage = image; SelectedIndex = -1;
            OriginalVisible = false; Fit();
        }

        public void Fit()
        {
            if (Page == null || ClientSize.Width < 1 || ClientSize.Height < 1) return;
            zoom = Math.Max(.01f, Math.Min((ClientSize.Width - 32f) / Page.Width,
                (ClientSize.Height - 32f) / Page.Height));
            origin = new PointF((ClientSize.Width - Page.Width * zoom) / 2,
                (ClientSize.Height - Page.Height * zoom) / 2);
            Invalidate(); Notify(ViewChanged);
        }

        public void ZoomAt(float multiplier, Point at)
        {
            if (Page == null) return;
            var old = zoom;
            zoom = Math.Max(.01f, Math.Min(8f, zoom * multiplier));
            origin = new PointF(at.X - (at.X - origin.X) * zoom / old,
                at.Y - (at.Y - origin.Y) * zoom / old);
            Invalidate(); Notify(ViewChanged);
        }

        public void ActualSize()
        { ZoomAt(1 / zoom, new Point(ClientSize.Width / 2, ClientSize.Height / 2)); }

        public void SetHandMode(bool enabled)
        {
            handMode = enabled;
            Cursor = handMode || dragKind == 5 ? Cursors.Hand : Cursors.Default;
        }

        public void CancelHandPan()
        { SetHandMode(false); if (dragKind == 5) CancelDrag(); }

        protected override void OnLostFocus(EventArgs e)
        { SetHandMode(false); base.OnLostFocus(e); }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            if (!Capture && dragKind != 0)
            {
                if (edited) Notify(IssuesChanged);
                CancelDrag();
            }
            base.OnMouseCaptureChanged(e);
        }

        public void SelectIssue(int index, bool reveal)
        {
            SelectedIndex = index;
            if (reveal && Page != null && index >= 0 && index < Page.Issues.Count)
            {
                var issue = Page.Issues[index];
                var target = ScreenPoint(issue.X, issue.Y);
                if (!ClientRectangle.Contains(Point.Round(target)))
                    origin = new PointF(ClientSize.Width / 2f - (float)issue.X * Page.Width * zoom,
                        ClientSize.Height / 2f - (float)issue.Y * Page.Height * zoom);
            }
            Invalidate();
        }

        static void Notify(EventHandler handler) { if (handler != null) handler(null, EventArgs.Empty); }
        PointF ImagePoint(Point point)
        {
            return new PointF((point.X - origin.X) / zoom, (point.Y - origin.Y) / zoom);
        }
        PointF Percent(Point point)
        {
            var p = ImagePoint(point);
            return new PointF(Clamp(p.X / Page.Width), Clamp(p.Y / Page.Height));
        }
        static float Clamp(double value) { return (float)Math.Max(0, Math.Min(1, value)); }
        PointF ScreenPoint(double x, double y)
        { return new PointF(origin.X + (float)x * Page.Width * zoom, origin.Y + (float)y * Page.Height * zoom); }
        static double Distance(PointF a, PointF b)
        { return Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y)); }

        public static float FontPixels(QcPage page) { return Math.Max(16, Math.Min(52, page.Width * .018f)); }
        public static bool AutomaticNote(QcIssue issue)
        { return Math.Abs(issue.NoteX - issue.TailX) < .000001 && Math.Abs(issue.NoteY - issue.TailY) < .000001; }

        // The initial note anchor equals the arrow tail. A separately dragged note
        // stores its actual top-left corner and remains manually positioned.
        public static RectangleF PlaceNote(QcPage page, QcIssue issue, float width, float height, float gap)
        {
            float tx = (float)issue.TailX * page.Width, ty = (float)issue.TailY * page.Height;
            float hx = (float)issue.X * page.Width, hy = (float)issue.Y * page.Height;
            Func<float, float, RectangleF> fit = (x, y) => new RectangleF(
                Math.Max(2, Math.Min(x, page.Width - width - 2)),
                Math.Max(2, Math.Min(y, page.Height - height - 2)), width, height);
            if (!AutomaticNote(issue))
                return fit((float)issue.NoteX * page.Width, (float)issue.NoteY * page.Height);
            var sides = new[] {
                new PointF(tx - gap - width, ty - height / 2),
                new PointF(tx + gap, ty - height / 2),
                new PointF(tx - width / 2, ty - gap - height),
                new PointF(tx - width / 2, ty + gap) };
            int preferred = Math.Abs(hx - tx) >= Math.Abs(hy - ty) ? (hx >= tx ? 0 : 1) : (hy >= ty ? 2 : 3);
            var candidates = new List<PointF> { sides[preferred] };
            candidates.AddRange(sides);
            candidates.AddRange(new[] {
                new PointF(tx - gap - width, ty - gap - height), new PointF(tx + gap, ty - gap - height),
                new PointF(tx - gap - width, ty + gap), new PointF(tx + gap, ty + gap),
                new PointF(2, 2), new PointF(page.Width - width - 2, 2),
                new PointF(2, page.Height - height - 2), new PointF(page.Width - width - 2, page.Height - height - 2) });
            RectangleF best = fit(sides[preferred].X, sides[preferred].Y);
            double bestScore = double.MaxValue;
            foreach (var candidate in candidates)
            {
                var rect = fit(candidate.X, candidate.Y);
                var clearance = rect; clearance.Inflate(gap * .25f, gap * .25f);
                float overlap = SegmentOverlap(clearance, new PointF(tx, ty), new PointF(hx, hy));
                if (overlap < 0) return rect;
                double score = (clearance.Contains(hx, hy) ? 1e12 : 0) + overlap * 1e6 +
                    Math.Pow(rect.X + width / 2 - tx, 2) + Math.Pow(rect.Y + height / 2 - ty, 2);
                if (score < bestScore) { bestScore = score; best = rect; }
            }
            return best;
        }
        static float SegmentOverlap(RectangleF rect, PointF a, PointF b)
        {
            float low = 0, high = 1;
            float[] start = { a.X, a.Y }, delta = { b.X - a.X, b.Y - a.Y };
            float[] minimum = { rect.Left, rect.Top }, maximum = { rect.Right, rect.Bottom };
            for (int axis = 0; axis < 2; axis++)
            {
                if (Math.Abs(delta[axis]) < .000001)
                { if (start[axis] < minimum[axis] || start[axis] > maximum[axis]) return -1; }
                else
                {
                    float first = (minimum[axis] - start[axis]) / delta[axis], second = (maximum[axis] - start[axis]) / delta[axis];
                    low = Math.Max(low, Math.Min(first, second)); high = Math.Min(high, Math.Max(first, second));
                    if (low > high) return -1;
                }
            }
            return high - low;
        }
        public static RectangleF NoteBounds(Graphics g, QcPage page, QcIssue issue, Font font)
        {
            float pad = FontPixels(page) * .4f;
            float width = Math.Min(page.Width - 4, Math.Max(100, page.Width * .30f));
            string text = "#" + issue.Id + "  " + (string.IsNullOrEmpty(issue.Text) ? "输入问题说明…" : issue.Text);
            var measured = g.MeasureString(text, font, new SizeF(Math.Max(1, width - 2 * pad), 100000));
            float height = Math.Min(page.Height - 4, Math.Max(font.Height + 2 * pad, measured.Height + 2 * pad));
            return PlaceNote(page, issue, width, height, FontPixels(page) * .6f);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            if (Page == null || DisplayImage == null)
            {
                TextRenderer.DrawText(g, "监修模式\r\n\r\n选择成品文件夹，或打开已有返修单。\r\n从说明位置按下鼠标，拖到问题位置后松开。",
                    Font, ClientRectangle, Color.Gainsboro, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.InterpolationMode = zoom >= 1 && Math.Abs(zoom - Math.Round(zoom)) < .0001
                ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
            g.TranslateTransform(origin.X, origin.Y); g.ScaleTransform(zoom, zoom);
            using (var attributes = new ImageAttributes())
            {
                attributes.SetWrapMode(WrapMode.TileFlipXY);
                g.DrawImage(DisplayImage, new Rectangle(0, 0, Page.Width, Page.Height),
                    0, 0, DisplayImage.Width, DisplayImage.Height, GraphicsUnit.Pixel, attributes);
            }
            using (var font = new Font("Microsoft YaHei", FontPixels(Page), FontStyle.Regular, GraphicsUnit.Pixel))
            {
                for (int i = 0; i < Page.Issues.Count; i++) DrawIssue(g, Page.Issues[i], font, i == SelectedIndex);
            }
            if (dragKind == 1)
            {
                var head = new PointF(arrowEnd.X * Page.Width, arrowEnd.Y * Page.Height);
                var tail = new PointF(arrowStart.X * Page.Width, arrowStart.Y * Page.Height);
                DrawArrow(g, tail, head, FontPixels(Page));
            }
            g.ResetTransform();
            float uiScale = DeviceDpi / 96f;
            using (var background = new SolidBrush(Color.FromArgb(220, 35, 38, 42)))
                g.FillRectangle(background, 10 * uiScale, 10 * uiScale, (OriginalVisible ? 180 : 125) * uiScale, 28 * uiScale);
            TextRenderer.DrawText(g, OriginalVisible ? "原图对照 · 只读" : "成品图 · 可标注", Font,
                new Point((int)(18 * uiScale), (int)(16 * uiScale)), Color.White);
        }

        void DrawIssue(Graphics g, QcIssue issue, Font font, bool selected)
        {
            float size = FontPixels(Page);
            var head = new PointF((float)issue.X * Page.Width, (float)issue.Y * Page.Height);
            var tail = new PointF((float)issue.TailX * Page.Width, (float)issue.TailY * Page.Height);
            DrawArrow(g, tail, head, size);
            var rect = NoteBounds(g, Page, issue, font);
            using (var fill = new SolidBrush(Color.FromArgb(245, Color.White)))
            using (var pen = new Pen(selected ? Color.DodgerBlue : Red, Math.Max(1, size * .075f)))
            using (var brush = new SolidBrush(Red))
            {
                g.FillRectangle(fill, rect); g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
                float pad = size * .4f;
                g.DrawString("#" + issue.Id + "  " + (string.IsNullOrEmpty(issue.Text) ? "输入问题说明…" : issue.Text),
                    font, brush, new RectangleF(rect.X + pad, rect.Y + pad, rect.Width - 2 * pad, rect.Height - 2 * pad));
            }
            if (selected && !OriginalVisible)
            {
                float handle = Math.Max(5 / zoom, size * .3f);
                using (var brush = new SolidBrush(Color.DodgerBlue))
                {
                    g.FillEllipse(brush, head.X - handle / 2, head.Y - handle / 2, handle, handle);
                    g.FillRectangle(brush, tail.X - handle / 2, tail.Y - handle / 2, handle, handle);
                }
            }
        }

        static void DrawArrow(Graphics g, PointF tail, PointF head, float fontSize)
        {
            float width = Math.Max(2, fontSize * .12f);
            using (var white = new Pen(Color.White, width + 3))
            using (var red = new Pen(Red, width))
            {
                g.DrawLine(white, tail, head); g.DrawLine(red, tail, head);
                double angle = Math.Atan2(head.Y - tail.Y, head.X - tail.X);
                float length = fontSize * .8f;
                var points = new[] { head,
                    new PointF(head.X - length * (float)Math.Cos(angle - .48), head.Y - length * (float)Math.Sin(angle - .48)),
                    new PointF(head.X - length * (float)Math.Cos(angle + .48), head.Y - length * (float)Math.Sin(angle + .48)) };
                using (var brush = new SolidBrush(Red)) { g.FillPolygon(brush, points); g.DrawPolygon(white, points); g.FillPolygon(brush, points); }
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e); Focus();
            if (Page == null || DisplayImage == null) return;
            mouseStart = e.Location; edited = false;
            if (e.Button == MouseButtons.Middle || e.Button == MouseButtons.Right ||
                (e.Button == MouseButtons.Left && handMode))
            { dragKind = 5; oldOrigin = origin; Capture = true; Cursor = Cursors.Hand; return; }
            if (e.Button != MouseButtons.Left) return;
            var imagePoint = ImagePoint(e.Location);
            if (imagePoint.X < 0 || imagePoint.Y < 0 || imagePoint.X > Page.Width || imagePoint.Y > Page.Height) return;
            int hit = -1, kind = 0;
            RectangleF hitNote = RectangleF.Empty;
            using (var g = CreateGraphics())
            using (var font = new Font("Microsoft YaHei", FontPixels(Page), FontStyle.Regular, GraphicsUnit.Pixel))
            {
                for (int i = Page.Issues.Count - 1; i >= 0; i--)
                {
                    var issue = Page.Issues[i];
                    if (Distance(ScreenPoint(issue.X, issue.Y), e.Location) < 12) { hit = i; kind = 2; break; }
                    if (Distance(ScreenPoint(issue.TailX, issue.TailY), e.Location) < 12) { hit = i; kind = 3; break; }
                    var note = NoteBounds(g, Page, issue, font);
                    if (note.Contains(imagePoint)) { hit = i; kind = 4; hitNote = note; break; }
                }
            }
            SelectedIndex = hit; Notify(SelectionChanged); Invalidate();
            if (OriginalVisible) return;
            if (hit >= 0)
            {
                beforeDrag = Page.Issues[hit].Clone(); dragKind = kind;
                if (kind == 4 && AutomaticNote(beforeDrag))
                { beforeDrag.NoteX = hitNote.X / Page.Width; beforeDrag.NoteY = hitNote.Y / Page.Height; }
            }
            else { arrowStart = Percent(e.Location); arrowEnd = arrowStart; dragKind = 1; }
            Capture = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragKind == 0) return;
            if (dragKind == 5)
            { origin = new PointF(oldOrigin.X + e.X - mouseStart.X, oldOrigin.Y + e.Y - mouseStart.Y); Invalidate(); return; }
            if (dragKind == 1) { arrowEnd = Percent(e.Location); Invalidate(); return; }
            if (Distance(e.Location, mouseStart) < 3 && !edited) return;
            if (!edited) { Notify(BeforeEdit); edited = true; }
            var issue = Page.Issues[SelectedIndex];
            var p = Percent(e.Location);
            var start = Percent(mouseStart);
            if (dragKind == 2) { issue.X = p.X; issue.Y = p.Y; }
            else if (dragKind == 3)
            {
                issue.TailX = Clamp(beforeDrag.TailX + p.X - start.X); issue.TailY = Clamp(beforeDrag.TailY + p.Y - start.Y);
                issue.NoteX = Clamp(beforeDrag.NoteX + p.X - start.X); issue.NoteY = Clamp(beforeDrag.NoteY + p.Y - start.Y);
            }
            else
            { issue.NoteX = Clamp(beforeDrag.NoteX + p.X - start.X); issue.NoteY = Clamp(beforeDrag.NoteY + p.Y - start.Y); }
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (dragKind == 1 && Distance(e.Location, mouseStart) >= 5)
            {
                Notify(BeforeEdit);
                int nextId = 1;
                foreach (var item in Page.Issues) nextId = Math.Max(nextId, item.Id + 1);
                arrowEnd = Percent(e.Location);
                Page.Issues.Add(new QcIssue { Id = nextId, X = arrowEnd.X, Y = arrowEnd.Y,
                    TailX = arrowStart.X, TailY = arrowStart.Y, NoteX = arrowStart.X, NoteY = arrowStart.Y, Text = "" });
                SelectedIndex = Page.Issues.Count - 1;
                Notify(IssuesChanged); Notify(SelectionChanged); Notify(IssueCreated);
            }
            else if (edited) Notify(IssuesChanged);
            CancelDrag();
        }

        public void CancelDrag()
        { dragKind = 0; Capture = false; Cursor = handMode ? Cursors.Hand : Cursors.Default; edited = false; Invalidate(); }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (Page == null || DisplayImage == null || e.Delta == 0) return;
            if ((ModifierKeys & Keys.Alt) != 0)
                ZoomAt((float)Math.Pow(1.15, e.Delta / 120.0), e.Location);
            else
            {
                if ((ModifierKeys & Keys.Control) != 0) origin.X += e.Delta * .8f;
                else origin.Y += e.Delta * .8f;
                Invalidate();
            }
        }
        protected override bool IsInputKey(Keys keyData)
        { return keyData == Keys.Left || keyData == Keys.Right || keyData == Keys.Space || base.IsInputKey(keyData); }
    }
}
