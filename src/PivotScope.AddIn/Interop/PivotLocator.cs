using ExcelDna.Integration;
using Xl = Microsoft.Office.Interop.Excel;

namespace PivotScope.AddIn.Interop;

/// <summary>
/// Identifies a PivotTable independently of the cursor. Excel names the first
/// PivotTable of EVERY sheet "PivotTable1" (or its translation): the name
/// alone designates nothing, only workbook + sheet + name does.
/// </summary>
public sealed record PivotRef(string Workbook, string Sheet, string Name)
{
    public string Key => $"{Workbook}|{Sheet}|{Name}";
}

/// <summary>
/// Finds the PivotTable under the cursor, and finds it again later. An
/// operation that lasts (resolving a list of members, several seconds) must
/// act on the PivotTable it started from: the user may have clicked into
/// another one in the meantime, possibly with the same cube field.
///
/// Call exclusively through <see cref="ExcelThread"/>.
/// </summary>
public static class PivotLocator
{
    /// <summary>The PivotTable under the cursor, or null outside any PivotTable.</summary>
    public static Xl.PivotTable? Active()
    {
        var app = (Xl.Application)ExcelDnaUtil.Application;
        try { return app.ActiveCell?.PivotTable; }
        catch { return null; /* outside a PivotTable: COM throws, that is expected */ }
    }

    public static PivotRef? Describe(Xl.PivotTable? pivot)
    {
        if (pivot is null) return null;
        try
        {
            var sheet = (Xl.Worksheet)pivot.Parent;
            var book = (Xl.Workbook)sheet.Parent;
            return new PivotRef(book.FullName, sheet.Name, pivot.Name);
        }
        catch
        {
            return null;
        }
    }

    public static PivotRef? ActiveRef() => Describe(Active());

    /// <summary>Finds the PivotTable again, wherever the cursor is now.</summary>
    public static Xl.PivotTable Resolve(PivotRef reference)
    {
        var app = (Xl.Application)ExcelDnaUtil.Application;
        foreach (Xl.Workbook book in app.Workbooks)
        {
            if (!string.Equals(book.FullName, reference.Workbook, StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                var sheet = (Xl.Worksheet)book.Worksheets[reference.Sheet];
                return (Xl.PivotTable)sheet.PivotTables(reference.Name);
            }
            catch
            {
                break;
            }
        }

        throw new InvalidOperationException(
            $"Le tableau croisé dynamique « {reference.Name} » (feuille {reference.Sheet}) " +
            "n'existe plus : le classeur a été fermé ou le tableau supprimé pendant l'opération.");
    }
}
