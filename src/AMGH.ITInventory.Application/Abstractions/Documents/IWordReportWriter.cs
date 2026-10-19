namespace AMGH.ITInventory.Application.Abstractions.Documents;

/// <summary>A tabular report ready to render: all values already formatted as text.</summary>
public sealed record WordReportDocument(
    string Title,
    string CompanyName,
    byte[]? LogoImage,
    string? LogoContentType,
    string Confidentiality,
    string GeneratedBy,
    string GeneratedAt,
    IReadOnlyList<KeyValuePair<string, string>> Filters,
    IReadOnlyList<string> Summary,
    IReadOnlyList<string> Headers,
    IReadOnlyList<IReadOnlyList<string>> Rows,
    IReadOnlyList<bool> RightAligned,
    int? GroupColumn,
    IReadOnlyList<double>? RelativeWidths = null);

/// <summary>Writes a Word (.docx) report: logo, title, filters, table, header/footer with page numbers.</summary>
public interface IWordReportWriter
{
    byte[] Write(WordReportDocument document);
}
