using System.Text;
using System.Text.Json;
using AMGH.ITInventory.Shared;
using AMGH.ITInventory.Shared.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMGH.ITInventory.Web.Controllers;

/// <summary>
/// The mobile app. EAMS is installed from the browser as a Progressive Web App on iPhone/iPad (Safari: Share, Add to Home Screen) and on Android
/// (Chrome: Install app): own icon, full-screen, camera scanning, no app-store account needed. This serves the web app manifest and the service worker.
/// </summary>
[AllowAnonymous]
public sealed class PwaController : Controller
{
    private readonly ITenantAccessor _tenants;

    public PwaController(ITenantAccessor tenants) => _tenants = tenants;

    /// <summary>The company's name when the request is inside a company (the installed icon is then named after it), else the product name.</summary>
    [HttpGet("manifest.webmanifest")]
    [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Client)]
    public IActionResult Manifest()
    {
        var name = _tenants.CurrentTenant?.Name is { Length: > 0 } company ? company : AppInfo.Name;
        var shortName = name.Length <= 14 ? name : AppInfo.ShortName.Length <= 14 ? AppInfo.ShortName : "EAMS";
        var manifest = new Dictionary<string, object>
        {
            ["id"] = "/",
            ["name"] = name,
            ["short_name"] = shortName,
            ["description"] = AppInfo.Tagline,
            ["start_url"] = "/",
            ["scope"] = "/",
            ["display"] = "standalone",
            ["orientation"] = "portrait",
            ["background_color"] = "#0E3B43",
            ["theme_color"] = "#0E3B43",
            ["icons"] = new object[]
            {
                new { src = "/images/icons/icon-192.png", sizes = "192x192", type = "image/png", purpose = "any" },
                new { src = "/images/icons/icon-512.png", sizes = "512x512", type = "image/png", purpose = "any" },
                new { src = "/images/icons/icon-maskable-512.png", sizes = "512x512", type = "image/png", purpose = "maskable" }
            },
            ["shortcuts"] = new object[]
            {
                new { name = "Scan an asset", short_name = "Scan", url = "/scan", icons = new[] { new { src = "/images/icons/icon-192.png", sizes = "192x192", type = "image/png" } } }
            }
        };
        return Content(JsonSerializer.Serialize(manifest), "application/manifest+json", Encoding.UTF8);
    }

    /// <summary>
    /// The service worker (it must be served from the site root to cover every page). It keeps the look of the app (styles, scripts, icons) so it opens fast and shows
    /// a friendly page when there is no connection. Data and signed-in pages are never stored on the phone.
    /// </summary>
    [HttpGet("sw.js")]
    public IActionResult ServiceWorker()
    {
        Response.Headers.CacheControl = "no-cache";
        Response.Headers["Service-Worker-Allowed"] = "/";
        var version = JsonSerializer.Serialize(AppInfo.Version + "-" + AppInfo.Build);
        return Content($"const VERSION = {version};\n{ServiceWorkerBody}", "text/javascript", Encoding.UTF8);
    }

    private const string ServiceWorkerBody = """
const SHELL = "eams-shell-" + VERSION;
const STATIC = /^\/(lib|css|js|images)\//;
self.addEventListener("install", function (event) {
    event.waitUntil(caches.open(SHELL).then(function (cache) {
        return cache.addAll(["/offline.html", "/images/icons/icon-192.png", "/images/icons/icon-512.png"]);
    }).then(function () { return self.skipWaiting(); }));
});
self.addEventListener("activate", function (event) {
    event.waitUntil(caches.keys().then(function (names) {
        return Promise.all(names.filter(function (n) { return n.indexOf("eams-shell-") === 0 && n !== SHELL; }).map(function (n) { return caches.delete(n); }));
    }).then(function () { return self.clients.claim(); }));
});
self.addEventListener("fetch", function (event) {
    var request = event.request;
    if (request.method !== "GET") { return; }
    var url = new URL(request.url);
    if (url.origin !== self.location.origin) { return; }
    if (request.mode === "navigate") {
        // pages come from the server (never from the phone's storage); offline there is a short explanation instead of an error
        event.respondWith(fetch(request).catch(function () { return caches.match("/offline.html"); }));
        return;
    }
    if (STATIC.test(url.pathname)) {
        // the look of the app: show the stored copy at once and refresh it in the background
        event.respondWith(caches.open(SHELL).then(function (cache) {
            return cache.match(request).then(function (stored) {
                var fresh = fetch(request).then(function (response) {
                    if (response.ok) { cache.put(request, response.clone()); }
                    return response;
                }).catch(function () { return stored; });
                return stored || fresh;
            });
        }));
    }
});
""";
}
