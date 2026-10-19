// Print pages (labels, hand-over forms): Print button and automatic print dialog for label sheets.
(function () {
    "use strict";
    document.addEventListener("click", function (e) {
        if (e.target.closest("[data-print]")) window.print();
    });
    if (document.body.dataset.autoprint === "true") {
        window.addEventListener("load", function () { setTimeout(function () { window.print(); }, 300); });
    }
})();
