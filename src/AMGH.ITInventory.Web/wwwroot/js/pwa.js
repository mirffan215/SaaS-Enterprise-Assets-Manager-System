// The mobile app (installable web app): registers the service worker and offers "Install app".
//   [data-pwa-card]     the whole "Get the mobile app" box (hidden once the app is installed or already running as an app)
//   [data-pwa-install]  button, shown when the browser can install the app with one tap (Android, desktop Chrome/Edge)
//   [data-pwa-ios]      instructions, shown on iPhone/iPad Safari (Share, Add to Home Screen)
//   [data-pwa-other]    general hint, shown when neither of the above applies
(function () {
    "use strict";

    var standalone = window.matchMedia("(display-mode: standalone)").matches || window.navigator.standalone === true;
    var ua = window.navigator.userAgent || "";
    var isIos = /iphone|ipad|ipod/i.test(ua) || (/macintosh/i.test(ua) && window.navigator.maxTouchPoints > 1);
    var deferredPrompt = null;

    function each(selector, fn) { Array.prototype.forEach.call(document.querySelectorAll(selector), fn); }
    function show(selector, visible) { each(selector, function (el) { el.hidden = !visible; }); }

    function refresh() {
        show("[data-pwa-card]", !standalone);
        if (standalone) { return; }
        show("[data-pwa-install]", deferredPrompt !== null);
        show("[data-pwa-ios]", isIos && deferredPrompt === null);
        show("[data-pwa-other]", !isIos && deferredPrompt === null);
    }

    if ("serviceWorker" in navigator && window.isSecureContext) {
        window.addEventListener("load", function () { navigator.serviceWorker.register("/sw.js").catch(function () { /* the app still works as a normal page */ }); });
    }

    window.addEventListener("beforeinstallprompt", function (event) {
        event.preventDefault();
        deferredPrompt = event;
        refresh();
    });
    window.addEventListener("appinstalled", function () { deferredPrompt = null; standalone = true; refresh(); });

    each("[data-pwa-install]", function (button) {
        button.addEventListener("click", function () {
            if (!deferredPrompt) { return; }
            deferredPrompt.prompt();
            deferredPrompt.userChoice.then(function () { deferredPrompt = null; refresh(); });
        });
    });

    refresh();
})();
