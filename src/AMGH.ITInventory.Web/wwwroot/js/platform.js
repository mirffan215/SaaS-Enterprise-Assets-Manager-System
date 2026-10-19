// Company setup wizard and company edit page.
(function () {
    "use strict";

    // ---- colours: picker <-> text field, live preview (CSSOM changes are allowed by the CSP)
    document.querySelectorAll("input[data-colour-for]").forEach(function (picker) {
        var text = document.getElementById(picker.dataset.colourFor);
        picker.addEventListener("input", function () { text.value = picker.value.toUpperCase(); preview(); });
        text.addEventListener("input", function () { if (/^#[0-9a-fA-F]{6}$/.test(text.value)) picker.value = text.value; preview(); });
    });
    function preview() {
        document.querySelectorAll("[data-colour-role]").forEach(function (text) {
            if (!/^#[0-9a-fA-F]{6}$/.test(text.value)) return;
            document.querySelectorAll('[data-preview="' + text.dataset.colourRole + '"]').forEach(function (el) { el.style.backgroundColor = text.value; });
        });
    }
    preview();

    document.querySelectorAll("input[data-uppercase]").forEach(function (i) {
        i.addEventListener("input", function () { var p = i.selectionStart; i.value = i.value.toUpperCase(); i.setSelectionRange(p, p); });
    });

    // ---- administrator: new or existing account
    var modes = document.querySelectorAll("input[data-admin-mode]");
    function applyMode() {
        var chosen = document.querySelector("input[data-admin-mode]:checked");
        document.querySelectorAll("[data-admin-panel]").forEach(function (p) { p.hidden = !chosen || p.dataset.adminPanel !== chosen.value; });
    }
    modes.forEach(function (m) { m.addEventListener("change", applyMode); });
    applyMode();

    // ---- document numbering: live example (same rules as DocumentNumberFormat on the server)
    var docBox = document.querySelector("[data-doc-numbering]");
    if (docBox) {
        var get = function (k) { return docBox.querySelector('[data-doc="' + k + '"]'); };
        var refresh = function () {
            var code = docBox.dataset.companyCode || "XYZ", yearly = get("mode").value === "Yearly";
            var start = parseInt(get("start").value, 10) || 1, digits = parseInt(get("digits").value, 10) || 6;
            var now = new Date(), y = now.getFullYear(), endYear = (start === 1 || (now.getMonth() + 1) < start) ? y : y + 1;
            var label = get("year").value === "FY" ? "FY" + String(endYear % 100).padStart(2, "0") : String(endYear);
            get("sample").textContent = code + "-MOV-" + (yearly ? label + "-" : "") + String(123).padStart(digits, "0");
            get("start").disabled = get("year").disabled = !yearly;
        };
        ["mode", "start", "year", "digits"].forEach(function (k) { get(k).addEventListener("change", refresh); });
        refresh();
        // disabled selects are not posted: re-enable them on submit so the saved values are kept
        var form = docBox.closest("form");
        if (form) form.addEventListener("submit", function () { get("start").disabled = get("year").disabled = false; });
    }

    // (the wizard code below returns early on pages without the wizard, so shared code must stay above it)
    // ---- wizard steps
    var form = document.getElementById("companyWizard");
    if (!form) return;
    var steps = Array.prototype.slice.call(form.querySelectorAll("fieldset[data-step]"));
    var tabs = Array.prototype.slice.call(document.querySelectorAll("[data-step-tab]"));
    var back = form.querySelector("[data-step-back]"), next = form.querySelector("[data-step-next]"), submit = form.querySelector("[data-step-submit]");
    var current = 0;

    // after a failed submit, open the first step that has an error and mark every step with errors
    steps.forEach(function (s, i) { if (s.querySelector(".is-invalid")) tabs[i].classList.add("has-error"); });
    var firstError = steps.findIndex(function (s) { return s.querySelector(".is-invalid"); });
    if (firstError >= 0) current = firstError;

    function show(i) {
        current = i;
        steps.forEach(function (s, k) { s.hidden = k !== i; });
        tabs.forEach(function (t, k) { t.classList.toggle("current", k === i); });
        back.disabled = i === 0;
        next.hidden = i === steps.length - 1;
        submit.hidden = i !== steps.length - 1;
        if (i === steps.length - 1) review();
        var focus = steps[i].querySelector("input:not([type=hidden]):not([type=radio]), select");
        if (focus) focus.focus();
    }
    function stepValid(i) {
        var ok = true;
        steps[i].querySelectorAll("input[required]").forEach(function (input) {
            if (input.closest("[hidden]") && input.closest("[hidden]") !== steps[i]) return;
            var empty = input.value.trim() === "";
            input.classList.toggle("is-invalid", empty);
            if (empty) ok = false;
        });
        return ok;
    }
    function review() {
        var list = document.getElementById("reviewList");
        list.textContent = "";
        form.querySelectorAll("[data-review]").forEach(function (input) {
            if (input.closest("[data-admin-panel]") && input.closest("[data-admin-panel]").hidden) return;
            if (!input.value.trim()) return;
            var dt = document.createElement("dt"); dt.className = "col-sm-4"; dt.textContent = input.dataset.review;
            var dd = document.createElement("dd"); dd.className = "col-sm-8"; dd.textContent = input.value.trim();
            list.appendChild(dt); list.appendChild(dd);
        });
        var logo = document.getElementById("logo");
        var dt2 = document.createElement("dt"); dt2.className = "col-sm-4"; dt2.textContent = "Logo";
        var dd2 = document.createElement("dd"); dd2.className = "col-sm-8"; dd2.textContent = logo && logo.files.length ? logo.files[0].name : "none (company code shown instead)";
        list.appendChild(dt2); list.appendChild(dd2);
    }
    next.addEventListener("click", function () { if (stepValid(current)) show(current + 1); });
    back.addEventListener("click", function () { if (current > 0) show(current - 1); });
    tabs.forEach(function (t, k) { t.addEventListener("click", function () { if (k < current || stepValid(current)) show(k); }); });
    show(current);

})();
