using System.ComponentModel.DataAnnotations;

namespace AMGH.ITInventory.Web.Models;

public sealed class TenantSelectViewModel
{
    [Required(ErrorMessage = "Enter your company code.")]
    [StringLength(10)]
    public string? Code { get; set; }
}
