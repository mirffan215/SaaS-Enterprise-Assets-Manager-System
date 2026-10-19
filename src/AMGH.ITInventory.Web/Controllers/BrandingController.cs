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
        Response.Headers.XContentTypeOptions = "nosniff";
        // No uploaded logo: a clean generated badge (inline SVG, initials of the company code) instead of a stock image.
        return logo is null ? BadgeResult(code) : File(logo.Content, logo.ContentType);
    }

    /// <summary>The neutral badge shown when no company context is loaded.</summary>
    [HttpGet("default-logo.svg")]
    [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any)]
    public IActionResult DefaultLogo()
    {
        Response.Headers.XContentTypeOptions = "nosniff";
        return BadgeResult(null);
    }

    private static ContentResult BadgeResult(string? label) => new() { Content = BadgeSvg(label), ContentType = "image/svg+xml" };

    /// <summary>Inline SVG badge. The label is reduced to up to three letters/digits, so nothing user-supplied reaches the markup.</summary>
    internal static string BadgeSvg(string? label)
    {
        var text = new string((label ?? string.Empty).Where(char.IsAsciiLetterOrDigit).Take(3).ToArray()).ToUpperInvariant();
        if (text.Length == 0) text = "IT";
        var size = text.Length == 3 ? 38 : 48;
        return "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 120 120\" role=\"img\" aria-label=\"Company badge\">" +
               "<defs><linearGradient id=\"g\" x1=\"0\" y1=\"0\" x2=\"1\" y2=\"1\"><stop offset=\"0\" stop-color=\"#2E6BD6\"/><stop offset=\"1\" stop-color=\"#123A86\"/></linearGradient></defs>" +
               "<rect x=\"4\" y=\"4\" width=\"112\" height=\"112\" rx=\"26\" fill=\"url(#g)\"/>" +
               "<rect x=\"10\" y=\"10\" width=\"100\" height=\"100\" rx=\"21\" fill=\"none\" stroke=\"#FFFFFF\" stroke-opacity=\".35\" stroke-width=\"2\"/>" +
               $"<text x=\"60\" y=\"60\" text-anchor=\"middle\" dominant-baseline=\"central\" fill=\"#FFFFFF\" font-family=\"Segoe UI, Arial, sans-serif\" font-weight=\"700\" font-size=\"{size}\">{text}</text>" +
               "</svg>";
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
