using ExcelDna.Integration;
using Xl = Microsoft.Office.Interop.Excel;

namespace PivotScope.AddIn.Interop;

/// <summary>Un champ du cube, tel qu'il apparaît dans la liste de champs.</summary>
public sealed record FieldVisibility(
    string Name, string Caption, bool ShownInFieldList, string Area);

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
