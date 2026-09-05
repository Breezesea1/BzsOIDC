using Bzs.Blazor;

namespace BzsOIDC.Idp.Client.Services.Theme;

/// <summary>
/// Keeps the requested theme mode in sync across the root provider and
/// preference widgets, including updates received from another browser tab.
/// </summary>
public sealed class ThemePreferenceState
{
    public BzsThemeMode Mode { get; private set; } = BzsThemeMode.System;

    public string RequestedTheme => Mode switch
    {
        BzsThemeMode.Light => "light",
        BzsThemeMode.Dark => "dark",
        _ => "system"
    };

    public event Action? Changed;

    public void SetRequestedTheme(string? theme)
    {
        var nextMode = theme switch
        {
            "light" => BzsThemeMode.Light,
            "dark" => BzsThemeMode.Dark,
            _ => BzsThemeMode.System
        };

        if (Mode == nextMode)
        {
            return;
        }

        Mode = nextMode;
        Changed?.Invoke();
    }
}
