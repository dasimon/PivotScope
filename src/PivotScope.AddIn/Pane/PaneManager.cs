using ExcelDna.Integration.CustomUI;
using PivotScope.AddIn.Diagnostics;

namespace PivotScope.AddIn.Pane;

/// <summary>
/// Creates the Office task pane on demand, only once per Excel session.
/// Documented fallback if the CustomTaskPane turns out to be unusable (keyboard focus):
/// replace the creation with a modeless Form hosting the same PaneControl.
/// </summary>
internal static class PaneManager
{
    private static CustomTaskPane? _pane;
    private static PaneControl? _control;
    private static WebBridge? _bridge;

    internal static PaneControl? Control => _control;

    internal static void Show()
    {
        if (_pane is null)
        {
            _pane = CustomTaskPaneFactory.CreateCustomTaskPane(typeof(PaneControl), "PivotScope");
            _pane.Width = 480;
            _control = (PaneControl)_pane.ContentControl;
            _bridge = new WebBridge(_control);
            _ = _control.InitializeAsync();
            FileLog.Write("Pane created.");
        }

        _pane.Visible = true;
        _ = _bridge; // kept alive as long as the pane exists
    }

    /// <summary>
    /// Opens the task pane on a given tab. The message goes out after a short delay
    /// if the SPA is not ready yet: it replays the last requested tab
    /// when it initializes.
    /// </summary>
    internal static void ShowOn(string tab)
    {
        Show();
        _control?.PostToWeb($$"""{"id":"0","ok":true,"event":"showTab","tab":"{{tab}}"}""");
    }
}
