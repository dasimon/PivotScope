using ExcelDna.Integration;
using PivotScope.AddIn.Diagnostics;
using Office = Microsoft.Office.Core;
using Xl = Microsoft.Office.Interop.Excel;

namespace PivotScope.AddIn;

/// <summary>
/// Labels of the ribbon and the context menu.
///
/// They cannot go through vue-i18n: they live in Excel, outside the
/// task pane. Since the ribbon is built only once at load time, the language is
/// Excel's — not the one chosen later in the pane. That is the
/// deliberate trade-off: a ribbon that switched language mid-session
/// would have to be rebuilt entirely, for about fifteen words.
/// </summary>
internal static class RibbonText
{
    private const int FrenchLcid = 1036;

    private static readonly Lazy<bool> UseFrench = new(DetectFrench);

    internal static bool IsFrench => UseFrench.Value;

    /// <summary>Picks between two labels based on Excel's display language.</summary>
    internal static string T(string french, string english) => IsFrench ? french : english;

    private static bool DetectFrench()
    {
        try
        {
            var app = (Xl.Application)ExcelDnaUtil.Application;
            var lcid = app.LanguageSettings.LanguageID[Office.MsoAppLanguageID.msoLanguageIDUI];
            return lcid == FrenchLcid;
        }
        catch (Exception ex)
        {
            // Language cannot be determined: stay in French, the language of
            // the author and of daily use.
            FileLog.Write("Could not determine Excel's language, ribbon falls back to French.", ex);
            return true;
        }
    }
}
