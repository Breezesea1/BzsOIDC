const channel = typeof BroadcastChannel === "function" ? new BroadcastChannel("bzsoidc-preferences") : null;

export function publishPreference(kind, value) {
    const message = JSON.stringify({ kind, value });
    channel?.postMessage({ kind, value });
    try {
        localStorage.setItem("bzsoidc-preference", message);
        localStorage.removeItem("bzsoidc-preference");
    } catch { }
}

export function subscribePreferences(dotNetRef) {
    const notify = data => data?.kind && data?.value && dotNetRef.invokeMethodAsync("OnPreferenceChanged", data.kind, data.value);
    const onMessage = event => notify(event.data);
    const onStorage = event => {
        if (event.key === "bzsoidc-preference" && event.newValue) {
            try { notify(JSON.parse(event.newValue)); } catch { }
        }
    };
    channel?.addEventListener("message", onMessage);
    window.addEventListener("storage", onStorage);
    return { dispose: () => { channel?.removeEventListener("message", onMessage); window.removeEventListener("storage", onStorage); } };
}

const sessionChannel = typeof BroadcastChannel === "function" ? new BroadcastChannel("bzsoidc-session") : null;

export function publishSession(value) {
    const message = JSON.stringify({ kind: "session", value });
    sessionChannel?.postMessage({ kind: "session", value });
    try {
        localStorage.setItem("bzsoidc-session", message);
        localStorage.removeItem("bzsoidc-session");
    } catch { }
}

export function subscribeSession(dotNetRef) {
    const notify = data => data?.kind === "session" && data?.value && dotNetRef.invokeMethodAsync("OnSessionChanged", data.value);
    const onMessage = event => notify(event.data);
    const onStorage = event => {
        if (event.key === "bzsoidc-session" && event.newValue) {
            try { notify(JSON.parse(event.newValue)); } catch { }
        }
    };
    const onVisibility = () => {
        if (document.visibilityState === "visible") {
            dotNetRef.invokeMethodAsync("OnVisibilityChanged");
        }
    };
    sessionChannel?.addEventListener("message", onMessage);
    window.addEventListener("storage", onStorage);
    document.addEventListener("visibilitychange", onVisibility);
    return { dispose: () => { sessionChannel?.removeEventListener("message", onMessage); window.removeEventListener("storage", onStorage); document.removeEventListener("visibilitychange", onVisibility); } };
}
