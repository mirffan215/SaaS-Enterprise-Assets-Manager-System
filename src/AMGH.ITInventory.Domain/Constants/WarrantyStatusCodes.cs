namespace AMGH.ITInventory.Domain.Constants;

/// <summary>Values of dbo.tblWarrantyStatus.StatusCode (see dbo.fn_GetWarrantyStateCode).</summary>
public static class WarrantyStatusCodes
{
    public const string Active = "ACTIVE";
    public const string Expiring = "EXPIRING";
    public const string Expired = "EXPIRED";
    public const string Void = "VOID";

    /// <summary>Mirrors dbo.fn_GetWarrantyStateCode so the UI can compute state without a round trip.</summary>
    public static string Evaluate(DateOnly endDate, DateOnly asOf, int expiringDays)
    {
        if (endDate < asOf) return Expired;
        return endDate <= asOf.AddDays(expiringDays) ? Expiring : Active;
    }
}
