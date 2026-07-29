using ExcelDna.Integration;
using PivotScope.AddIn.Diagnostics;
using Xl = Microsoft.Office.Interop.Excel;

namespace PivotScope.AddIn.Interop;

/// <summary>
/// Prévient le volet quand ce qu'il affiche n'est plus d'actualité.
///
/// Sans ça, le volet montre l'état du TCD tel qu'il était au dernier clic sur
/// « Actualiser » — c'est-à-dire potentiellement faux, sans rien signaler.
/// C'était pourtant l'argument principal contre la boîte de dialogue modale de
/// l'add-in d'origine : un volet qui ne suit pas n'est qu'une boîte qu'on ne
/// referme pas.
///
/// Deux précautions :
/// — on ne notifie que si le TCD ou la cellule ont réellement changé, sinon
///   chaque déplacement de curseur déclencherait un aller-retour ;
/// — les gestionnaires d'événements Excel ne doivent JAMAIS lever : une
///   exception qui remonte dans le pompage d'événements d'Excel le déstabilise.
/// </summary>
internal sealed class PivotWatcher : IDisposable
{
    private readonly Xl.Application _app;
    private readonly Action<bool> _onChanged;

    private string _lastPivot = string.Empty;
    private string _lastCell = string.Empty;
    private bool _disposed;

    /// <param name="onChanged">
    /// Reçoit vrai si le TCD lui-même a changé (contexte à relire entièrement),
    /// faux si seule la cellule active a bougé (provenance uniquement).
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
    /// Le TCD a été remanié — champ déposé, filtre appliqué, actualisation.
    /// Le contexte est forcément périmé.
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
    /// Un gestionnaire d'événement Excel qui lève déstabilise le pompage
    /// d'événements : on avale et on journalise, toujours.
    /// </summary>
    private static void Safe(Action work)
    {
        try { work(); }
        catch (Exception ex) { FileLog.Write("Suivi du TCD : événement ignoré.", ex); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { _app.SheetSelectionChange -= OnSelectionChange; } catch { /* Excel ferme */ }
        try { _app.SheetPivotTableUpdate -= OnPivotUpdate; } catch { /* Excel ferme */ }
        try { _app.WorkbookActivate -= OnWorkbookActivate; } catch { /* Excel ferme */ }
    }
}
