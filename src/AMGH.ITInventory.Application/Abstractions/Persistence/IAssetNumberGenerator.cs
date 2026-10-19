namespace AMGH.ITInventory.Application.Abstractions.Persistence;

/// <summary>Issues permanent, never-reused numbers such as AMGH-IT-LAP-00001 (dbo.usp_AssetNumber_Generate).</summary>
public interface IAssetNumberGenerator
{
    /// <param name="sequenceCode">One of <c>Domain.Constants.SequenceCodes</c>.</param>
    /// <param name="cancellationToken">Cancels the database call.</param>
    Task<string> NextAsync(string sequenceCode, CancellationToken cancellationToken = default);
}
