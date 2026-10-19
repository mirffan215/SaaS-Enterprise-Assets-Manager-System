// Operator page: "Show companies" is a second submit button of the create form, which asks for confirmation; skip that question for it.
(function () {
    "use strict";
    document.addEventListener("click", function (e) {
        var button = e.target.closest("[data-no-confirm]");
        var form = button && button.form;
        if (form) { form.dataset.confirmSaved = form.dataset.confirm || ""; form.removeAttribute("data-confirm"); setTimeout(function () { form.dataset.confirm = form.dataset.confirmSaved; }, 0); }
    }, true);
})();
