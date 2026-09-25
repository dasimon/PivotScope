using System.Diagnostics;
using System.Reflection;

namespace PivotScope.AddIn;

/// <summary>
/// What the "About" section shows. The version is read from the assembly, not
/// written anywhere by hand: it is the one the release set, so it cannot lie
/// about which build Excel actually loaded.
/// </summary>
internal static class AppInfo
{
    internal const string Repository = "https://github.com/dasimon/PivotScope";

    /// <summary>"0.6.1", without the "+commit" suffix the SDK appends.</summary>
    internal static string Version => Informational.Split('+')[0];

    /// <summary>Short commit hash of the build, or null for a build without source control.</summary>
    internal static string? Commit
    {
        get
        {
            var parts = Informational.Split('+');
            return parts.Length > 1 && parts[1].Length > 0 ? parts[1][..Math.Min(7, parts[1].Length)] : null;
        }
    }

    private static string Informational =>
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(AppInfo).Assembly.GetName().Version?.ToString()
        ?? "?";

    /// <summary>
    /// Opens the repository in the default browser. The pane itself never
    /// navigates away from the embedded SPA, and the URL is fixed here: the
    /// SPA cannot ask for any other.
    /// </summary>
    internal static void OpenRepository()
        => Process.Start(new ProcessStartInfo(Repository) { UseShellExecute = true })?.Dispose();
}
