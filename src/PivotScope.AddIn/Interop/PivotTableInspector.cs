using ExcelDna.Integration;
using PivotScope.Core.Models;
using Xl = Microsoft.Office.Interop.Excel;

namespace PivotScope.AddIn.Interop;

/// <summary>
/// Lit le TCD sous le curseur. Ne lève jamais : tout échec devient un
/// PivotContext dégradé porteur d'un diagnostic affichable dans le volet.
/// À appeler exclusivement via <see cref="ExcelThread"/>.
/// </summary>
public static class PivotTableInspector
{
    public static PivotContext Capture()
    {
        var app = (Xl.Application)ExcelDnaUtil.Application;

        Xl.PivotTable? pivot = null;
        try { pivot = app.ActiveCell?.PivotTable; }
        catch { /* le curseur n'est pas dans un TCD : COM lève, c'est normal */ }

        if (pivot is null)
            return PivotContext.None("Placez le curseur dans un tableau croisé dynamique.");

        Xl.PivotCache cache;
        try { cache = pivot.PivotCache(); }
        catch (Exception ex) { return PivotContext.None($"Cache du TCD illisible : {ex.Message}"); }

        if (!cache.OLAP)
            return PivotContext.None(
                "Ce tableau croisé dynamique n'est pas connecté à un cube OLAP. " +
                "PivotScope ne prend en charge que SSAS Multidimensional.");

        // Documenté : PivotTable.MDX lève s'il n'y a aucun élément de données.
        string? mdx = null;
        try { mdx = pivot.MDX; } catch { /* TCD vide */ }

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
        catch { /* liste partielle plutôt que rien */ }
        return fields;
    }

    /// <summary>
    /// Pour une connexion xlCmdCube, CommandText porte le nom du cube. Vide sur
    /// une connexion d'un autre type : la SPA proposera alors un sélecteur.
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

    /// <summary>Extrait Data Source et Initial Catalog de la chaîne OLE DB du classeur.</summary>
    private static (string? Server, string? Catalog) ConnectionParts(Xl.PivotCache cache)
    {
        string? connectionString = null;
        try { connectionString = cache.WorkbookConnection?.OLEDBConnection?.Connection as string; }
        catch { /* connexion indisponible */ }

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
