// E-mail settings: show only the fields the chosen sending method needs, and offer its standard server settings.
(function () {
    "use strict";
    var mode = document.getElementById("Mail_Mode");
    if (!mode) return;
    function apply(fillDefaults) {
        var o = mode.options[mode.selectedIndex].dataset;
        document.querySelectorAll(".needs-user").forEach(function (e) { e.hidden = o.user !== "True" && mode.value !== "CustomSmtp"; });
        document.querySelectorAll(".needs-secret").forEach(function (e) { e.hidden = o.secret !== "True" && mode.value !== "CustomSmtp"; });
        document.querySelectorAll(".needs-oauth").forEach(function (e) { e.hidden = o.oauth !== "True"; });
        document.querySelectorAll(".mode-help").forEach(function (e) { e.hidden = e.dataset.for !== mode.value; });
        var label = document.querySelector(".secret-label");
        if (label) label.textContent = o.oauth === "True" ? "Client secret" : "Password";
        if (fillDefaults && o.host) {
            document.getElementById("Mail_Host").value = o.host;
            document.getElementById("Mail_Port").value = o.port;
            document.getElementById("Mail_Security").value = o.security;
        }
    }
    mode.addEventListener("change", function () { apply(true); });
    apply(false);
})();
