using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using PivotScope.AddIn.Diagnostics;

namespace PivotScope.AddIn.Pane;

/// <summary>
/// Interface COM par défaut du volet. Volontairement vide : Office instancie le
/// contrôle d'un CustomTaskPane **par COM**, et sans interface par défaut la
/// création échoue avec « Impossible de créer le contrôle ActiveX spécifié »
/// (0x80004005). Elle sert aussi de garde-fou : seul ce qu'elle déclare est
/// exposé à COM, donc les membres génériques du contrôle ne le sont jamais.
/// </summary>
[ComVisible(true)]
public interface IPaneControl;

/// <summary>
/// UserControl WinForms hébergeant WebView2. Le CustomTaskPane d'Office exige un
/// contrôle exposable en ActiveX : WPF ne l'est pas nativement, WinForms si.
/// La SPA est servie depuis les ressources embarquées sur une origine virtuelle,
/// donc sans aucun fichier extrait sur disque.
///
/// Tous les membres utiles sont internes : ils ne servent qu'à PaneManager et
/// WebBridge, dans le même assembly, et les garder publics exposerait à COM un
/// événement générique — que COM ne sait pas représenter.
/// </summary>
[ComVisible(true)]
[ComDefaultInterface(typeof(IPaneControl))]
public sealed class PaneControl : UserControl, IPaneControl
{
    private const string VirtualHost = "pivotscope.local";
    private const string ResourcePrefix = "spa/";

    private readonly WebView2 _web = new() { Dock = DockStyle.Fill };
    private readonly Label _error = new()
    {
        Dock = DockStyle.Top,
        AutoSize = false,
        Height = 72,
        Padding = new Padding(10),
        Visible = false,
        BackColor = System.Drawing.Color.FromArgb(69, 26, 26),
        ForeColor = System.Drawing.Color.FromArgb(248, 113, 113),
    };

    /// <summary>Message JSON brut envoyé par la SPA.</summary>
    internal event EventHandler<string>? MessageReceived;

    public PaneControl()
    {
        Dock = DockStyle.Fill;
        Controls.Add(_web);
        Controls.Add(_error);
    }

    internal async Task InitializeAsync()
    {
        try
        {
            var userData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PivotScope", "webview");
            Directory.CreateDirectory(userData);

            var env = await CoreWebView2Environment.CreateAsync(null, userData);
            await _web.EnsureCoreWebView2Async(env);

            var core = _web.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreDevToolsEnabled = true;

            core.AddWebResourceRequestedFilter(
                $"https://{VirtualHost}/*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += OnWebResourceRequested;
            core.WebMessageReceived += (_, e) =>
                MessageReceived?.Invoke(this, e.TryGetWebMessageAsString() ?? string.Empty);

            core.Navigate($"https://{VirtualHost}/index.html");
            FileLog.Write("Volet initialisé.");
        }
        catch (Exception ex)
        {
            FileLog.Write("Échec d'initialisation de WebView2.", ex);
            ShowError(
                "Le volet n'a pas pu démarrer. Vérifiez que le runtime WebView2 " +
                "est installé. Détail dans %LOCALAPPDATA%\\PivotScope\\logs.");
        }
    }

    /// <summary>Envoie une réponse JSON à la SPA.</summary>
    internal void PostToWeb(string json)
    {
        try { _web.CoreWebView2?.PostWebMessageAsString(json); }
        catch (Exception ex) { FileLog.Write("Échec d'envoi vers la SPA.", ex); }
    }

    private void ShowError(string message)
    {
        if (InvokeRequired) { BeginInvoke(() => ShowError(message)); return; }
        _error.Text = message;
        _error.Visible = true;
    }

    private void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        var core = _web.CoreWebView2;
        if (core is null) return;

        var path = new Uri(e.Request.Uri).AbsolutePath.TrimStart('/');
        if (path.Length == 0) path = "index.html";

        var stream = OpenResource(path);

        if (stream is null)
        {
            // Route cliente inconnue : on rend index.html, comme un serveur SPA.
            stream = OpenResource("index.html");
            if (stream is null)
            {
                e.Response = core.Environment.CreateWebResourceResponse(
                    null, 404, "Not Found", string.Empty);
                return;
            }
            path = "index.html";
        }

        e.Response = core.Environment.CreateWebResourceResponse(
            stream, 200, "OK", $"Content-Type: {ContentType(path)}");
    }

    /// <summary>
    /// Ouvre une ressource embarquée. On tente aussi la variante à antislash :
    /// MSBuild rend %(RecursiveDir) avec le séparateur Windows, et un nom
    /// d'assemblage mal normalisé se traduirait par un 404 muet — l'échec le
    /// plus pénible à diagnostiquer côté navigateur.
    /// </summary>
    private static Stream? OpenResource(string path)
    {
        var assembly = Assembly.GetExecutingAssembly();
        return assembly.GetManifestResourceStream(ResourcePrefix + path)
            ?? assembly.GetManifestResourceStream(ResourcePrefix + path.Replace('/', '\\'));
    }

    private static string ContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".html" => "text/html; charset=utf-8",
        ".js" or ".mjs" => "text/javascript; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".json" => "application/json; charset=utf-8",
        ".svg" => "image/svg+xml",
        ".woff2" => "font/woff2",
        ".woff" => "font/woff",
        ".ttf" => "font/ttf",
        ".png" => "image/png",
        ".ico" => "image/x-icon",
        _ => "application/octet-stream",
    };

    protected override void Dispose(bool disposing)
    {
        if (disposing) _web.Dispose();
        base.Dispose(disposing);
    }
}
