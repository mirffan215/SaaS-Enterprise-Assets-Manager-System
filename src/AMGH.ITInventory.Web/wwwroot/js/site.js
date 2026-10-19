// EAMS (Enterprise Asset Management System) - site behaviour. No inline scripts: the Content-Security-Policy forbids them.
(function () {
    "use strict";

    // ---- confirmation for destructive forms: <form data-confirm="Question?">
    document.addEventListener("submit", function (e) {
        var form = e.target;
        if (form instanceof HTMLFormElement && form.dataset.confirm && !window.confirm(form.dataset.confirm)) {
            e.preventDefault();
        }
    });

    // ---- copy buttons: <button data-copy-target="#id">
    document.addEventListener("click", function (e) {
        var button = e.target.closest("[data-copy-target]");
        if (!button) return;
        var source = document.querySelector(button.dataset.copyTarget);
        if (!source || !navigator.clipboard) return;
        navigator.clipboard.writeText(source.textContent.trim()).then(function () {
            var original = button.innerHTML;
            button.textContent = "Copied";
            setTimeout(function () { button.innerHTML = original; }, 2000);
        });
    });


    // ---- cascading drop-downs: <select data-parent-select="parentId"> with <option data-parent="id">
    function cascade(child) {
        var parent = document.getElementById(child.dataset.parentSelect);
        if (!parent) return;
        var apply = function (initial) {
            var pv = parent.value;
            Array.prototype.forEach.call(child.options, function (o) {
                if (!o.value) return;
                var show = !pv || o.dataset.parent === pv;
                o.hidden = !show;
                o.disabled = !show;
            });
            var selected = child.options[child.selectedIndex];
            if (!initial && selected && selected.value && selected.disabled) {
                child.value = "";
                child.dispatchEvent(new Event("change"));
            }
        };
        parent.addEventListener("change", function () { apply(false); });
        apply(true);
    }
    document.querySelectorAll("select[data-parent-select]").forEach(cascade);

    // ---- submit a GET form when a select changes: <select data-autosubmit="true">
    document.querySelectorAll("select[data-autosubmit]").forEach(function (s) {
        s.addEventListener("change", function () { s.form.submit(); });
    });

    // ---- tabs: open the first tab with a validation error, or the tab named in ?tab=
    if (window.bootstrap) {
        var errorTab = document.querySelector(".form-tabs .tab-error");
        var named = new URLSearchParams(window.location.search).get("tab");
        var target = errorTab || (named && document.getElementById("tab-" + named));
        if (target) bootstrap.Tab.getOrCreateInstance(target).show();
    }


    // ---- type-to-filter a long drop-down: <input data-filter-select="selectId">
    document.querySelectorAll("input[data-filter-select]").forEach(function (input) {
        var select = document.getElementById(input.dataset.filterSelect);
        if (!select) return;
        input.addEventListener("input", function () {
            var term = input.value.trim().toLowerCase();
            var firstMatch = null;
            Array.prototype.forEach.call(select.options, function (o) {
                if (!o.value) return;
                var show = !term || o.text.toLowerCase().indexOf(term) >= 0;
                o.hidden = !show;
                if (show && !firstMatch) firstMatch = o;
            });
            if (term && firstMatch && (select.selectedIndex <= 0 || select.options[select.selectedIndex].hidden)) select.value = firstMatch.value;
        });
    });

    // ---- show a field only for some values: <select data-toggle-target="id" data-toggle-hide-value="Permanent">
    document.querySelectorAll("select[data-toggle-target]").forEach(function (select) {
        var target = document.getElementById(select.dataset.toggleTarget);
        if (!target) return;
        var apply = function () { target.hidden = select.value === select.dataset.toggleHideValue; };
        select.addEventListener("change", apply);
        apply();
    });

    // ---- row selection for bulk label printing
    var labelForm = document.getElementById("labelForm");
    if (labelForm) {
        var boxes = labelForm.querySelectorAll("input[name='ids']");
        var all = labelForm.querySelector("[data-select-all]");
        var buttons = labelForm.querySelectorAll("[data-requires-selection]");
        var count = labelForm.querySelector("[data-selection-count]");
        var refresh = function () {
            var n = labelForm.querySelectorAll("input[name='ids']:checked").length;
            buttons.forEach(function (b) { b.disabled = n === 0; });
            if (count) count.textContent = n ? n + " selected" : "";
            if (all) all.checked = n > 0 && n === boxes.length;
        };
        labelForm.addEventListener("change", function (e) {
            if (e.target === all) boxes.forEach(function (b) { b.checked = all.checked; });
            refresh();
        });
        refresh();
    }

    // ---- label page: show tag-only options when the asset-tag design is chosen
    var designRadios = document.querySelectorAll("input[data-design-switch]");
    if (designRadios.length) {
        var applyDesign = function () {
            var chosen = document.querySelector("input[data-design-switch]:checked");
            document.querySelectorAll("[data-design-only]").forEach(function (el) {
                el.hidden = !chosen || el.dataset.designOnly !== chosen.value;
            });
        };
        designRadios.forEach(function (r) { r.addEventListener("change", applyDesign); });
        applyDesign();
    }

    // ---- inactivity warning, matched to the server's sliding session timeout
    var body = document.body;
    var minutes = parseInt(body.dataset.sessionMinutes, 10);
    var keepAliveUrl = body.dataset.keepaliveUrl;
    var loginUrl = body.dataset.loginUrl;
    var modalElement = document.getElementById("sessionModal");
    if (!minutes || !keepAliveUrl || !modalElement || !window.bootstrap) return;

    var warnSeconds = 60;
    var modal = new bootstrap.Modal(modalElement);
    var countdown = document.getElementById("sessionCountdown");
    var warnTimer, tickTimer, remaining;

    function schedule() {
        clearTimeout(warnTimer);
        clearInterval(tickTimer);
        warnTimer = setTimeout(warn, Math.max(minutes * 60 - warnSeconds, 30) * 1000);
    }

    function warn() {
        remaining = warnSeconds;
        countdown.textContent = remaining;
        modal.show();
        tickTimer = setInterval(function () {
            remaining -= 1;
            countdown.textContent = remaining;
            if (remaining <= 0) {
                clearInterval(tickTimer);
                window.location.href = loginUrl;
            }
        }, 1000);
    }

    document.getElementById("sessionContinue").addEventListener("click", function () {
        fetch(keepAliveUrl, { credentials: "same-origin", headers: { "X-Requested-With": "XMLHttpRequest" }, cache: "no-store" })
            .then(function (r) {
                if (r.status === 401) { window.location.href = loginUrl; return; }
                modal.hide();
                schedule();
            })
            .catch(function () { window.location.href = loginUrl; });
    });

    schedule();
})();
