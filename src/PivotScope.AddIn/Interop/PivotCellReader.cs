using ExcelDna.Integration;
using PivotScope.AddIn.Diagnostics;
using Xl = Microsoft.Office.Interop.Excel;

namespace PivotScope.AddIn.Interop;

/// <summary>
/// Reads the full MDX tuple of the active cell — report filters included.
///
/// Two limits documented by Microsoft, to be turned into usable messages
/// rather than COM exceptions: <c>PivotCell.MDX</c> throws outside the values
/// area, and also throws when a report filter has several items
/// selected.
///
/// Call exclusively through <see cref="ExcelThread"/>.
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
            // The exact format returned by Excel is not documented: log it
            // on first use rather than conclude on an assumption.
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
