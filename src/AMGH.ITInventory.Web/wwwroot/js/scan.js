// Scan an asset with the phone's camera. Reads the QR code of an EAMS label, a barcode (asset tag) or a typed number, and opens the asset.
// Uses the browser's BarcodeDetector where it exists (Chrome, Android) and the bundled jsQR (QR codes) everywhere else, iPhone included.
(function () {
    "use strict";

    var video = document.getElementById("scan-video");
    var startButton = document.getElementById("scan-start");
    var stopButton = document.getElementById("scan-stop");
    var status = document.getElementById("scan-status");
    var form = document.getElementById("scan-form");
    var input = document.getElementById("scan-input");
    var canvas = document.createElement("canvas");
    var stream = null;
    var timer = null;
    var detector = null;
    var handled = false;

    function say(text) { if (status) { status.textContent = text; } }

    /** What the scan means: an EAMS link (/a/NUMBER), an RFID EPC (24 hex) or an asset number. Returns the path to open, or null. */
    function target(text) {
        var t = (text || "").trim();
        if (!t) { return null; }
        var link = t.match(/\/a\/([A-Za-z0-9._-]{2,40})(?:[?#\s]|$)/);   // the QR label starts with the asset number, then a link
        if (link) { return "/a/" + encodeURIComponent(link[1]); }
        var epcLink = t.match(/\/r\/([0-9A-Fa-f]{24})(?:[?#\s]|$)/);
        if (epcLink) { return "/r/" + epcLink[1].toUpperCase(); }
        if (/^[0-9A-Fa-f]{24}$/.test(t)) { return "/r/" + t.toUpperCase(); }
        if (/^[A-Za-z0-9][A-Za-z0-9._-]{2,39}$/.test(t)) { return "/a/" + encodeURIComponent(t.toUpperCase()); }
        return null;
    }

    function open(text) {
        var path = target(text);
        if (!path) { say("That code is not an EAMS asset label."); return false; }
        handled = true;
        stop();
        say("Opening " + decodeURIComponent(path.split("/").pop()) + "…");
        window.location.href = path;
        return true;
    }

    function stop() {
        if (timer) { window.clearInterval(timer); timer = null; }
        if (stream) { stream.getTracks().forEach(function (t) { t.stop(); }); stream = null; }
        if (video) { video.srcObject = null; video.hidden = true; }
        if (startButton) { startButton.hidden = false; }
        if (stopButton) { stopButton.hidden = true; }
    }

    async function decodeFrame() {
        if (handled || !video || video.readyState < 2) { return; }
        try {
            if (detector) {
                var codes = await detector.detect(video);
                if (codes.length > 0) { open(codes[0].rawValue); }
                return;
            }
            if (typeof window.jsQR === "function") {
                var w = video.videoWidth, h = video.videoHeight;
                if (!w || !h) { return; }
                canvas.width = w; canvas.height = h;
                var context = canvas.getContext("2d", { willReadFrequently: true });
                context.drawImage(video, 0, 0, w, h);
                var result = window.jsQR(context.getImageData(0, 0, w, h).data, w, h, { inversionAttempts: "dontInvert" });
                if (result && result.data) { open(result.data); }
            }
        } catch (e) { /* a frame that cannot be read: try the next one */ }
    }

    async function start() {
        if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) {
            say("This browser cannot use the camera here (it needs a secure https address). Type the asset number below instead.");
            return;
        }
        handled = false;
        try {
            if ("BarcodeDetector" in window) { detector = new window.BarcodeDetector({ formats: ["qr_code", "code_128", "code_39", "ean_13"] }); }
            stream = await navigator.mediaDevices.getUserMedia({ video: { facingMode: { ideal: "environment" } }, audio: false });
            video.srcObject = stream;
            video.hidden = false;
            await video.play();
            startButton.hidden = true;
            stopButton.hidden = false;
            say("Point the camera at the label.");
            timer = window.setInterval(decodeFrame, detector ? 250 : 200);
        } catch (e) {
            stop();
            say(e && e.name === "NotAllowedError" ? "Camera access was refused. Allow it in the browser's settings, or type the asset number below." : "The camera could not be started. Type the asset number below instead.");
        }
    }

    if (startButton) { startButton.addEventListener("click", start); }
    if (stopButton) { stopButton.addEventListener("click", function () { stop(); say(""); }); }
    if (form) {
        form.addEventListener("submit", function (event) {
            event.preventDefault();
            open(input.value);
        });
    }
    window.addEventListener("pagehide", stop);
    window.EamsScan = { target: target };   // for tests and other pages
})();
