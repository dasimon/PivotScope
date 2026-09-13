using System.Text;
using PivotScope.Core.Models;

namespace PivotScope.Core.Ai;

/// <summary>
/// Formats the PivotTable state for the prompt.
///
/// This is the only context CubeScope cannot provide, and it is what makes
/// the assistant relevant here: knowing which fields are on rows, on columns
/// and in filters completely changes the explanation of a query or the
/// diagnosis of a slowdown.
/// </summary>
public static class PivotAiContext
{
    public static string Describe(PivotContext? context)
    {
        if (context is not { HasPivot: true, IsOlap: true }) return string.Empty;

        var text = new StringBuilder("Contexte du tableau croisé dynamique :\n");
        if (context.Cube is not null) text.Append("- Cube : ").Append(context.Cube).Append('\n');

        AppendArea(text, context, "row", "Champs en ligne");
        AppendArea(text, context, "column", "Champs en colonne");
        AppendArea(text, context, "filter", "Filtres de rapport");
        AppendArea(text, context, "data", "Mesures affichées");

        if (context.Fields.Count == 0)
            text.Append("- Aucun champ posé : le tableau est vide.\n");

        return text.ToString();
    }

    private static void AppendArea(
        StringBuilder text, PivotContext context, string area, string label)
    {
        var fields = context.Fields.Where(f => f.Area == area).ToList();
        if (fields.Count == 0) return;

        text.Append("- ").Append(label).Append(" : ")
            .AppendJoin(", ", fields.Select(f => f.Caption))
            .Append('\n');
    }
}
