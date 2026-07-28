using ExcelDna.Integration;
using PivotScope.AddIn.Diagnostics;
using Xl = Microsoft.Office.Interop.Excel;

namespace PivotScope.AddIn.Interop;

/// <summary>
/// Lit le tuple MDX complet de la cellule active — filtres de rapport inclus.
///
/// Deux limites documentées par Microsoft, à traduire en messages utilisables
/// plutôt qu'en exceptions COM : <c>PivotCell.MDX</c> lève hors de la zone de
/// valeurs, et lève aussi quand un filtre de rapport a plusieurs éléments
/// sélectionnés.
///
/// À appeler exclusivement via <see cref="ExcelThread"/>.
/// </summary>
public static class PivotCellReader
{
    public static string ReadTuple()
    {
        var app = (Xl.Application)ExcelDnaUtil.Application;
        var cell = app.ActiveCell
            ?? throw new InvalidOperationException("Aucune cellule active.");

        Xl.PivotCell pivotCell;
        try { pivotCell = cell.PivotCell; }
        catch
        {
            throw new InvalidOperationException(
                "Cette cellule n'appartient pas à un tableau croisé dynamique.");
        }

        Xl.XlPivotCellType type;
        try { type = pivotCell.PivotCellType; }
        catch { type = Xl.XlPivotCellType.xlPivotCellValue; }

        if (type != Xl.XlPivotCellType.xlPivotCellValue)
            throw new InvalidOperationException(
                "Sélectionnez une cellule de valeur : les en-têtes et les totaux " +
                "n'ont pas de coordonnées complètes.");

        try
        {
            var tuple = pivotCell.MDX;
            // Le format exact rendu par Excel n'est pas documenté : on le trace
            // au premier usage plutôt que de conclure sur une supposition.
            FileLog.Write($"PivotCell.MDX = {tuple}");
            return tuple;
        }
        catch (Exception ex)
        {
            FileLog.Write("PivotCell.MDX a échoué.", ex);
            throw new InvalidOperationException(
                "Excel ne peut pas donner les coordonnées de cette cellule. " +
                "C'est notamment le cas lorsqu'un filtre de rapport a plusieurs " +
                "éléments sélectionnés : réduisez-le à un seul.");
        }
    }
}
