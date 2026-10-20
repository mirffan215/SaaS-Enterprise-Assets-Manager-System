using AMGH.ITInventory.Application.Assignments;
using AMGH.ITInventory.Application.Labels;
using AMGH.ITInventory.Domain.Enums;

namespace AMGH.ITInventory.Web.Models;

public sealed class LabelOptionsViewModel
{
    public required LabelPreview Preview { get; init; }
    public LabelDesign Design { get; init; } = LabelDesign.QrLabel;
    public string Style { get; init; } = "colour";
    public LabelSize Size { get; init; } = LabelSize.Size70x40;
    public int Copies { get; init; } = 1;
    public string? ReprintReason { get; init; }
    public bool CanReprint { get; init; }
    public bool AnyPrinted(LabelDesign design) => Preview.Assets.Any(a => Preview.WasPrinted(design, a.AssetId));
    public bool AnyPrintedAtAll => AnyPrinted(LabelDesign.QrLabel) || AnyPrinted(LabelDesign.AssetTag) || AnyPrinted(LabelDesign.RfidLabel);
}

public sealed class AssignViewModel
{
    public required AssignmentContext Context { get; init; }
    public AssignForm Form { get; init; } = new();
}

public sealed class ReturnViewModel
{
    public required AssignmentContext Context { get; init; }
    public ReturnForm Form { get; init; } = new();
}
