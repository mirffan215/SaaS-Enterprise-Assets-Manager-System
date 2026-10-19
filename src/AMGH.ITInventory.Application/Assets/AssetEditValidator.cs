using System.Linq.Expressions;
using System.Net;
using System.Text.RegularExpressions;
using AMGH.ITInventory.Shared.Abstractions;
using FluentValidation;

namespace AMGH.ITInventory.Application.Assets;

/// <summary>
/// Field rules for the asset form and the Excel import (same rules for both).
/// Rules that need the database (codes exist, hierarchy, uniqueness) are checked in AssetService.
/// </summary>
public sealed partial class AssetEditValidator : AbstractValidator<AssetEditModel>
{
    [GeneratedRegex(@"^[A-Za-z0-9]([A-Za-z0-9-]{0,61}[A-Za-z0-9])?(\.[A-Za-z0-9]([A-Za-z0-9-]{0,61}[A-Za-z0-9])?)*$", RegexOptions.CultureInvariant)]
    private static partial Regex HostnamePattern();

    [GeneratedRegex(@"^([0-9A-Fa-f]{2}[:-]){5}[0-9A-Fa-f]{2}$", RegexOptions.CultureInvariant)]
    private static partial Regex MacPattern();

    [GeneratedRegex(@"^\d{14,16}$", RegexOptions.CultureInvariant)]
    private static partial Regex ImeiPattern();

    public AssetEditValidator(IDateTimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        RuleFor(x => x.AssetName).Must(v => !string.IsNullOrWhiteSpace(v)).WithMessage("Enter the asset name.");
        RuleFor(x => x.AssetCategoryId).Must(v => v > 0).WithMessage("Select a category.");
        RuleFor(x => x.AssetStatusId).Must(v => v > 0).WithMessage("Select a status.");

        MaxLength(x => x.AssetName, 200, "Asset name");
        MaxLength(x => x.ModelName, 150, "Model");
        MaxLength(x => x.SerialNumber, 100, "Serial number");
        MaxLength(x => x.LegacyAssetTag, 50, "Legacy asset tag");
        MaxLength(x => x.Description, 1000, "Description");
        MaxLength(x => x.Hostname, 100, "Hostname");
        MaxLength(x => x.OperatingSystem, 100, "Operating system");
        MaxLength(x => x.OsVersion, 50, "OS version");
        MaxLength(x => x.Processor, 150, "Processor");
        MaxLength(x => x.FirmwareVersion, 50, "Firmware version");
        MaxLength(x => x.PhoneNumber, 30, "Phone number");
        MaxLength(x => x.PurchaseOrderNumber, 50, "PO number");
        MaxLength(x => x.InvoiceNumber, 50, "Invoice number");
        MaxLength(x => x.SecurityNotes, 1000, "Security notes");
        MaxLength(x => x.Notes, 2000, "Notes");

        RuleFor(x => x.Hostname).Must(v => string.IsNullOrEmpty(v) || HostnamePattern().IsMatch(v))
            .WithMessage("Hostname may contain letters, digits, hyphens and dots only (e.g. AMGH-LAP-0142).");
        RuleFor(x => x.IpAddress).Must(v => string.IsNullOrEmpty(v) || IsIpAddress(v))
            .WithMessage("Enter a valid IPv4 or IPv6 address.");
        RuleFor(x => x.MacAddress).Must(v => string.IsNullOrEmpty(v) || MacPattern().IsMatch(v))
            .WithMessage("Enter the MAC address as six pairs, e.g. 3C:52:82:1A:2B:3C.");
        RuleFor(x => x.ImeiNumber).Must(v => string.IsNullOrEmpty(v) || ImeiPattern().IsMatch(v))
            .WithMessage("IMEI must be 14 to 16 digits.");

        RuleFor(x => x.RamGb).Must(v => v is null || (v >= 0 && v <= 9_999_999m)).WithMessage("RAM must be between 0 and 9,999,999 GB.");
        RuleFor(x => x.StorageGb).Must(v => v is null || (v >= 0 && v <= 9_999_999_999m)).WithMessage("Storage must be 0 GB or more.");
        RuleFor(x => x.PurchaseCost).Must(v => v is null || (v >= 0 && v <= 999_999_999_999m)).WithMessage("Purchase cost must be 0 or more.");
        RuleFor(x => x.DepreciationMethod).Must(v => v is null || AssetEditModel.DepreciationMethods.Contains(v)).WithMessage("Choose a depreciation method from the list.");
        RuleFor(x => x.AssetCondition).Must(v => v is null || AssetEditModel.Conditions.Contains(v)).WithMessage("Choose a condition from the list.");
        RuleFor(x => x.FixedAssetGroup).MaximumLength(50);
        RuleFor(x => x.CostCenter).MaximumLength(50);
        RuleFor(x => x.ErpFixedAssetId).MaximumLength(50).Matches("^[A-Za-z0-9._/-]*$").WithMessage("D365 fixed asset ID: letters, digits and . _ / - only.");
        RuleFor(x => x.RecordedBookValue).Must(v => v is null || v >= 0).WithMessage("Book value cannot be negative.");
        RuleFor(x => x.RecordedBookValueDate).Must((m, d) => m.RecordedBookValue is null || d is not null).WithMessage("Enter the date of the recorded book value.");
        RuleFor(x => x.RecordedBookValueDate).Must(d => d is null || d <= DateOnly.FromDateTime(DateTime.UtcNow)).WithMessage("The book value date cannot be in the future.");
        RuleFor(x => x.CapitalizationDate).Must((m, d) => d is null || m.PurchaseDate is null || d >= m.PurchaseDate).WithMessage("The capitalisation date cannot be before the purchase date.");
        RuleFor(x => x.SalvageValue).Must(v => v is null || v >= 0).WithMessage("Salvage value must be 0 or more.");
        RuleFor(x => x.SalvageValue).Must((m, v) => v is null || m.PurchaseCost is null || v <= m.PurchaseCost)
            .WithMessage("Salvage value cannot be higher than the purchase cost.");
        RuleFor(x => x.CurrencyId).Must((m, v) => v is not null || (m.PurchaseCost is null && m.SalvageValue is null))
            .WithMessage("Select the currency of the purchase cost.");
        RuleFor(x => x.UsefulLifeMonths).Must(v => v is null || (v >= 1 && v <= 600)).WithMessage("Useful life must be 1 to 600 months.");

        RuleFor(x => x.PurchaseDate).Must(v => v is null || v <= clock.UtcToday.AddDays(1))
            .WithMessage("Purchase date cannot be in the future.");
        RuleFor(x => x.CommissionDate).Must((m, v) => v is null || m.PurchaseDate is null || v >= m.PurchaseDate)
            .WithMessage("Commission date cannot be before the purchase date.");
        RuleFor(x => x.EndOfLifeDate).Must((m, v) => v is null || m.PurchaseDate is null || v >= m.PurchaseDate)
            .WithMessage("End-of-life date cannot be before the purchase date.");

        RuleFor(x => x.BuildingId).Must((m, v) => v is null || m.LocationId is not null).WithMessage("Select the location of the building.");
        RuleFor(x => x.FloorId).Must((m, v) => v is null || m.BuildingId is not null).WithMessage("Select the building of the floor.");
        RuleFor(x => x.RoomId).Must((m, v) => v is null || m.FloorId is not null).WithMessage("Select the floor of the room.");

        RuleFor(x => x.ClassificationId).Must((m, v) => v is not null || !m.StoresSensitiveData)
            .WithMessage("Assets that store sensitive data must have an information classification.");
    }

    /// <summary>IPv6, or IPv4 written as four numbers ("1.2.3" is technically parseable but is almost always a typo).</summary>
    internal static bool IsIpAddress(string value)
    {
        if (value.Length > 45 || !IPAddress.TryParse(value, out var ip)) return false;
        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6) return true;
        var parts = value.Split('.');
        return parts.Length == 4 && parts.All(p => p.Length is >= 1 and <= 3 && p.All(char.IsAsciiDigit));
    }

    private void MaxLength(Expression<Func<AssetEditModel, string?>> property, int max, string label) =>
        RuleFor(property).Must(v => v is null || v.Length <= max).WithMessage($"{label} must be {max} characters or fewer.");
}
