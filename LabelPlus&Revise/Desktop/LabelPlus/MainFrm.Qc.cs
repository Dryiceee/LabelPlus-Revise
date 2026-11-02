using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace LabelPlus
{
    public partial class MainFrm
    {
        QcWorkspace qcWorkspace;
        ToolStripButtonGroup qcModeButtons;
        bool qcActive;
        bool handShortcutActive;

        void InitializeQc(ToolStripButtonGroup modeButtons)
        {
            qcModeButtons = modeButtons;
            var button = new ToolStripButton("监修模式 (F6)") { CheckOnClick = true, Name = "toolStripButton_QcMode" };
            modeButtons.AddButton(button);
            toolStrip.Items.Remove(button); toolStrip.Items.Insert(4, button);
            qcWorkspace = new QcWorkspace { Visible = false };
            Controls.Add(qcWorkspace);
            modeButtons.IndexChanged += delegate
            {
                ResetCanvasNavigation();
                qcActive = modeButtons.SelectedButtonIndex == 4;
                splitContainer.Visible = !qcActive;
                qcWorkspace.Visible = qcActive;
                imageToolStripMenuItem.Enabled = !qcActive;
                outputPToolStripMenuItem.Enabled = !qcActive;
                // Translation group controls stay with the translation workspace.
                for (int i = toolStrip.Items.IndexOf(toolStripSeparator3) + 1; i < toolStrip.Items.Count; i++)
                    toolStrip.Items[i].Visible = !qcActive;
                if (qcActive) { qcWorkspace.BringToFront(); qcWorkspace.Focus(); }
                UpdateQcTitle();
            };
            qcWorkspace.ProjectChanged += delegate { if (qcActive) UpdateQcTitle(); };
            float uiScale = DeviceDpi / 96f;
            var workingArea = Screen.FromControl(this).WorkingArea;
            MinimumSize = new Size(Math.Min((int)(1040 * uiScale), workingArea.Width),
                Math.Min((int)(660 * uiScale), workingArea.Height));
            Size = new Size(Math.Min((int)(1200 * uiScale), workingArea.Width),
                Math.Min((int)(820 * uiScale), workingArea.Height));
            Shown += delegate
            {
                string[] args = Environment.GetCommandLineArgs();
                if (args.Length > 1 && (args[1].EndsWith(".revise.txt", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(Path.GetFileName(args[1]), QcWorkspace.DefaultFileName, StringComparison.OrdinalIgnoreCase)))
                { qcModeButtons.SelectedButtonIndex = 4; qcWorkspace.OpenPath(args[1]); }
            };
        }

        void UpdateQcTitle()
        {
            Text = qcActive ? "LabelPlus&Revise 0.6.1 · 监修 · " +
                (qcWorkspace.Dirty ? "* " : "") +
                (string.IsNullOrEmpty(qcWorkspace.FilePath) ? "未命名返修单" : Path.GetFileName(qcWorkspace.FilePath))
                : FROM_TITLE + (wsp.HavePath ? wsp.Filename : "");
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.F6 && qcModeButtons != null)
            { qcModeButtons.SelectedButtonIndex = 4; return true; }
            if (qcActive && qcWorkspace.HandleShortcut(keyData)) return true;
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space && e.Modifiers == Keys.None)
            {
                bool handled = qcActive ? qcWorkspace.BeginHandPan() : picView.ContainsFocus;
                if (handled)
                {
                    if (!qcActive) picView.SetHandMode(true);
                    handShortcutActive = true;
                    e.Handled = true; e.SuppressKeyPress = true; return;
                }
            }
            base.OnKeyDown(e);
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space && handShortcutActive)
            {
                handShortcutActive = false;
                qcWorkspace.EndHandPan(); picView.SetHandMode(false);
                e.Handled = true; e.SuppressKeyPress = true; return;
            }
            base.OnKeyUp(e);
        }

        void ResetCanvasNavigation()
        {
            handShortcutActive = false;
            if (qcWorkspace != null) qcWorkspace.CancelNavigation();
            if (picView != null) picView.CancelHandPan();
        }

        protected override void OnDeactivate(EventArgs e)
        { ResetCanvasNavigation(); base.OnDeactivate(e); }
    }
}
