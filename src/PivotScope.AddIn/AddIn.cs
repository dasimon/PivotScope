using ExcelDna.Integration;
using PivotScope.AddIn.Diagnostics;

namespace PivotScope.AddIn;

/// <summary>
/// Point d'entrée Excel-DNA. Démarrage volontairement minimal : on enregistre le
/// ruban et rien d'autre. SSAS, SQLite et WebView2 sont initialisés
/// paresseusement à la première ouverture du volet.
/// </summary>
public sealed class PivotScopeAddIn : IExcelAddIn
{
    public void AutoOpen()
    {
        try
        {
            // Sans cet appel, les contrôles WinForms du volet sont rendus dans
            // le style Windows 95 — l'exemple officiel Excel-DNA le fait aussi.
            System.Windows.Forms.Application.EnableVisualStyles();
            Interop.ContextMenu.Install();
            FileLog.Write($"PivotScope chargé (Excel {ExcelDnaUtil.ExcelVersion}).");
        }
        catch (Exception ex)
        {
            FileLog.Write("Échec au chargement.", ex);
        }
    }

    public void AutoClose()
    {
        // Les CommandBars survivent au déchargement du complément : sans ce
        // nettoyage, Excel garde des entrées mortes dans le menu contextuel.
        Interop.ContextMenu.Remove();
        FileLog.Write("PivotScope déchargé.");
    }
}
