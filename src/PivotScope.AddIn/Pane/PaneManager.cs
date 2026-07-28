using ExcelDna.Integration.CustomUI;
using PivotScope.AddIn.Diagnostics;

namespace PivotScope.AddIn.Pane;

/// <summary>
/// Crée le volet Office à la demande, une seule fois par session Excel.
/// Repli documenté si le CustomTaskPane s'avère inutilisable (focus clavier) :
/// remplacer la création par une Form non-modale hébergeant le même PaneControl.
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
            FileLog.Write("Volet créé.");
        }

        _pane.Visible = true;
        _ = _bridge; // conservé vivant tant que le volet existe
    }

    /// <summary>
    /// Ouvre le volet sur un onglet donné. Le message part après un court délai
    /// si la SPA n'est pas encore prête : elle rejoue le dernier onglet demandé
    /// à son initialisation.
    /// </summary>
    internal static void ShowOn(string tab)
    {
        Show();
        _control?.PostToWeb($$"""{"id":"0","ok":true,"event":"showTab","tab":"{{tab}}"}""");
    }
}
