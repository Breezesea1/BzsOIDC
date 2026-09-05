// Keep this file as early-boot global helpers.
// Program.cs invokes bzsPreferences.getCultureCookie during WebAssembly startup.
(function () {
    // Resolve the persisted theme before first paint. The preference cookie
    // ("bzs-theme": light | dark | system) wins; without it the OS preference
    // decides. Both data-theme (app CSS, E2E hooks) and data-bzs-theme
    // (Bzs.Blazor component CSS) are kept in sync on <html>.
    var THEME_COOKIE = "bzs-theme";
    var MEDIA_DARK = "(prefers-color-scheme: dark)";

    function readCookie(name) {
        var rows = document.cookie.split("; ");
        for (var i = 0; i < rows.length; i++) {
            if (rows[i].indexOf(name + "=") === 0) {
                try {
                    return decodeURIComponent(rows[i].substring(name.length + 1));
                } catch {
                    return null;
                }
            }
        }

        return null;
    }

    function systemTheme() {
        var mediaDark = window.matchMedia && window.matchMedia(MEDIA_DARK);
        return mediaDark && mediaDark.matches ? "dark" : "light";
    }

    function resolveTheme() {
        var stored = readCookie(THEME_COOKIE);
        if (stored === "light" || stored === "dark") {
            return stored;
        }

        return systemTheme();
    }

    function applyResolvedTheme(resolved) {
        var root = document.documentElement;
        root.setAttribute("data-theme", resolved);
        root.setAttribute("data-bzs-theme", resolved);
        root.style.colorScheme = resolved;
    }

    applyResolvedTheme(resolveTheme());

    // Follow OS theme changes live while the user preference is "system".
    var media = window.matchMedia ? window.matchMedia(MEDIA_DARK) : null;
    if (media && typeof media.addEventListener === "function") {
        media.addEventListener("change", function () {
            var stored = readCookie(THEME_COOKIE);
            if (stored !== "light" && stored !== "dark") {
                applyResolvedTheme(systemTheme());
            }
        });
    }

    if (!window.bzsPreferences) {
        window.bzsPreferences = {};
    }

    window.bzsPreferences.getCultureCookie = function () {
        return readCookie(".AspNetCore.Culture");
    };

    // App.razor reads this synchronously at first render so the provider can
    // continue observing the OS when the requested preference is "system".
    window.bzsPreferences.getThemePreference = function () {
        var stored = readCookie(THEME_COOKIE);
        return stored === "light" || stored === "dark" ? stored : "system";
    };

    // Keep this helper for callers that only need the currently painted theme.
    window.bzsPreferences.getEffectiveTheme = function () {
        return document.documentElement.getAttribute("data-theme") || "light";
    };
})();

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
    window.addEventListener("error", recover, { once: true });
    window.addEventListener("unhandledrejection", recover, { once: true });

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
