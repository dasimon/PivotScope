using ExcelDna.Integration;
using Xl = Microsoft.Office.Interop.Excel;

namespace PivotScope.AddIn.Interop;

/// <summary>
/// Écrit un tableau rectangulaire dans la feuille.
///
/// Une seule affectation à Range.Value2 : écrire cellule par cellule à travers
/// COM est des ordres de grandeur plus lent, et c'est le genre de détail qui
/// décide si l'outil est utilisable sur un vrai résultat.
///
/// À appeler exclusivement via <see cref="ExcelThread"/>.
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

            // Écraser un TCD par une plage brute le corromprait.
            try
            {
                if (anchor.PivotTable is not null)
                    throw new InvalidOperationException(
                        "La cellule active est dans un tableau croisé dynamique. " +
                        "Choisissez une autre cellule ou cochez « nouvelle feuille ».");
            }
            catch (InvalidOperationException) { throw; }
            catch { /* hors TCD : c'est le cas nominal, COM lève */ }
        }

        var target = anchor.Resize[rows, columns];
        target.Value2 = grid;
        target.Worksheet.Activate();

        return $"{target.Worksheet.Name}!{target.Address[false, false]}";
    }
}
