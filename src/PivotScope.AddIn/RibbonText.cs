using ExcelDna.Integration;
using PivotScope.AddIn.Diagnostics;
using Office = Microsoft.Office.Core;
using Xl = Microsoft.Office.Interop.Excel;

namespace PivotScope.AddIn;

/// <summary>
/// Libellés du ruban et du menu contextuel.
///
/// Ils ne peuvent pas passer par vue-i18n : ils vivent dans Excel, hors du
/// volet. Le ruban étant construit une seule fois au chargement, la langue est
/// celle d'Excel — pas celle choisie plus tard dans le volet. C'est le
/// compromis assumé : un ruban qui changerait de langue en cours de session
/// demanderait de le reconstruire entièrement, pour une quinzaine de mots.
/// </summary>
internal static class RibbonText
{
    private const int FrenchLcid = 1036;

    private static readonly Lazy<bool> UseFrench = new(DetectFrench);

    internal static bool IsFrench => UseFrench.Value;

    /// <summary>Choisit entre deux libellés selon la langue d'affichage d'Excel.</summary>
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
            // Langue indéterminable : on reste en français, la langue de
            // l'auteur et de l'usage quotidien.
            FileLog.Write("Langue d'Excel indéterminable, ruban en français.", ex);
            return true;
        }
    }
}
