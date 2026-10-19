// Permission matrix: "all" box per module, kept in step with the module's boxes.
(function () {
    "use strict";
    document.querySelectorAll("[data-module-all]").forEach(function (all) {
        var key = all.dataset.moduleAll;
        var boxes = Array.prototype.slice.call(document.querySelectorAll('input[data-module="' + key + '"]'));
        function sync() { var n = boxes.filter(function (b) { return b.checked; }).length; all.checked = n === boxes.length; all.indeterminate = n > 0 && n < boxes.length; }
        all.addEventListener("change", function () { boxes.forEach(function (b) { b.checked = all.checked; }); sync(); });
        boxes.forEach(function (b) { b.addEventListener("change", sync); });
        sync();
    });
})();
