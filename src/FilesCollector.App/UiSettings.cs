namespace FilesCollector.App;

public enum ThemeMode
{
    System,
    Light,
    Dark
}

public enum DensityMode
{
    Comfortable,
    Compact
}

public sealed record UiSettings(ThemeMode Theme, DensityMode Density, bool HasSeenOnboarding);
