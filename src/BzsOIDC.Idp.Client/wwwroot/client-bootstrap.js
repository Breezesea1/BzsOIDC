(function () {
    const loading = document.getElementById("blazor-loading");
    const error = document.getElementById("blazor-error-ui");
    const errorMessage = document.getElementById("blazor-error-message");
    const reload = document.getElementById("blazor-reload");
    const recoveryKey = "bzsoidc-wasm-recovery";
    let settled = false;

    function showError(message) {
        settled = true;
        if (loading) {
            loading.hidden = true;
        }

        if (error) {
            error.hidden = false;
        }

        if (errorMessage && message) {
            errorMessage.textContent = message;
        }
    }

    function recover() {
        try {
            if (sessionStorage.getItem(recoveryKey) !== "1") {
                sessionStorage.setItem(recoveryKey, "1");
                location.reload();
                return;
            }
            sessionStorage.removeItem(recoveryKey);
        } catch { }
        showError("BzsOIDC could not start. Reload the page to try again.");
    }

    reload?.addEventListener("click", recover);
    window.addEventListener("error", () => recover(), { once: true });
    window.addEventListener("unhandledrejection", () => recover(), { once: true });

    const timeout = window.setTimeout(() => {
        if (!settled) {
            showError("BzsOIDC is taking longer than expected to start.");
        }
    }, 15000);

    if (!window.Blazor || typeof window.Blazor.start !== "function") {
        window.clearTimeout(timeout);
        showError("BzsOIDC could not load its runtime.");
        return;
    }

    window.Blazor.start()
        .then(function () {
            settled = true;
            window.clearTimeout(timeout);
            try { sessionStorage.removeItem(recoveryKey); } catch { }
            if (loading) {
                loading.hidden = true;
            }
        })
        .catch(() => {
            window.clearTimeout(timeout);
            recover();
        });
})();
