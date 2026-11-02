using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using LabelPlus;
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8")]

class ReviseSmoke
{
    static int checks;
    [DllImport("user32.dll")] static extern IntPtr GetThreadDpiAwarenessContext();
    [DllImport("user32.dll")] static extern bool AreDpiAwarenessContextsEqual(IntPtr first, IntPtr second);
    static void Check(bool condition, string message)
    { checks++; if (!condition) throw new Exception(message); }
    static object Field(object target, string name)
    { return target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target); }
    static void Mouse(QcCanvas canvas, string method, Point point)
    { typeof(QcCanvas).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(canvas,
        new object[] { new MouseEventArgs(MouseButtons.Left, 1, point.X, point.Y, 0) }); }
    static Point Screen(QcCanvas canvas, double x, double y)
    { return Point.Round(new PointF(canvas.ViewOrigin.X + (float)x * canvas.Page.Width * canvas.Zoom,
        canvas.ViewOrigin.Y + (float)y * canvas.Page.Height * canvas.Zoom)); }
    static void Arrow(QcCanvas canvas, double tx, double ty, double x, double y)
    {
        Point tail = Screen(canvas, tx, ty), head = Screen(canvas, x, y);
        Mouse(canvas, "OnMouseDown", tail); Mouse(canvas, "OnMouseMove", head); Mouse(canvas, "OnMouseUp", head);
    }
    static void Key(MainFrm form, string method, Keys key)
    { typeof(MainFrm).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form,
        new object[] { new KeyEventArgs(key) }); }

    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            string path = Path.GetFullPath(args[0]), root = Path.GetDirectoryName(path);
            var project = new QcProject { SourceFolder = Path.Combine(root, "finished"), OriginalFolder = Path.Combine(root, "images") };
            project.Pages.Add(new QcPage { FileName = "001.png", Width = 1200, Height = 1600 });
            project.Pages.Add(new QcPage { FileName = "002.png", Width = 1200, Height = 1600 });
            project.Save(path);
            using (var form = new MainFrm())
            {
                form.Show(); Application.DoEvents();
                var workspace = (QcWorkspace)Field(form, "qcWorkspace");
                var canvas = (QcCanvas)Field(workspace, "canvas");
                var editor = (TextBox)Field(workspace, "editor");
                var issues = (ListBox)Field(workspace, "issues");
                Check(form.Controls.Count == 1 && form.Controls[0] == workspace && workspace.Visible,
                    "standalone window starts directly in the existing revise workspace");
                Check(form.Text.IndexOf("LabelRevise 1.0") >= 0 && workspace.Project.Pages.Count == 2,
                    "command-line repair sheet loaded by packaged standalone shell");
                Check(typeof(MainFrm).Assembly.GetType("LabelPlus.Workspace") == null &&
                    typeof(MainFrm).Assembly.GetType("LabelPlus.PicView") == null,
                    "legacy workspace and image control are absent from the assembly");
                Check(AreDpiAwarenessContextsEqual(GetThreadDpiAwarenessContext(), new IntPtr(-4)), "PerMonitorV2 enabled");
                var layout = (TableLayoutPanel)workspace.Controls[0];
                Check(Math.Abs(layout.RowStyles[0].Height - 32 * workspace.DeviceDpi / 96f) < 1,
                    "workspace toolbar scales to native DPI");
                Check(QcWorkspace.FindOriginalFolder(project.SourceFolder) == project.OriginalFolder, "images folder auto-detection");
                canvas.Fit();
                Arrow(canvas, .18, .16, .60, .42);
                editor.Text = "右下箭头：漏字，请补上“一”字。";
                Arrow(canvas, .18, .55, .60, .21);
                editor.Text = "交叉箭头：调整字距。\r\n第二行用于检查中文换行。";
                Arrow(canvas, .72, .86, .88, .72);
                editor.Text = "独立箭头：检查居中和描边。";
                Check(canvas.Page.Issues.Count == 3 && workspace.Dirty, "three arrows and CJK notes created through workspace events");
                Check(canvas.Page.Issues[0].X > canvas.Page.Issues[0].TailX &&
                    canvas.Page.Issues[0].Y > canvas.Page.Issues[0].TailY, "right-down drag points at release position");
                issues.SelectedIndex = 0;
                var row = issues.GetItemRectangle(0);
                typeof(ListBox).GetMethod("OnMouseUp", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(issues,
                    new object[] { new MouseEventArgs(MouseButtons.Left, 1, row.X + 3, row.Y + 3, 0) });
                Check(editor.Focused, "clicking an issue focuses its text editor");
                canvas.ZoomAt(1.3f, new Point(200, 200));
                float zoom = canvas.Zoom; PointF origin = canvas.ViewOrigin;
                workspace.ToggleOriginal();
                Check(workspace.ShowingOriginal && canvas.Zoom == zoom && canvas.ViewOrigin == origin,
                    "original comparison preserves viewport");
                workspace.ToggleOriginal();
                Check(!workspace.ShowingOriginal, "finished view restored");
                workspace.HandleShortcut(Keys.Control | Keys.Enter);
                int count = canvas.Page.Issues.Count;
                origin = canvas.ViewOrigin;
                Key(form, "OnKeyDown", Keys.Space);
                Point pan = Screen(canvas, .5, .5);
                Mouse(canvas, "OnMouseDown", pan);
                Mouse(canvas, "OnMouseMove", new Point(pan.X + 45, pan.Y + 30));
                Mouse(canvas, "OnMouseUp", new Point(pan.X + 45, pan.Y + 30));
                Key(form, "OnKeyUp", Keys.Space);
                Check(canvas.Page.Issues.Count == count && Math.Abs(canvas.ViewOrigin.X - origin.X - 45) < 1,
                    "space-hand pans without adding an issue");
                Check(canvas.Cursor == Cursors.Default, "releasing space restores the cursor");
                canvas.Fit();
                Check(workspace.SaveProject(false) && !workspace.Dirty, "workspace saves revise.txt without a dialog");
                var saved = QcProject.FromText(File.ReadAllText(path));
                Check(saved.Pages[0].Issues.Count == 3 && saved.Pages[1].Issues.Count == 0 &&
                    saved.Pages[0].Issues[1].Text.IndexOf("第二行") >= 0, "saved CJK text, geometry and empty page round-trip");
                using (var bitmap = new Bitmap(form.Width, form.Height))
                { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size)); bitmap.Save(Path.Combine(root, "desktop.png")); }
                workspace.OpenPath(path);
                Check(workspace.Project.Pages[0].Issues.Count == 3, "saved repair sheet reopens in standalone app");
                string report = "PASS: " + checks + " desktop flow checks; DPI=" + workspace.DeviceDpi;
                File.WriteAllText(Path.Combine(root, "desktop-result.txt"), report);
                Console.WriteLine(report);
                form.Close();
            }
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
