using CubeScope.Core.Models;

namespace PivotScope.Core.Abstractions;

/// <summary>
/// Reads the metadata of the current cube. Implemented by an adapter over
/// CubeScope.Core, so the sharing mechanism stays replaceable without touching
/// feature code.
/// </summary>
public interface ICubeMetadataReader
{
    /// <summary>Dimensions, hierarchies, levels and measures of the cube.</summary>
    Task<CubeMeta> GetCubeMetaAsync(string cube, CancellationToken ct = default);

    /// <summary>Members of a hierarchy, capped (lazy loading).</summary>
    Task<IReadOnlyList<MemberMeta>> GetMembersAsync(
        string cube, string hierarchyUniqueName, int limit = 1000, CancellationToken ct = default);
}
