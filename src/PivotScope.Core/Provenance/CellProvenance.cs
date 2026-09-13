using CubeScope.Core.Models;

namespace PivotScope.Core.Provenance;

/// <summary>
/// Everything that can be said about a PivotTable cell.
///
/// <see cref="Note"/> carries the answers that are answers all the same:
/// "this measure is physical, it has no expression", "the cube script could
/// not be read". These are not errors, and the UI must not display them as
/// such.
/// </summary>
public sealed record CellProvenance(
    string Tuple,
    string? Measure,
    IReadOnlyList<string> Coordinates,
    string? Expression,
    int? StartLine,
    DependencyGraph? Dependencies,
    string? Note);
