// Sign-in pages (no inline script: the content security policy forbids it).
(function () {
    "use strict";
    // company picker: filter the cards as you type
    var input = document.querySelector("[data-filter-cards]");
    if (input) {
        var list = document.getElementById(input.dataset.filterCards);
        input.addEventListener("input", function () {
            var term = input.value.trim().toLowerCase();
            list.querySelectorAll("[data-card-text]").forEach(function (li) {
                li.hidden = term.length > 0 && li.dataset.cardText.toLowerCase().indexOf(term) < 0;
            });
        });
    }

    // show / hide the password
    document.querySelectorAll("[data-toggle-password]").forEach(function (btn) {
        var field = document.getElementById(btn.dataset.togglePassword);
        if (!field) return;
        btn.addEventListener("click", function () {
            var show = field.type === "password";
            field.type = show ? "text" : "password";
            btn.setAttribute("aria-pressed", show ? "true" : "false");
            btn.setAttribute("aria-label", show ? "Hide password" : "Show password");
            btn.querySelector("i").className = show ? "bi bi-eye-slash" : "bi bi-eye";
            field.focus();
        });
        // never submit while visible (a password manager or shoulder could keep the shown state)
        if (field.form) field.form.addEventListener("submit", function () { field.type = "password"; });
    });

    // focus: the password when the user name is remembered, otherwise the user name
    var user = document.getElementById("Username"), pwd = document.getElementById("Password");
    if (user && pwd && !document.querySelector("[data-filter-cards]")) (user.value ? pwd : user).focus();
})();
