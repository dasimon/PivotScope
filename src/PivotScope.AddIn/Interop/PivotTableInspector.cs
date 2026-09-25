using System.Data.Common;
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
            return PivotContext.None("Placez le curseur dans un tableau croisé dynamique.", "noPivot");

        Xl.PivotCache cache;
        try { cache = pivot.PivotCache(); }
        catch (Exception ex) { return PivotContext.None($"Cache du TCD illisible : {ex.Message}"); }

        if (!cache.OLAP)
            return PivotContext.None(
                "Ce tableau croisé dynamique n'est pas connecté à un cube OLAP. " +
                "PivotScope ne prend en charge que SSAS Multidimensional.", "notOlap");

        // Documented: PivotTable.MDX throws if there is no data item.
        string? mdx = null;
        try { mdx = pivot.MDX; } catch { /* empty PivotTable */ }

        var (server, catalog) = ConnectionParts(cache);

        // The workbook Data Model (Power Pivot) is OLAP to Excel too, served
        // by an embedded engine: out of scope, and the SSAS errors it would
        // produce further down would say nothing useful.
        if (server is not null && server.StartsWith("$Embedded$", StringComparison.OrdinalIgnoreCase))
            return PivotContext.None(
                "Ce tableau croisé dynamique repose sur le modèle de données du classeur " +
                "(Power Pivot). PivotScope ne prend en charge que SSAS Multidimensional.", "powerPivot");

        if (server is null || catalog is null)
            return new PivotContext(true, true, server, catalog, null, mdx, ReadFields(pivot),
                "La connexion de ce tableau croisé dynamique n'a pas pu être lue (serveur ou " +
                "catalogue absent). Vérifiez la connexion du classeur.",
                PivotLocator.Describe(pivot)?.Key, "connectionUnreadable");

        var cube = CubeName(cache);
        var fields = ReadFields(pivot);

        return new PivotContext(true, true, server, catalog, cube, mdx, fields, null,
            PivotLocator.Describe(pivot)?.Key);
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

    /// <summary>
    /// Extracts the server and the catalog from the workbook's OLE DB string.
    /// Parsed by DbConnectionStringBuilder, which handles quoted values and
    /// ";" inside them; the synonyms accepted by MSOLAP are recognized.
    /// </summary>
    private static (string? Server, string? Catalog) ConnectionParts(Xl.PivotCache cache)
    {
        string? connectionString = null;
        try { connectionString = cache.WorkbookConnection?.OLEDBConnection?.Connection as string; }
        catch { /* connection unavailable */ }

        if (string.IsNullOrWhiteSpace(connectionString)) return (null, null);

        // Excel prefixes the string with the connection type ("OLEDB;").
        if (connectionString.StartsWith("OLEDB;", StringComparison.OrdinalIgnoreCase))
            connectionString = connectionString["OLEDB;".Length..];

        var builder = new DbConnectionStringBuilder();
        try { builder.ConnectionString = connectionString; }
        catch (ArgumentException) { return (null, null); }

        return (First(builder, "Data Source", "Location", "Server"),
                First(builder, "Initial Catalog", "Catalog", "Database"));
    }

    private static string? First(DbConnectionStringBuilder builder, params string[] keys)
    {
        foreach (var key in keys)
            if (builder.TryGetValue(key, out var value) && value is string text && !string.IsNullOrWhiteSpace(text))
                return text.Trim();
        return null;
    }
}
