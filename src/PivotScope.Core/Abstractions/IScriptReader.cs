using CubeScope.Core.Models;

namespace PivotScope.Core.Abstractions;

/// <summary>Reads the MDX Script of a cube. Implemented by a CubeScope adapter.</summary>
public interface IScriptReader
{
    Task<CubeScript> GetScriptAsync(string cube, CancellationToken ct = default);
}
