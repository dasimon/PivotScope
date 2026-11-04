using System.Reflection;
using CubeScope.Core.Updates;

namespace PivotScope.Core.Tests;

/// <summary>
/// The update stamp and the checksum are build conventions, not code: without them the shipped
/// add-in never looks for an update, and nothing would say so. These tests hold them in place.
/// </summary>
public sealed class UpdateConventionTests
{
    [Fact]
    public void The_release_workflow_packs_on_the_release_channel_and_ships_the_checksum()
    {
        var root = RepositoryRoot();
        var workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "release.yml"));
        var pack = File.ReadAllText(Path.Combine(root, "build", "pack.ps1"));
        var addIn = File.ReadAllText(Path.Combine(root, "src", "PivotScope.AddIn", "PivotScope.AddIn.csproj"));

        Assert.Contains("pack.ps1 -Version ${{ steps.version.outputs.value }} -Channel release", workflow, StringComparison.Ordinal);
        Assert.Contains("artifacts/PivotScope.zip.sha256", workflow, StringComparison.Ordinal);
        Assert.Contains("-p:UpdateChannel=$Channel", pack, StringComparison.Ordinal);
        Assert.Contains("$hash  PivotScope.zip", pack, StringComparison.Ordinal);
        Assert.Contains("<AssemblyMetadata Include=\"UpdateChannel\" Value=\"$(UpdateChannel)\"", addIn, StringComparison.Ordinal);
    }

    [Fact]
    public void CI_builds_plain_add_ins()
    {
        // A channel in CI would let a CI artefact look for updates.
        var ci = File.ReadAllText(Path.Combine(RepositoryRoot(), ".github", "workflows", "ci.yml"));

        Assert.DoesNotContain("UpdateChannel", ci, StringComparison.Ordinal);
        Assert.DoesNotContain("-Channel", ci, StringComparison.Ordinal);
    }

    [Fact]
    public void The_names_match_what_pack_ps1_produces()
    {
        // The release assets are the zip and its listing; the checker looks for exactly these.
        Assert.Equal("dasimon/PivotScope", UpdateProduct.PivotScope.Repository);
        Assert.Equal("PivotScope.zip", UpdateProduct.PivotScope.AssetName);
        Assert.Equal("PivotScope.zip.sha256", UpdateProduct.PivotScope.SumsName);
    }

    [Fact]
    public void A_test_build_carries_no_channel()
    {
        Assert.False(UpdateBuild.Read(typeof(UpdateConventionTests).Assembly).CanUpdate);
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PivotScope.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
