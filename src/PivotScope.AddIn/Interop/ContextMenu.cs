using ExcelDna.Integration;
using PivotScope.AddIn.Diagnostics;
using PivotScope.AddIn.Pane;
using Office = Microsoft.Office.Core;
using Xl = Microsoft.Office.Interop.Excel;

namespace PivotScope.AddIn.Interop;

/// <summary>
/// Injects the PivotScope entries into the PivotTable context menu.
///
/// **Three entries, not one more.** The original add-in injected eight and
/// made the menu unreadable; the task pane is the main entry point, the
/// context menu is only a shortcut to the actions that start from a
/// specific cell.
/// </summary>
public static class ContextMenu
{
    private const string Tag = "PivotScope";
    private const string PivotContextMenu = "PivotTable Context Menu";

    /// <summary>
    /// The buttons must stay referenced: the Click subscription lives on the
    /// COM wrapper, and once the garbage collector reclaims a local variable
    /// the entry is still displayed but no longer does anything (known Office
    /// behaviour).
    /// </summary>
    private static readonly List<Office.CommandBarButton> Buttons = [];

    public static void Install()
    {
        try
        {
            Remove();

            var app = (Xl.Application)ExcelDnaUtil.Application;
            var bar = app.CommandBars[PivotContextMenu];

            Add(bar, RibbonText.T("Ouvrir le volet PivotScope", "Open the PivotScope pane"),
                beginGroup: true, () => PaneManager.Show());
            Add(bar, RibbonText.T("Filtrer par une liste…", "Filter by a list…"),
                beginGroup: false, () => PaneManager.ShowOn("tableau"));
            Add(bar, RibbonText.T("D'où vient ce chiffre ?", "Where does this figure come from?"),
                beginGroup: false, () => PaneManager.ShowOn("provenance"));
        }
        catch (Exception ex)
        {
            // A missing context menu does not prevent using the ribbon.
            FileLog.Write("Failed to install the context menu.", ex);
        }
    }

    public static void Remove()
    {
        Buttons.Clear();
        try
        {
            var app = (Xl.Application)ExcelDnaUtil.Application;
            var bar = app.CommandBars[PivotContextMenu];

            // Backwards: deleting while moving forward shifts the remaining indices.
            for (var i = bar.Controls.Count; i >= 1; i--)
            {
                var control = bar.Controls[i];
                if (string.Equals(control.Tag, Tag, StringComparison.Ordinal))
                    control.Delete();
            }
        }
        catch (Exception ex)
        {
            FileLog.Write("Failed to clean up the context menu.", ex);
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
            catch (Exception ex) { FileLog.Write($"'{caption}' failed.", ex); }
        };
        Buttons.Add(button);
    }
}
