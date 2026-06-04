using MudBlazor;

namespace OSPi.Web.Theme;

/// <summary>
/// App-wide MudBlazor theme. Full dark, built around the existing navy brand colour
/// (<c>#1b1b2f</c>, also the PWA <c>theme-color</c>) used for the app bar and drawer.
/// </summary>
public static class OspiTheme
{
    public static readonly MudTheme Dark = new()
    {
        PaletteDark = new PaletteDark
        {
            Primary = "#4f9eea",       // water blue, legible on dark
            Secondary = "#26a69a",     // teal accent
            Background = "#14141e",
            BackgroundGray = "#1b1b2f",
            Surface = "#1f2133",
            AppbarBackground = "#1b1b2f",
            AppbarText = "#ffffff",
            DrawerBackground = "#181826",
            DrawerText = "#cfd2e1",
            DrawerIcon = "#cfd2e1",
            Success = "#43a047",
            Warning = "#fb8c00",
            Error = "#e53935",
        },
        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = "8px",
        },
    };
}
