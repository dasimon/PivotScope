using ExcelDna.Integration;
using ExcelDna.Integration.CustomUI;
using PivotScope.AddIn.Diagnostics;
using PivotScope.AddIn.Interop;
using PivotScope.Core.Calculations;
using Xl = Microsoft.Office.Interop.Excel;

namespace PivotScope.AddIn.Pane;

/// <summary>
/// Creates the Office task pane on demand, one per Excel window.
///
/// A CustomTaskPane belongs to ONE window. Since Excel 2013 every workbook
/// has its own window: a single pane for the whole session opened in the
/// first workbook's window when asked from the second, and once that
/// workbook was closed, the pane was dead and could not be shown again until
/// Excel restarted. Hence one pane per window handle, and dead ones dropped.
///
/// Documented fallback if the CustomTaskPane turns out to be unusable (keyboard focus):
/// replace the creation with a modeless Form hosting the same PaneControl.
/// </summary>
internal static class PaneManager
{
    private sealed record Pane(CustomTaskPane TaskPane, PaneControl Control, WebBridge Bridge);

    private static readonly Dictionary<int, Pane> Panes = [];

    /// <summary>Shared by every pane: one set of connections and one library per process.</summary>
    private static readonly SessionProvider Sessions = new();
    private static readonly Lazy<CalculationLibrary> Library = new(() => new CalculationLibrary());

    internal static void Show() => Current().TaskPane.Visible = true;

    /// <summary>
    /// Opens the task pane on a given tab. The tab is parked in the control
    /// and the SPA collects it when it starts: right after creation, WebView2
    /// is not ready yet and a posted message would be lost.
    /// </summary>
    internal static void ShowOn(string tab)
    {
        var pane = Current();
        pane.Control.RequestTab(tab);
        pane.TaskPane.Visible = true;
    }

    private static Pane Current()
    {
        DropDeadPanes();

        var app = (Xl.Application)ExcelDnaUtil.Application;
        Xl.Window? window = null;
        try { window = app.ActiveWindow; } catch { /* no workbook open */ }
        var key = window?.Hwnd ?? 0;

        if (Panes.TryGetValue(key, out var existing)) return existing;

        var taskPane = window is null
            ? CustomTaskPaneFactory.CreateCustomTaskPane(typeof(PaneControl), "PivotScope")
            : CustomTaskPaneFactory.CreateCustomTaskPane(typeof(PaneControl), "PivotScope", window);
        taskPane.Width = 480;

        var control = (PaneControl)taskPane.ContentControl;
        var pane = new Pane(taskPane, control, new WebBridge(control, Sessions, Library));
        _ = control.InitializeAsync();

        Panes[key] = pane;
        FileLog.Write($"Pane created for window {key}.");
        return pane;
    }

    /// <summary>A pane whose window was closed throws on any access: release it.</summary>
    private static void DropDeadPanes()
    {
        foreach (var (key, pane) in Panes.ToList())
        {
            try
            {
                _ = pane.TaskPane.Visible;
                continue;
            }
            catch
            {
                // The window is gone.
            }

            Panes.Remove(key);
            try { pane.Bridge.Dispose(); } catch (Exception ex) { FileLog.Write("Bridge disposal failed.", ex); }
            try { pane.TaskPane.Delete(); } catch { /* already destroyed with its window */ }
            FileLog.Write($"Pane of window {key} released.");
        }
    }

    /// <summary>Called when the add-in unloads.</summary>
    internal static void Shutdown()
    {
        foreach (var pane in Panes.Values)
            try { pane.Bridge.Dispose(); } catch { /* Excel is closing */ }
        Panes.Clear();
        Sessions.Dispose();
        if (Library.IsValueCreated) Library.Value.Dispose();
    }
}
