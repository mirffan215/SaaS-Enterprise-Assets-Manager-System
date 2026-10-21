// A panel marked data-refresh-seconds (for example a backup that is running) reloads the page every so often until the work is done.
(function () {
    "use strict";
    var el = document.querySelector("[data-refresh-seconds]");
    if (!el) { return; }
    var seconds = parseInt(el.getAttribute("data-refresh-seconds"), 10);
    if (seconds >= 5) { window.setTimeout(function () { window.location.reload(); }, seconds * 1000); }
})();
