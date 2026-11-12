using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Packaging;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace SDK_HD.Dim
{
    internal static class SheetTableWorkbook
    {
        private static readonly XNamespace S = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private const string Office = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument";
        internal static void Write(string path, IEnumerable<string[]> data)
        {
            using (var package = Package.Open(path, FileMode.Create, FileAccess.ReadWrite))
            {
                var book = package.CreatePart(new Uri("/xl/workbook.xml", UriKind.Relative), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml");
                var sheet = package.CreatePart(new Uri("/xl/worksheets/sheet1.xml", UriKind.Relative), "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml");
                package.CreateRelationship(book.Uri, TargetMode.Internal, Office, "rId1");
                book.CreateRelationship(new Uri("worksheets/sheet1.xml", UriKind.Relative), TargetMode.Internal, R.NamespaceName + "/worksheet", "rId1");
                Save(book, new XDocument(new XElement(S + "workbook", new XAttribute(XNamespace.Xmlns + "r", R), new XElement(S + "sheets", new XElement(S + "sheet", new XAttribute("name", "Sheets"), new XAttribute("sheetId", 1), new XAttribute(R + "id", "rId1"))))));
                var values = new List<string[]> { new[] { "Sheet Number", "Sheet Name", "Title Blocks" } }; values.AddRange(data);
                var rows = values.Select((row, i) => new XElement(S + "row", new XAttribute("r", i + 1), row.Take(3).Select((value, col) =>
                    new XElement(S + "c", new XAttribute("r", ((char)('A' + col)).ToString() + (i + 1)), new XAttribute("t", "inlineStr"), new XElement(S + "is", new XElement(S + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), value ?? ""))))));
                Save(sheet, new XDocument(new XElement(S + "worksheet", new XElement(S + "sheetViews", new XElement(S + "sheetView", new XAttribute("workbookViewId", 0),
                    new XElement(S + "pane", new XAttribute("ySplit", 1), new XAttribute("topLeftCell", "A2"), new XAttribute("state", "frozen")))),
                    new XElement(S + "cols", new XElement(S + "col", new XAttribute("min", 1), new XAttribute("max", 1), new XAttribute("width", 20), new XAttribute("customWidth", 1)),
                        new XElement(S + "col", new XAttribute("min", 2), new XAttribute("max", 3), new XAttribute("width", 48), new XAttribute("customWidth", 1))), new XElement(S + "sheetData", rows))));
            }
        }
        internal static List<string[]> Read(string path)
        {
            using (var package = Package.Open(path, FileMode.Open, FileAccess.Read))
            {
                var office = package.GetRelationshipsByType(Office).FirstOrDefault();
                if (office == null || office.TargetMode == TargetMode.External) throw new InvalidOperationException("Không phải workbook .xlsx hợp lệ.");
                var bookUri = PackUriHelper.ResolvePartUri(new Uri("/", UriKind.Relative), office.TargetUri); var book = package.GetPart(bookUri);
                var first = Load(book).Root.Element(S + "sheets")?.Elements(S + "sheet").FirstOrDefault();
                if (first == null) throw new InvalidOperationException("Workbook không có worksheet.");
                var relation = book.GetRelationship((string)first.Attribute(R + "id"));
                if (relation.TargetMode == TargetMode.External) throw new InvalidOperationException("Worksheet phải nằm trong workbook.");
                var worksheet = package.GetPart(PackUriHelper.ResolvePartUri(book.Uri, relation.TargetUri));
                var shared = new List<string>(); var sharedRelation = book.GetRelationshipsByType(R.NamespaceName + "/sharedStrings").FirstOrDefault();
                if (sharedRelation != null && sharedRelation.TargetMode == TargetMode.Internal)
                    shared = Load(package.GetPart(PackUriHelper.ResolvePartUri(book.Uri, sharedRelation.TargetUri))).Root.Elements(S + "si").Select(e => string.Concat(e.Descendants(S + "t").Select(t => t.Value))).ToList();
                var result = new List<string[]>();
                foreach (var row in Load(worksheet).Descendants(S + "sheetData").Elements(S + "row"))
                {
                    var values = new[] { "", "", "" };
                    foreach (var cell in row.Elements(S + "c"))
                    {
                        string address = (string)cell.Attribute("r") ?? ""; string column = new string(address.TakeWhile(char.IsLetter).ToArray()).ToUpperInvariant();
                        if (column.Length != 1 || column[0] < 'A' || column[0] > 'C') continue;
                        string value = (string)cell.Element(S + "v") ?? "", type = (string)cell.Attribute("t");
                        if (type == "inlineStr") value = string.Concat(cell.Descendants(S + "t").Select(t => t.Value));
                        else if (type == "s") { int index; if (!int.TryParse(value, out index) || index < 0 || index >= shared.Count) throw new InvalidOperationException("Shared string không hợp lệ tại " + address); value = shared[index]; }
                        if (type == "e" || (cell.Element(S + "f") != null && cell.Element(S + "v") == null)) throw new InvalidOperationException("Ô " + address + " có lỗi/công thức chưa lưu kết quả. Mở và lưu bằng Excel trước.");
                        values[column[0] - 'A'] = value.Trim();
                    }
                    if (values.Any(v => v.Length > 0)) result.Add(values);
                }
                if (result.Count == 0 || !result[0][0].Equals("Sheet Number", StringComparison.OrdinalIgnoreCase) || !result[0][1].Equals("Sheet Name", StringComparison.OrdinalIgnoreCase)
                    || !result[0][2].Equals("Title Blocks", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Dùng mẫu Excel có ba cột: Sheet Number, Sheet Name, Title Blocks.");
                return result.Skip(1).ToList();
            }
        }
        private static void Save(PackagePart part, XDocument document) { using (var stream = part.GetStream(FileMode.Create, FileAccess.Write)) document.Save(stream); }
        private static XDocument Load(PackagePart part)
        {
            using (var stream = part.GetStream(FileMode.Open, FileAccess.Read))
            using (var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null })) return XDocument.Load(reader);
        }
    }
}

