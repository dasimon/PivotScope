using ExcelDna.Integration;
using PivotScope.Core.Models;
using Xl = Microsoft.Office.Interop.Excel;

namespace PivotScope.AddIn.Interop;

/// <summary>
/// Reads the PivotTable under the cursor. Never throws: any failure becomes a
/// degraded PivotContext carrying a diagnostic that can be shown in the task pane.
/// Call exclusively through <see cref="ExcelThread"/>.
/// </summary>
public static class PivotTableInspector
{
    public static PivotContext Capture()
    {
        var app = (Xl.Application)ExcelDnaUtil.Application;

        Xl.PivotTable? pivot = null;
        try { pivot = app.ActiveCell?.PivotTable; }
        catch { /* the cursor is not in a PivotTable: COM throws, that is expected */ }

        if (pivot is null)
            return PivotContext.None("Placez le curseur dans un tableau croisé dynamique.");

        Xl.PivotCache cache;
        try { cache = pivot.PivotCache(); }
        catch (Exception ex) { return PivotContext.None($"Cache du TCD illisible : {ex.Message}"); }

        if (!cache.OLAP)
            return PivotContext.None(
                "Ce tableau croisé dynamique n'est pas connecté à un cube OLAP. " +
                "PivotScope ne prend en charge que SSAS Multidimensional.");

        // Documented: PivotTable.MDX throws if there is no data item.
        string? mdx = null;
        try { mdx = pivot.MDX; } catch { /* empty PivotTable */ }

        var (server, catalog) = ConnectionParts(cache);
        var cube = CubeName(cache);
        var fields = ReadFields(pivot);

        return new PivotContext(true, true, server, catalog, cube, mdx, fields, null);
    }

    private static List<PivotFieldInfo> ReadFields(Xl.PivotTable pivot)
    {
        var fields = new List<PivotFieldInfo>();
        try
        {
            foreach (Xl.CubeField cf in pivot.CubeFields)
            {
                var area = cf.Orientation switch
                {
                    Xl.XlPivotFieldOrientation.xlRowField => "row",
                    Xl.XlPivotFieldOrientation.xlColumnField => "column",
                    Xl.XlPivotFieldOrientation.xlPageField => "filter",
                    Xl.XlPivotFieldOrientation.xlDataField => "data",
                    _ => null,
                };
                if (area is null) continue;
                fields.Add(new PivotFieldInfo(cf.Caption, cf.Name, area));
            }
        }
        catch { /* a partial list rather than nothing */ }
        return fields;
    }

    /// <summary>
    /// For an xlCmdCube connection, CommandText holds the cube name. Empty on
    /// a connection of another type: the SPA will then offer a picker.
    /// </summary>
    private static string? CubeName(Xl.PivotCache cache)
    {
        try
        {
            var oledb = cache.WorkbookConnection?.OLEDBConnection;
            if (oledb is null) return null;
            if (oledb.CommandType != Xl.XlCmdType.xlCmdCube) return null;
            return oledb.CommandText as string;
        }
        catch { return null; }
    }

    /// <summary>Extracts Data Source and Initial Catalog from the workbook's OLE DB string.</summary>
    private static (string? Server, string? Catalog) ConnectionParts(Xl.PivotCache cache)
    {
        string? connectionString = null;
        try { connectionString = cache.WorkbookConnection?.OLEDBConnection?.Connection as string; }
        catch { /* connection unavailable */ }

        if (string.IsNullOrWhiteSpace(connectionString)) return (null, null);

        string? server = null, catalog = null;
        foreach (var part in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var sep = part.IndexOf('=');
            if (sep <= 0) continue;

            var key = part[..sep].Trim();
            var value = part[(sep + 1)..].Trim();

            if (key.Equals("Data Source", StringComparison.OrdinalIgnoreCase)) server = value;
            else if (key.Equals("Initial Catalog", StringComparison.OrdinalIgnoreCase)) catalog = value;
        }

        return (server, catalog);
    }
}
