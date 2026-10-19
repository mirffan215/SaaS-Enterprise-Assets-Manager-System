using System.Data;
using AMGH.ITInventory.Application.Documents;

namespace AMGH.ITInventory.Persistence.Procedures;

/// <summary>Document counters via dbo.usp_DocumentNumber_Next (script 18): runs inside the caller's transaction.</summary>
internal sealed class DocumentCounter : IDocumentCounter
{
    private readonly StoredProcedureExecutor _executor;

    public DocumentCounter(StoredProcedureExecutor executor) => _executor = executor;

    public async Task<int> NextAsync(string typeCode, string period, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeCode);
        var output = StoredProcedureExecutor.Out("@Next", SqlDbType.Int);
        await _executor.ExecuteAsync(ProcedureNames.DocumentNumberNext,
        [
            StoredProcedureExecutor.In("@DocumentType", SqlDbType.NVarChar, typeCode, 10),
            StoredProcedureExecutor.In("@Period", SqlDbType.NVarChar, period ?? string.Empty, 10),
            output
        ], cancellationToken);
        return (int)output.Value;
    }
}
