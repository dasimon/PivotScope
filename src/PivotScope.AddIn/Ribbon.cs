using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using ExcelDna.Integration.CustomUI;
using PivotScope.AddIn.Diagnostics;
using PivotScope.AddIn.Interop;
using PivotScope.AddIn.Pane;

namespace PivotScope.AddIn;

/// <summary>
/// Onglet de ruban. L'add-in d'origine n'en avait aucun : tout passait par un
/// clic droit, ce qui rend le produit invisible. Point d'entrée assumé ici.
///
/// <para><b>Piège des imageMso</b> : un identifiant inconnu d'Excel n'émet
/// aucune erreur — le bouton s'affiche simplement sans icône. Un identifiant
/// qui n'existe qu'en 16 × 16 fait de même sur un bouton <c>size="large"</c>,
/// qui réclame une variante 32 × 32. Dans les deux cas l'échec est muet : tout
/// ajout d'icône se vérifie à l'œil, jamais au compilateur.</para>
/// </summary>
[ComVisible(true)]
public class PivotScopeRibbon : ExcelRibbon
{
    private static IRibbonUI? _ribbon;

    public override string GetCustomUI(string ribbonId) =>
        """
        <customUI xmlns="http://schemas.microsoft.com/office/2009/07/customui"
                  onLoad="OnLoad" loadImage="LoadImage">
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
                  <toggleButton id="btnDeferLayout"
                                label="Différer la mise en page"
                                screentip="Déposer plusieurs champs sans interroger le serveur"
                                supertip="Enfoncé = différé. Rien n'est envoyé au serveur tant que vous n'avez pas appliqué. L'état reste visible ici, pour ne pas croire ensuite que le tableau est faux."
                                size="large"
                                image="defer"
                                getPressed="GetDeferLayoutPressed"
                                onAction="OnToggleDeferLayout"/>
                  <button id="btnRefreshNow"
                          label="Appliquer et actualiser"
                          screentip="Appliquer les changements en attente et interroger le serveur"
                          size="large"
                          imageMso="RefreshAll"
                          onAction="OnRefreshNow"/>
                </group>
              </tab>
            </tabs>
          </ribbon>
        </customUI>
        """;

    public void OnLoad(IRibbonUI ribbon) => _ribbon = ribbon;

    /// <summary>
    /// Fournit nos propres icônes. Deux tentatives d'<c>imageMso</c> sont
    /// restées vides sans le moindre message : ces identifiants échouent en
    /// silence, et un nom valide en 16 × 16 ne rend rien sur un bouton
    /// <c>size="large"</c>. Dessiner l'icône supprime la devinette — si elle ne
    /// s'affiche pas, c'est la plomberie qui est en cause, pas un nom.
    /// </summary>
    public override object LoadImage(string imageId) => imageId switch
    {
        "defer" => PauseGlyph(),
        _ => base.LoadImage(imageId),
    };

    /// <summary>Deux barres verticales, dans le vert de PivotScope.</summary>
    private static Bitmap PauseGlyph()
    {
        var bitmap = new Bitmap(32, 32);
        using var graphics = Graphics.FromImage(bitmap);

        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);

        using var brush = new SolidBrush(Color.FromArgb(5, 150, 105));
        graphics.FillRectangle(brush, 8, 6, 6, 20);
        graphics.FillRectangle(brush, 18, 6, 6, 20);

        return bitmap;
    }

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
    /// Hors TCD, on affiche « non différé » : c'est l'état par défaut d'Excel,
    /// et un bouton enfoncé laisserait croire à un réglage en vigueur.
    /// </summary>
    public bool GetDeferLayoutPressed(IRibbonControl control)
    {
        try { return PivotComfort.IsLayoutDeferred(); }
        catch { return false; }
    }

    public void OnToggleDeferLayout(IRibbonControl control, bool pressed)
        => Run(() => PivotComfort.SetDeferLayout(pressed), "bascule de la mise en page différée");

    public void OnRefreshNow(IRibbonControl control)
        => Run(() => { PivotComfort.RefreshNow(); return true; }, "actualisation");

    /// <summary>
    /// Exécute une action Excel hors du callback du ruban, puis réinvalide le
    /// ruban pour que l'état affiché reste celui de la réalité.
    /// </summary>
    private static void Run<T>(Func<T> work, string label)
    {
        _ = ExcelThread.RunAsync(work).ContinueWith(task =>
        {
            if (task.IsFaulted) FileLog.Write($"Échec : {label}.", task.Exception);
            Invalidate();
        }, TaskScheduler.Default);
    }
}
