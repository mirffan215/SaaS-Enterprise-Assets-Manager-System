namespace AMGH.ITInventory.Domain.Constants;

/// <summary>Values of dbo.tblAssetStatus.StatusCode referenced by business rules and stored procedures.</summary>
public static class AssetStatusCodes
{
    public const string InStock = "IN_STOCK";
    public const string Reserved = "RESERVED";
    public const string Assigned = "ASSIGNED";
    public const string UnderMaintenance = "IN_MAINT";
    public const string Missing = "MISSING";
    public const string Stolen = "STOLEN";
    public const string Retired = "RETIRED";
    public const string PendingDisposal = "PEND_DISP";
    public const string Disposed = "DISPOSED";
}
