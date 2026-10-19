// Runs in <head> before the page paints: applies the saved theme (light / dark / auto) and the collapsed sidebar,
// so there is no flash of the wrong theme. Kept tiny and dependency-free.
(function () {
    "use strict";
    var root = document.documentElement;
    function stored(key, fallback) { try { return localStorage.getItem(key) || fallback; } catch (e) { return fallback; } }
    var choice = stored("amgh-theme", "auto");
    var dark = choice === "dark" || (choice === "auto" && window.matchMedia && window.matchMedia("(prefers-color-scheme: dark)").matches);
    root.setAttribute("data-bs-theme", dark ? "dark" : "light");
    root.setAttribute("data-theme-choice", choice);
    if (stored("amgh-sidebar", "open") === "collapsed") root.classList.add("sidebar-collapsed");
})();
