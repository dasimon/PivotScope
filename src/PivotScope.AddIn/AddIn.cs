using ExcelDna.Integration;
using PivotScope.AddIn.Diagnostics;

namespace PivotScope.AddIn;

/// <summary>
/// Excel-DNA entry point. Startup is deliberately minimal: register the
/// ribbon and nothing else. SSAS, SQLite and WebView2 are initialized
/// lazily the first time the task pane opens.
/// </summary>
public sealed class PivotScopeAddIn : IExcelAddIn
{
    public void AutoOpen()
    {
        try
        {
            // Without this call, the task pane's WinForms controls are rendered in
            // Windows 95 style — the official Excel-DNA sample does it too.
            System.Windows.Forms.Application.EnableVisualStyles();
            Interop.ContextMenu.Install();
            FileLog.Write($"PivotScope loaded (Excel {ExcelDnaUtil.ExcelVersion}).");
        }
        catch (Exception ex)
        {
            FileLog.Write("Load failed.", ex);
        }
    }

    public void AutoClose()
    {
        // CommandBars survive the add-in being unloaded: without this
        // cleanup, Excel keeps dead entries in the context menu.
        Interop.ContextMenu.Remove();
        FileLog.Write("PivotScope unloaded.");
    }
}
