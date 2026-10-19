// Physical verification: camera scanning with the browser's built-in BarcodeDetector (Chrome/Edge on Android, no library),
// "Record finding" posts the chosen finding as the result, and the progress bar width is set without inline styles.
(function () {
    "use strict";
    var bar = document.querySelector(".progress-bar[data-width]");
    if (bar) bar.style.width = bar.dataset.width + "%";

    var other = document.querySelector("[data-submit-other]");
    if (other) other.addEventListener("click", function () {
        var form = other.closest("form"), sel = form.querySelector("select[name=ResultOther]");
        var hidden = document.createElement("input"); hidden.type = "hidden"; hidden.name = "Result"; hidden.value = sel.value;
        form.appendChild(hidden);
    });

    var btn = document.getElementById("cameraBtn"), video = document.getElementById("cameraView");
    if (!btn || !("BarcodeDetector" in window) || !navigator.mediaDevices) return; // handheld scanners and typing still work
    btn.hidden = false;
    var stream = null, running = false;
    btn.addEventListener("click", async function () {
        if (running) { stop(); return; }
        try {
            var detector = new BarcodeDetector({ formats: ["qr_code", "code_128"] });
            stream = await navigator.mediaDevices.getUserMedia({ video: { facingMode: "environment" } });
            video.srcObject = stream; video.hidden = false; document.getElementById("cameraHint").hidden = false;
            await video.play(); running = true; btn.textContent = "Stop camera";
            (async function loop() {
                if (!running) return;
                try {
                    var codes = await detector.detect(video);
                    if (codes.length) {
                        document.getElementById("scan").value = codes[0].rawValue;
                        document.getElementById("cameraFlag").value = "true";
                        stop(); document.getElementById("scanForm").submit(); return;
                    }
                } catch (e) { /* frame not ready */ }
                setTimeout(loop, 250);
            })();
        } catch (e) { alert("The camera could not be started: " + e.message); stop(); }
    });
    function stop() {
        running = false; btn.textContent = "Camera";
        if (stream) stream.getTracks().forEach(function (t) { t.stop(); });
        video.hidden = true; document.getElementById("cameraHint").hidden = true;
    }
})();
