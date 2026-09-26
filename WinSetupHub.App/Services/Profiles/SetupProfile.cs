using WinSetupHub.App.Models;

namespace WinSetupHub.App.Services.Profiles;

public sealed record SetupProfile(
    string Name,
    IReadOnlySet<string> Categories,
    bool EssentialsOnly = false)
{
    public bool Matches(PackageDefinition package)
    {
        return EssentialsOnly
            ? package.Essential
            : Categories.Contains(package.Category);
    }
}
