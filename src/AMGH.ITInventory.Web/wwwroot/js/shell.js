// Application shell: theme switching, collapsible sidebar, global search with suggestions and keyboard support.
(function () {
    "use strict";
    var root = document.documentElement;
    function save(key, value) { try { localStorage.setItem(key, value); } catch (e) { /* private mode */ } }

    // ---- theme
    function applyTheme(choice) {
        var dark = choice === "dark" || (choice === "auto" && window.matchMedia("(prefers-color-scheme: dark)").matches);
        root.setAttribute("data-bs-theme", dark ? "dark" : "light");
        root.setAttribute("data-theme-choice", choice);
        document.querySelectorAll("[data-theme-choice]").forEach(function (b) {
            if (b !== root) b.classList.toggle("active", b.dataset.themeChoice === choice);
        });
    }
    document.querySelectorAll("button[data-theme-choice]").forEach(function (b) {
        b.addEventListener("click", function () {
            save("amgh-theme", b.dataset.themeChoice);
            applyTheme(b.dataset.themeChoice);
            document.dispatchEvent(new CustomEvent("amgh:theme")); // only on a real change (listeners may redraw)
        });
    });
    applyTheme(root.getAttribute("data-theme-choice") || "auto");
    if (window.matchMedia) window.matchMedia("(prefers-color-scheme: dark)").addEventListener("change", function () {
        if ((root.getAttribute("data-theme-choice") || "auto") === "auto") { applyTheme("auto"); document.dispatchEvent(new CustomEvent("amgh:theme")); }
    });

    // ---- sidebar collapse (desktop); on small screens Bootstrap's offcanvas handles the menu
    document.querySelectorAll("[data-sidebar-toggle]").forEach(function (b) {
        b.addEventListener("click", function () {
            var collapsed = root.classList.toggle("sidebar-collapsed");
            save("amgh-sidebar", collapsed ? "collapsed" : "open");
        });
    });

    // ---- asset register column chooser: the ticked boxes become the "cols" value
    var colForm = document.getElementById("columnForm");
    if (colForm) colForm.addEventListener("submit", function () {
        var keys = Array.prototype.map.call(colForm.querySelectorAll("input[data-col-choice]:checked"), function (c) { return c.value; });
        document.getElementById("colsValue").value = keys.join(",");
    });

    // ---- global search: "/" or Ctrl+K focuses it; suggestions as you type; arrows + Enter to open
    var input = document.getElementById("globalSearch");
    if (!input) return;
    var list = document.getElementById("searchSuggestions");
    var timer = null, items = [], active = -1, lastTerm = "";
    document.addEventListener("keydown", function (e) {
        var typing = /input|textarea|select/i.test(document.activeElement.tagName) || document.activeElement.isContentEditable;
        if ((e.key === "/" && !typing) || ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === "k")) { e.preventDefault(); input.focus(); input.select(); }
    });
    function close() { list.hidden = true; input.setAttribute("aria-expanded", "false"); active = -1; }
    function render(data) {
        list.textContent = "";
        items = [];
        if (!data.length) { close(); return; }
        var lastGroup = null;
        data.forEach(function (d) {
            if (d.group !== lastGroup) {
                var h = document.createElement("li"); h.className = "search-group"; h.textContent = d.group; h.setAttribute("role", "presentation");
                list.appendChild(h); lastGroup = d.group;
            }
            var li = document.createElement("li"); li.setAttribute("role", "option"); li.id = "sugg" + items.length;
            var a = document.createElement("a"); a.href = d.url; a.className = "search-hit";
            var t = document.createElement("span"); t.className = "search-title"; t.textContent = d.title;
            var s = document.createElement("span"); s.className = "search-sub"; s.textContent = d.subtitle || "";
            a.appendChild(t); a.appendChild(s); li.appendChild(a); list.appendChild(li); items.push(li);
        });
        list.hidden = false; input.setAttribute("aria-expanded", "true");
    }
    function highlight(i) {
        items.forEach(function (li, k) { li.classList.toggle("active", k === i); });
        active = i;
        if (i >= 0) input.setAttribute("aria-activedescendant", items[i].id); else input.removeAttribute("aria-activedescendant");
    }
    input.addEventListener("input", function () {
        clearTimeout(timer);
        var term = input.value.trim();
        if (term.length < 2) { close(); return; }
        timer = setTimeout(function () {
            if (term === lastTerm) return;
            lastTerm = term;
            fetch(input.dataset.suggestUrl + "?q=" + encodeURIComponent(term), { headers: { "Accept": "application/json" }, credentials: "same-origin" })
                .then(function (r) { return r.ok ? r.json() : []; })
                .then(function (d) { if (input.value.trim() === term) render(d); })
                .catch(function () { /* offline or signed out: plain search still works */ });
        }, 200);
    });
    input.addEventListener("keydown", function (e) {
        if (list.hidden) return;
        if (e.key === "ArrowDown") { e.preventDefault(); highlight(Math.min(items.length - 1, active + 1)); }
        else if (e.key === "ArrowUp") { e.preventDefault(); highlight(Math.max(-1, active - 1)); }
        else if (e.key === "Enter" && active >= 0) { e.preventDefault(); window.location.href = items[active].querySelector("a").href; }
        else if (e.key === "Escape") { close(); }
    });
    document.addEventListener("click", function (e) { if (!e.target.closest(".global-search")) close(); });
})();
