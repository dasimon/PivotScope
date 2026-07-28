using ExcelDna.Integration;
using PivotScope.AddIn.Diagnostics;
using PivotScope.AddIn.Pane;
using Office = Microsoft.Office.Core;
using Xl = Microsoft.Office.Interop.Excel;

namespace PivotScope.AddIn.Interop;

/// <summary>
/// Injecte les entrées PivotScope dans le menu contextuel du TCD.
///
/// **Trois entrées, pas une de plus.** L'add-in d'origine en injectait huit et
/// rendait le menu illisible ; le volet est le point d'entrée principal, le
/// menu contextuel n'est qu'un raccourci vers les gestes qui partent d'une
/// cellule précise.
/// </summary>
public static class ContextMenu
{
    private const string Tag = "PivotScope";
    private const string PivotContextMenu = "PivotTable Context Menu";

    public static void Install()
    {
        try
        {
            Remove();

            var app = (Xl.Application)ExcelDnaUtil.Application;
            var bar = app.CommandBars[PivotContextMenu];

            Add(bar, "Ouvrir le volet PivotScope", beginGroup: true, () => PaneManager.Show());
            Add(bar, "Filtrer par une liste…", beginGroup: false,
                () => PaneManager.ShowOn("filtre"));
            Add(bar, "D'où vient ce chiffre ?", beginGroup: false,
                () => PaneManager.ShowOn("provenance"));
        }
        catch (Exception ex)
        {
            // Un menu contextuel absent n'empêche pas d'utiliser le ruban.
            FileLog.Write("Échec d'installation du menu contextuel.", ex);
        }
    }

    public static void Remove()
    {
        try
        {
            var app = (Xl.Application)ExcelDnaUtil.Application;
            var bar = app.CommandBars[PivotContextMenu];

            // À rebours : supprimer en avançant décale les indices restants.
            for (var i = bar.Controls.Count; i >= 1; i--)
            {
                var control = bar.Controls[i];
                if (string.Equals(control.Tag, Tag, StringComparison.Ordinal))
                    control.Delete();
            }
        }
        catch (Exception ex)
        {
            FileLog.Write("Échec de nettoyage du menu contextuel.", ex);
        }
    }

    private static void Add(
        Office.CommandBar bar, string caption, bool beginGroup, Action action)
    {
        var button = (Office.CommandBarButton)bar.Controls.Add(
            Office.MsoControlType.msoControlButton,
            Type.Missing, Type.Missing, Type.Missing, true);

        button.Caption = caption;
        button.Tag = Tag;
        button.BeginGroup = beginGroup;
        button.Click += (Office.CommandBarButton _, ref bool _) =>
        {
            try { action(); }
            catch (Exception ex) { FileLog.Write($"Échec de « {caption} ».", ex); }
        };
    }
}
