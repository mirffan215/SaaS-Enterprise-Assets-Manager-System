namespace AMGH.ITInventory.Application.Common;

/// <summary>An item for a drop-down list. <see cref="ParentId"/> drives cascading lists (location → building → floor → room).</summary>
public sealed record LookupOption(int Id, string Text, int? ParentId = null, string? Code = null);

/// <summary>What the current user may see and change on an asset, derived from their permissions.</summary>
public sealed record AssetAccess(bool CanViewFinancials, bool CanViewSecurityDetails)
{
    public static readonly AssetAccess Full = new(true, true);
}
