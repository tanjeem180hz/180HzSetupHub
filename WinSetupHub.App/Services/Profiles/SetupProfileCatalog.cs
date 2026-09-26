namespace WinSetupHub.App.Services.Profiles;

public static class SetupProfileCatalog
{
    private static IReadOnlySet<string> EmptyCategories { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<SetupProfile> Profiles { get; } =
    [
        new("Starter Essentials", EmptyCategories, EssentialsOnly: true),
        new("Gaming Setup", CategorySet("Gaming", "Drivers", "Communication", "Media & Streaming")),
        new("Full Stack Developer", CategorySet("Developer", "Databases", "Networking", "Virtualization", "Security", "Drivers", "Browsers")),
        new("Problem Solving & Engineering", CategorySet("Problem Solving", "Developer", "Networking", "Drivers", "Utilities")),
        new("Video Editor / Creator", CategorySet("Video Editing", "Design & Creative", "Media & Streaming", "Communication", "Utilities")),
        new("Game Development", CategorySet("Game Development", "Developer", "Design & Creative", "Gaming", "Drivers")),
        new("Drivers", CategorySet("Drivers")),
        new("Browsers", CategorySet("Browsers")),
        new("Communication", CategorySet("Communication")),
        new("Productivity", CategorySet("Productivity")),
        new("Utilities", CategorySet("Utilities"))
    ];

    public static SetupProfile Default => Profiles[0];

    public static SetupProfile FindByName(string? name)
    {
        return Profiles.FirstOrDefault(profile => string.Equals(profile.Name, name, StringComparison.OrdinalIgnoreCase))
            ?? Default;
    }

    private static IReadOnlySet<string> CategorySet(params string[] categories)
    {
        return new HashSet<string>(categories, StringComparer.OrdinalIgnoreCase);
    }
}
