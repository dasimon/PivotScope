using CubeScope.Core.Models;

namespace PivotScope.Core.Abstractions;

/// <summary>Lecture du MDX Script d'un cube. Implémenté par un adaptateur CubeScope.</summary>
public interface IScriptReader
{
    Task<CubeScript> GetScriptAsync(string cube, CancellationToken ct = default);
}
