// QC extension, GPL-2.0, based on LabelPlus.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace LabelPlus
{
    [DataContract]
    public sealed class QcIssue
    {
        [DataMember(Name = "id", Order = 0)] public int Id { get; set; }
        [DataMember(Name = "x", Order = 1)] public double X { get; set; }
        [DataMember(Name = "y", Order = 2)] public double Y { get; set; }
        [DataMember(Name = "tailX", Order = 3)] public double TailX { get; set; }
        [DataMember(Name = "tailY", Order = 4)] public double TailY { get; set; }
        [DataMember(Name = "noteX", Order = 5)] public double NoteX { get; set; }
        [DataMember(Name = "noteY", Order = 6)] public double NoteY { get; set; }
        [DataMember(Name = "text", Order = 7)] public string Text { get; set; }
        public QcIssue Clone() { return (QcIssue)MemberwiseClone(); }
    }

    [DataContract]
    public sealed class QcPage
    {
        [DataMember(Name = "file", Order = 0)] public string FileName { get; set; }
        [DataMember(Name = "width", Order = 1)] public int Width { get; set; }
        [DataMember(Name = "height", Order = 2)] public int Height { get; set; }
        [DataMember(Name = "issues", Order = 3)] public List<QcIssue> Issues { get; set; }
        public QcPage() { Issues = new List<QcIssue>(); }
        public override string ToString() { return FileName + "  (" + Issues.Count + ")"; }
    }

    [DataContract]
    public sealed class QcProject
    {
        public const string Header = "LabelPlus-QC 1";
        [DataMember(Name = "kind", Order = 0)] public string Kind { get; set; }
        [DataMember(Name = "version", Order = 1)] public int Version { get; set; }
        [DataMember(Name = "sourceFolder", Order = 2)] public string SourceFolder { get; set; }
        [DataMember(Name = "originalFolder", Order = 3)] public string OriginalFolder { get; set; }
        [DataMember(Name = "pages", Order = 4)] public List<QcPage> Pages { get; set; }

        public QcProject()
        {
            Kind = "labelplus-qc"; Version = 1;
            SourceFolder = ""; OriginalFolder = "";
            Pages = new List<QcPage>();
        }

        public string ToText()
        {
            Validate();
            using (var stream = new MemoryStream())
            {
                using (var writer = JsonReaderWriterFactory.CreateJsonWriter(stream, Encoding.UTF8, false, true, "  "))
                {
                    new DataContractJsonSerializer(typeof(QcProject)).WriteObject(writer, this);
                    writer.Flush();
                    return Header + "\r\n" + Encoding.UTF8.GetString(stream.ToArray()) + "\r\n";
                }
            }
        }

        public static QcProject FromText(string text)
        {
            text = text.TrimStart('\uFEFF');
            int end = text.IndexOf('\n');
            if (end < 0 || text.Substring(0, end).TrimEnd('\r') != Header)
                throw new InvalidDataException("这不是受支持的 QC 返修单（LabelPlus-QC 1）。");
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(text.Substring(end + 1))))
            {
                var project = (QcProject)new DataContractJsonSerializer(typeof(QcProject)).ReadObject(stream);
                project.Validate();
                return project;
            }
        }

        public void Validate()
        {
            if (Kind != "labelplus-qc" || Version != 1 || Pages == null)
                throw new InvalidDataException("不支持的 QC 文件类型或版本。");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var stems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var page in Pages)
            {
                if (page == null || string.IsNullOrWhiteSpace(page.FileName) ||
                    page.FileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                    page.FileName == "." || page.FileName == ".." ||
                    !names.Add(page.FileName) || !stems.Add(Path.GetFileNameWithoutExtension(page.FileName)))
                    throw new InvalidDataException("图片名无效或存在同名页；每页必须唯一对应一个 PSD。");
                if (page.Width <= 0 || page.Height <= 0 || page.Width > 300000 || page.Height > 300000 || page.Issues == null)
                    throw new InvalidDataException("图片尺寸或问题列表无效：" + page.FileName);
                var ids = new HashSet<int>();
                foreach (var issue in page.Issues)
                {
                    if (issue == null || issue.Id < 1 || !ids.Add(issue.Id) || issue.Text == null ||
                        !Coordinate(issue.X) || !Coordinate(issue.Y) ||
                        !Coordinate(issue.TailX) || !Coordinate(issue.TailY) ||
                        !Coordinate(issue.NoteX) || !Coordinate(issue.NoteY))
                        throw new InvalidDataException("问题编号、说明或坐标无效：" + page.FileName);
                }
            }
            SourceFolder = SourceFolder ?? ""; OriginalFolder = OriginalFolder ?? "";
        }

        private static bool Coordinate(double value)
        { return !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0 && value <= 1; }

        // Same-directory replace keeps the previous file intact if writing fails.
        public void Save(string path)
        {
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, ToText(), new UTF8Encoding(true));
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
