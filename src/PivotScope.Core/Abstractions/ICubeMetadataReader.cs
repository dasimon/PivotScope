using CubeScope.Core.Models;

namespace PivotScope.Core.Abstractions;

/// <summary>
/// Lecture des métadonnées du cube courant. Implémentée par un adaptateur vers
/// CubeScope.Core : le mécanisme de partage reste ainsi remplaçable sans toucher
/// au code des fonctionnalités.
/// </summary>
public interface ICubeMetadataReader
{
    /// <summary>Dimensions, hiérarchies, niveaux et mesures du cube.</summary>
    Task<CubeMeta> GetCubeMetaAsync(string cube, CancellationToken ct = default);

    /// <summary>Membres d'une hiérarchie, plafonnés (chargement paresseux).</summary>
    Task<IReadOnlyList<MemberMeta>> GetMembersAsync(
        string cube, string hierarchyUniqueName, int limit = 1000, CancellationToken ct = default);
}
