using System.ComponentModel.DataAnnotations;

namespace AMGH.ITInventory.Web.Models;

public sealed class TenantSelectViewModel
{
    [Required(ErrorMessage = "Enter your company code.")]
    [StringLength(10)]
    public string? Code { get; set; }

    /// <summary>Offer the one-click demo environment (display only).</summary>
    public bool ShowExploreDemo { get; set; }
}

public sealed record TenantRow(string Code, string Name, bool IsDemo, bool IsActive, DateTime CreatedUtc, string Url);

/// <summary>The operator page: create a customer company (its own database) and list the existing ones.</summary>
public sealed class TenantAdminViewModel
{
    [DataType(DataType.Password)] [StringLength(200)] public string? OperatorKey { get; set; }
    [StringLength(10)] public string? Code { get; set; }
    [StringLength(100)] public string? Name { get; set; }
    [StringLength(256)] public string? AdminEmail { get; set; }
    [DataType(DataType.Password)] [StringLength(128)] public string? AdminPassword { get; set; }

    public string? Message { get; set; }
    public bool Succeeded { get; set; }
    public IReadOnlyList<TenantRow> Tenants { get; set; } = Array.Empty<TenantRow>();
}
