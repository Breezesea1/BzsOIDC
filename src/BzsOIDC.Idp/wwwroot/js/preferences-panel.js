const THEME_COOKIE = "bzs-theme";
const MEDIA_DARK = "(prefers-color-scheme: dark)";

function getCookie(name) {
    const match = document.cookie
        .split("; ")
        .find((row) => row.startsWith(name + "="));

    if (!match) {
        return null;
    }

    try {
        return decodeURIComponent(match.substring(name.length + 1));
    } catch {
        return null;
    }
}

export function getThemeCookie() {
    return getCookie(THEME_COOKIE);
}

export function applyTheme(theme) {
    let resolved = theme;

    if (theme === "system") {
        resolved = window.matchMedia(MEDIA_DARK).matches ? "dark" : "light";
    }

    const root = document.documentElement;
    root.setAttribute("data-theme", resolved);
    root.setAttribute("data-bzs-theme", resolved);
    root.style.colorScheme = resolved;
}

export function init(widgetElement, dotNetRef) {
    if (!widgetElement) {
        return;
    }

    const onClickOutside = (event) => {
        if (!widgetElement.contains(event.target)) {
            dotNetRef.invokeMethodAsync("ClosePanel");
        }
    };

    document.addEventListener("click", onClickOutside, { passive: true });

    widgetElement._prefCleanup = () => {
        document.removeEventListener("click", onClickOutside);
    };
}

export function dispose(widgetElement) {
    if (widgetElement && typeof widgetElement._prefCleanup === "function") {
        widgetElement._prefCleanup();
        delete widgetElement._prefCleanup;
    }
}
