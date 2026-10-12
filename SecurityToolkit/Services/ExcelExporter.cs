using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Packaging;
using System.Linq;
using System.Xml.Linq;

namespace SecurityToolkit.Services
{
    /// <summary>
    /// Writes a headers/rows table as a genuine .xlsx workbook using the OPC packaging APIs built into
    /// .NET Framework, so no third-party Excel library is needed.
    /// </summary>
    internal static class ExcelExporter
    {
        private const string RelationshipsNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        public static void Write(string filePath, string sheetName, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows)
        {
            var data = rows.ToList();
            XNamespace rNs = RelationshipsNs;

            var sheetData = new XElement(Main + "sheetData");
            sheetData.Add(BuildRow(1, headers, headerStyle: true));
            for (var i = 0; i < data.Count; i++)
            {
                sheetData.Add(BuildRow(i + 2, data[i], headerStyle: false));
            }

            var cols = new XElement(Main + "cols");
            for (var c = 0; c < headers.Count; c++)
            {
                var column = c;
                var longest = Math.Max(headers[c]?.Length ?? 0, data.Count == 0 ? 0 : data.Max(r => column < r.Count ? r[column]?.Length ?? 0 : 0));
                cols.Add(new XElement(Main + "col",
                    new XAttribute("min", c + 1), new XAttribute("max", c + 1),
                    new XAttribute("width", Math.Min(60, Math.Max(10, longest + 2))), new XAttribute("customWidth", 1)));
            }

            var workbookDoc = new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
                new XElement(Main + "workbook", new XAttribute(XNamespace.Xmlns + "r", RelationshipsNs),
                    new XElement(Main + "sheets",
                        new XElement(Main + "sheet", new XAttribute("name", SafeSheetName(sheetName)), new XAttribute("sheetId", "1"),
                            new XAttribute(rNs + "id", "rId1")))));
            var worksheetDoc = new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
                new XElement(Main + "worksheet", cols, sheetData));

            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }

            using (var package = Package.Open(filePath, FileMode.Create))
            {
                var workbookUri = PackUriHelper.CreatePartUri(new Uri("/xl/workbook.xml", UriKind.Relative));
                WritePart(package, workbookUri, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml", workbookDoc);
                package.CreateRelationship(workbookUri, TargetMode.Internal, RelationshipsNs + "/officeDocument", "rId1");
                var workbookPart = package.GetPart(workbookUri);

                var sheetUri = PackUriHelper.CreatePartUri(new Uri("/xl/worksheets/sheet1.xml", UriKind.Relative));
                WritePart(package, sheetUri, "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml", worksheetDoc);
                workbookPart.CreateRelationship(sheetUri, TargetMode.Internal, RelationshipsNs + "/worksheet", "rId1");

                var stylesUri = PackUriHelper.CreatePartUri(new Uri("/xl/styles.xml", UriKind.Relative));
                WritePart(package, stylesUri, "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml", BuildStyles());
                workbookPart.CreateRelationship(stylesUri, TargetMode.Internal, RelationshipsNs + "/styles", "rId2");
            }
        }

        /// <summary>Excel limits sheet names to 31 characters and forbids : \ / ? * [ ].</summary>
        internal static string SafeSheetName(string name)
        {
            var cleaned = new string((name ?? "Sheet1").Where(ch => ":\\/?*[]".IndexOf(ch) < 0).ToArray()).Trim();
            if (cleaned.Length == 0)
            {
                cleaned = "Sheet1";
            }

            return cleaned.Length > 31 ? cleaned.Substring(0, 31) : cleaned;
        }

        internal static string ColumnLetter(int zeroBasedIndex)
        {
            var letter = string.Empty;
            for (var n = zeroBasedIndex + 1; n > 0; n = (n - 1) / 26)
            {
                letter = (char)('A' + (n - 1) % 26) + letter;
            }

            return letter;
        }

        private static XElement BuildRow(int rowNumber, IEnumerable<string> values, bool headerStyle)
        {
            return new XElement(Main + "row", new XAttribute("r", rowNumber),
                values.Select((value, index) =>
                {
                    var cell = new XElement(Main + "c",
                        new XAttribute("r", ColumnLetter(index) + rowNumber),
                        new XAttribute("t", "inlineStr"),
                        new XElement(Main + "is", new XElement(Main + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), StripInvalidXml(value))));
                    if (headerStyle)
                    {
                        cell.Add(new XAttribute("s", 1));
                    }

                    return cell;
                }));
        }

        private static string StripInvalidXml(string value)
        {
            return value == null ? string.Empty : new string(value.Where(ch => ch >= 0x20 || ch == '\t' || ch == '\n' || ch == '\r').ToArray());
        }

        private static XDocument BuildStyles()
        {
            return new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
                new XElement(Main + "styleSheet",
                    new XElement(Main + "fonts", new XAttribute("count", "2"),
                        new XElement(Main + "font", new XElement(Main + "sz", new XAttribute("val", "11")), new XElement(Main + "name", new XAttribute("val", "Calibri"))),
                        new XElement(Main + "font", new XElement(Main + "b"), new XElement(Main + "sz", new XAttribute("val", "11")), new XElement(Main + "name", new XAttribute("val", "Calibri")))),
                    new XElement(Main + "fills", new XAttribute("count", "1"),
                        new XElement(Main + "fill", new XElement(Main + "patternFill", new XAttribute("patternType", "none")))),
                    new XElement(Main + "borders", new XAttribute("count", "1"),
                        new XElement(Main + "border", new XElement(Main + "left"), new XElement(Main + "right"), new XElement(Main + "top"), new XElement(Main + "bottom"), new XElement(Main + "diagonal"))),
                    new XElement(Main + "cellStyleXfs", new XAttribute("count", "1"),
                        new XElement(Main + "xf", new XAttribute("numFmtId", "0"), new XAttribute("fontId", "0"), new XAttribute("fillId", "0"), new XAttribute("borderId", "0"))),
                    new XElement(Main + "cellXfs", new XAttribute("count", "2"),
                        new XElement(Main + "xf", new XAttribute("numFmtId", "0"), new XAttribute("fontId", "0"), new XAttribute("fillId", "0"), new XAttribute("borderId", "0"), new XAttribute("xfId", "0")),
                        new XElement(Main + "xf", new XAttribute("numFmtId", "0"), new XAttribute("fontId", "1"), new XAttribute("fillId", "0"), new XAttribute("borderId", "0"), new XAttribute("xfId", "0"), new XAttribute("applyFont", "1")))));
        }

        private static void WritePart(Package package, Uri uri, string contentType, XDocument content)
        {
            var part = package.CreatePart(uri, contentType, CompressionOption.Normal);
            using (var stream = part.GetStream(FileMode.Create, FileAccess.Write))
            {
                content.Save(stream);
            }
        }
    }
}
