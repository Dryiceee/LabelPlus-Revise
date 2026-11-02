// Revise-only shell, trimmed from MainFrm.Qc.cs. The existing QC workspace,
// canvas, data model and editing behavior are reused. GPL-2.0.
using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace LabelPlus
{
    public sealed class MainFrm : Form
    {
        readonly QcWorkspace qcWorkspace;
        bool handShortcutActive;

        public MainFrm()
        {
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96, 96);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Microsoft YaHei UI", 9);
            ClientSize = new Size(1200, 820);
            StartPosition = FormStartPosition.CenterScreen;
            DoubleBuffered = true;
            KeyPreview = true;
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            qcWorkspace = new QcWorkspace();
            Controls.Add(qcWorkspace);
            qcWorkspace.ProjectChanged += delegate { UpdateTitle(); };
            UpdateTitle();
            ResumeLayout(true);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            float uiScale = DeviceDpi / 96f;
            var workingArea = Screen.FromControl(this).WorkingArea;
            MinimumSize = new Size(Math.Min((int)(1040 * uiScale), workingArea.Width),
                Math.Min((int)(660 * uiScale), workingArea.Height));
            Size = new Size(Math.Min((int)(1200 * uiScale), workingArea.Width),
                Math.Min((int)(820 * uiScale), workingArea.Height));
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            string[] args = Environment.GetCommandLineArgs();
            if (args.Length > 1) qcWorkspace.OpenPath(args[1]);
            qcWorkspace.Focus();
        }

        void UpdateTitle()
        {
            Text = "LabelRevise 0.6.1 · " + (qcWorkspace.Dirty ? "* " : "") +
                (string.IsNullOrEmpty(qcWorkspace.FilePath) ? "未命名返修单" : Path.GetFileName(qcWorkspace.FilePath));
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (qcWorkspace.HandleShortcut(keyData)) return true;
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space && e.Modifiers == Keys.None && qcWorkspace.BeginHandPan())
            {
                handShortcutActive = true;
                e.Handled = true; e.SuppressKeyPress = true; return;
            }
            base.OnKeyDown(e);
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space && handShortcutActive)
            {
                handShortcutActive = false;
                qcWorkspace.EndHandPan();
                e.Handled = true; e.SuppressKeyPress = true; return;
            }
            base.OnKeyUp(e);
        }

        protected override void OnDeactivate(EventArgs e)
        {
            handShortcutActive = false;
            if (qcWorkspace != null) qcWorkspace.CancelNavigation();
            base.OnDeactivate(e);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!qcWorkspace.ConfirmDiscard()) e.Cancel = true;
            base.OnFormClosing(e);
        }
    }
}
