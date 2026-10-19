using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using AMGH.ITInventory.Application.Abstractions.Spreadsheets;

namespace AMGH.ITInventory.Infrastructure.Spreadsheets;

/// <summary>
/// Reads the first worksheet of an .xlsx file produced by Excel, LibreOffice or this system.
/// Hardened for untrusted uploads: DTDs prohibited, limits on parts, size and rows.
/// </summary>
internal sealed class XlsxReader : ISpreadsheetReader
{
    private const string MainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace Main = MainNs;
    private const string RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const long MaxUncompressedBytes = 100L * 1024 * 1024; // zip-bomb guard
    private const int MaxEntries = 500;
    private const int MaxColumns = 200;

    private static readonly XmlReaderSettings Settings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        IgnoreComments = true,
        IgnoreWhitespace = false,
        MaxCharactersInDocument = MaxUncompressedBytes
    };

    public SheetTable ReadFirstSheet(Stream xlsx, int maxRows)
    {
        ArgumentNullException.ThrowIfNull(xlsx);
        ZipArchive zip;
        try
        {
            zip = new ZipArchive(xlsx, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (InvalidDataException)
        {
            throw new InvalidDataException("The file is not a valid .xlsx workbook. Save it from Excel as 'Excel Workbook (*.xlsx)'.");
        }

        using (zip)
        {
            if (zip.Entries.Count > MaxEntries || zip.Entries.Sum(e => e.Length) > MaxUncompressedBytes)
                throw new InvalidDataException("The workbook is too large.");

            var sheetPath = FindFirstSheetPath(zip);
            var shared = ReadSharedStrings(zip);
            var entry = zip.GetEntry(sheetPath) ?? throw new InvalidDataException("The first worksheet is missing.");
            return ReadSheet(entry, shared, maxRows);
        }
    }

    public IReadOnlyDictionary<string, SheetTable> ReadAllSheets(Stream xlsx, int maxRowsPerSheet)
    {
        ArgumentNullException.ThrowIfNull(xlsx);
        ZipArchive zip;
        try
        {
            zip = new ZipArchive(xlsx, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (InvalidDataException)
        {
            throw new InvalidDataException("The file is not a valid .xlsx workbook. Save it from Excel as 'Excel Workbook (*.xlsx)'.");
        }

        using (zip)
        {
            if (zip.Entries.Count > MaxEntries || zip.Entries.Sum(e => e.Length) > MaxUncompressedBytes)
                throw new InvalidDataException("The workbook is too large.");
            var shared = ReadSharedStrings(zip);
            var result = new Dictionary<string, SheetTable>(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, path) in SheetPaths(zip))
            {
                var entry = zip.GetEntry(path);
                if (entry is null || result.ContainsKey(name)) continue;
                var table = ReadSheet(entry, shared, maxRowsPerSheet + 1);
                result[name] = table.Rows.Count > maxRowsPerSheet
                    ? new SheetTable(table.Headers, table.Rows.Take(maxRowsPerSheet).ToList()) { Truncated = true }
                    : table;
            }
            if (result.Count == 0) throw new InvalidDataException("The workbook has no worksheets.");
            return result;
        }
    }

    /// <summary>Every worksheet's name and zip path, in workbook order.</summary>
    private static List<(string Name, string Path)> SheetPaths(ZipArchive zip)
    {
        var workbook = zip.GetEntry("xl/workbook.xml") ?? throw new InvalidDataException("This is not an Excel workbook.");
        var sheets = new List<(string Name, string RelId)>();
        using (var reader = XmlReader.Create(workbook.Open(), Settings))
        {
            while (reader.Read())
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "sheet"
                    && reader.GetAttribute("name") is { } name && reader.GetAttribute("id", RelNs) is { } rel)
                    sheets.Add((name.Trim(), rel));
        }
        var rels = zip.GetEntry("xl/_rels/workbook.xml.rels") ?? throw new InvalidDataException("The workbook relationships are missing.");
        var targets = new Dictionary<string, string>(StringComparer.Ordinal);
        using (var relReader = XmlReader.Create(rels.Open(), Settings))
        {
            while (relReader.Read())
                if (relReader.NodeType == XmlNodeType.Element && relReader.LocalName == "Relationship"
                    && relReader.GetAttribute("Id") is { } id && relReader.GetAttribute("Target") is { } target)
                {
                    target = target.Replace('\\', '/');
                    targets[id] = target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target;
                }
        }
        return sheets.Where(s => targets.ContainsKey(s.RelId)).Select(s => (s.Name, targets[s.RelId])).ToList();
    }

    private static string FindFirstSheetPath(ZipArchive zip)
    {
        var workbook = zip.GetEntry("xl/workbook.xml") ?? throw new InvalidDataException("This is not an Excel workbook.");
        string? relId = null;
        using (var reader = XmlReader.Create(workbook.Open(), Settings))
        {
            while (reader.Read())
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "sheet")
                {
                    relId = reader.GetAttribute("id", RelNs);
                    break;
                }
        }
        if (relId is null) throw new InvalidDataException("The workbook has no worksheets.");

        var rels = zip.GetEntry("xl/_rels/workbook.xml.rels") ?? throw new InvalidDataException("The workbook relationships are missing.");
        using var relReader = XmlReader.Create(rels.Open(), Settings);
        while (relReader.Read())
        {
            if (relReader.NodeType != XmlNodeType.Element || relReader.LocalName != "Relationship" || relReader.GetAttribute("Id") != relId) continue;
            var target = relReader.GetAttribute("Target") ?? string.Empty;
            target = target.Replace('\\', '/');
            return target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target;
        }
        throw new InvalidDataException("The first worksheet could not be located.");
    }

    private static List<string> ReadSharedStrings(ZipArchive zip)
    {
        var list = new List<string>();
        var entry = zip.GetEntry("xl/sharedStrings.xml");
        if (entry is null) return list;
        using var reader = XmlReader.Create(entry.Open(), Settings);
        foreach (var si in Elements(reader, "si")) list.Add(RichText(si));
        return list;
    }

    /// <summary>
    /// Streams the named elements one at a time as small XElements. XNode.ReadFrom leaves the reader on the
    /// node after the element, so the loop must not call Read() again in that case (that would skip a sibling).
    /// </summary>
    private static IEnumerable<XElement> Elements(XmlReader reader, string localName)
    {
        reader.MoveToContent();
        while (!reader.EOF)
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == localName && reader.NamespaceURI == MainNs)
                yield return (XElement)XNode.ReadFrom(reader);
            else
                reader.Read();
        }
    }

    /// <summary>Concatenates every &lt;t&gt; (plain or rich-text runs), ignoring phonetic hints.</summary>
    private static string RichText(XElement element) =>
        string.Concat(element.Descendants(Main + "t")
            .Where(t => !t.Ancestors(Main + "rPh").Any())
            .Select(t => t.Value));

    private static SheetTable ReadSheet(ZipArchiveEntry entry, List<string> shared, int maxRows)
    {
        var headers = new List<string>();
        var rows = new List<SheetRow>();
        var headerRead = false;
        var lastRow = 0;

        using var reader = XmlReader.Create(entry.Open(), Settings);
        foreach (var row in Elements(reader, "row"))
        {
            var rowNumber = int.TryParse((string?)row.Attribute("r"), out var rn) ? rn : lastRow + 1;
            lastRow = rowNumber;
            var cells = ReadRow(row, shared);

            if (!headerRead)
            {
                if (cells.All(string.IsNullOrWhiteSpace)) continue;
                headers.AddRange(cells.Select(c => c?.Trim() ?? string.Empty));
                headerRead = true;
                continue;
            }
            rows.Add(new SheetRow(rowNumber, cells));
            if (rows.Count >= maxRows) break;
        }
        return new SheetTable(headers, rows);
    }

    private static List<string?> ReadRow(XElement row, List<string> shared)
    {
        var cells = new List<string?>();
        var nextIndex = 0;
        foreach (var c in row.Elements(Main + "c"))
        {
            var index = ColumnIndex((string?)c.Attribute("r")) ?? nextIndex;
            nextIndex = index + 1;
            if (index >= MaxColumns) continue;

            var type = (string?)c.Attribute("t");
            var raw = c.Element(Main + "v")?.Value;
            var value = type switch
            {
                "s" => int.TryParse(raw, out var si) && si >= 0 && si < shared.Count ? shared[si] : null,
                "inlineStr" => c.Element(Main + "is") is { } inline ? RichText(inline) : null,
                "b" => raw == "1" ? "1" : "0",
                "e" => null, // error values (#N/A etc.) are treated as empty
                _ => raw
            };

            while (cells.Count <= index) cells.Add(null);
            cells[index] = value;
        }
        return cells;
    }

    /// <summary>"C12" → 2 (zero-based).</summary>
    internal static int? ColumnIndex(string? reference)
    {
        if (string.IsNullOrEmpty(reference)) return null;
        var n = 0;
        foreach (var ch in reference)
        {
            if (ch is >= 'A' and <= 'Z') n = n * 26 + (ch - 'A' + 1);
            else if (ch is >= 'a' and <= 'z') n = n * 26 + (ch - 'a' + 1);
            else break;
        }
        return n == 0 ? null : n - 1;
    }
}
