using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using LabelPlus;
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8")]
class RenderQc
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern IntPtr GetThreadDpiAwarenessContext();
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool AreDpiAwarenessContextsEqual(IntPtr first, IntPtr second);
    static object Call(string type, string method, params object[] args)
    { return typeof(MainFrm).Assembly.GetType("LabelPlus." + type).GetMethod(method, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Invoke(null, args); }
    static object Field(object target, string field)
    { return target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target); }
    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            Directory.SetCurrentDirectory(Path.GetDirectoryName(typeof(MainFrm).Assembly.Location));
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            Call("StringResources", "SetValue", "lang", "ZH"); Call("Language", "InitStringResouce", "ZH"); Call("GlobalVar", "Reload");
            string projectPath = Path.GetFullPath(args[0]); string target = Path.GetFullPath(args[1]);
            using (var form = new MainFrm())
            {
                float scale = form.DeviceDpi / 96f;
                form.Size = new Size((int)(1260 * scale), (int)(920 * scale)); form.Show(); Application.DoEvents();
                if (!AreDpiAwarenessContextsEqual(GetThreadDpiAwarenessContext(), new IntPtr(-4)))
                    throw new Exception("PerMonitorV2 DPI awareness was not enabled");
                var mode = (ToolStripButtonGroup)Field(form, "qcModeButtons"); mode.SelectedButtonIndex = 4;
                var workspace = (QcWorkspace)Field(form, "qcWorkspace"); workspace.OpenPath(projectPath);
                form.PerformLayout(); workspace.PerformLayout(); Application.DoEvents();
                var canvas = (QcCanvas)Field(workspace, "canvas"); canvas.Fit();
                Console.WriteLine("PerMonitorV2 active; form DPI=" + form.DeviceDpi + "; canvas DPI=" + canvas.DeviceDpi);
                Console.WriteLine("QC autoscale baseline=" + workspace.AutoScaleDimensions + "; current=" + workspace.CurrentAutoScaleDimensions);
                var layout = (TableLayoutPanel)workspace.Controls[0];
                var body = (TableLayoutPanel)layout.GetControlFromPosition(0, 2);
                if (Math.Abs(layout.RowStyles[0].Height - 32 * scale) > 1 ||
                    Math.Abs(body.ColumnStyles[2].Width - 265 * scale) > 1)
                    throw new Exception("QC fixed layout sizes did not scale to native DPI");
                using (var bitmap = new Bitmap(form.Width, form.Height))
                { form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height)); bitmap.Save(target); }
                workspace.ToggleOriginal();
                using (var bitmap = new Bitmap(form.Width, form.Height))
                { form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height)); bitmap.Save(Path.Combine(Path.GetDirectoryName(target), "desktop-original.png")); }
            }
            Console.WriteLine("Rendered: " + target); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
