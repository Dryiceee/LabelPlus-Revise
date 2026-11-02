using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace LabelPlus
{
    public sealed class QcWorkspace : UserControl
    {
        public const string DefaultFileName = "revise.txt";
        public QcProject Project { get; private set; }
        public string FilePath { get; private set; }
        public bool Dirty { get; private set; }
        public event EventHandler ProjectChanged;
        readonly QcCanvas canvas = new QcCanvas();
        readonly ListBox pages = new ListBox();
        readonly ListBox issues = new ListBox();
        readonly TextBox editor = new TextBox();
        readonly Label issueTitle = new Label();
        readonly ToolStripStatusLabel status = new ToolStripStatusLabel();
        readonly ToolStripButton originalButton = new ToolStripButton("显示原图 (F7)");
        readonly ToolStripLabel zoomLabel = new ToolStripLabel();
        readonly Stack<string> undo = new Stack<string>();
        readonly Stack<string> redo = new Stack<string>();
        Image finishedImage, originalImage;
        bool updating, editingText;
        string originalProblem = "";
        public bool ShowingOriginal { get { return canvas.OriginalVisible; } }

        public QcWorkspace()
        {
            SuspendLayout();
            AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96, 96);
            Dock = DockStyle.Fill; Font = new Font("Microsoft YaHei UI", 9);
            canvas.Dock = DockStyle.Fill; canvas.Margin = Padding.Empty;
            Project = new QcProject(); FilePath = "";
            var tools = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Fill };
            Add(tools, "新建监修 / 成品目录", delegate { NewProject(); });
            Add(tools, "打开返修单", delegate { OpenProject(); });
            Add(tools, "保存返修单", delegate { SaveProject(false); });
            Add(tools, "另存为", delegate { SaveProject(true); });
            tools.Items.Add(new ToolStripSeparator());
            Add(tools, "选择原图目录", delegate { ChooseOriginal(); });
            Add(tools, "重定位成品目录", delegate { RelocateFinished(); });

            var navigation = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Fill };
            Add(navigation, "上一页", delegate { ChangePage(-1); });
            Add(navigation, "下一页", delegate { ChangePage(1); });
            navigation.Items.Add(new ToolStripSeparator());
            originalButton.Click += delegate { ToggleOriginal(); };
            navigation.Items.Add(originalButton);
            navigation.Items.Add(new ToolStripSeparator());
            Add(navigation, "−", delegate { canvas.ZoomAt(1 / 1.2f, CenterPoint()); });
            Add(navigation, "+", delegate { canvas.ZoomAt(1.2f, CenterPoint()); });
            Add(navigation, "适合窗口", delegate { canvas.Fit(); });
            Add(navigation, "100%", delegate { canvas.ActualSize(); });
            navigation.Items.Add(zoomLabel);
            navigation.Items.Add(new ToolStripSeparator());
            Add(navigation, "撤销", delegate { Undo(false); });
            Add(navigation, "重做", delegate { Undo(true); });

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = Padding.Empty };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 27));
            layout.Controls.Add(tools, 0, 0); layout.Controls.Add(navigation, 0, 1);
            var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 174));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 265));
            pages.Dock = DockStyle.Fill; pages.IntegralHeight = false; pages.HorizontalScrollbar = true;
            pages.SelectedIndexChanged += delegate { if (!updating) LoadPage(); };
            body.Controls.Add(pages, 0, 0); body.Controls.Add(canvas, 1, 0);
            var right = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 6, ColumnCount = 1, Padding = new Padding(6) };
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 27));
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 27));
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
            right.Controls.Add(new Label { Text = "本页问题", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            issues.Dock = DockStyle.Fill; issues.IntegralHeight = false; issues.HorizontalScrollbar = true;
            issues.SelectedIndexChanged += delegate
            {
                if (updating) return;
                editingText = false; canvas.SelectIssue(issues.SelectedIndex, true); SelectIssue();
            };
            issues.MouseUp += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left && issues.IndexFromPoint(e.Location) >= 0)
                    FocusIssueEditor();
            };
            issues.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.F2)
                { FocusIssueEditor(); e.Handled = true; e.SuppressKeyPress = true; }
            };
            right.Controls.Add(issues, 0, 1);
            issueTitle.Dock = DockStyle.Fill; issueTitle.TextAlign = ContentAlignment.MiddleLeft;
            right.Controls.Add(issueTitle, 0, 2);
            editor.Multiline = true; editor.AcceptsReturn = true; editor.ScrollBars = ScrollBars.Vertical;
            editor.Dock = DockStyle.Fill; editor.Enabled = false;
            editor.TextChanged += EditText;
            editor.Leave += delegate { editingText = false; };
            right.Controls.Add(editor, 0, 3);
            var delete = new Button { Text = "删除选中问题", Dock = DockStyle.Fill };
            delete.Click += delegate { DeleteIssue(); };
            right.Controls.Add(delete, 0, 4);
            right.Controls.Add(new Label { Text = "左键从说明位置拖向问题位置。\r\n空格＋左键 / 右键 / 中键拖动画面。\r\n滚轮上下，Ctrl＋滚轮左右。\r\nAlt＋滚轮缩放。F7 切换原图。", Dock = DockStyle.Fill, ForeColor = Color.DimGray }, 0, 5);
            body.Controls.Add(right, 2, 0); layout.Controls.Add(body, 0, 2);
            var strip = new StatusStrip { Dock = DockStyle.Fill, SizingGrip = false };
            strip.Items.Add(status); layout.Controls.Add(strip, 0, 3);
            Controls.Add(layout);
            canvas.BeforeEdit += delegate { PushUndo(); };
            canvas.IssuesChanged += delegate { Changed(); RefreshIssues(); RefreshPages(); };
            canvas.SelectionChanged += delegate { SelectIssue(); };
            canvas.IssueCreated += delegate { FocusIssueEditor(); };
            canvas.ViewChanged += delegate { zoomLabel.Text = (canvas.Zoom * 100).ToString("0") + "%"; };
            UpdateStatus();
            ResumeLayout(true);
        }

        static void Add(ToolStrip strip, string text, Action action)
        { var button = new ToolStripButton(text); button.Click += delegate { action(); }; strip.Items.Add(button); }
        Point CenterPoint() { return new Point(canvas.Width / 2, canvas.Height / 2); }
        QcPage CurrentPage { get { return pages.SelectedIndex >= 0 && pages.SelectedIndex < Project.Pages.Count ? Project.Pages[pages.SelectedIndex] : null; } }
        QcIssue CurrentIssue
        {
            get { var page = CurrentPage; return page != null && canvas.SelectedIndex >= 0 && canvas.SelectedIndex < page.Issues.Count ? page.Issues[canvas.SelectedIndex] : null; }
        }

        void PushUndo()
        {
            undo.Push(Project.ToText()); redo.Clear();
            if (undo.Count > 100)
            {
                var newest = undo.ToArray().Take(100).Reverse().ToArray(); undo.Clear();
                foreach (var text in newest) undo.Push(text);
            }
        }
        void Changed()
        {
            Dirty = true; UpdateStatus();
            if (ProjectChanged != null) ProjectChanged(this, EventArgs.Empty);
        }
        void UpdateStatus()
        {
            int total = Project.Pages.Sum(p => p.Issues.Count);
            status.Text = (Dirty ? "未保存 · " : "") + Project.Pages.Count + " 页 · " + total + " 条问题" +
                (CurrentPage != null ? " · " + CurrentPage.FileName : "") +
                (canvas.OriginalVisible ? " · 原图对照" : " · 成品图") +
                (originalProblem.Length > 0 ? " · " + originalProblem : "");
        }
        void RefreshPages()
        {
            updating = true;
            for (int i = 0; i < Project.Pages.Count; i++)
            {
                string label = Project.Pages[i].ToString();
                if (i >= pages.Items.Count) pages.Items.Add(label); else pages.Items[i] = label;
            }
            updating = false;
        }
        string IssueLabel(QcIssue issue)
        { return "#" + issue.Id + "  " + (string.IsNullOrEmpty(issue.Text) ? "（待填写）" : issue.Text.Replace("\r", "").Replace("\n", " ")); }
        void RefreshIssues()
        {
            updating = true; issues.Items.Clear();
            if (CurrentPage != null)
                foreach (var issue in CurrentPage.Issues) issues.Items.Add(IssueLabel(issue));
            if (canvas.SelectedIndex >= 0 && canvas.SelectedIndex < issues.Items.Count) issues.SelectedIndex = canvas.SelectedIndex;
            updating = false; SelectIssue();
        }
        void SelectIssue()
        {
            updating = true;
            var item = CurrentIssue;
            issues.SelectedIndex = item == null ? -1 : canvas.SelectedIndex;
            editor.Enabled = item != null && !canvas.OriginalVisible;
            editor.Text = item == null ? "" : item.Text;
            issueTitle.Text = item == null ? "拖出箭头后填写问题" : "问题 #" + item.Id;
            updating = false;
        }
        void FocusIssueEditor()
        {
            if (!editor.Enabled || CurrentIssue == null) return;
            editor.Focus(); editor.SelectionStart = editor.TextLength; editor.SelectionLength = 0;
        }
        void EditText(object sender, EventArgs e)
        {
            if (updating || CurrentIssue == null || canvas.OriginalVisible) return;
            if (!editingText) { PushUndo(); editingText = true; }
            CurrentIssue.Text = editor.Text;
            updating = true;
            if (canvas.SelectedIndex < issues.Items.Count) issues.Items[canvas.SelectedIndex] = IssueLabel(CurrentIssue);
            updating = false;
            canvas.Invalidate(); Changed();
        }
        public void DeleteIssue()
        {
            if (CurrentIssue == null || canvas.OriginalVisible) return;
            PushUndo(); CurrentPage.Issues.RemoveAt(canvas.SelectedIndex); canvas.SelectedIndex = -1;
            editingText = false; Changed(); RefreshIssues(); RefreshPages(); canvas.Invalidate();
        }
        public void Undo(bool forward)
        {
            var from = forward ? redo : undo; var to = forward ? undo : redo;
            if (from.Count == 0) return;
            int pageIndex = pages.SelectedIndex;
            string source = Project.SourceFolder, original = Project.OriginalFolder;
            to.Push(Project.ToText()); Project = QcProject.FromText(from.Pop());
            Project.SourceFolder = source; Project.OriginalFolder = original;
            editingText = false; RefreshPages();
            updating = true; pages.SelectedIndex = Math.Min(pageIndex, Project.Pages.Count - 1); updating = false;
            // Keep the viewport for undoing geometry and text edits on the same page.
            canvas.Page = CurrentPage; canvas.SelectedIndex = -1; canvas.CancelDrag();
            RefreshIssues(); Changed();
        }
        public void ChangePage(int delta)
        {
            int next = pages.SelectedIndex + delta;
            if (next >= 0 && next < pages.Items.Count) pages.SelectedIndex = next;
        }

        static Image ReadImage(string path)
        { using (var image = Image.FromFile(path)) return new Bitmap(image); }
        void DisposeImages()
        {
            canvas.DisplayImage = null;
            if (finishedImage != null) { finishedImage.Dispose(); finishedImage = null; }
            if (originalImage != null) { originalImage.Dispose(); originalImage = null; }
        }
        string FindOriginal(QcPage page)
        {
            if (!Directory.Exists(Project.OriginalFolder)) return null;
            var matches = Directory.GetFiles(Project.OriginalFolder).Where(IsImage)
                .Where(f => string.Equals(Path.GetFileName(f), page.FileName, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length == 1) return matches[0];
            matches = Directory.GetFiles(Project.OriginalFolder).Where(IsImage)
                .Where(f => string.Equals(Path.GetFileNameWithoutExtension(f), Path.GetFileNameWithoutExtension(page.FileName), StringComparison.OrdinalIgnoreCase)).ToArray();
            return matches.Length == 1 ? matches[0] : null;
        }
        void LoadPage()
        {
            DisposeImages(); editingText = false; originalProblem = "";
            var page = CurrentPage;
            if (page != null)
            {
                try
                {
                    finishedImage = ReadImage(Path.Combine(Project.SourceFolder, page.FileName));
                    if (finishedImage.Width != page.Width || finishedImage.Height != page.Height)
                    { finishedImage.Dispose(); finishedImage = null; throw new InvalidDataException("成品尺寸已改变，请使用建立返修单时的成品图片。"); }
                }
                catch (Exception ex) { originalProblem = "成品未加载：" + ex.Message; }
                try
                {
                    string originalPath = FindOriginal(page);
                    if (originalPath != null)
                    {
                        originalImage = ReadImage(originalPath);
                        if (originalImage.Width != page.Width || originalImage.Height != page.Height)
                        { originalImage.Dispose(); originalImage = null; originalProblem = "原图尺寸不一致，不能对齐切换"; }
                    }
                    else if (Project.OriginalFolder.Length > 0) originalProblem = "未匹配到原图";
                }
                catch (Exception ex) { originalProblem = "原图未加载：" + ex.Message; }
            }
            canvas.SetPage(page, finishedImage);
            originalButton.Text = "显示原图 (F7)";
            RefreshIssues(); UpdateStatus();
        }
        public void ToggleOriginal()
        {
            if (CurrentPage == null) return;
            if (originalImage == null)
            {
                MessageBox.Show(this, originalProblem.Length > 0 ? originalProblem : "请先选择与成品同名、同尺寸的原图目录。", "原图对照");
                return;
            }
            canvas.CancelDrag(); canvas.OriginalVisible = !canvas.OriginalVisible;
            canvas.DisplayImage = canvas.OriginalVisible ? originalImage : finishedImage;
            originalButton.Text = canvas.OriginalVisible ? "显示成品图 (F7)" : "显示原图 (F7)";
            originalButton.Checked = canvas.OriginalVisible;
            editingText = false; SelectIssue(); canvas.Invalidate(); UpdateStatus();
        }
        static bool IsImage(string path)
        { return new[] { ".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff", ".gif" }.Contains(Path.GetExtension(path).ToLowerInvariant()); }
        string ChooseFolder(string title, string initial)
        {
            using (var dialog = new FolderBrowserDialog { Description = title, SelectedPath = Directory.Exists(initial) ? initial : "", ShowNewFolderButton = false })
                return dialog.ShowDialog(this) == DialogResult.OK ? dialog.SelectedPath : null;
        }
        public static string FindOriginalFolder(string sourceFolder)
        {
            if (!Directory.Exists(sourceFolder)) return null;
            var source = new DirectoryInfo(sourceFolder);
            if (source.Parent != null)
            {
                string sibling = Path.Combine(source.Parent.FullName, "images");
                if (Directory.Exists(sibling) && !string.Equals(source.FullName, sibling, StringComparison.OrdinalIgnoreCase))
                    return sibling;
            }
            string child = Path.Combine(source.FullName, "images");
            return Directory.Exists(child) ? child : null;
        }
        public bool ConfirmDiscard()
        {
            if (!Dirty) return true;
            var choice = MessageBox.Show(this, "监修内容尚未保存。是否先保存返修单？", "监修", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            return choice == DialogResult.No || (choice == DialogResult.Yes && SaveProject(false));
        }
        public void NewProject()
        {
            string folder = ChooseFolder("选择嵌字完成的成品图片文件夹", Project.SourceFolder);
            if (folder == null || !ConfirmDiscard()) return;
            try
            {
                string original = FindOriginalFolder(folder);
                var project = new QcProject { SourceFolder = folder, OriginalFolder = original ?? "" };
                var errors = new List<string>();
                foreach (string file in Directory.GetFiles(folder).Where(IsImage).OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase))
                {
                    try
                    {
                        using (var image = Image.FromFile(file))
                            project.Pages.Add(new QcPage { FileName = Path.GetFileName(file), Width = image.Width, Height = image.Height });
                    }
                    catch { errors.Add(Path.GetFileName(file)); }
                }
                if (project.Pages.Count == 0) throw new InvalidDataException("该目录没有可读取的图片。");
                project.Validate(); SetProject(project, ""); Changed();
                if (original == null)
                {
                    MessageBox.Show(this, "没有找到与成品目录同级的 images 原图文件夹，请手动选择。\r\n取消选择后仍可继续监修。", "选择原图");
                    ChooseOriginal();
                }
                if (errors.Count > 0) MessageBox.Show(this, "以下图片未能读取：\r\n" + string.Join("\r\n", errors), "读取图片");
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "新建监修失败"); }
        }
        public void OpenProject()
        {
            using (var dialog = new OpenFileDialog { Filter = "QC 返修单（revise.txt 等）|*.txt", Title = "打开 QC 返修单" })
                if (dialog.ShowDialog(this) == DialogResult.OK) OpenPath(dialog.FileName);
        }
        public void OpenPath(string path)
        {
            try
            {
                var project = QcProject.FromText(File.ReadAllText(path, EncodingUtf8));
                if (!ConfirmDiscard()) return;
                string root = Path.GetDirectoryName(Path.GetFullPath(path));
                if (!Path.IsPathRooted(project.SourceFolder)) project.SourceFolder = Path.GetFullPath(Path.Combine(root, project.SourceFolder));
                if (project.OriginalFolder.Length > 0 && !Path.IsPathRooted(project.OriginalFolder))
                    project.OriginalFolder = Path.GetFullPath(Path.Combine(root, project.OriginalFolder));
                SetProject(project, path);
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "打开返修单失败"); }
        }
        static readonly System.Text.Encoding EncodingUtf8 = new System.Text.UTF8Encoding(false, true);
        public void SetProject(QcProject project, string path)
        {
            project.Validate(); DisposeImages(); Project = project; FilePath = path; Dirty = false;
            undo.Clear(); redo.Clear(); updating = true; pages.Items.Clear(); updating = false;
            RefreshPages(); if (pages.Items.Count > 0) pages.SelectedIndex = 0; else LoadPage();
            if (ProjectChanged != null) ProjectChanged(this, EventArgs.Empty);
        }
        public bool SaveProject(bool saveAs)
        {
            string path = FilePath;
            if (saveAs || string.IsNullOrEmpty(path))
                using (var dialog = CreateSaveDialog())
                { if (dialog.ShowDialog(this) != DialogResult.OK) return false; path = dialog.FileName; }
            try
            {
                Project.Save(path); FilePath = path; Dirty = false; UpdateStatus();
                if (ProjectChanged != null) ProjectChanged(this, EventArgs.Empty);
                return true;
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "保存返修单失败"); return false; }
        }
        SaveFileDialog CreateSaveDialog()
        {
            string directory = string.IsNullOrEmpty(FilePath) ? "" : Path.GetDirectoryName(FilePath);
            if (string.IsNullOrEmpty(directory) && Directory.Exists(Project.SourceFolder))
            {
                var source = new DirectoryInfo(Project.SourceFolder);
                directory = source.Parent == null ? source.FullName : source.Parent.FullName;
            }
            return new SaveFileDialog { Filter = "QC 返修单|*.txt", DefaultExt = "txt", AddExtension = true,
                InitialDirectory = directory ?? "", FileName = DefaultFileName };
        }
        void ChooseOriginal()
        {
            string initial = Project.OriginalFolder;
            if (!Directory.Exists(initial) && Directory.Exists(Project.SourceFolder))
            {
                var source = new DirectoryInfo(Project.SourceFolder);
                initial = source.Parent == null ? source.FullName : source.Parent.FullName;
            }
            string folder = ChooseFolder("选择原图文件夹（同名、同尺寸）", initial);
            if (folder == null) return;
            Project.OriginalFolder = folder; Changed(); ReloadPreservingView();
        }
        void RelocateFinished()
        {
            string folder = ChooseFolder("重新指定成品图片目录，不改变返修内容", Project.SourceFolder);
            if (folder == null) return;
            Project.SourceFolder = folder; Changed(); ReloadPreservingView();
        }
        void ReloadPreservingView() { LoadPage(); }

        public bool BeginHandPan()
        {
            if (editor.ContainsFocus) return false;
            canvas.Focus(); canvas.SetHandMode(true); return true;
        }
        public void EndHandPan() { canvas.SetHandMode(false); }
        public void CancelNavigation() { canvas.CancelHandPan(); }

        public bool HandleShortcut(Keys keys)
        {
            if (keys == (Keys.Control | Keys.S)) { SaveProject(false); return true; }
            if (keys == (Keys.Control | Keys.Shift | Keys.S)) { SaveProject(true); return true; }
            if (keys == (Keys.Control | Keys.O)) { OpenProject(); return true; }
            if (keys == (Keys.Control | Keys.N)) { NewProject(); return true; }
            if (keys == Keys.F7) { ToggleOriginal(); return true; }
            if (keys == (Keys.Control | Keys.Enter)) { canvas.Focus(); return true; }
            if (!editor.Focused)
            {
                if (keys == (Keys.Control | Keys.Z)) { Undo(false); return true; }
                if (keys == (Keys.Control | Keys.Y)) { Undo(true); return true; }
                if (keys == Keys.Delete) { DeleteIssue(); return true; }
                if (keys == Keys.Left || keys == Keys.PageUp) { ChangePage(-1); return true; }
                if (keys == Keys.Right || keys == Keys.PageDown) { ChangePage(1); return true; }
            }
            return false;
        }
        protected override void Dispose(bool disposing)
        { if (disposing) DisposeImages(); base.Dispose(disposing); }
    }
}
