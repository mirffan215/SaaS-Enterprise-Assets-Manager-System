namespace AMGH.ITInventory.Application.Abstractions.Spreadsheets;

public enum SheetColumnKind
{
    Text,
    WholeNumber,
    Number,
    Date
}

public sealed record SheetColumn(string Header, SheetColumnKind Kind = SheetColumnKind.Text, double Width = 18);

/// <summary>One worksheet. Row values may be string, int, long, decimal, double, DateOnly, DateTime, bool or null.</summary>
public sealed record SheetData(string Name, IReadOnlyList<SheetColumn> Columns, IReadOnlyList<IReadOnlyList<object?>> Rows);

/// <summary>Raw cell text of the first worksheet. Numbers keep their invariant text; Excel dates arrive as serial numbers.</summary>
public sealed record SheetTable(IReadOnlyList<string> Headers, IReadOnlyList<SheetRow> Rows)
{
    /// <summary>True when the sheet had more rows than were read.</summary>
    public bool Truncated { get; init; }
}

public sealed record SheetRow(int RowNumber, IReadOnlyList<string?> Cells)
{
    public string? this[int index] => index >= 0 && index < Cells.Count ? Cells[index] : null;
    public bool IsEmpty => Cells.All(string.IsNullOrWhiteSpace);
}

public interface ISpreadsheetWriter
{
    /// <summary>Creates an .xlsx workbook.</summary>
    byte[] Write(IReadOnlyList<SheetData> sheets);
}

public interface ISpreadsheetReader
{
    /// <summary>Reads the first worksheet of an .xlsx file. Throws <see cref="InvalidDataException"/> for unreadable files.</summary>
    SheetTable ReadFirstSheet(Stream xlsx, int maxRows);

    /// <summary>
    /// Reads every worksheet, keyed by sheet name (case-insensitive). Each sheet keeps at most <paramref name="maxRowsPerSheet"/>
    /// data rows plus its header; more rows are reported by <see cref="SheetTable.Truncated"/>. Same limits and protections as above.
    /// </summary>
    IReadOnlyDictionary<string, SheetTable> ReadAllSheets(Stream xlsx, int maxRowsPerSheet);
}
