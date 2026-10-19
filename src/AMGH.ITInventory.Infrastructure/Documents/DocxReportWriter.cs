using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using AMGH.ITInventory.Application.Abstractions.Documents;

namespace AMGH.ITInventory.Infrastructure.Documents;

/// <summary>
/// Writes a Word report (Office Open XML, ISO/IEC 29500) with the built-in zip support: company logo and title,
/// generation details, filters and summary, a data table whose header row repeats on every page, group headings,
/// and a header/footer with the confidentiality label and "Page X of Y". Wide reports use landscape A4.
/// </summary>
internal sealed class DocxReportWriter : IWordReportWriter
{
    private const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture; // XML numbers must never use regional digits

    public byte[] Write(WordReportDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);
        var landscape = doc.Headers.Count > 6;
        var logo = doc.LogoImage is { Length: > 0 } bytes && ImageSize(bytes) is { } size ? (bytes, size, Png: bytes[0] == 0x89) : default;

        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, "[Content_Types].xml", ContentTypes(logo.bytes is not null, logo.Png));
            Add(zip, "_rels/.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/>" +
                "</Relationships>");
            Add(zip, "word/_rels/document.xml.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rIdStyles\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
                "<Relationship Id=\"rIdHeader\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/header\" Target=\"header1.xml\"/>" +
                "<Relationship Id=\"rIdFooter\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/footer\" Target=\"footer1.xml\"/>" +
                (logo.bytes is not null
                    ? $"<Relationship Id=\"rIdLogo\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/image\" Target=\"media/logo.{(logo.Png ? "png" : "jpeg")}\"/>"
                    : string.Empty) +
                "</Relationships>");
            Add(zip, "word/styles.xml", Styles());
            Add(zip, "word/header1.xml", HeaderXml(doc));
            Add(zip, "word/footer1.xml", FooterXml(doc));
            Add(zip, "word/document.xml", DocumentXml(doc, landscape, logo.bytes is not null ? logo.size : null));
            if (logo.bytes is not null)
            {
                var entry = zip.CreateEntry($"word/media/logo.{(logo.Png ? "png" : "jpeg")}", CompressionLevel.Optimal);
                using var s = entry.Open();
                s.Write(logo.bytes);
            }
        }
        return output.ToArray();
    }

    // ------------------------------------------------------------------ parts

    private static string ContentTypes(bool hasLogo, bool png) =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
        "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
        "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
        (hasLogo ? png ? "<Default Extension=\"png\" ContentType=\"image/png\"/>" : "<Default Extension=\"jpeg\" ContentType=\"image/jpeg\"/>" : string.Empty) +
        "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>" +
        "<Override PartName=\"/word/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml\"/>" +
        "<Override PartName=\"/word/header1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml\"/>" +
        "<Override PartName=\"/word/footer1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.footer+xml\"/>" +
        "</Types>";

    private static string Styles() =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        $"<w:styles xmlns:w=\"{W}\">" +
        "<w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:ascii=\"Calibri\" w:hAnsi=\"Calibri\" w:cs=\"Arial\" w:eastAsia=\"Calibri\"/><w:sz w:val=\"18\"/><w:szCs w:val=\"18\"/><w:lang w:val=\"en-GB\" w:bidi=\"ar-SA\"/></w:rPr></w:rPrDefault>" +
        "<w:pPrDefault><w:pPr><w:spacing w:after=\"60\" w:line=\"240\" w:lineRule=\"auto\"/></w:pPr></w:pPrDefault></w:docDefaults>" +
        "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/></w:style>" +
        "<w:style w:type=\"paragraph\" w:styleId=\"Title\"><w:name w:val=\"Title\"/><w:basedOn w:val=\"Normal\"/><w:pPr><w:spacing w:before=\"120\" w:after=\"60\"/></w:pPr><w:rPr><w:b/><w:color w:val=\"0E3B43\"/><w:sz w:val=\"36\"/></w:rPr></w:style>" +
        "<w:style w:type=\"paragraph\" w:styleId=\"Heading2\"><w:name w:val=\"heading 2\"/><w:basedOn w:val=\"Normal\"/><w:pPr><w:keepNext/><w:spacing w:before=\"160\" w:after=\"60\"/></w:pPr><w:rPr><w:b/><w:color w:val=\"13707A\"/><w:sz w:val=\"22\"/></w:rPr></w:style>" +
        "<w:style w:type=\"paragraph\" w:styleId=\"Small\"><w:name w:val=\"Small\"/><w:basedOn w:val=\"Normal\"/><w:rPr><w:color w:val=\"555555\"/><w:sz w:val=\"16\"/></w:rPr></w:style>" +
        "<w:style w:type=\"table\" w:styleId=\"ReportTable\"><w:name w:val=\"Report Table\"/><w:tblPr><w:tblBorders>" +
        "<w:top w:val=\"single\" w:sz=\"4\" w:color=\"9AA6A8\"/><w:left w:val=\"single\" w:sz=\"4\" w:color=\"9AA6A8\"/><w:bottom w:val=\"single\" w:sz=\"4\" w:color=\"9AA6A8\"/>" +
        "<w:right w:val=\"single\" w:sz=\"4\" w:color=\"9AA6A8\"/><w:insideH w:val=\"single\" w:sz=\"4\" w:color=\"D5DEDF\"/><w:insideV w:val=\"single\" w:sz=\"4\" w:color=\"D5DEDF\"/>" +
        "</w:tblBorders><w:tblCellMar><w:top w:w=\"30\" w:type=\"dxa\"/><w:left w:w=\"60\" w:type=\"dxa\"/><w:bottom w:w=\"30\" w:type=\"dxa\"/><w:right w:w=\"60\" w:type=\"dxa\"/></w:tblCellMar></w:tblPr></w:style>" +
        "</w:styles>";

    private static string HeaderXml(WordReportDocument d) =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        $"<w:hdr xmlns:w=\"{W}\"><w:p><w:pPr><w:pStyle w:val=\"Small\"/><w:jc w:val=\"right\"/></w:pPr>" +
        Run($"{d.CompanyName} · {d.Title}") + "</w:p></w:hdr>";

    private static string FooterXml(WordReportDocument d) =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        $"<w:ftr xmlns:w=\"{W}\"><w:p><w:pPr><w:pStyle w:val=\"Small\"/><w:tabs><w:tab w:val=\"right\" w:pos=\"15400\"/></w:tabs></w:pPr>" +
        Run(d.Confidentiality.ToUpperInvariant(), bold: true) + Run($" · Generated {d.GeneratedAt} by {d.GeneratedBy} · {Shared.AppInfo.ShortName}") +
        "<w:r><w:tab/></w:r>" + Run("Page ") + Field("PAGE") + Run(" of ") + Field("NUMPAGES") +
        "</w:p></w:ftr>";

    private static string DocumentXml(WordReportDocument d, bool landscape, (int W, int H)? logoSize)
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        sb.Append($"<w:document xmlns:w=\"{W}\" xmlns:r=\"{R}\" ");
        sb.Append("xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" ");
        sb.Append("xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" ");
        sb.Append("xmlns:pic=\"http://schemas.openxmlformats.org/drawingml/2006/picture\"><w:body>");

        if (logoSize is { } ls) sb.Append("<w:p>").Append(Logo(ls)).Append("</w:p>");
        sb.Append("<w:p><w:pPr><w:pStyle w:val=\"Small\"/></w:pPr>").Append(Run(d.CompanyName, bold: true)).Append("</w:p>");
        sb.Append("<w:p><w:pPr><w:pStyle w:val=\"Title\"/></w:pPr>").Append(Run(d.Title)).Append("</w:p>");
        sb.Append("<w:p><w:pPr><w:pStyle w:val=\"Small\"/></w:pPr>")
          .Append(Run($"Generated {d.GeneratedAt} by {d.GeneratedBy} · Classification: ")).Append(Run(d.Confidentiality, bold: true))
          .Append(Run(string.Create(Inv, $" · {d.Rows.Count:N0} row(s)"))).Append("</w:p>");

        if (d.Filters.Count > 0)
        {
            sb.Append("<w:p><w:pPr><w:pStyle w:val=\"Heading2\"/></w:pPr>").Append(Run("Filters")).Append("</w:p>");
            foreach (var f in d.Filters) sb.Append("<w:p>").Append(Run(f.Key + ": ", bold: true)).Append(Run(f.Value)).Append("</w:p>");
        }
        if (d.Summary.Count > 0)
        {
            sb.Append("<w:p><w:pPr><w:pStyle w:val=\"Heading2\"/></w:pPr>").Append(Run("Summary")).Append("</w:p>");
            foreach (var s in d.Summary) sb.Append("<w:p>").Append(Run("• " + s)).Append("</w:p>");
        }

        sb.Append("<w:p><w:pPr><w:pStyle w:val=\"Heading2\"/></w:pPr>").Append(Run("Details")).Append("</w:p>");
        var cols = d.Headers.Count;
        var tableSize = cols > 12 ? 14 : cols > 8 ? 16 : 18; // half-points: 7 / 8 / 9 pt, so wide tables fit without breaking words
        var usable = landscape ? 15398 : 10466; // A4 minus 0.5" margins, in twentieths of a point
        // proportional column widths (from the report's column definitions), so short columns do not squeeze long text
        var weights = Enumerable.Range(0, cols).Select(i => d.RelativeWidths is { } rw && i < rw.Count && rw[i] > 0 ? rw[i] : 16d).ToList();
        var widths = weights.Select(w => (int)Math.Max(500, usable * w / weights.Sum())).ToList();
        sb.Append("<w:tbl><w:tblPr><w:tblStyle w:val=\"ReportTable\"/><w:tblW w:w=\"5000\" w:type=\"pct\"/><w:tblLayout w:type=\"fixed\"/></w:tblPr><w:tblGrid>");
        foreach (var w in widths) sb.Append(Inv, $"<w:gridCol w:w=\"{w}\"/>");
        sb.Append("</w:tblGrid>");

        // header row: repeats on every page
        sb.Append("<w:tr><w:trPr><w:tblHeader/><w:cantSplit/></w:trPr>");
        for (var i = 0; i < cols; i++) sb.Append(Cell(d.Headers[i], bold: true, fill: "0E3B43", color: "FFFFFF", width: widths[i], size: tableSize));
        sb.Append("</w:tr>");

        string? currentGroup = null;
        var firstRow = true;
        foreach (var row in d.Rows)
        {
            if (d.GroupColumn is { } g && (firstRow || !string.Equals(row[g], currentGroup, StringComparison.Ordinal)))
            {
                currentGroup = row[g];
                var count = d.Rows.Count(r => string.Equals(r[g], currentGroup, StringComparison.Ordinal));
                sb.Append("<w:tr><w:trPr><w:cantSplit/></w:trPr><w:tc><w:tcPr>").Append(Inv, $"<w:gridSpan w:val=\"{cols}\"/>")
                  .Append("<w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"E3EEEF\"/></w:tcPr><w:p><w:pPr><w:keepNext/></w:pPr>")
                  .Append(Run(string.Create(Inv, $"{d.Headers[g]}: {(string.IsNullOrEmpty(currentGroup) ? "(none)" : currentGroup)} ({count})"), bold: true))
                  .Append("</w:p></w:tc></w:tr>");
            }
            firstRow = false;
            sb.Append("<w:tr><w:trPr><w:cantSplit/></w:trPr>");
            for (var i = 0; i < cols; i++) sb.Append(Cell(i < row.Count ? row[i] : string.Empty, right: d.RightAligned.Count > i && d.RightAligned[i], width: widths[i], size: tableSize));
            sb.Append("</w:tr>");
        }
        sb.Append("</w:tbl>");
        if (d.Rows.Count == 0) sb.Append("<w:p>").Append(Run("No records match the filters.")).Append("</w:p>");

        // section: A4, landscape when the table is wide; header/footer
        var (pw, ph) = landscape ? (16838, 11906) : (11906, 16838);
        sb.Append("<w:sectPr><w:headerReference w:type=\"default\" r:id=\"rIdHeader\"/><w:footerReference w:type=\"default\" r:id=\"rIdFooter\"/>");
        sb.Append(Inv, $"<w:pgSz w:w=\"{pw}\" w:h=\"{ph}\"{(landscape ? " w:orient=\"landscape\"" : "")}/>");
        sb.Append("<w:pgMar w:top=\"900\" w:right=\"720\" w:bottom=\"900\" w:left=\"720\" w:header=\"360\" w:footer=\"360\" w:gutter=\"0\"/></w:sectPr>");
        sb.Append("</w:body></w:document>");
        return sb.ToString();
    }

    private static string Logo((int W, int H) size)
    {
        const long heightEmu = 548640; // 0.6 inch
        var widthEmu = (long)Math.Round(heightEmu * (double)size.W / Math.Max(1, size.H));
        widthEmu = Math.Min(widthEmu, 2743200); // at most 3 inches wide
        return "<w:r><w:drawing><wp:inline distT=\"0\" distB=\"0\" distL=\"0\" distR=\"0\">" +
               string.Create(Inv, $"<wp:extent cx=\"{widthEmu}\" cy=\"{heightEmu}\"/><wp:docPr id=\"1\" name=\"Company logo\"/>") +
               "<a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/picture\"><pic:pic>" +
               "<pic:nvPicPr><pic:cNvPr id=\"1\" name=\"logo\"/><pic:cNvPicPr/></pic:nvPicPr>" +
               "<pic:blipFill><a:blip r:embed=\"rIdLogo\"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill>" +
               string.Create(Inv, $"<pic:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"{widthEmu}\" cy=\"{heightEmu}\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></pic:spPr>") +
               "</pic:pic></a:graphicData></a:graphic></wp:inline></w:drawing></w:r>";
    }

    // ------------------------------------------------------------------ helpers

    private static string Cell(string? text, bool bold = false, string? fill = null, string? color = null, bool right = false, int? width = null, int? size = null) =>
        "<w:tc><w:tcPr>" + (width is { } w ? string.Create(Inv, $"<w:tcW w:w=\"{w}\" w:type=\"dxa\"/>") : string.Empty) +
        (fill is null ? string.Empty : $"<w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"{fill}\"/>") + "</w:tcPr>" +
        "<w:p><w:pPr><w:spacing w:after=\"0\"/>" + (right ? "<w:jc w:val=\"right\"/>" : string.Empty) + "</w:pPr>" +
        Run(text ?? string.Empty, bold, color, size) + "</w:p></w:tc>";

    private static string Run(string text, bool bold = false, string? color = null, int? size = null)
    {
        var rtl = text.Any(c => c is >= '\u0590' and <= '\u08FF');
        var props = (bold ? "<w:b/><w:bCs/>" : string.Empty) + (color is null ? string.Empty : $"<w:color w:val=\"{color}\"/>") +
                    (size is { } sz ? string.Create(Inv, $"<w:sz w:val=\"{sz}\"/><w:szCs w:val=\"{sz}\"/>") : string.Empty) + (rtl ? "<w:rtl/>" : string.Empty);
        return "<w:r>" + (props.Length > 0 ? "<w:rPr>" + props + "</w:rPr>" : string.Empty) +
               "<w:t xml:space=\"preserve\">" + Escape(text) + "</w:t></w:r>";
    }

    private static string Field(string instruction) =>
        "<w:r><w:fldChar w:fldCharType=\"begin\"/></w:r>" +
        $"<w:r><w:instrText xml:space=\"preserve\"> {instruction} </w:instrText></w:r>" +
        "<w:r><w:fldChar w:fldCharType=\"separate\"/></w:r><w:r><w:t>1</w:t></w:r>" +
        "<w:r><w:fldChar w:fldCharType=\"end\"/></w:r>";

    /// <summary>XML-escapes and drops characters XML 1.0 does not allow (control characters from pasted data).</summary>
    internal static string Escape(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (c < 0x20 && c is not '\t' and not '\n' and not '\r') continue;
            if (c is '\uFFFE' or '\uFFFF') continue;
            sb.Append(c switch { '&' => "&amp;", '<' => "&lt;", '>' => "&gt;", '"' => "&quot;", _ => c.ToString() });
        }
        return sb.ToString();
    }

    private static void Add(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var s = entry.Open();
        var bytes = Encoding.UTF8.GetBytes(content);
        s.Write(bytes);
    }

    /// <summary>Pixel size of a PNG (IHDR) or JPEG (SOF marker); null when unreadable (the logo is then left out).</summary>
    internal static (int W, int H)? ImageSize(byte[] b)
    {
        if (b.Length > 24 && b[0] == 0x89 && b[1] == 0x50)
            return (BinaryPrimitives.ReadInt32BigEndian(b.AsSpan(16)), BinaryPrimitives.ReadInt32BigEndian(b.AsSpan(20)));
        if (b.Length > 4 && b[0] == 0xFF && b[1] == 0xD8)
        {
            var i = 2;
            while (i + 9 < b.Length)
            {
                if (b[i] != 0xFF) { i++; continue; }
                var marker = b[i + 1];
                var len = (b[i + 2] << 8) | b[i + 3];
                if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
                    return ((b[i + 7] << 8) | b[i + 8], (b[i + 5] << 8) | b[i + 6]);
                i += 2 + len;
            }
        }
        return null;
    }
}
