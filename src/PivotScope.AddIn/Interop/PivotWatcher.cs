using ExcelDna.Integration;
using PivotScope.AddIn.Diagnostics;
using Xl = Microsoft.Office.Interop.Excel;

namespace PivotScope.AddIn.Interop;

/// <summary>
/// Tells the task pane when what it shows is out of date.
///
/// Without it, the pane shows the PivotTable state as it was at the last click on
/// "Actualiser" — that is, potentially wrong, without any warning.
/// Yet that was the main argument against the original add-in's modal
/// dialog box: a pane that does not keep up is just a dialog that is never
/// closed.
///
/// Two precautions:
/// — notify only if the PivotTable or the cell has really changed, otherwise
///   every cursor move would trigger a round trip;
/// — Excel event handlers must NEVER throw: an exception that bubbles up
///   into Excel's event pump destabilizes it.
/// </summary>
internal sealed class PivotWatcher : IDisposable
{
    private readonly Xl.Application _app;
    private readonly Action<bool> _onChanged;

    private string _lastPivot = string.Empty;
    private string _lastCell = string.Empty;
    private bool _disposed;

    /// <param name="onChanged">
    /// Receives true if the PivotTable itself has changed (context to be fully re-read),
    /// false if only the active cell has moved (provenance only).
    /// </param>
    internal PivotWatcher(Action<bool> onChanged)
    {
        _onChanged = onChanged;
        _app = (Xl.Application)ExcelDnaUtil.Application;

        _app.SheetSelectionChange += OnSelectionChange;
        _app.SheetPivotTableUpdate += OnPivotUpdate;
        _app.WorkbookActivate += OnWorkbookActivate;
    }

    private void OnSelectionChange(object sheet, Xl.Range target)
    {
        Safe(() =>
        {
            var pivot = PivotKey(target);
            var cell = CellKey(target);

            var pivotChanged = pivot != _lastPivot;
            var cellChanged = cell != _lastCell;
            if (!pivotChanged && !cellChanged) return;

            _lastPivot = pivot;
            _lastCell = cell;
            _onChanged(pivotChanged);
        });
    }

    /// <summary>
    /// The PivotTable has been reworked — field dropped, filter applied, refresh.
    /// The context is necessarily stale.
    /// </summary>
    private void OnPivotUpdate(object sheet, Xl.PivotTable target)
        => Safe(() => { _lastPivot = string.Empty; _onChanged(true); });

    private void OnWorkbookActivate(Xl.Workbook book)
        => Safe(() => { _lastPivot = string.Empty; _onChanged(true); });

    private static string PivotKey(Xl.Range target)
    {
        try { return target.PivotTable?.Name ?? string.Empty; }
        catch { return string.Empty; }
    }

    private static string CellKey(Xl.Range target)
    {
        try { return target.Address[true, true]; }
        catch { return string.Empty; }
    }

    /// <summary>
    /// An Excel event handler that throws destabilizes the event
    /// pump: swallow and log, always.
    /// </summary>
    private static void Safe(Action work)
    {
        try { work(); }
        catch (Exception ex) { FileLog.Write("PivotTable tracking: event ignored.", ex); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { _app.SheetSelectionChange -= OnSelectionChange; } catch { /* Excel is closing */ }
        try { _app.SheetPivotTableUpdate -= OnPivotUpdate; } catch { /* Excel is closing */ }
        try { _app.WorkbookActivate -= OnWorkbookActivate; } catch { /* Excel is closing */ }
    }
}
