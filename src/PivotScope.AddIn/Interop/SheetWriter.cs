using ExcelDna.Integration;
using Xl = Microsoft.Office.Interop.Excel;

namespace PivotScope.AddIn.Interop;

/// <summary>
/// Writes a rectangular array to the sheet.
///
/// A single assignment to Range.Value2: writing cell by cell through
/// COM is orders of magnitude slower, and that is the kind of detail that
/// decides whether the tool is usable on a real result.
///
/// Call exclusively through <see cref="ExcelThread"/>.
/// </summary>
public static class SheetWriter
{
    public static string Write(object?[,] grid, bool newSheet)
    {
        var rows = grid.GetLength(0);
        var columns = grid.GetLength(1);
        if (rows == 0 || columns == 0)
            throw new InvalidOperationException("La requête n'a produit aucune donnée.");

        var app = (Xl.Application)ExcelDnaUtil.Application;
        var book = app.ActiveWorkbook
            ?? throw new InvalidOperationException("Aucun classeur ouvert.");

        Xl.Range anchor;
        if (newSheet)
        {
            var sheet = (Xl.Worksheet)book.Worksheets.Add();
            anchor = (Xl.Range)sheet.Cells[1, 1];
        }
        else
        {
            anchor = app.ActiveCell
                ?? throw new InvalidOperationException("Aucune cellule active.");

            // Overwriting a PivotTable with a raw range would corrupt it.
            try
            {
                if (anchor.PivotTable is not null)
                    throw new InvalidOperationException(
                        "La cellule active est dans un tableau croisé dynamique. " +
                        "Choisissez une autre cellule ou cochez « nouvelle feuille ».");
            }
            catch (InvalidOperationException) { throw; }
            catch { /* outside a PivotTable: the nominal case, COM throws */ }
        }

        var target = anchor.Resize[rows, columns];
        target.Value2 = grid;
        target.Worksheet.Activate();

        return $"{target.Worksheet.Name}!{target.Address[false, false]}";
    }
}
