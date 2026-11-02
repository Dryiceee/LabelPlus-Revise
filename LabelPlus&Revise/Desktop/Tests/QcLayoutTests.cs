using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Windows.Forms;
using LabelPlus;
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8")]

[DataContract]
public sealed class NoteRect
{
    [DataMember(Name="x")] public float X;
    [DataMember(Name="y")] public float Y;
}
[DataContract]
public sealed class LayoutCase
{
    [DataMember(Name="issue")] public QcIssue Issue;
    [DataMember(Name="width")] public float Width;
    [DataMember(Name="height")] public float Height;
    [DataMember(Name="expected")] public NoteRect Expected;
}
class QcLayoutTests
{
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static bool HitsArrow(RectangleF rect, QcIssue issue)
    {
        // Independent sampling of the visible shaft, rather than the production clipper.
        for (int step = 0; step <= 1000; step++)
        {
            float t = step / 1000f;
            float x = (float)(issue.TailX + (issue.X - issue.TailX) * t) * 1000;
            float y = (float)(issue.TailY + (issue.Y - issue.TailY) * t) * 1000;
            if (rect.Contains(x, y)) return true;
        }
        return false;
    }
    static void Mouse(QcCanvas canvas, string name, Point point)
    { typeof(QcCanvas).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(canvas, new object[] { new MouseEventArgs(MouseButtons.Left, 1, point.X, point.Y, 0) }); }
    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            var page = new QcPage { FileName = "layout.png", Width = 1000, Height = 1000 };
            var cases = new List<LayoutCase>();
            Action<double, double, double, double, float> test = (tx, ty, hx, hy, height) =>
            {
                var issue = new QcIssue { Id = 1, X = hx, Y = hy, TailX = tx, TailY = ty, NoteX = tx, NoteY = ty, Text = "test" };
                var rect = QcCanvas.PlaceNote(page, issue, 300, height, 12);
                Check(rect.Left >= 0 && rect.Top >= 0 && rect.Right <= 1000 && rect.Bottom <= 1000, "note remains inside the page");
                Check(!HitsArrow(rect, issue), "automatic note covers the arrow at " + tx + "," + ty + " -> " + hx + "," + hy);
                cases.Add(new LayoutCase { Issue = issue, Width = 300, Height = height, Expected = new NoteRect { X = rect.X, Y = rect.Y } });
            };
            foreach (float height in new[] { 60f, 160f })
            {
                for (int angle = 0; angle < 360; angle += 30)
                {
                    double radians = angle * Math.PI / 180;
                    test(.5, .5, .5 + .3 * Math.Cos(radians), .5 + .3 * Math.Sin(radians), height);
                }
                test(.04, .04, .85, .85, height);
                test(.96, .04, .15, .85, height);
                test(.04, .96, .85, .15, height);
                test(.96, .96, .15, .15, height);
                test(.5, .5, .8, .8, height); // exact lower-right regression
            }
            var manual = new QcIssue { Id = 2, X = .8, Y = .8, TailX = .5, TailY = .5, NoteX = .1, NoteY = .8, Text = "manual" };
            var placed = QcCanvas.PlaceNote(page, manual, 300, 60, 12);
            Check(placed.X == 100 && placed.Y == 800, "manual placement preserved");
            cases.Add(new LayoutCase { Issue = manual, Width = 300, Height = 60, Expected = new NoteRect { X = placed.X, Y = placed.Y } });

            using (var canvas = new QcCanvas())
            using (var image = new Bitmap(1000, 1000))
            {
                page.Issues.Add(new QcIssue { Id = 1, X = .8, Y = .8, TailX = .55, TailY = .55, NoteX = .55, NoteY = .55, Text = "移动说明" });
                canvas.Size = new Size(1032, 1032); canvas.CreateControl(); canvas.SetPage(page, image);
                RectangleF before;
                using (var g = canvas.CreateGraphics())
                using (var font = new Font("Microsoft YaHei", QcCanvas.FontPixels(page), FontStyle.Regular, GraphicsUnit.Pixel))
                    before = QcCanvas.NoteBounds(g, page, page.Issues[0], font);
                var point = new Point((int)(canvas.ViewOrigin.X + (before.X + before.Width / 2) * canvas.Zoom),
                    (int)(canvas.ViewOrigin.Y + (before.Y + before.Height / 2) * canvas.Zoom));
                Mouse(canvas, "OnMouseDown", point);
                var moved = new Point(point.X + 40, point.Y + 30);
                Mouse(canvas, "OnMouseMove", moved); Mouse(canvas, "OnMouseUp", moved);
                Check(!QcCanvas.AutomaticNote(page.Issues[0]), "dragging an auto note makes its position explicit");
                using (var g = canvas.CreateGraphics())
                using (var font = new Font("Microsoft YaHei", QcCanvas.FontPixels(page), FontStyle.Regular, GraphicsUnit.Pixel))
                {
                    var after = QcCanvas.NoteBounds(g, page, page.Issues[0], font);
                    Check(Math.Abs(after.X - before.X - 40 / canvas.Zoom) < 1 && Math.Abs(after.Y - before.Y - 30 / canvas.Zoom) < 1,
                        "dragged note follows the pointer without jumping back to its tail");
                }
            }
            using (var stream = new FileStream(args[0], FileMode.Create, FileAccess.Write))
                new DataContractJsonSerializer(typeof(List<LayoutCase>)).WriteObject(stream, cases);
            Console.WriteLine("PASS: " + checks + " desktop layout checks; " + cases.Count + " cross-platform reference cases");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
