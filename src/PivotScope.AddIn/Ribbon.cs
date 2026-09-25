using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using ExcelDna.Integration;
using ExcelDna.Integration.CustomUI;
using PivotScope.AddIn.Diagnostics;
using PivotScope.AddIn.Interop;
using PivotScope.AddIn.Pane;

namespace PivotScope.AddIn;

/// <summary>
/// Ribbon tab. The original add-in had none: everything went through a
/// right-click, which makes the product invisible. Deliberate entry point here.
///
/// <para><b>imageMso pitfall</b>: an id unknown to Excel raises
/// no error — the button simply shows without an icon. An id
/// that only exists in 16 × 16 does the same on a <c>size="large"</c> button,
/// which asks for a 32 × 32 variant. In both cases the failure is silent: any
/// added icon is checked by eye, never by the compiler.</para>
/// </summary>
[ComVisible(true)]
public class PivotScopeRibbon : ExcelRibbon
{
    private static IRibbonUI? _ribbon;

    public override string GetCustomUI(string ribbonId)
    {
        string T(string fr, string en) => System.Security.SecurityElement.Escape(
            RibbonText.T(fr, en))!;

        // The XML is assembled rather than written in one block: labels must
        // follow Excel's language, and all inserted text is escaped — an
        // unescaped apostrophe or ampersand makes the ribbon invalid,
        // and Excel then silently ignores it.
        return $"""
        <customUI xmlns="http://schemas.microsoft.com/office/2009/07/customui"
                  onLoad="OnLoad" loadImage="LoadImage">
          <ribbon>
            <tabs>
              <tab id="tabPivotScope" label="PivotScope">
                <group id="grpPane" label="{T("Analyse", "Analysis")}">
                  <button id="btnPane"
                          label="{T("Volet PivotScope", "PivotScope pane")}"
                          screentip="{T("Ouvrir le volet PivotScope", "Open the PivotScope pane")}"
                          supertip="{T(
                              "Affiche le MDX du tableau croisé dynamique actif, l'explorateur de métadonnées du cube, l'éditeur de requêtes et le filtre par liste.",
                              "Shows the active PivotTable's MDX, the cube metadata explorer, the query editor and the filter by list.")}"
                          size="large"
                          imageMso="TableOfContentsGallery"
                          onAction="OnOpenPane"/>
                </group>
                <group id="grpComfort" label="{T("Construction", "Building")}">
                  <toggleButton id="btnDeferLayout"
                                label="{T("Différer la mise en page", "Defer layout update")}"
                                screentip="{T(
                                    "Déposer plusieurs champs sans interroger le serveur",
                                    "Drop several fields without querying the server")}"
                                supertip="{T(
                                    "Enfoncé = différé. Rien n'est envoyé au serveur tant que vous n'avez pas appliqué. L'état reste visible ici, pour ne pas croire ensuite que le tableau est faux.",
                                    "Pressed = deferred. Nothing is sent to the server until you apply. The state stays visible here, so you cannot conclude the table is wrong.")}"
                                size="large"
                                image="defer"
                                getPressed="GetDeferLayoutPressed"
                                onAction="OnToggleDeferLayout"/>
                  <button id="btnRefreshNow"
                          label="{T("Appliquer et actualiser", "Apply and refresh")}"
                          screentip="{T(
                              "Appliquer les changements en attente et interroger le serveur",
                              "Apply pending changes and query the server")}"
                          size="large"
                          imageMso="RefreshAll"
                          onAction="OnRefreshNow"/>
                </group>
              </tab>
            </tabs>
          </ribbon>
        </customUI>
        """;
    }

    public void OnLoad(IRibbonUI ribbon) => _ribbon = ribbon;

    /// <summary>
    /// Supplies our own icons. Two <c>imageMso</c> attempts stayed
    /// blank without the slightest message: these ids fail
    /// silently, and a name valid in 16 × 16 renders nothing on a
    /// <c>size="large"</c> button. Drawing the icon removes the guesswork — if it does not
    /// show, the plumbing is at fault, not a name.
    /// </summary>
    public override object LoadImage(string imageId) => imageId switch
    {
        "defer" => PauseGlyph(),
        _ => base.LoadImage(imageId),
    };

    /// <summary>Two vertical bars, in PivotScope green.</summary>
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

    /// <summary>
    /// Asks the ribbon to re-read the displayed state. Safe from any thread:
    /// the call itself is COM, so it is queued to Excel's main thread.
    /// </summary>
    internal static void Invalidate()
    {
        try
        {
            ExcelAsyncUtil.QueueAsMacro(() =>
            {
                try { _ribbon?.Invalidate(); }
                catch (Exception ex) { FileLog.Write("Ribbon invalidation failed.", ex); }
            });
        }
        catch (Exception ex) { FileLog.Write("Ribbon invalidation could not be queued.", ex); }
    }

    public void OnOpenPane(IRibbonControl control)
    {
        try
        {
            PaneManager.Show();
        }
        catch (Exception ex)
        {
            // Never a MessageBox: a modal dialog from a ribbon callback
            // blocks Excel. The task pane and the log carry the diagnostic.
            FileLog.Write("Failed to open the pane from the ribbon.", ex);
        }
    }

    /// <summary>
    /// Outside a PivotTable, show "not deferred": it is Excel's default state,
    /// and a pressed button would suggest a setting in effect.
    /// </summary>
    public bool GetDeferLayoutPressed(IRibbonControl control)
    {
        try { return PivotComfort.IsLayoutDeferred(); }
        catch { return false; }
    }

    public void OnToggleDeferLayout(IRibbonControl control, bool pressed)
        => Run(() => PivotComfort.SetDeferLayout(pressed), "deferred layout toggle");

    public void OnRefreshNow(IRibbonControl control)
        => Run(() => { PivotComfort.RefreshNow(); return true; }, "refresh");

    /// <summary>
    /// Runs an Excel action outside the ribbon callback, then invalidates the
    /// ribbon again so the displayed state keeps matching reality.
    /// </summary>
    private static void Run<T>(Func<T> work, string label)
    {
        _ = ExcelThread.RunAsync(work).ContinueWith(task =>
        {
            if (task.IsFaulted) FileLog.Write($"Failed: {label}.", task.Exception);
            Invalidate();
        }, TaskScheduler.Default);
    }
}
