using ExcelDna.Integration;
using PivotScope.AddIn.Diagnostics;
using Xl = Microsoft.Office.Interop.Excel;

namespace PivotScope.AddIn.Interop;

/// <summary>Un champ du cube, tel qu'il apparaît dans la liste de champs.</summary>
public sealed record FieldVisibility(
    string Name, string Caption, bool ShownInFieldList, string Area);

/// <summary>Un niveau d'une hiérarchie, et son affichage dans le tableau.</summary>
public sealed record LevelVisibility(string Name, string Caption, bool Shown);

/// <summary>
/// Confort de construction du TCD.
///
/// À ne pas confondre avec le menu natif « Afficher/masquer les champs »
/// d'Excel, qui bascule les PROPRIÉTÉS DE MEMBRE d'un champ donné. Ici on
/// masque des champs entiers de la LISTE DE CHAMPS, ce qui est le seul moyen
/// de rendre exploitable un cube qui en expose des centaines.
///
/// À appeler exclusivement via <see cref="ExcelThread"/>.
/// </summary>
public static class PivotComfort
{
    private static Xl.PivotTable RequirePivot()
    {
        var app = (Xl.Application)ExcelDnaUtil.Application;
        Xl.PivotTable? pivot = null;
        try { pivot = app.ActiveCell?.PivotTable; } catch { /* hors TCD */ }

        return pivot ?? throw new InvalidOperationException(
            "Placez le curseur dans un tableau croisé dynamique.");
    }

    public static IReadOnlyList<FieldVisibility> ListFields()
    {
        var pivot = RequirePivot();
        var fields = new List<FieldVisibility>();

        foreach (Xl.CubeField cf in pivot.CubeFields)
        {
            var area = cf.Orientation switch
            {
                Xl.XlPivotFieldOrientation.xlRowField => "row",
                Xl.XlPivotFieldOrientation.xlColumnField => "column",
                Xl.XlPivotFieldOrientation.xlPageField => "filter",
                Xl.XlPivotFieldOrientation.xlDataField => "data",
                _ => "",
            };

            bool shown;
            try { shown = cf.ShowInFieldList; } catch { shown = true; }

            fields.Add(new FieldVisibility(cf.Name, cf.Caption, shown, area));
        }

        return fields;
    }

    public static void SetFieldVisibility(string cubeFieldName, bool visible)
    {
        var pivot = RequirePivot();

        foreach (Xl.CubeField cf in pivot.CubeFields)
        {
            if (!string.Equals(cf.Name, cubeFieldName, StringComparison.Ordinal)) continue;

            // Masquer un champ posé sur le TCD le retirerait de la vue sans
            // que l'utilisateur l'ait demandé : on refuse plutôt que surprendre.
            if (!visible && cf.Orientation != Xl.XlPivotFieldOrientation.xlHidden)
                throw new InvalidOperationException(
                    $"« {cf.Caption} » est utilisé dans le tableau croisé dynamique. " +
                    "Retirez-le de la disposition avant de le masquer de la liste.");

            cf.ShowInFieldList = visible;
            return;
        }

        throw new InvalidOperationException(
            $"Champ introuvable dans le tableau croisé dynamique : {cubeFieldName}");
    }

    /// <summary>
    /// Les niveaux d'une hiérarchie posée sur le tableau. Un CubeField expose
    /// un PivotField par niveau ; c'est leur propriété Hidden qui décide de
    /// l'affichage.
    /// </summary>
    public static IReadOnlyList<LevelVisibility> ListLevels(string cubeFieldName)
    {
        var field = FindCubeField(RequirePivot(), cubeFieldName);
        var levels = new List<LevelVisibility>();

        foreach (Xl.PivotField pf in field.PivotFields)
        {
            bool hidden;
            try { hidden = pf.Hidden; } catch { hidden = false; }
            levels.Add(new LevelVisibility(pf.Name, SafeCaption(pf), !hidden));
        }

        return levels;
    }

    /// <summary>
    /// Applique la sélection de niveaux.
    ///
    /// **Deux passes, et l'ordre n'est pas cosmétique** : Excel refuse de
    /// masquer le dernier niveau visible d'une hiérarchie. On affiche donc
    /// d'abord ce qui doit l'être, on masque seulement ensuite.
    /// </summary>
    public static IReadOnlyList<LevelVisibility> SetLevelVisibility(
        string cubeFieldName, IReadOnlyList<string> shownLevelNames)
    {
        if (shownLevelNames.Count == 0)
            throw new InvalidOperationException(
                "Gardez au moins un niveau affiché : une hiérarchie sans niveau " +
                "visible n'a plus de sens dans le tableau.");

        var pivot = RequirePivot();
        var field = FindCubeField(pivot, cubeFieldName);
        var wanted = new HashSet<string>(shownLevelNames, StringComparer.Ordinal);

        foreach (Xl.PivotField pf in field.PivotFields)
            if (wanted.Contains(pf.Name)) TrySetHidden(pf, false);

        foreach (Xl.PivotField pf in field.PivotFields)
            if (!wanted.Contains(pf.Name)) TrySetHidden(pf, true);

        return ListLevels(cubeFieldName);
    }

    private static void TrySetHidden(Xl.PivotField field, bool hidden)
    {
        try
        {
            if (field.Hidden != hidden) field.Hidden = hidden;
        }
        catch (Exception ex)
        {
            // Excel peut refuser un niveau précis ; on le journalise et on
            // continue, plutôt que d'abandonner toute la sélection.
            FileLog.Write($"Niveau « {field.Name} » : bascule refusée par Excel.", ex);
        }
    }

    private static Xl.CubeField FindCubeField(Xl.PivotTable pivot, string name)
    {
        foreach (Xl.CubeField cf in pivot.CubeFields)
            if (string.Equals(cf.Name, name, StringComparison.Ordinal))
                return cf;

        throw new InvalidOperationException(
            $"Champ introuvable dans le tableau croisé dynamique : {name}");
    }

    private static string SafeCaption(Xl.PivotField field)
    {
        try { return field.Caption; } catch { return field.Name; }
    }

    public static int ShowAllFields()
    {
        var pivot = RequirePivot();
        var restored = 0;

        foreach (Xl.CubeField cf in pivot.CubeFields)
        {
            try
            {
                if (cf.ShowInFieldList) continue;
                cf.ShowInFieldList = true;
                restored++;
            }
            catch { /* champ récalcitrant : on continue, le compte le dira */ }
        }

        return restored;
    }

    /// <summary>
    /// Le rafraîchissement automatique, mécanique reprise de l'add-in d'origine :
    /// EnableRefresh sur le cache, plus le mode de calcul d'Excel.
    /// </summary>
    public static bool IsAutoRefreshEnabled()
    {
        var app = (Xl.Application)ExcelDnaUtil.Application;
        Xl.PivotTable? pivot = null;
        try { pivot = app.ActiveCell?.PivotTable; } catch { /* hors TCD */ }
        if (pivot is null) return true;

        try { return pivot.PivotCache().EnableRefresh; } catch { return true; }
    }

    public static bool SetAutoRefresh(bool enabled)
    {
        var app = (Xl.Application)ExcelDnaUtil.Application;
        var pivot = RequirePivot();

        pivot.PivotCache().EnableRefresh = enabled;
        app.Calculation = enabled
            ? Xl.XlCalculation.xlCalculationAutomatic
            : Xl.XlCalculation.xlCalculationManual;

        return enabled;
    }
}
