// Executive dashboard: renders the charts from the JSON data block and links bars/slices to the asset list.
(function () {
    "use strict";
    var printBtn = document.querySelector("[data-print-page]");
    if (printBtn) printBtn.addEventListener("click", function () { window.print(); });

    var el = document.getElementById("dashboardData");
    if (!el || typeof Chart === "undefined") return;
    var data = JSON.parse(el.textContent);
    var css = getComputedStyle(document.documentElement);
    var primary = (css.getPropertyValue("--amgh-teal") || "#13707A").trim();
    var accent = (css.getPropertyValue("--amgh-coral") || "#E8604C").trim();
    Chart.defaults.font.family = getComputedStyle(document.body).fontFamily;
    Chart.defaults.plugins.legend.labels.boxWidth = 12;
    var charts = [];
    // text and grid colours follow the light/dark theme
    function themeColours() {
        var dark = document.documentElement.getAttribute("data-bs-theme") === "dark";
        return { text: dark ? "#C9D6D8" : "#44575B", grid: dark ? "rgba(255,255,255,.08)" : "rgba(0,0,0,.08)" };
    }
    function applyColours(chart) {
        var c = themeColours();
        chart.options.color = c.text;
        Object.values(chart.options.scales || {}).forEach(function (s) {
            s.ticks = Object.assign(s.ticks || {}, { color: c.text });
            s.grid = Object.assign(s.grid || {}, { color: c.grid });
            if (s.title) s.title.color = c.text;
        });
        if (chart.options.plugins && chart.options.plugins.legend) chart.options.plugins.legend.labels.color = c.text;
    }
    document.addEventListener("amgh:theme", function () { charts.forEach(function (ch) { applyColours(ch); ch.update(); }); });

    data.charts.forEach(function (c) {
        var canvas = document.getElementById("chart-" + c.key);
        if (!canvas) return;
        var type = c.kind === "barH" ? "bar" : c.kind;
        var config = {
            type: type,
            data: { labels: c.labels, datasets: [] },
            options: {
                responsive: true, maintainAspectRatio: false, animation: false,
                plugins: { legend: { display: type === "doughnut" || !!c.secondValues, position: type === "doughnut" ? "right" : "top" },
                           tooltip: { callbacks: {} } },
                onHover: function (evt, active) { evt.native.target.style.cursor = active.length && c.drillQueries ? "pointer" : "default"; },
                onClick: function (evt, active) {
                    if (!active.length || !c.drillQueries) return;
                    var q = c.drillQueries[active[0].index];
                    if (q) window.location.href = data.assetsUrl + q;
                }
            }
        };
        if (type === "doughnut") {
            config.data.datasets.push({ data: c.values, backgroundColor: c.colors, borderWidth: 1 });
            config.options.cutout = "58%";
            config.options.plugins.tooltip.callbacks.label = function (ctx) {
                var total = ctx.dataset.data.reduce(function (a, b) { return a + b; }, 0);
                var pct = total ? Math.round(ctx.parsed * 1000 / total) / 10 : 0;
                return " " + ctx.label + ": " + ctx.parsed.toLocaleString() + " (" + pct + "%)";
            };
        } else if (type === "line") {
            config.data.datasets.push({ type: "line", label: "In inventory", data: c.values, borderColor: primary, backgroundColor: primary + "22", fill: true, tension: .25, pointRadius: 3, yAxisID: "y" });
            if (c.secondValues) config.data.datasets.push({ type: "bar", label: c.secondLabel, data: c.secondValues, backgroundColor: accent + "AA", yAxisID: "y1" });
            config.options.scales = { y: { beginAtZero: true, title: { display: true, text: "In inventory" } },
                                      y1: { beginAtZero: true, position: "right", grid: { drawOnChartArea: false }, title: { display: true, text: "Acquired" } } };
        } else {
            config.data.datasets.push({ label: c.title, data: c.values, backgroundColor: c.colors || primary, borderRadius: 3, maxBarThickness: 42 });
            config.options.indexAxis = c.kind === "barH" ? "y" : "x";
            config.options.scales = { x: { beginAtZero: true }, y: { beginAtZero: true } };
        }
        var chart = new Chart(canvas, config);
        applyColours(chart);
        chart.update();
        charts.push(chart);
    });
})();
