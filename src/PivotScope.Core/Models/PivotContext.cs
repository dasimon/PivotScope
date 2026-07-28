namespace PivotScope.Core.Models;

/// <summary>Un champ du TCD. Area ∈ row | column | filter | data.</summary>
public sealed record PivotFieldInfo(string Caption, string UniqueName, string Area);

/// <summary>
/// Photo du tableau croisé dynamique actif, sans aucun type Excel : c'est ce qui
/// permet de la sérialiser vers la SPA et de la tester hors d'Excel.
/// Quand HasPivot ou IsOlap est faux, Diagnostic porte le message à afficher —
/// l'absence de TCD n'est pas une erreur, c'est un état normal du volet.
/// </summary>
public sealed record PivotContext(
    bool HasPivot,
    bool IsOlap,
    string? Server,
    string? Catalog,
    string? Cube,
    string? Mdx,
    IReadOnlyList<PivotFieldInfo> Fields,
    string? Diagnostic)
{
    public static PivotContext None(string diagnostic) =>
        new(false, false, null, null, null, null, [], diagnostic);
}
