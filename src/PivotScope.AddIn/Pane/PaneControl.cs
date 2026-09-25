using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using PivotScope.AddIn.Diagnostics;

namespace PivotScope.AddIn.Pane;

/// <summary>
/// Default COM interface of the task pane. Deliberately empty: Office instantiates the
/// control of a CustomTaskPane **through COM**, and without a default interface
/// creation fails with "Impossible de créer le contrôle ActiveX spécifié"
/// (0x80004005). It also acts as a safeguard: only what it declares is
/// exposed to COM, so the control's generic members never are.
/// </summary>
[ComVisible(true)]
public interface IPaneControl;

/// <summary>
/// WinForms UserControl hosting WebView2. Office's CustomTaskPane requires a
/// control that can be exposed as ActiveX: WPF cannot natively, WinForms can.
/// The SPA is served from embedded resources on a virtual origin,
/// so without any file extracted to disk.
///
/// All useful members are internal: they only serve PaneManager and
/// WebBridge, in the same assembly, and keeping them public would expose a
/// generic event to COM — which COM cannot represent.
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

    /// <summary>Raw JSON message sent by the SPA.</summary>
    internal event EventHandler<string>? MessageReceived;

    private const string Origin = $"https://{VirtualHost}/";

    /// <summary>Tab asked for by the context menu, until the SPA collects it.</summary>
    private string? _pendingTab;
    private bool _loaded;

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
#if DEBUG
            core.Settings.AreDevToolsEnabled = true;
#else
            core.Settings.AreDevToolsEnabled = false;
            // F5 / Ctrl+R would reload the SPA and lose everything typed in
            // the pane; the shortcuts the SPA uses still reach it.
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
#endif

            core.AddWebResourceRequestedFilter(
                $"https://{VirtualHost}/*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += OnWebResourceRequested;
            core.WebMessageReceived += OnWebMessageReceived;

            // The pane only ever shows the embedded SPA: the bridge can write
            // to the workbook and call the AI with the user's key, so no other
            // page may end up talking to it.
            core.NavigationStarting += (_, e) =>
            {
                if (!e.Uri.StartsWith(Origin, StringComparison.OrdinalIgnoreCase)) e.Cancel = true;
            };
            core.NewWindowRequested += (_, e) => e.Handled = true;
            core.NavigationCompleted += (_, _) =>
            {
                _loaded = true;
                if (_pendingTab is not null) PostShowTab(_pendingTab);
            };

            core.Navigate($"https://{VirtualHost}/index.html");
            FileLog.Write("Pane initialized.");
        }
        catch (Exception ex)
        {
            FileLog.Write("WebView2 initialization failed.", ex);
            ShowError(
                "Le volet n'a pas pu démarrer. Vérifiez que le runtime WebView2 " +
                "est installé. Détail dans %LOCALAPPDATA%\\PivotScope\\logs.");
        }
    }

    /// <summary>Messages from anything but the embedded SPA are ignored.</summary>
    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            if (!e.Source.StartsWith(Origin, StringComparison.OrdinalIgnoreCase))
            {
                FileLog.Write($"Message ignored from an unexpected origin: {e.Source}");
                return;
            }
            MessageReceived?.Invoke(this, e.TryGetWebMessageAsString() ?? string.Empty);
        }
        catch (Exception ex)
        {
            // Raised on Excel's UI thread: never let it through.
            FileLog.Write("Unreadable message from the SPA.", ex);
        }
    }

    /// <summary>
    /// Asks the SPA to show a tab. If it is not loaded yet, the request is
    /// parked: the SPA collects it at startup (pane.takeTab), or it is posted
    /// once navigation completes.
    /// </summary>
    internal void RequestTab(string tab)
    {
        _pendingTab = tab;
        if (_loaded) PostShowTab(tab);
    }

    internal string? TakePendingTab()
    {
        var tab = _pendingTab;
        _pendingTab = null;
        return tab;
    }

    private void PostShowTab(string tab)
        => PostToWeb($$"""{"event":"showTab","tab":{{System.Text.Json.JsonSerializer.Serialize(tab)}}}""");

    /// <summary>Sends a JSON response to the SPA.</summary>
    internal void PostToWeb(string json)
    {
        try { _web.CoreWebView2?.PostWebMessageAsString(json); }
        catch (Exception ex) { FileLog.Write("Failed to send to the SPA.", ex); }
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
            // Unknown client route: serve index.html, like an SPA server.
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
    /// Opens an embedded resource. The backslash variant is tried too:
    /// MSBuild renders %(RecursiveDir) with the Windows separator, and a poorly
    /// normalized manifest name would result in a silent 404 — the failure
    /// most painful to diagnose on the browser side.
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
