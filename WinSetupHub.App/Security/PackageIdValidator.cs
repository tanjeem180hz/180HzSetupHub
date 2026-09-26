using System.Text.RegularExpressions;

namespace WinSetupHub.App.Security;

public static class PackageIdValidator
{
    private static readonly Regex SafePackageIdPattern = new(
        @"^[A-Za-z0-9][A-Za-z0-9._+-]{1,127}$",
        RegexOptions.Compiled);

    public static bool IsSafe(string packageId)
    {
        return SafePackageIdPattern.IsMatch(packageId);
    }

    public static void ThrowIfUnsafe(string packageId)
    {
        if (!IsSafe(packageId))
        {
            throw new InvalidOperationException($"Package ID is not allowed: {packageId}");
        }
    }
}
