using System.Text;
using ExcelDna.Integration;
using PivotScope.AddIn.Diagnostics;
using Xl = Microsoft.Office.Interop.Excel;

namespace PivotScope.AddIn.Interop;

/// <summary>
/// Applies an inclusive manual filter on a level of a PivotTable field.
///
/// Three pitfalls, all silent or misleading if ignored:
/// 1. ClearManualFilter must be called on the CubeField; on a PivotField
///    in OLAP, it raises a run-time error (documented).
/// 2. If IncludeNewItemsInFilter is True, VisibleItemsList "stays empty and
///    accepts no item" (documented): the assignment does nothing.
/// 3. A hierarchy CubeField exposes ONE PivotField PER LEVEL. Writing
///    unique names of the "Magasin" level into the PivotField of the
///    "Etablissement" level makes Excel answer "Élément introuvable dans le
///    cube OLAP" — observed on a real cube. You must target the right level.
///
/// Call exclusively through <see cref="ExcelThread"/>.
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
        try { pivot = app.ActiveCell?.PivotTable; } catch { /* outside a PivotTable */ }

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
    /// Finds the PivotField matching the requested level. The exact naming
    /// of the PivotFields of an OLAP hierarchy is not documented: we try the
    /// level's unique name, then its last segment, and as a last resort we
    /// log all candidates so we never have to guess twice.
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
            $"Level '{levelUniqueName}' not found among the PivotFields of " +
            $"'{field.Name}'. Candidates:{inventory}");

        // A single level: no ambiguity possible, use it.
        if (candidates.Count == 1) return candidates[0];

        throw new InvalidOperationException(
            $"Impossible d'identifier le niveau « {levelName} » dans le champ du TCD. " +
            "Les candidats ont été journalisés dans %LOCALAPPDATA%\\PivotScope\\logs.");
    }

    private static bool Matches(string? value, string levelUniqueName, string levelName)
        => value is not null &&
           (string.Equals(value, levelUniqueName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, levelName, StringComparison.OrdinalIgnoreCase));

    /// <summary>"[Dim].[Hier].[Magasin]" → "Magasin".</summary>
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
