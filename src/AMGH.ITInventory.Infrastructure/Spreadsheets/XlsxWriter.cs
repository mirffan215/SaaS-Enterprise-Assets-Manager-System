using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using AMGH.ITInventory.Application.Abstractions.Spreadsheets;

namespace AMGH.ITInventory.Infrastructure.Spreadsheets;

/// <summary>
/// Writes Office Open XML workbooks (.xlsx) directly: frozen bold header row, auto-filter, column
/// widths, real Excel dates and numbers. Text is written as inline strings, which Excel never
/// evaluates as formulas.
/// </summary>
internal sealed class XlsxWriter : ISpreadsheetWriter
{
    private const string MainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string PkgRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const int MaxCellText = 32767;

    // Style indexes in styles.xml (cellXfs)
    private const int StyleHeader = 1, StyleDate = 2, StyleNumber = 3, StyleWhole = 4;

    public byte[] Write(IReadOnlyList<SheetData> sheets)
    {
        ArgumentNullException.ThrowIfNull(sheets);
        if (sheets.Count == 0) throw new ArgumentException("At least one sheet is required.", nameof(sheets));

        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            WritePart(zip, "[Content_Types].xml", w =>
            {
                w.WriteStartElement("Types", "http://schemas.openxmlformats.org/package/2006/content-types");
                Default(w, "rels", "application/vnd.openxmlformats-package.relationships+xml");
                Default(w, "xml", "application/xml");
                Override(w, "/xl/workbook.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml");
                Override(w, "/xl/styles.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml");
                for (var i = 1; i <= sheets.Count; i++)
                    Override(w, $"/xl/worksheets/sheet{i}.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml");
                w.WriteEndElement();
            });

            WritePart(zip, "_rels/.rels", w =>
            {
                w.WriteStartElement("Relationships", PkgRelNs);
                Relationship(w, "rId1", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument", "xl/workbook.xml");
                w.WriteEndElement();
            });

            WritePart(zip, "xl/workbook.xml", w =>
            {
                w.WriteStartElement("workbook", MainNs);
                w.WriteAttributeString("xmlns", "r", null, RelNs);
                w.WriteStartElement("sheets", MainNs);
                var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < sheets.Count; i++)
                {
                    w.WriteStartElement("sheet", MainNs);
                    w.WriteAttributeString("name", UniqueSheetName(sheets[i].Name, i, used));
                    w.WriteAttributeString("sheetId", (i + 1).ToString(CultureInfo.InvariantCulture));
                    w.WriteAttributeString("id", RelNs, $"rId{i + 1}");
                    w.WriteEndElement();
                }
                w.WriteEndElement();
                w.WriteEndElement();
            });

            WritePart(zip, "xl/_rels/workbook.xml.rels", w =>
            {
                w.WriteStartElement("Relationships", PkgRelNs);
                for (var i = 1; i <= sheets.Count; i++)
                    Relationship(w, $"rId{i}", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet", $"worksheets/sheet{i}.xml");
                Relationship(w, $"rId{sheets.Count + 1}", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles", "styles.xml");
                w.WriteEndElement();
            });

            WritePart(zip, "xl/styles.xml", WriteStyles);

            for (var i = 0; i < sheets.Count; i++)
            {
                var sheet = sheets[i];
                WritePart(zip, $"xl/worksheets/sheet{i + 1}.xml", w => WriteSheet(w, sheet));
            }
        }
        return output.ToArray();
    }

    private static void WriteSheet(XmlWriter w, SheetData sheet)
    {
        var columns = sheet.Columns;
        w.WriteStartElement("worksheet", MainNs);

        w.WriteStartElement("sheetViews", MainNs);
        w.WriteStartElement("sheetView", MainNs);
        w.WriteAttributeString("workbookViewId", "0");
        w.WriteStartElement("pane", MainNs);
        w.WriteAttributeString("ySplit", "1");
        w.WriteAttributeString("topLeftCell", "A2");
        w.WriteAttributeString("activePane", "bottomLeft");
        w.WriteAttributeString("state", "frozen");
        w.WriteEndElement();
        w.WriteEndElement();
        w.WriteEndElement();

        w.WriteStartElement("sheetFormatPr", MainNs);
        w.WriteAttributeString("defaultRowHeight", "15");
        w.WriteEndElement();

        if (columns.Count > 0)
        {
            w.WriteStartElement("cols", MainNs);
            for (var c = 0; c < columns.Count; c++)
            {
                w.WriteStartElement("col", MainNs);
                var n = (c + 1).ToString(CultureInfo.InvariantCulture);
                w.WriteAttributeString("min", n);
                w.WriteAttributeString("max", n);
                w.WriteAttributeString("width", Math.Clamp(columns[c].Width, 4, 100).ToString("0.##", CultureInfo.InvariantCulture));
                w.WriteAttributeString("customWidth", "1");
                w.WriteEndElement();
            }
            w.WriteEndElement();
        }

        w.WriteStartElement("sheetData", MainNs);
        StartRow(w, 1);
        for (var c = 0; c < columns.Count; c++) TextCell(w, Ref(c, 1), columns[c].Header, StyleHeader);
        w.WriteEndElement();

        var rowNumber = 1;
        foreach (var row in sheet.Rows)
        {
            rowNumber++;
            StartRow(w, rowNumber);
            for (var c = 0; c < row.Count; c++)
            {
                var kind = c < columns.Count ? columns[c].Kind : SheetColumnKind.Text;
                WriteCell(w, Ref(c, rowNumber), row[c], kind);
            }
            w.WriteEndElement();
        }
        w.WriteEndElement(); // sheetData

        if (columns.Count > 0)
        {
            w.WriteStartElement("autoFilter", MainNs);
            w.WriteAttributeString("ref", $"A1:{Ref(columns.Count - 1, Math.Max(rowNumber, 1))}");
            w.WriteEndElement();
        }
        w.WriteEndElement(); // worksheet
    }

    private static void WriteCell(XmlWriter w, string reference, object? value, SheetColumnKind kind)
    {
        switch (value)
        {
            case null:
                return;
            case string s:
                if (s.Length > 0) TextCell(w, reference, s, 0);
                return;
            case bool b:
                w.WriteStartElement("c", MainNs);
                w.WriteAttributeString("r", reference);
                w.WriteAttributeString("t", "b");
                w.WriteElementString("v", MainNs, b ? "1" : "0");
                w.WriteEndElement();
                return;
            case DateOnly d:
                NumberCell(w, reference, d.ToDateTime(TimeOnly.MinValue).ToOADate(), StyleDate);
                return;
            case DateTime dt:
                NumberCell(w, reference, dt.Date.ToOADate(), StyleDate);
                return;
            case decimal m:
                NumberCell(w, reference, m, kind == SheetColumnKind.WholeNumber ? StyleWhole : StyleNumber);
                return;
            case double db:
                NumberCell(w, reference, db, kind == SheetColumnKind.WholeNumber ? StyleWhole : StyleNumber);
                return;
            case int or long or short or byte:
                NumberCell(w, reference, Convert.ToInt64(value, CultureInfo.InvariantCulture), kind == SheetColumnKind.Number ? StyleNumber : StyleWhole);
                return;
            default:
                TextCell(w, reference, Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty, 0);
                return;
        }
    }

    private static void NumberCell(XmlWriter w, string reference, IFormattable number, int style)
    {
        w.WriteStartElement("c", MainNs);
        w.WriteAttributeString("r", reference);
        w.WriteAttributeString("s", style.ToString(CultureInfo.InvariantCulture));
        w.WriteElementString("v", MainNs, number.ToString(null, CultureInfo.InvariantCulture));
        w.WriteEndElement();
    }

    private static void TextCell(XmlWriter w, string reference, string text, int style)
    {
        text = CleanText(text);
        w.WriteStartElement("c", MainNs);
        w.WriteAttributeString("r", reference);
        if (style != 0) w.WriteAttributeString("s", style.ToString(CultureInfo.InvariantCulture));
        w.WriteAttributeString("t", "inlineStr");
        w.WriteStartElement("is", MainNs);
        w.WriteStartElement("t", MainNs);
        if (text.Length > 0 && (char.IsWhiteSpace(text[0]) || char.IsWhiteSpace(text[^1])))
            w.WriteAttributeString("xml", "space", null, "preserve");
        w.WriteString(text);
        w.WriteEndElement();
        w.WriteEndElement();
        w.WriteEndElement();
    }

    private static void StartRow(XmlWriter w, int number)
    {
        w.WriteStartElement("row", MainNs);
        w.WriteAttributeString("r", number.ToString(CultureInfo.InvariantCulture));
    }

    private static void WriteStyles(XmlWriter w)
    {
        w.WriteStartElement("styleSheet", MainNs);

        w.WriteStartElement("numFmts", MainNs);
        w.WriteAttributeString("count", "2");
        NumFmt(w, 164, "#,##0.00");
        NumFmt(w, 165, "yyyy-mm-dd");
        w.WriteEndElement();

        w.WriteStartElement("fonts", MainNs);
        w.WriteAttributeString("count", "2");
        Font(w, bold: false);
        Font(w, bold: true);
        w.WriteEndElement();

        w.WriteStartElement("fills", MainNs);
        w.WriteAttributeString("count", "3");
        Fill(w, "none", null);
        Fill(w, "gray125", null);
        Fill(w, "solid", "FFDCE8E9");
        w.WriteEndElement();

        w.WriteStartElement("borders", MainNs);
        w.WriteAttributeString("count", "1");
        w.WriteStartElement("border", MainNs);
        foreach (var side in new[] { "left", "right", "top", "bottom", "diagonal" }) { w.WriteStartElement(side, MainNs); w.WriteEndElement(); }
        w.WriteEndElement();
        w.WriteEndElement();

        w.WriteStartElement("cellStyleXfs", MainNs);
        w.WriteAttributeString("count", "1");
        Xf(w, 0, 0, 0, inCellXfs: false);
        w.WriteEndElement();

        w.WriteStartElement("cellXfs", MainNs);
        w.WriteAttributeString("count", "5");
        Xf(w, 0, 0, 0);       // 0 default
        Xf(w, 0, 1, 2);       // 1 header: bold + fill
        Xf(w, 165, 0, 0);     // 2 date
        Xf(w, 164, 0, 0);     // 3 decimal
        Xf(w, 1, 0, 0);       // 4 whole number ("0")
        w.WriteEndElement();

        w.WriteStartElement("cellStyles", MainNs);
        w.WriteAttributeString("count", "1");
        w.WriteStartElement("cellStyle", MainNs);
        w.WriteAttributeString("name", "Normal");
        w.WriteAttributeString("xfId", "0");
        w.WriteAttributeString("builtinId", "0");
        w.WriteEndElement();
        w.WriteEndElement();

        w.WriteEndElement();
    }

    private static void NumFmt(XmlWriter w, int id, string code)
    {
        w.WriteStartElement("numFmt", MainNs);
        w.WriteAttributeString("numFmtId", id.ToString(CultureInfo.InvariantCulture));
        w.WriteAttributeString("formatCode", code);
        w.WriteEndElement();
    }

    private static void Font(XmlWriter w, bool bold)
    {
        w.WriteStartElement("font", MainNs);
        if (bold) { w.WriteStartElement("b", MainNs); w.WriteEndElement(); }
        w.WriteStartElement("sz", MainNs); w.WriteAttributeString("val", "11"); w.WriteEndElement();
        w.WriteStartElement("name", MainNs); w.WriteAttributeString("val", "Calibri"); w.WriteEndElement();
        w.WriteStartElement("family", MainNs); w.WriteAttributeString("val", "2"); w.WriteEndElement();
        w.WriteEndElement();
    }

    private static void Fill(XmlWriter w, string pattern, string? rgb)
    {
        w.WriteStartElement("fill", MainNs);
        w.WriteStartElement("patternFill", MainNs);
        w.WriteAttributeString("patternType", pattern);
        if (rgb is not null)
        {
            w.WriteStartElement("fgColor", MainNs); w.WriteAttributeString("rgb", rgb); w.WriteEndElement();
            w.WriteStartElement("bgColor", MainNs); w.WriteAttributeString("indexed", "64"); w.WriteEndElement();
        }
        w.WriteEndElement();
        w.WriteEndElement();
    }

    private static void Xf(XmlWriter w, int numFmtId, int fontId, int fillId, bool inCellXfs = true)
    {
        w.WriteStartElement("xf", MainNs);
        w.WriteAttributeString("numFmtId", numFmtId.ToString(CultureInfo.InvariantCulture));
        w.WriteAttributeString("fontId", fontId.ToString(CultureInfo.InvariantCulture));
        w.WriteAttributeString("fillId", fillId.ToString(CultureInfo.InvariantCulture));
        w.WriteAttributeString("borderId", "0");
        if (inCellXfs)
        {
            w.WriteAttributeString("xfId", "0");
            if (numFmtId != 0) w.WriteAttributeString("applyNumberFormat", "1");
            if (fontId != 0) w.WriteAttributeString("applyFont", "1");
            if (fillId != 0) w.WriteAttributeString("applyFill", "1");
        }
        w.WriteEndElement();
    }

    private static void Default(XmlWriter w, string extension, string contentType)
    {
        w.WriteStartElement("Default", "http://schemas.openxmlformats.org/package/2006/content-types");
        w.WriteAttributeString("Extension", extension);
        w.WriteAttributeString("ContentType", contentType);
        w.WriteEndElement();
    }

    private static void Override(XmlWriter w, string part, string contentType)
    {
        w.WriteStartElement("Override", "http://schemas.openxmlformats.org/package/2006/content-types");
        w.WriteAttributeString("PartName", part);
        w.WriteAttributeString("ContentType", contentType);
        w.WriteEndElement();
    }

    private static void Relationship(XmlWriter w, string id, string type, string target)
    {
        w.WriteStartElement("Relationship", PkgRelNs);
        w.WriteAttributeString("Id", id);
        w.WriteAttributeString("Type", type);
        w.WriteAttributeString("Target", target);
        w.WriteEndElement();
    }

    private static void WritePart(ZipArchive zip, string name, Action<XmlWriter> write)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false) });
        writer.WriteStartDocument(standalone: true);
        write(writer);
        writer.WriteEndDocument();
    }

    internal static string ColumnName(int index)
    {
        var name = string.Empty;
        for (var n = index + 1; n > 0; n = (n - 1) / 26) name = (char)('A' + (n - 1) % 26) + name;
        return name;
    }

    private static string Ref(int columnIndex, int row) => ColumnName(columnIndex) + row.ToString(CultureInfo.InvariantCulture);

    private static string UniqueSheetName(string name, int index, HashSet<string> used)
    {
        var clean = new string((name ?? string.Empty).Where(ch => "[]:*?/\\".IndexOf(ch, StringComparison.Ordinal) < 0).ToArray()).Trim('\'').Trim();
        if (clean.Length == 0) clean = $"Sheet{index + 1}";
        if (clean.Length > 31) clean = clean[..31];
        var candidate = clean;
        for (var i = 2; !used.Add(candidate); i++)
            candidate = clean[..Math.Min(clean.Length, 28)] + " " + i.ToString(CultureInfo.InvariantCulture);
        return candidate;
    }

    /// <summary>Removes characters XML cannot carry and limits length to Excel's maximum.</summary>
    private static string CleanText(string text)
    {
        var sb = new StringBuilder(Math.Min(text.Length, MaxCellText));
        foreach (var ch in text)
        {
            if (sb.Length >= MaxCellText) break;
            if (ch == '\t' || ch == '\n' || ch == '\r' || ch >= 0x20 && !(ch >= 0xFFFE)) sb.Append(ch);
        }
        return sb.ToString();
    }
}
