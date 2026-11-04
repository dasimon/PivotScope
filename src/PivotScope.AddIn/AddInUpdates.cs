using System.Diagnostics;
using CubeScope.Core.Updates;
using ExcelDna.Integration;
using PivotScope.AddIn.Diagnostics;

namespace PivotScope.AddIn;

/// <summary>
/// PivotScope's update, on CubeScope's (and Lucarne's) core. Excel keeps the .xll and its native
/// DLLs locked as long as it runs, so the add-in cannot replace itself: it finds a newer release,
/// downloads <c>PivotScope.zip</c> and verifies it against <c>PivotScope.zip.sha256</c> and GitHub's
/// digest, then hands the verified zip to the user in their Downloads folder, with the folder of
/// the loaded .xll to unzip it into once Excel is closed.
/// </summary>
/// <remarks>
/// One checker per Excel process, shared by every pane (one per window). Nothing touches the
/// network before a pane asks — the add-in's startup stays free of disk and network — and only a
/// build stamped <c>AssemblyMetadata("UpdateChannel", "release")</c> (release.yml, through
/// pack.ps1 -Channel release) ever asks. The add-in's own assembly is read: Excel has no entry
/// assembly of ours.
/// </remarks>
internal static class AddInUpdates
{
    /// <summary>Every pane asks on opening: one real check per process and per half-day is enough.</summary>
    private static readonly TimeSpan AutomaticInterval = TimeSpan.FromHours(12);

    private static readonly Lazy<UpdateChecker> Checker = new(() =>
        new UpdateChecker(UpdateProduct.PivotScope, UpdateBuild.Read(typeof(AppInfo).Assembly)));

    private static string WorkDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PivotScope", "update");

    internal static UpdateStatus Status => Checker.Value.Status;

    /// <param name="automatic">True for the check a pane makes by itself: skipped when this
    /// process already checked recently. A check the user asks for always goes to GitHub.</param>
    internal static async Task<UpdateStatus> CheckAsync(bool automatic, CancellationToken ct)
    {
        var checker = Checker.Value;
        if (automatic && checker.Status.LastCheckUtc is { } last && DateTime.UtcNow - last < AutomaticInterval)
            return checker.Status;

        var status = await checker.CheckAsync(ct).ConfigureAwait(false);
        if (status.LastError is { } error) FileLog.Write($"Update check failed: {error}");
        else if (status.Available is { } available) FileLog.Write($"Update available: {available.Version}");
        return status;
    }

    /// <summary>Downloads and verifies the available release, copies it to Downloads as
    /// <c>PivotScope-&lt;version&gt;.zip</c> and shows it in Explorer.</summary>
    /// <returns>Where the zip is, and the folder of the .xll Excel loaded (what to replace).</returns>
    internal static async Task<object> DownloadAsync(CancellationToken ct)
    {
        var checker = Checker.Value;
        var download = await checker.DownloadAsync(WorkDirectory, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                "Aucune mise à jour à télécharger, ou une recherche est déjà en cours.");

        string? error = null;
        try
        {
            var (release, file) = download;
            var saved = UpdateDownloads.Save(file, $"PivotScope-{release.Version}.zip");
            FileLog.Write($"Update {release.Version} verified and saved to {saved}.");
            UpdateDownloads.Reveal(saved);
            return new { path = saved, addInFolder = Path.GetDirectoryName(ExcelDnaUtil.XllPath) };
        }
        catch (Exception ex)
        {
            error = ex.Message;
            throw;
        }
        finally
        {
            checker.EndInstall(error);
            DeleteWorkDirectory();
        }
    }

    /// <summary>Opens the release page in the default browser. The address comes from GitHub's
    /// answer: it is opened only if it is an https page on github.com.</summary>
    internal static void OpenNotes()
    {
        if (Checker.Value.Status.Available?.PageUrl is { } page
            && UpdateHosts.IsAllowed(page)
            && page.Host == "github.com")
            Process.Start(new ProcessStartInfo(page.AbsoluteUri) { UseShellExecute = true })?.Dispose();
    }

    private static void DeleteWorkDirectory()
    {
        try
        {
            if (Directory.Exists(WorkDirectory)) Directory.Delete(WorkDirectory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Locked or gone: the next download overwrites it.
        }
    }
}
