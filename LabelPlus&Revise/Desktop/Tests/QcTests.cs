using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using LabelPlus;
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8")]

class QcTests
{
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static void Reject(Action action, string message)
    { bool rejected = false; try { action(); } catch { rejected = true; } Check(rejected, message); }
    static void Mouse(Control control, string name, int x, int y, MouseButtons button)
    { control.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(control, new object[] { new MouseEventArgs(button, 1, x, y, 0) }); }
    static void DrawPage(string path, bool original, int number)
    {
        using (var image = new Bitmap(1200, 1600))
        using (var g = Graphics.FromImage(image))
        using (var title = new Font("Microsoft YaHei", 32, GraphicsUnit.Pixel))
        using (var text = new Font("Microsoft YaHei", 27, GraphicsUnit.Pixel))
        using (var line = new Pen(Color.FromArgb(45, 45, 45), 5))
        {
            g.Clear(Color.White);
            g.DrawString("LabelPlus QC · " + (original ? "原图示例" : "成品示例") + " · " + number.ToString("00"), title, Brushes.Black, 60, 40);
            g.DrawRectangle(line, 55, 120, 1090, 620); g.DrawRectangle(line, 55, 790, 1090, 690);
            g.FillRectangle(Brushes.Gainsboro, 80, 145, 1040, 570);
            g.FillRectangle(Brushes.WhiteSmoke, 80, 815, 1040, 640);
            g.FillEllipse(Brushes.White, 170, 240, 440, 260); g.DrawEllipse(line, 170, 240, 440, 260);
            g.DrawString(original ? "今日は一緒に\n出かけよう。" : "今天起\n出去吧。", text, Brushes.Black, 255, 315);
            g.FillEllipse(Brushes.White, 680, 930, 340, 230); g.DrawEllipse(line, 680, 930, 340, 230);
            g.DrawString(original ? "わかった！" : "好！", text, Brushes.Black, 765, 1020);
            g.DrawString("测试页面，用于检查定位、切换和批量导入。", text, Brushes.DimGray, 80, 1520);
            image.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }
    }
    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            string root = Path.GetFullPath(args[0]); Directory.CreateDirectory(root);
            Directory.CreateDirectory(Path.Combine(root, "成品")); Directory.CreateDirectory(Path.Combine(root, "原图"));
            for (int i = 1; i <= 2; i++)
            {
                DrawPage(Path.Combine(root, "成品", i.ToString("000") + ".png"), false, i);
                DrawPage(Path.Combine(root, "原图", i.ToString("000") + ".png"), true, i);
            }
            var project = new QcProject { SourceFolder = "成品", OriginalFolder = "原图" };
            var page = new QcPage { FileName = "001.png", Width = 1200, Height = 1600 };
            page.Issues.Add(new QcIssue { Id = 1, X = .30, Y = .22, TailX = .59, TailY = .32, NoteX = .59, NoteY = .32,
                Text = "漏了“一”字：\n“今天起”应为“今天一起”。" });
            page.Issues.Add(new QcIssue { Id = 2, X = .73, Y = .66, TailX = .20, TailY = .72, NoteX = .20, NoteY = .72,
                Text = "文字需要向左移动，保持在气泡内居中。" });
            project.Pages.Add(page); project.Pages.Add(new QcPage { FileName = "002.png", Width = 1200, Height = 1600 });
            string path = Path.Combine(root, QcWorkspace.DefaultFileName); project.Save(path);
            string textFile = File.ReadAllText(path);
            var copy = QcProject.FromText("\uFEFF" + textFile);
            Check(copy.Pages.Count == 2 && copy.Pages[1].Issues.Count == 0, "empty page round trip");
            Check(copy.Pages[0].Issues[0].Text == page.Issues[0].Text, "CJK, quote and newline round trip");
            Check(copy.Pages[0].Issues[0].X == .30, "coordinate precision");
            project.Save(path); Check(QcProject.FromText(File.ReadAllText(path)).Pages.Count == 2, "atomic overwrite");
            Reject(delegate { QcProject.FromText("1,0\ntranslation"); }, "reject translation input");
            copy.Version = 2; Reject(delegate { copy.ToText(); }, "reject unknown version"); copy.Version = 1;
            copy.Pages[0].Issues[0].X = double.NaN; Reject(delegate { copy.ToText(); }, "reject NaN");
            copy.Pages[0].Issues[0].X = 1.1; Reject(delegate { copy.ToText(); }, "reject out of bounds");
            copy.Pages[0].Issues[0].X = .3; copy.Pages[0].FileName = "../001.png"; Reject(delegate { copy.ToText(); }, "reject path traversal");
            copy.Pages[0].FileName = "001.png"; copy.Pages[1].FileName = "001.jpg"; Reject(delegate { copy.ToText(); }, "reject ambiguous stems");
            using (var canvas = new QcCanvas())
            using (var image = new Bitmap(1000, 1000))
            {
                canvas.Size = new Size(1032, 1032); canvas.CreateControl();
                var drawingPage = new QcPage { FileName = "test.png", Width = 1000, Height = 1000 };
                canvas.SetPage(drawingPage, image);
                Mouse(canvas, "OnMouseDown", 216, 316, MouseButtons.Left);
                Mouse(canvas, "OnMouseMove", 616, 516, MouseButtons.Left);
                Mouse(canvas, "OnMouseUp", 616, 516, MouseButtons.Left);
                Check(drawingPage.Issues.Count == 1, "draw one arrow");
                var arrow = drawingPage.Issues[0];
                Check(Math.Abs(arrow.X - .6) < .000001 && Math.Abs(arrow.Y - .5) < .000001, "head at mouse release");
                Check(Math.Abs(arrow.TailX - .2) < .000001 && Math.Abs(arrow.NoteY - .3) < .000001, "tail and note at mouse press");
                canvas.OriginalVisible = true;
                Mouse(canvas, "OnMouseDown", 100, 100, MouseButtons.Left); Mouse(canvas, "OnMouseUp", 300, 200, MouseButtons.Left);
                Check(drawingPage.Issues.Count == 1, "original comparison is read-only");
                using (var g = Graphics.FromImage(image))
                using (var font = new Font("Microsoft YaHei", 18, GraphicsUnit.Pixel))
                {
                    arrow.NoteX = .99; arrow.NoteY = .99;
                    var rect = QcCanvas.NoteBounds(g, drawingPage, arrow, font);
                    Check(rect.Right <= 1000 && rect.Bottom <= 1000, "edge note remains on page");
                }
            }
            using (var canvas = new QcCanvas())
            using (var image = new Bitmap(1000, 1000))
            using (var g = Graphics.FromImage(image))
            {
                image.SetResolution(600, 600); g.Clear(Color.White);
                for (int x = 50; x < 950; x += 2) g.FillRectangle(Brushes.Black, x, 100, 1, 800);
                canvas.Size = new Size(1032, 1032); canvas.CreateControl();
                canvas.SetPage(new QcPage { FileName = "pixels.png", Width = 1000, Height = 1000 }, image);
                canvas.ZoomAt(1.7f, new Point(500, 500)); canvas.ActualSize();
                Check(Math.Abs(canvas.Zoom - 1) < .000001, "100% view is one image pixel per device pixel");
                canvas.Fit();
                using (var rendered = new Bitmap(canvas.Width, canvas.Height))
                {
                    canvas.DrawToBitmap(rendered, canvas.ClientRectangle);
                    Check(rendered.GetPixel(616, 616).ToArgb() == Color.Black.ToArgb() &&
                          rendered.GetPixel(617, 616).ToArgb() == Color.White.ToArgb(), "one-pixel source detail preserved despite image DPI metadata");
                }
            }
            string autodetect = Path.Combine(root, "auto-detect-" + Guid.NewGuid().ToString("N"));
            string sourceFolder = Path.Combine(autodetect, "finished"); Directory.CreateDirectory(sourceFolder);
            Check(QcWorkspace.FindOriginalFolder(sourceFolder) == null, "missing images directory detected");
            Directory.CreateDirectory(Path.Combine(autodetect, "image"));
            Check(QcWorkspace.FindOriginalFolder(sourceFolder) == null, "singular image directory is not auto-selected");
            string childImage = Path.Combine(sourceFolder, "images"); Directory.CreateDirectory(childImage);
            Check(QcWorkspace.FindOriginalFolder(sourceFolder) == childImage, "nested images directory supported");
            string siblingImage = Path.Combine(autodetect, "images"); Directory.CreateDirectory(siblingImage);
            Check(QcWorkspace.FindOriginalFolder(sourceFolder) == siblingImage, "sibling images directory preferred");
            project.SourceFolder = Path.Combine(root, "成品"); project.OriginalFolder = Path.Combine(root, "原图");
            using (var window = new Form())
            using (var workspace = new QcWorkspace())
            {
                window.Size = new Size(1100, 700); window.Controls.Add(workspace); window.Show(); Application.DoEvents();
                workspace.SetProject(project, "");
                var field = typeof(QcWorkspace).GetField("canvas", BindingFlags.Instance | BindingFlags.NonPublic);
                var canvas = (QcCanvas)field.GetValue(workspace);
                canvas.ZoomAt(1.7f, new Point(200, 200));
                float zoom = canvas.Zoom; PointF origin = canvas.ViewOrigin;
                workspace.ToggleOriginal(); Check(workspace.ShowingOriginal, "toggle to original");
                Check(canvas.Zoom == zoom && canvas.ViewOrigin == origin, "toggle preserves exact viewport");
                workspace.ToggleOriginal(); Check(!workspace.ShowingOriginal, "toggle back to finished");
                var issueList = (ListBox)typeof(QcWorkspace).GetField("issues", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(workspace);
                var editor = (TextBox)typeof(QcWorkspace).GetField("editor", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(workspace);
                issueList.SelectedIndex = 0; issueList.Focus();
                var itemRect = issueList.GetItemRectangle(0);
                Mouse(issueList, "OnMouseUp", itemRect.X + 5, itemRect.Y + 3, MouseButtons.Left);
                Check(editor.Focused && editor.SelectionStart == editor.TextLength, "clicking an issue focuses its editor at the text end");
                string originalNote = project.Pages[0].Issues[0].Text;
                editor.SelectedText = " 直接输入";
                Check(project.Pages[0].Issues[0].Text.EndsWith("直接输入"), "immediate typing edits the selected issue");
                editor.Text = originalNote;
                using (var save = (SaveFileDialog)typeof(QcWorkspace).GetMethod("CreateSaveDialog", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(workspace, null))
                {
                    Check(save.FileName == "revise.txt" && save.DefaultExt == "txt", "default name is exactly revise.txt");
                    Check(save.InitialDirectory == root, "repair sheet defaults to project directory");
                }
                Console.WriteLine("Native workspace DPI: " + workspace.DeviceDpi);
            }
            // Sample uses relative folder paths for portability.
            project.SourceFolder = "成品"; project.OriginalFolder = "原图"; project.Save(path);
            Console.WriteLine("PASS: " + checks + " desktop/model checks; sample: " + path); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
