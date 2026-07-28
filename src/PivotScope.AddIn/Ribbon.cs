using System.Runtime.InteropServices;
using ExcelDna.Integration.CustomUI;
using PivotScope.AddIn.Diagnostics;
using PivotScope.AddIn.Interop;
using PivotScope.AddIn.Pane;

namespace PivotScope.AddIn;

/// <summary>
/// Onglet de ruban. L'add-in d'origine n'en avait aucun : tout passait par un
/// clic droit, ce qui rend le produit invisible. Point d'entrée assumé ici.
/// </summary>
[ComVisible(true)]
public class PivotScopeRibbon : ExcelRibbon
{
    private static IRibbonUI? _ribbon;

    public override string GetCustomUI(string ribbonId) =>
        """
        <customUI xmlns="http://schemas.microsoft.com/office/2009/07/customui" onLoad="OnLoad">
          <ribbon>
            <tabs>
              <tab id="tabPivotScope" label="PivotScope">
                <group id="grpPane" label="Analyse">
                  <button id="btnPane"
                          label="Volet PivotScope"
                          screentip="Ouvrir le volet PivotScope"
                          supertip="Affiche le MDX du tableau croisé dynamique actif, l'explorateur de métadonnées du cube, l'éditeur de requêtes et le filtre par liste."
                          size="large"
                          imageMso="TableOfContentsGallery"
                          onAction="OnOpenPane"/>
                </group>
                <group id="grpComfort" label="Construction">
                  <toggleButton id="btnAutoRefresh"
                                label="Rafraîchissement auto"
                                screentip="Couper le rafraîchissement automatique du tableau croisé dynamique"
                                supertip="Enfoncé = actif. Coupez-le pour déposer plusieurs champs sans attendre le serveur à chaque geste. L'état reste visible ici, pour ne pas croire ensuite que le tableau est faux."
                                size="large"
                                imageMso="RefreshAll"
                                getPressed="GetAutoRefreshPressed"
                                onAction="OnToggleAutoRefresh"/>
                </group>
              </tab>
            </tabs>
          </ribbon>
        </customUI>
        """;

    public void OnLoad(IRibbonUI ribbon) => _ribbon = ribbon;

    /// <summary>Redemande au ruban de relire l'état affiché.</summary>
    internal static void Invalidate()
    {
        try { _ribbon?.Invalidate(); }
        catch (Exception ex) { FileLog.Write("Échec d'invalidation du ruban.", ex); }
    }

    public void OnOpenPane(IRibbonControl control)
    {
        try
        {
            PaneManager.Show();
        }
        catch (Exception ex)
        {
            // Jamais de MessageBox : une boîte modale depuis un callback de ruban
            // bloque Excel. Le volet et le log portent le diagnostic.
            FileLog.Write("Échec à l'ouverture du volet depuis le ruban.", ex);
        }
    }

    /// <summary>
    /// Hors TCD, on affiche « actif » : c'est l'état par défaut d'Excel, et un
    /// bouton relâché laisserait croire à un réglage en vigueur qui n'existe pas.
    /// </summary>
    public bool GetAutoRefreshPressed(IRibbonControl control)
    {
        try { return PivotComfort.IsAutoRefreshEnabled(); }
        catch { return true; }
    }

    public void OnToggleAutoRefresh(IRibbonControl control, bool pressed)
    {
        _ = ExcelThread.RunAsync(() => PivotComfort.SetAutoRefresh(pressed))
            .ContinueWith(task =>
            {
                if (task.IsFaulted)
                    FileLog.Write("Échec de bascule du rafraîchissement.", task.Exception);
                Invalidate();
            }, TaskScheduler.Default);
    }
}
