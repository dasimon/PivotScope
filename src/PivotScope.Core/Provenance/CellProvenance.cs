using CubeScope.Core.Models;

namespace PivotScope.Core.Provenance;

/// <summary>
/// Tout ce qu'on peut dire d'une cellule du TCD.
///
/// <see cref="Note"/> porte les réponses qui n'en sont pas moins des réponses :
/// « cette mesure est physique, elle n'a pas d'expression », « le script du cube
/// n'a pas pu être lu ». Ce ne sont pas des erreurs, et l'interface ne doit pas
/// les afficher comme telles.
/// </summary>
public sealed record CellProvenance(
    string Tuple,
    string? Measure,
    IReadOnlyList<string> Coordinates,
    string? Expression,
    int? StartLine,
    DependencyGraph? Dependencies,
    string? Note);
