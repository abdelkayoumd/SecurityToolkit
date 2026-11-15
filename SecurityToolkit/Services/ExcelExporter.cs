using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Packaging;
using System.Linq;
using System.Xml.Linq;

namespace SecurityToolkit.Services
{
    /// <summary>What an export says about itself, written to the workbook's "About" sheet.</summary>
    internal sealed class ExportInfo
    {
        /// <summary>The name of the report, for example "Privilege finder".</summary>
        public string Title { get; set; }

        /// <summary>What was asked, in words, for example "Roles that grant Read on 2 table(s) at User level or wider".</summary>
        public string Description { get; set; }

        /// <summary>The environment the data was read from.</summary>
        public string Environment { get; set; }

        public DateTime ExportedOn { get; set; } = DateTime.Now;

        /// <summary>Colour the privilege depths (None, User, Business Unit...) the way the grid draws them.</summary>
        public bool ColourDepths { get; set; }
    }

    /// <summary>
    /// Writes a headers/rows table as a genuine .xlsx workbook using the OPC packaging APIs built into
    /// .NET Framework, so no third-party Excel library is needed.
    /// The sheet is ready to work with: a coloured header that stays in place while scrolling, a filter on every
    /// column, numbers stored as numbers, and a landscape page that fits the width when printed. With an
    /// <see cref="ExportInfo"/> a second sheet records what was exported, from where and when.
    /// </summary>
    internal static class ExcelExporter
    {
        private const string RelationshipsNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        // Cell styles: indexes into cellXfs of BuildStyles.
        private const int StyleHeader = 1;
        private const int StyleText = 2;
        private const int StyleNumber = 3;
        private const int StyleDepthNone = 4;
        private const int StyleDepthUser = 5;
        private const int StyleDepthBusinessUnit = 6;
        private const int StyleDepthParentChild = 7;
        private const int StyleDepthOrganization = 8;
        private const int StyleAboutTitle = 9;
        private const int StyleAboutLabel = 10;
        private const int StyleAboutValue = 11;

        public static void Write(string filePath, string sheetName, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows)
        {
            Write(filePath, sheetName, headers, rows, null);
        }

        public static void Write(string filePath, string sheetName, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows, ExportInfo info)
        {
            var data = rows.ToList();
            var safeName = SafeSheetName(sheetName);
            XNamespace rNs = RelationshipsNs;

            var sheets = new XElement(Main + "sheets",
                new XElement(Main + "sheet", new XAttribute("name", safeName), new XAttribute("sheetId", "1"), new XAttribute(rNs + "id", "rId1")));
            if (info != null)
            {
                sheets.Add(new XElement(Main + "sheet", new XAttribute("name", AboutSheetName(safeName)), new XAttribute("sheetId", "2"), new XAttribute(rNs + "id", "rId3")));
            }

            var quoted = "'" + safeName.Replace("'", "''") + "'";
            var lastCell = "$" + ColumnLetter(Math.Max(0, headers.Count - 1)) + "$" + (data.Count + 1);
            var workbookDoc = new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
                new XElement(Main + "workbook", new XAttribute(XNamespace.Xmlns + "r", RelationshipsNs),
                    new XElement(Main + "bookViews", new XElement(Main + "workbookView")),
                    sheets,
                    new XElement(Main + "definedNames",
                        // What the filter covers, and the header row repeated on every printed page.
                        new XElement(Main + "definedName", new XAttribute("name", "_xlnm._FilterDatabase"), new XAttribute("localSheetId", "0"), new XAttribute("hidden", "1"),
                            quoted + "!$A$1:" + lastCell),
                        new XElement(Main + "definedName", new XAttribute("name", "_xlnm.Print_Titles"), new XAttribute("localSheetId", "0"),
                            quoted + "!$1:$1"))));

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
                WritePart(package, sheetUri, "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml", BuildDataSheet(headers, data, info));
                workbookPart.CreateRelationship(sheetUri, TargetMode.Internal, RelationshipsNs + "/worksheet", "rId1");

                var stylesUri = PackUriHelper.CreatePartUri(new Uri("/xl/styles.xml", UriKind.Relative));
                WritePart(package, stylesUri, "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml", BuildStyles());
                workbookPart.CreateRelationship(stylesUri, TargetMode.Internal, RelationshipsNs + "/styles", "rId2");

                if (info != null)
                {
                    var aboutUri = PackUriHelper.CreatePartUri(new Uri("/xl/worksheets/sheet2.xml", UriKind.Relative));
                    WritePart(package, aboutUri, "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml", BuildAboutSheet(info, data.Count));
                    workbookPart.CreateRelationship(aboutUri, TargetMode.Internal, RelationshipsNs + "/worksheet", "rId3");
                }
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

        /// <summary>
        /// True when every filled cell of a column is a plain whole number, so the column can be stored as numbers
        /// (and summed or sorted as such). Values with a leading zero stay text: they are codes, not quantities.
        /// </summary>
        internal static bool IsNumberColumn(IEnumerable<string> values)
        {
            var any = false;
            foreach (var value in values)
            {
                if (string.IsNullOrEmpty(value))
                {
                    continue;
                }

                var digits = value[0] == '-' ? value.Substring(1) : value;
                if (digits.Length == 0 || digits.Length > 15 || !digits.All(ch => ch >= '0' && ch <= '9') || (digits.Length > 1 && digits[0] == '0'))
                {
                    return false;
                }

                any = true;
            }

            return any;
        }

        /// <summary>The second sheet must not take the name of the data sheet.</summary>
        private static string AboutSheetName(string dataSheetName)
        {
            return string.Equals(dataSheetName, "About", StringComparison.OrdinalIgnoreCase) ? "About this export" : "About";
        }

        private static XDocument BuildDataSheet(IReadOnlyList<string> headers, List<IReadOnlyList<string>> data, ExportInfo info)
        {
            var numeric = new bool[headers.Count];
            var cols = new XElement(Main + "cols");
            for (var c = 0; c < headers.Count; c++)
            {
                var column = c;
                var values = data.Select(r => column < r.Count ? r[column] : null).ToList();
                numeric[c] = IsNumberColumn(values);

                // Room for the text, plus the filter arrow in the header.
                var longest = Math.Max((headers[c]?.Length ?? 0) + 3, values.Count == 0 ? 0 : values.Max(v => v?.Length ?? 0));
                cols.Add(new XElement(Main + "col",
                    new XAttribute("min", c + 1), new XAttribute("max", c + 1),
                    new XAttribute("width", Math.Min(60, Math.Max(10, longest + 2))), new XAttribute("customWidth", 1)));
            }

            var sheetData = new XElement(Main + "sheetData");
            sheetData.Add(new XElement(Main + "row", new XAttribute("r", 1), new XAttribute("ht", 22), new XAttribute("customHeight", 1),
                headers.Select((value, index) => TextCell(index, 1, value, StyleHeader))));
            for (var i = 0; i < data.Count; i++)
            {
                var rowNumber = i + 2;
                sheetData.Add(new XElement(Main + "row", new XAttribute("r", rowNumber),
                    data[i].Select((value, index) =>
                    {
                        if (index < numeric.Length && numeric[index] && !string.IsNullOrEmpty(value))
                        {
                            return new XElement(Main + "c", new XAttribute("r", ColumnLetter(index) + rowNumber), new XAttribute("s", StyleNumber),
                                new XElement(Main + "v", value));
                        }

                        return TextCell(index, rowNumber, value, info != null && info.ColourDepths ? DepthStyle(value) : StyleText);
                    })));
            }

            var range = "A1:" + ColumnLetter(Math.Max(0, headers.Count - 1)) + (data.Count + 1);
            var footer = "&L" + EscapeHeaderFooter(info?.Title) + "&RPage &P of &N";
            return new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
                new XElement(Main + "worksheet",
                    new XElement(Main + "sheetPr", new XElement(Main + "pageSetUpPr", new XAttribute("fitToPage", 1))),
                    new XElement(Main + "dimension", new XAttribute("ref", range)),
                    new XElement(Main + "sheetViews",
                        new XElement(Main + "sheetView", new XAttribute("workbookViewId", 0),
                            // The header row stays visible while the rows scroll.
                            new XElement(Main + "pane", new XAttribute("ySplit", 1), new XAttribute("topLeftCell", "A2"), new XAttribute("activePane", "bottomLeft"), new XAttribute("state", "frozen")),
                            new XElement(Main + "selection", new XAttribute("pane", "bottomLeft"), new XAttribute("activeCell", "A2"), new XAttribute("sqref", "A2")))),
                    cols,
                    sheetData,
                    new XElement(Main + "autoFilter", new XAttribute("ref", range)),
                    new XElement(Main + "pageMargins", new XAttribute("left", "0.4"), new XAttribute("right", "0.4"), new XAttribute("top", "0.5"), new XAttribute("bottom", "0.6"),
                        new XAttribute("header", "0.3"), new XAttribute("footer", "0.3")),
                    new XElement(Main + "pageSetup", new XAttribute("orientation", "landscape"), new XAttribute("fitToWidth", 1), new XAttribute("fitToHeight", 0)),
                    new XElement(Main + "headerFooter", new XElement(Main + "oddFooter", footer))));
        }

        private static XDocument BuildAboutSheet(ExportInfo info, int rowCount)
        {
            var version = typeof(ExcelExporter).Assembly.GetName().Version;
            var lines = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("What it shows", info.Description),
                new KeyValuePair<string, string>("Environment", info.Environment),
                new KeyValuePair<string, string>("Exported on", info.ExportedOn.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string>("Rows", rowCount.ToString(CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string>("Note", "The rows are what the screen showed when exporting: its filters and its sort order apply."),
                new KeyValuePair<string, string>("Created with", "Security Toolkit " + version.ToString(3) + " for XrmToolBox")
            };

            var sheetData = new XElement(Main + "sheetData");
            sheetData.Add(new XElement(Main + "row", new XAttribute("r", 1), new XAttribute("ht", 26), new XAttribute("customHeight", 1),
                TextCell(0, 1, string.IsNullOrWhiteSpace(info.Title) ? "Export" : info.Title, StyleAboutTitle)));

            var rowNumber = 3;
            foreach (var line in lines.Where(l => !string.IsNullOrWhiteSpace(l.Value)))
            {
                sheetData.Add(new XElement(Main + "row", new XAttribute("r", rowNumber),
                    TextCell(0, rowNumber, line.Key, StyleAboutLabel),
                    TextCell(1, rowNumber, line.Value, StyleAboutValue)));
                rowNumber++;
            }

            return new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
                new XElement(Main + "worksheet",
                    new XElement(Main + "sheetViews", new XElement(Main + "sheetView", new XAttribute("showGridLines", 0), new XAttribute("workbookViewId", 0))),
                    new XElement(Main + "cols",
                        new XElement(Main + "col", new XAttribute("min", 1), new XAttribute("max", 1), new XAttribute("width", 18), new XAttribute("customWidth", 1)),
                        new XElement(Main + "col", new XAttribute("min", 2), new XAttribute("max", 2), new XAttribute("width", 90), new XAttribute("customWidth", 1))),
                    sheetData));
        }

        private static XElement TextCell(int columnIndex, int rowNumber, string value, int style)
        {
            return new XElement(Main + "c",
                new XAttribute("r", ColumnLetter(columnIndex) + rowNumber),
                new XAttribute("s", style),
                new XAttribute("t", "inlineStr"),
                new XElement(Main + "is", new XElement(Main + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), StripInvalidXml(value))));
        }

        /// <summary>The style of a privilege depth written in words; plain text for anything else.</summary>
        private static int DepthStyle(string value)
        {
            switch (value)
            {
                case "None": return StyleDepthNone;
                case "User": return StyleDepthUser;
                case "Business Unit": return StyleDepthBusinessUnit;
                case "Parent: Child Business Units": return StyleDepthParentChild;
                case "Organization": return StyleDepthOrganization;
                default: return StyleText;
            }
        }

        /// <summary>In a page header or footer an ampersand starts a code, so a literal one is doubled.</summary>
        private static string EscapeHeaderFooter(string text)
        {
            return StripInvalidXml(text).Replace("&", "&&");
        }

        private static string StripInvalidXml(string value)
        {
            return value == null ? string.Empty : new string(value.Where(ch => ch >= 0x20 || ch == '\t' || ch == '\n' || ch == '\r').ToArray());
        }

        private static XDocument BuildStyles()
        {
            return new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
                new XElement(Main + "styleSheet",
                    new XElement(Main + "fonts", new XAttribute("count", "9"),
                        Font(null, false, 11),          // 0 body
                        Font("FFFFFFFF", true, 11),     // 1 header
                        Font("FFA19F9D", false, 11),    // 2 depth: None
                        Font("FF605E5C", false, 11),    // 3 depth: User
                        Font("FF005A9E", false, 11),    // 4 depth: Business Unit
                        Font("FFA4262C", false, 11),    // 5 depth: Parent: Child Business Units
                        Font("FF107C10", false, 11),    // 6 depth: Organization
                        Font("FF1F1F1F", true, 16),     // 7 About: title
                        Font("FF605E5C", true, 11)),    // 8 About: label
                    new XElement(Main + "fills", new XAttribute("count", "7"),
                        // The first two fills are reserved by Excel and must be exactly these.
                        new XElement(Main + "fill", new XElement(Main + "patternFill", new XAttribute("patternType", "none"))),
                        new XElement(Main + "fill", new XElement(Main + "patternFill", new XAttribute("patternType", "gray125"))),
                        Fill("FF0F548C"),   // 2 header
                        Fill("FFF3F2F1"),   // 3 depth: User
                        Fill("FFDEECF9"),   // 4 depth: Business Unit
                        Fill("FFFDE7E9"),   // 5 depth: Parent: Child Business Units
                        Fill("FFDFF6DD")),  // 6 depth: Organization
                    new XElement(Main + "borders", new XAttribute("count", "2"),
                        new XElement(Main + "border", new XElement(Main + "left"), new XElement(Main + "right"), new XElement(Main + "top"), new XElement(Main + "bottom"), new XElement(Main + "diagonal")),
                        new XElement(Main + "border", Edge("left"), Edge("right"), Edge("top"), Edge("bottom"), new XElement(Main + "diagonal"))),
                    new XElement(Main + "cellStyleXfs", new XAttribute("count", "1"),
                        new XElement(Main + "xf", new XAttribute("numFmtId", "0"), new XAttribute("fontId", "0"), new XAttribute("fillId", "0"), new XAttribute("borderId", "0"))),
                    new XElement(Main + "cellXfs", new XAttribute("count", "12"),
                        Xf(0, 0, 0, null),             // 0 default
                        Xf(1, 2, 1, "center"),         // 1 header
                        Xf(0, 0, 1, null),             // 2 text
                        Xf(0, 0, 1, null),             // 3 number
                        Xf(2, 0, 1, null),             // 4 depth: None
                        Xf(3, 3, 1, null),             // 5 depth: User
                        Xf(4, 4, 1, null),             // 6 depth: Business Unit
                        Xf(5, 5, 1, null),             // 7 depth: Parent: Child Business Units
                        Xf(6, 6, 1, null),             // 8 depth: Organization
                        Xf(7, 0, 0, "center"),         // 9 About: title
                        Xf(8, 0, 0, "top"),            // 10 About: label
                        Xf(0, 0, 0, "top", wrap: true))));   // 11 About: value
        }

        private static XElement Font(string argb, bool bold, int size)
        {
            var font = new XElement(Main + "font");
            if (bold)
            {
                font.Add(new XElement(Main + "b"));
            }

            font.Add(new XElement(Main + "sz", new XAttribute("val", size)));
            if (argb != null)
            {
                font.Add(new XElement(Main + "color", new XAttribute("rgb", argb)));
            }

            font.Add(new XElement(Main + "name", new XAttribute("val", "Calibri")));
            return font;
        }

        private static XElement Fill(string argb)
        {
            return new XElement(Main + "fill",
                new XElement(Main + "patternFill", new XAttribute("patternType", "solid"),
                    new XElement(Main + "fgColor", new XAttribute("rgb", argb)),
                    new XElement(Main + "bgColor", new XAttribute("indexed", 64))));
        }

        private static XElement Edge(string side)
        {
            return new XElement(Main + side, new XAttribute("style", "thin"), new XElement(Main + "color", new XAttribute("rgb", "FFD2D0CE")));
        }

        private static XElement Xf(int font, int fill, int border, string vertical, bool wrap = false)
        {
            var xf = new XElement(Main + "xf", new XAttribute("numFmtId", "0"), new XAttribute("fontId", font), new XAttribute("fillId", fill),
                new XAttribute("borderId", border), new XAttribute("xfId", "0"),
                new XAttribute("applyFont", 1), new XAttribute("applyFill", 1), new XAttribute("applyBorder", 1));
            if (vertical != null || wrap)
            {
                var alignment = new XElement(Main + "alignment");
                if (vertical != null)
                {
                    alignment.Add(new XAttribute("vertical", vertical));
                }

                if (wrap)
                {
                    alignment.Add(new XAttribute("wrapText", 1));
                }

                xf.Add(new XAttribute("applyAlignment", 1), alignment);
            }

            return xf;
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
