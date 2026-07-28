using System.Text;
using PivotScope.Core.Models;

namespace PivotScope.Core.Ai;

/// <summary>
/// Met l'état du tableau croisé dynamique en forme pour le prompt.
///
/// C'est le seul contexte que CubeScope ne peut pas fournir, et c'est ce qui
/// rend l'assistant pertinent ici : savoir quels champs sont en ligne, en
/// colonne et en filtre change complètement l'explication d'une requête ou le
/// diagnostic d'une lenteur.
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
