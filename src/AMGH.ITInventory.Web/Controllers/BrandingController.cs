using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AMGH.ITInventory.Security.Platform;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMGH.ITInventory.Web.Controllers;

/// <summary>
/// Public company branding: a generated stylesheet of colour variables and the logo. Public by design
/// (the company picker and sign-in page need them); contains no confidential information.
/// Served from this site, so the strict Content-Security-Policy (no inline styles) stays in force.
/// </summary>
[AllowAnonymous]
[Route("branding")]
public sealed partial class BrandingController : Controller
{
    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")] private static partial Regex Hex();

    private readonly ICompanyDirectory _directory;

    public BrandingController(ICompanyDirectory directory) => _directory = directory;

    [HttpGet("{code}.css")]
    [ResponseCache(Duration = 300, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> Stylesheet(string code, CancellationToken cancellationToken)
    {
        var b = await _directory.GetBrandingAsync(code, cancellationToken);
        if (b is null || !Hex().IsMatch(b.HeaderColor) || !Hex().IsMatch(b.PrimaryColor) || !Hex().IsMatch(b.AccentColor))
            return Content("/* default branding */", "text/css");
        var (r, g, bl) = Rgb(b.PrimaryColor);
        var css = new StringBuilder();
        css.Append(CultureInfo.InvariantCulture, $":root {{ --amgh-deep: {b.HeaderColor}; --amgh-teal: {b.PrimaryColor}; --amgh-teal-dark: {Darken(b.PrimaryColor)}; ");
        css.Append(CultureInfo.InvariantCulture, $"--amgh-coral: {b.AccentColor}; --bs-primary: {b.PrimaryColor}; --bs-primary-rgb: {r}, {g}, {bl}; ");
        css.Append(CultureInfo.InvariantCulture, $"--bs-link-color: {b.PrimaryColor}; --bs-link-color-rgb: {r}, {g}, {bl}; --bs-link-hover-color: {Darken(b.PrimaryColor)}; ");
        css.Append(CultureInfo.InvariantCulture, $"--bs-focus-ring-color: rgba({r}, {g}, {bl}, .35); --lp-brand: {b.PrimaryColor}; }}");
        return Content(css.ToString(), "text/css");
    }

    [HttpGet("{code}/logo")]
    [ResponseCache(Duration = 300, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> Logo(string code, CancellationToken cancellationToken)
    {
        var logo = await _directory.GetLogoAsync(code, cancellationToken);
        if (logo is null) return NotFound();
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(logo.Content, logo.ContentType);
    }

    private static (int R, int G, int B) Rgb(string hex) =>
        (int.Parse(hex.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
         int.Parse(hex.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
         int.Parse(hex.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));

    private static string Darken(string hex)
    {
        var (r, g, b) = Rgb(hex);
        return string.Create(CultureInfo.InvariantCulture, $"#{(int)(r * .8):X2}{(int)(g * .8):X2}{(int)(b * .8):X2}");
    }
}
