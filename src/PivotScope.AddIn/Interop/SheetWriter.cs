using System.Runtime.InteropServices;
using ExcelDna.Integration;
using PivotScope.Core.Query;
using Xl = Microsoft.Office.Interop.Excel;

namespace PivotScope.AddIn.Interop;

/// <summary>
/// Where a query result will go, decided when the query is LAUNCHED. Taking
/// the active cell when the query ends — possibly minutes later — would write
/// wherever the user happens to be working by then.
/// </summary>
public sealed class WriteTarget
{
    internal Xl.Workbook? Book { get; init; }
    internal Xl.Range? Anchor { get; init; }
    public bool NewSheet => Anchor is null;
}

/// <summary>What writing would do, checked before doing it.</summary>
public sealed record WritePlan(string Address, bool OverwritesData);

/// <summary>
/// Writes a rectangular array to the sheet.
///
/// A single assignment to Range.Value2: writing cell by cell through
/// COM is orders of magnitude slower, and that is the kind of detail that
/// decides whether the tool is usable on a real result.
///
/// COM writes empty Excel's undo stack: nothing written here can be undone
/// with Ctrl+Z. Hence the checks before writing, and the confirmation the
/// pane asks for when the destination is not empty.
///
/// Call exclusively through <see cref="ExcelThread"/>.
/// </summary>
public static class SheetWriter
{
    /// <summary>#VALUE! — what a cell in error becomes in the sheet.</summary>
    private const int XlErrValue = -2146826273;

    public static WriteTarget Capture(bool newSheet)
    {
        var app = (Xl.Application)ExcelDnaUtil.Application;
        var book = app.ActiveWorkbook
            ?? throw new InvalidOperationException("Aucun classeur ouvert.");

        if (newSheet) return new WriteTarget { Book = book };

        var anchor = app.ActiveCell
            ?? throw new InvalidOperationException("Aucune cellule active.");

        // Refused at launch rather than after a long query.
        if (InPivot(anchor))
            throw new InvalidOperationException(
                "La cellule active est dans un tableau croisé dynamique. " +
                "Choisissez une autre cellule ou cochez « nouvelle feuille ».");

        EnsureWritable(anchor.Worksheet);
        return new WriteTarget { Book = book, Anchor = anchor };
    }

    /// <summary>Checks the destination; throws if writing there is impossible.</summary>
    public static WritePlan Plan(WriteTarget target, object?[,] grid)
    {
        var (rows, columns) = Size(grid);
        if (target.NewSheet) return new WritePlan("nouvelle feuille", false);

        var anchor = Alive(target.Anchor!);
        var sheet = anchor.Worksheet;
        EnsureWritable(sheet);

        if (anchor.Row + rows - 1 > RangeProjection.MaxRows ||
            anchor.Column + columns - 1 > RangeProjection.MaxColumns)
            throw new InvalidOperationException(
                $"Le résultat ({rows:N0} × {columns:N0}) déborde de la feuille à partir de " +
                $"{anchor.Address[false, false]}. Choisissez une cellule plus haute ou plus à " +
                "gauche, ou une nouvelle feuille.");

        var range = anchor.Resize[rows, columns];

        // Overwriting part of a PivotTable with a raw range corrupts it.
        var app = (Xl.Application)ExcelDnaUtil.Application;
        foreach (Xl.PivotTable pivot in (Xl.PivotTables)sheet.PivotTables())
        {
            if (app.Intersect(range, pivot.TableRange2) is not null)
                throw new InvalidOperationException(
                    $"La plage {range.Address[false, false]} recouvre le tableau croisé " +
                    $"dynamique « {pivot.Name} ». Choisissez une autre cellule ou une nouvelle feuille.");
        }

        var used = app.WorksheetFunction.CountA(range) > 0;
        return new WritePlan($"{sheet.Name}!{range.Address[false, false]}", used);
    }

    public static string Write(WriteTarget target, object?[,] grid)
    {
        var (rows, columns) = Size(grid);

        Xl.Range anchor;
        if (target.NewSheet)
        {
            var book = target.Book ?? throw new InvalidOperationException("Aucun classeur ouvert.");
            Xl.Worksheet sheet;
            try { sheet = (Xl.Worksheet)book.Worksheets.Add(); }
            catch (COMException ex)
            {
                throw new InvalidOperationException(
                    "Impossible d'ajouter une feuille : la structure du classeur est " +
                    "peut-être protégée. Écrivez à la cellule active à la place.", ex);
            }
            anchor = (Xl.Range)sheet.Cells[1, 1];
        }
        else
        {
            anchor = Alive(target.Anchor!);
        }

        var range = anchor.Resize[rows, columns];
        range.Value2 = ForExcel(grid);
        range.Worksheet.Activate();

        return $"{range.Worksheet.Name}!{range.Address[false, false]}";
    }

    /// <summary>
    /// Excel re-interprets any string assigned to Value2 as if it were typed:
    /// "0012" loses its zeros, "01/02" becomes a date, "=x" a formula. A
    /// leading apostrophe keeps the text as text (it is not part of the value).
    /// A <see cref="CellError"/> becomes a real #VALUE!.
    /// </summary>
    private static object?[,] ForExcel(object?[,] grid)
    {
        var (rows, columns) = Size(grid);
        var copy = new object?[rows, columns];
        for (var r = 0; r < rows; r++)
            for (var c = 0; c < columns; c++)
                copy[r, c] = grid[r, c] switch
                {
                    string text => "'" + text,
                    CellError => new ErrorWrapper(XlErrValue),
                    var value => value,
                };
        return copy;
    }

    private static (int Rows, int Columns) Size(object?[,] grid)
    {
        var rows = grid.GetLength(0);
        var columns = grid.GetLength(1);
        if (rows == 0 || columns == 0)
            throw new InvalidOperationException("La requête n'a produit aucune donnée.");
        return (rows, columns);
    }

    private static Xl.Range Alive(Xl.Range anchor)
    {
        try
        {
            _ = anchor.Worksheet.Name;
            return anchor;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "La cellule de destination n'existe plus (classeur fermé ou feuille supprimée).", ex);
        }
    }

    private static bool InPivot(Xl.Range cell)
    {
        try { return cell.PivotTable is not null; }
        catch { return false; /* outside a PivotTable: the nominal case, COM throws */ }
    }

    private static void EnsureWritable(Xl.Worksheet sheet)
    {
        if (sheet.ProtectContents)
            throw new InvalidOperationException(
                $"La feuille « {sheet.Name} » est protégée : ôtez la protection ou écrivez " +
                "dans une nouvelle feuille.");
    }
}
