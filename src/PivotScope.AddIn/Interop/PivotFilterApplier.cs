using System.Text;
using ExcelDna.Integration;
using PivotScope.AddIn.Diagnostics;
using Xl = Microsoft.Office.Interop.Excel;

namespace PivotScope.AddIn.Interop;

/// <summary>
/// Applique un filtre manuel inclusif sur un niveau d'un champ du TCD.
///
/// Trois pièges, tous silencieux ou trompeurs si on les ignore :
/// 1. ClearManualFilter doit être appelé sur le CubeField ; sur un PivotField
///    en OLAP, il lève une erreur d'exécution (documenté).
/// 2. Si IncludeNewItemsInFilter vaut True, VisibleItemsList « reste vide et
///    n'accepte aucun élément » (documenté) : l'affectation ne fait rien.
/// 3. Un CubeField de hiérarchie expose UN PivotField PAR NIVEAU. Écrire des
///    noms uniques du niveau « Magasin » dans le PivotField du niveau
///    « Etablissement » fait répondre à Excel « Élément introuvable dans le
///    cube OLAP » — constaté sur un cube réel. Il faut viser le bon niveau.
///
/// À appeler exclusivement via <see cref="ExcelThread"/>.
/// </summary>
public static class PivotFilterApplier
{
    public static void Apply(
        string cubeFieldName, string levelUniqueName, IReadOnlyList<string> uniqueNames)
    {
        if (uniqueNames.Count == 0)
            throw new InvalidOperationException(
                "Aucune valeur n'a pu être résolue en membre du cube : le filtre n'a pas " +
                "été appliqué. Vérifiez le niveau choisi — les clés et les libellés sont " +
                "acceptés, mais ils doivent appartenir à ce niveau-là.");

        var app = (Xl.Application)ExcelDnaUtil.Application;

        Xl.PivotTable? pivot = null;
        try { pivot = app.ActiveCell?.PivotTable; } catch { /* hors TCD */ }

        if (pivot is null)
            throw new InvalidOperationException(
                "Placez le curseur dans un tableau croisé dynamique.");

        var field = FindCubeField(pivot, cubeFieldName)
            ?? throw new InvalidOperationException(
                $"Champ introuvable dans le tableau croisé dynamique : {cubeFieldName}");

        var pivotField = FindPivotFieldForLevel(field, levelUniqueName);

        field.ClearManualFilter();
        field.IncludeNewItemsInFilter = false;
        pivotField.VisibleItemsList = uniqueNames.ToArray();
    }

    private static Xl.CubeField? FindCubeField(Xl.PivotTable pivot, string name)
    {
        foreach (Xl.CubeField cf in pivot.CubeFields)
            if (string.Equals(cf.Name, name, StringComparison.Ordinal))
                return cf;
        return null;
    }

    /// <summary>
    /// Retrouve le PivotField correspondant au niveau demandé. Le nommage exact
    /// des PivotField d'une hiérarchie OLAP n'est pas documenté : on essaie le
    /// nom unique du niveau, puis son dernier segment, et en dernier recours on
    /// journalise tous les candidats pour ne pas avoir à deviner deux fois.
    /// </summary>
    private static Xl.PivotField FindPivotFieldForLevel(
        Xl.CubeField field, string levelUniqueName)
    {
        var levelName = LastSegment(levelUniqueName);
        var candidates = new List<Xl.PivotField>();

        foreach (Xl.PivotField pf in field.PivotFields) candidates.Add(pf);

        foreach (var pf in candidates)
        {
            if (Matches(SafeName(pf), levelUniqueName, levelName) ||
                Matches(SafeSourceName(pf), levelUniqueName, levelName) ||
                Matches(SafeCaption(pf), levelUniqueName, levelName))
            {
                return pf;
            }
        }

        var inventory = new StringBuilder();
        foreach (var pf in candidates)
            inventory.Append($"\n  Name={SafeName(pf)} | SourceName={SafeSourceName(pf)} " +
                             $"| Caption={SafeCaption(pf)}");

        FileLog.Write(
            $"Niveau '{levelUniqueName}' introuvable parmi les PivotFields de " +
            $"'{field.Name}'. Candidats :{inventory}");

        // Un seul niveau : pas d'ambiguïté possible, on l'utilise.
        if (candidates.Count == 1) return candidates[0];

        throw new InvalidOperationException(
            $"Impossible d'identifier le niveau « {levelName} » dans le champ du TCD. " +
            "Les candidats ont été journalisés dans %LOCALAPPDATA%\\PivotScope\\logs.");
    }

    private static bool Matches(string? value, string levelUniqueName, string levelName)
        => value is not null &&
           (string.Equals(value, levelUniqueName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, levelName, StringComparison.OrdinalIgnoreCase));

    /// <summary>« [Dim].[Hier].[Magasin] » → « Magasin ».</summary>
    private static string LastSegment(string uniqueName)
    {
        var last = uniqueName.LastIndexOf(".[", StringComparison.Ordinal);
        if (last < 0) return uniqueName;
        return uniqueName[(last + 2)..].TrimEnd(']');
    }

    private static string? SafeName(Xl.PivotField pf) { try { return pf.Name as string; } catch { return null; } }
    private static string? SafeSourceName(Xl.PivotField pf) { try { return pf.SourceName as string; } catch { return null; } }
    private static string? SafeCaption(Xl.PivotField pf) { try { return pf.Caption; } catch { return null; } }
}
