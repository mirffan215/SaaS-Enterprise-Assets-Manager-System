using System.Data;
using AMGH.ITInventory.Application.Abstractions.Persistence;

namespace AMGH.ITInventory.Persistence.Procedures;

internal sealed class AssetNumberGenerator : IAssetNumberGenerator
{
    private readonly StoredProcedureExecutor _executor;

    public AssetNumberGenerator(StoredProcedureExecutor executor) => _executor = executor;

    public async Task<string> NextAsync(string sequenceCode, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sequenceCode);
        var output = StoredProcedureExecutor.Out("@AssetNumber", SqlDbType.NVarChar, 30);
        await _executor.ExecuteAsync(ProcedureNames.AssetNumberGenerate,
        [
            StoredProcedureExecutor.In("@SequenceCode", SqlDbType.NVarChar, sequenceCode, 10),
            output
        ], cancellationToken);
        return (string)output.Value;
    }
}
