# PivotScope — plan d'implémentation, phases 0 et 1

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** livrer un `PivotScope.xll` installable qui ouvre un volet Office
suivant le TCD OLAP actif, y affiche le MDX généré et l'explorateur de
métadonnées du cube, et applique un filtre par liste de clés.

**Architecture:** complément Excel-DNA en `net10.0-windows`. `PivotScope.AddIn`
détient toute l'interop COM et héberge un WebView2 dans un `CustomTaskPane` ;
`PivotScope.Core` porte la logique testable sans Excel derrière des interfaces,
avec des adaptateurs vers `CubeScope.Core` (sous-module git). La SPA Vue 3 est
embarquée en ressources et servie sur une origine virtuelle.

**Tech Stack:** .NET 10 (`net10.0-windows`, x64), Excel-DNA 1.9,
`Microsoft.Web.WebView2`, `CubeScope.Core` (ADOMD .NET Core 19.84.1), Vue 3 +
TypeScript + Vite + Monaco, xUnit.

## Global Constraints

- Cible **`net10.0-windows`**, `PlatformTarget` **x64** pour tous les projets .NET.
- `Nullable=enable`, `TreatWarningsAsErrors=true`, `LangVersion=latest`.
- Versions de paquets **centralisées** dans `Directory.Packages.props`.
- **Aucune ligne de code reprise** de OLAP PivotTable Extensions. Licence **MIT**.
- **SSAS Multidimensional uniquement.** Aucune abstraction multi-moteurs.
- `PivotScope.Core` ne référence **jamais** `Microsoft.Office.Interop.Excel`.
- Tout appel COM Excel passe par `ExcelThread` ; toute requête SSAS part hors du
  thread UI.
- **Aucune `MessageBox`** : bandeau dans le volet, et log fichier.
- Sécurité intégrée Windows. Aucun credential stocké. Clé IA par
  `ANTHROPIC_API_KEY` uniquement.
- Messages d'interface en français, code et noms de symboles en anglais.

---

## Structure des fichiers

| Fichier | Responsabilité |
|---|---|
| `Directory.Build.props` | propriétés communes (TFM, nullable, warnings) |
| `Directory.Packages.props` | versions centralisées |
| `PivotScope.sln` | solution |
| `external/CubeScope` | sous-module git, épinglé |
| `src/PivotScope.Core/Abstractions/ICubeMetadataReader.cs` | contrat de lecture des métadonnées |
| `src/PivotScope.Core/Abstractions/IMdxExecutor.cs` | contrat d'exécution MDX |
| `src/PivotScope.Core/Models/PivotContext.cs` | photo du TCD actif, sans type Excel |
| `src/PivotScope.Core/Bridge/BridgeMessages.cs` | enveloppes requête/réponse |
| `src/PivotScope.Core/Bridge/BridgeRouter.cs` | routage méthode → handler |
| `src/PivotScope.Core/Filtering/MemberResolver.cs` | clés → noms uniques via `StrToMember` |
| `src/PivotScope.Core/Adapters/CubeScope*.cs` | adaptateurs vers `CubeScope.Core` |
| `src/PivotScope.AddIn/AddIn.cs` | `IExcelAddIn`, démarrage minimal |
| `src/PivotScope.AddIn/Ribbon.cs` + `.xml` | onglet de ruban |
| `src/PivotScope.AddIn/Excel/ExcelThread.cs` | marshalling COM + culture |
| `src/PivotScope.AddIn/Excel/PivotTableInspector.cs` | lecture du TCD (OLAP, MDX, champs) |
| `src/PivotScope.AddIn/Excel/PivotFilterApplier.cs` | écriture de `VisibleItemsList` |
| `src/PivotScope.AddIn/Pane/PaneControl.cs` | `UserControl` WinForms hébergeant WebView2 |
| `src/PivotScope.AddIn/Pane/EmbeddedSpa.cs` | ressources embarquées → origine virtuelle |
| `src/PivotScope.AddIn/Pane/WebBridge.cs` | `postMessage` ↔ `BridgeRouter` |
| `src/PivotScope.AddIn/Diagnostics/FileLog.cs` | log fichier tournant |
| `src/PivotScope.Web/` | SPA Vue 3 |
| `tests/PivotScope.Core.Tests/` | xUnit |

---

## Phase 0 — spike (GO / NO-GO)

### Task 1: Squelette du dépôt

**Files:**
- Create: `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig`,
  `.gitattributes`, `.gitignore`, `LICENSE`, `README.md`, `CHANGELOG.md`,
  `PivotScope.sln`
- Create: `src/PivotScope.Core/PivotScope.Core.csproj`
- Create: `tests/PivotScope.Core.Tests/PivotScope.Core.Tests.csproj`

**Interfaces:**
- Consumes: rien
- Produces: une solution qui compile et une suite de tests qui s'exécute.

- [ ] **Step 1: Écrire `Directory.Build.props`**

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <PlatformTarget>x64</PlatformTarget>
    <Platforms>x64</Platforms>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <Version>0.1.0</Version>
    <Authors>David Simon</Authors>
    <Product>PivotScope</Product>
  </PropertyGroup>
</Project>
```

- [ ] **Step 2: Écrire `Directory.Packages.props`**

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <PackageVersion Include="ExcelDna.AddIn" Version="1.9.0" />
    <PackageVersion Include="ExcelDna.Integration" Version="1.9.0" />
    <PackageVersion Include="Microsoft.Web.WebView2" Version="1.0.3485.44" />
    <PackageVersion Include="Microsoft.Identity.Client" Version="4.86.1" />
    <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageVersion Include="xunit" Version="2.9.3" />
    <PackageVersion Include="xunit.runner.visualstudio" Version="3.1.1" />
  </ItemGroup>
</Project>
```

Le pin de `Microsoft.Identity.Client` reproduit celui de CubeScope : ADOMD le
tire en transitive dans une version vulnérable (NU1901/NU1902).

- [ ] **Step 3: Créer les deux projets et la solution**

```bash
dotnet new classlib -o src/PivotScope.Core -n PivotScope.Core
dotnet new xunit    -o tests/PivotScope.Core.Tests -n PivotScope.Core.Tests
dotnet new sln -n PivotScope
dotnet sln add src/PivotScope.Core tests/PivotScope.Core.Tests
dotnet add tests/PivotScope.Core.Tests reference src/PivotScope.Core
```

Retirer les `Class1.cs` / `UnitTest1.cs` générés, et supprimer des `.csproj`
toute propriété redondante avec `Directory.Build.props` (`TargetFramework`,
`Nullable`, `ImplicitUsings`) ainsi que les `Version=` des `PackageReference`
(gérées centralement).

- [ ] **Step 4: Vérifier**

Run: `dotnet build -c Debug`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 5: Écrire LICENSE (MIT, © 2026 David Simon), README, CHANGELOG**

Le README doit contenir : le pitch, le périmètre (SSAS Multidimensional
uniquement), les prérequis (Excel 64-bit 2016+, .NET Desktop Runtime 10 x64,
WebView2 Evergreen), et le crédit : « Inspiré de OLAP PivotTable Extensions de
Greg Galloway (Ms-PL). PivotScope est une réécriture indépendante sous licence
MIT et ne contient aucun code de ce projet. » Plus un lien vers CubeScope.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "chore: squelette de la solution PivotScope"
```

---

### Task 2: Sous-module CubeScope et adaptateurs

**Files:**
- Create: `external/CubeScope` (sous-module)
- Create: `src/PivotScope.Core/Abstractions/ICubeMetadataReader.cs`
- Create: `src/PivotScope.Core/Abstractions/IMdxExecutor.cs`
- Create: `src/PivotScope.Core/Adapters/CubeScopeSession.cs`
- Modify: `src/PivotScope.Core/PivotScope.Core.csproj`
- Test: `tests/PivotScope.Core.Tests/AbstractionsTests.cs`

**Interfaces:**
- Consumes: `CubeScope.Core.Ssas.SsasSession(server, catalog)`,
  `MetadataService(SsasSession, StateStore)`, `QueryService(SsasSession)`,
  `CubeScope.Core.Models.CubeMeta`.
- Produces:
  - `interface ICubeMetadataReader { Task<CubeMeta> GetCubeMetaAsync(string cube, CancellationToken ct); Task<IReadOnlyList<MemberMeta>> GetMembersAsync(string cube, string hierarchyUniqueName, CancellationToken ct); }`
  - `interface IMdxExecutor { Task<QueryResult> ExecuteAsync(string mdx, CancellationToken ct); }`

- [ ] **Step 1: Ajouter le sous-module et la référence projet**

```bash
git submodule add https://github.com/dasimon/CubeScope.git external/CubeScope
git -C external/CubeScope checkout main
dotnet add src/PivotScope.Core reference external/CubeScope/CubeScope.Core/CubeScope.Core.csproj
```

Si le dépôt distant n'est pas encore poussé, utiliser le chemin local
`../CubeScope` comme URL de sous-module et le corriger avant publication.

- [ ] **Step 2: Écrire le test qui échoue**

```csharp
// tests/PivotScope.Core.Tests/AbstractionsTests.cs
using PivotScope.Core.Abstractions;

public class AbstractionsTests
{
    [Fact]
    public void CubeScopeTypes_AreReachable_ThroughAbstractions()
    {
        // Le seul but : prouver que le sous-module est bien référencé et que
        // les types de CubeScope.Core traversent la frontière d'assembly.
        var t = typeof(ICubeMetadataReader).GetMethod(nameof(ICubeMetadataReader.GetCubeMetaAsync))!;
        Assert.Equal(
            typeof(Task<CubeScope.Core.Models.CubeMeta>),
            t.ReturnType);
    }
}
```

- [ ] **Step 3: Vérifier l'échec**

Run: `dotnet test tests/PivotScope.Core.Tests`
Expected: échec de compilation, `ICubeMetadataReader` introuvable.

- [ ] **Step 4: Écrire les interfaces**

```csharp
// src/PivotScope.Core/Abstractions/ICubeMetadataReader.cs
using CubeScope.Core.Models;

namespace PivotScope.Core.Abstractions;

/// <summary>Lecture des métadonnées du cube. Implémenté par un adaptateur CubeScope.</summary>
public interface ICubeMetadataReader
{
    Task<CubeMeta> GetCubeMetaAsync(string cube, CancellationToken ct = default);

    Task<IReadOnlyList<MemberMeta>> GetMembersAsync(
        string cube, string hierarchyUniqueName, CancellationToken ct = default);
}
```

```csharp
// src/PivotScope.Core/Abstractions/IMdxExecutor.cs
using CubeScope.Core.Models;

namespace PivotScope.Core.Abstractions;

/// <summary>Exécution d'une requête MDX arbitraire sur le cube courant.</summary>
public interface IMdxExecutor
{
    Task<QueryResult> ExecuteAsync(string mdx, CancellationToken ct = default);
}
```

- [ ] **Step 5: Écrire l'adaptateur**

```csharp
// src/PivotScope.Core/Adapters/CubeScopeSession.cs
using CubeScope.Core.Models;
using CubeScope.Core.Ssas;
using CubeScope.Core.State;
using PivotScope.Core.Abstractions;

namespace PivotScope.Core.Adapters;

/// <summary>
/// Regroupe la session SSAS et les services CubeScope pour un couple
/// serveur/catalogue donné, et les expose derrière les abstractions PivotScope.
/// </summary>
public sealed class CubeScopeSession : ICubeMetadataReader, IMdxExecutor, IDisposable
{
    private readonly SsasSession _session;
    private readonly StateStore _store;
    private readonly MetadataService _metadata;
    private readonly QueryService _query;

    private CubeScopeSession(SsasSession session, StateStore store)
    {
        _session = session;
        _store = store;
        _metadata = new MetadataService(session, store);
        _query = new QueryService(session);
    }

    public static async Task<CubeScopeSession> ConnectAsync(
        string server, string catalog, string statePath, CancellationToken ct = default)
    {
        var session = new SsasSession();
        var store = new StateStore(statePath);
        await session.ConnectAsync(server, ct: ct);
        await session.SetCatalogAsync(catalog, ct);
        return new CubeScopeSession(session, store);
    }

    public Task<CubeMeta> GetCubeMetaAsync(string cube, CancellationToken ct = default)
        => _metadata.GetCubeMetaAsync(cube, ct: ct);

    public Task<IReadOnlyList<MemberMeta>> GetMembersAsync(
        string cube, string hierarchyUniqueName, CancellationToken ct = default)
        => _metadata.GetMembersAsync(cube, hierarchyUniqueName, ct: ct);

    public Task<QueryResult> ExecuteAsync(string mdx, CancellationToken ct = default)
        => _query.ExecuteAsync(mdx, ct);

    public void Dispose() { _session.Dispose(); _store.Dispose(); }
}
```

Adapter les appels si les signatures réelles de `MetadataService` /
`StateStore` diffèrent : leur forme fait foi, pas ce plan.

- [ ] **Step 6: Vérifier**

Run: `dotnet test tests/PivotScope.Core.Tests`
Expected: 1 test vert.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat(core): abstractions metadonnees/MDX et adaptateur CubeScope"
```

---

### Task 3: Complément Excel-DNA chargeable + ruban

**Files:**
- Create: `src/PivotScope.AddIn/PivotScope.AddIn.csproj`
- Create: `src/PivotScope.AddIn/AddIn.cs`
- Create: `src/PivotScope.AddIn/Ribbon.cs`
- Create: `src/PivotScope.AddIn/Diagnostics/FileLog.cs`
- Modify: `PivotScope.sln`

**Interfaces:**
- Consumes: rien.
- Produces: `FileLog.Write(string message, Exception? ex = null)` ;
  `PivotScopeRibbon` exposant `OnOpenPane(IRibbonControl)`.

- [ ] **Step 1: Créer le projet et le référencer**

```bash
dotnet new classlib -o src/PivotScope.AddIn -n PivotScope.AddIn
dotnet sln add src/PivotScope.AddIn
dotnet add src/PivotScope.AddIn package ExcelDna.AddIn
dotnet add src/PivotScope.AddIn reference src/PivotScope.Core
```

- [ ] **Step 2: Écrire le log fichier**

```csharp
// src/PivotScope.AddIn/Diagnostics/FileLog.cs
namespace PivotScope.AddIn.Diagnostics;

/// <summary>Log fichier minimal. Un complément qui lève au demarrage est desactive par Excel.</summary>
public static class FileLog
{
    private static readonly object Gate = new();
    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PivotScope", "logs");

    public static void Write(string message, Exception? ex = null)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Dir);
                var file = Path.Combine(Dir, $"pivotscope-{DateTime.Now:yyyyMMdd}.log");
                var line = $"{DateTime.Now:HH:mm:ss.fff} {message}";
                if (ex is not null) line += Environment.NewLine + ex;
                File.AppendAllText(file, line + Environment.NewLine);
            }
        }
        catch { /* le log ne doit jamais faire tomber Excel */ }
    }
}
```

- [ ] **Step 3: Écrire l'add-in et le ruban**

```csharp
// src/PivotScope.AddIn/AddIn.cs
using ExcelDna.Integration;
using PivotScope.AddIn.Diagnostics;

namespace PivotScope.AddIn;

/// <summary>Demarrage minimal : on enregistre le ruban et rien d'autre.</summary>
public sealed class PivotScopeAddIn : IExcelAddIn
{
    public void AutoOpen() => FileLog.Write("PivotScope charge.");
    public void AutoClose() => FileLog.Write("PivotScope decharge.");
}
```

```csharp
// src/PivotScope.AddIn/Ribbon.cs
using System.Runtime.InteropServices;
using ExcelDna.Integration.CustomUI;
using PivotScope.AddIn.Diagnostics;

namespace PivotScope.AddIn;

[ComVisible(true)]
public class PivotScopeRibbon : ExcelRibbon
{
    public override string GetCustomUI(string ribbonId) =>
        """
        <customUI xmlns="http://schemas.microsoft.com/office/2009/07/customui">
          <ribbon>
            <tabs>
              <tab id="tabPivotScope" label="PivotScope">
                <group id="grpMain" label="Analyse">
                  <button id="btnPane" label="Volet PivotScope" size="large"
                          imageMso="TableOfContentsGallery" onAction="OnOpenPane"/>
                </group>
              </tab>
            </tabs>
          </ribbon>
        </customUI>
        """;

    public void OnOpenPane(IRibbonControl control) => FileLog.Write("Bouton volet clique.");
}
```

- [ ] **Step 4: Construire et charger dans Excel**

Run: `dotnet build -c Debug`
Expected: `bin/Debug/net10.0-windows/PivotScope-AddIn64.xll` présent.

Puis dans Excel : Fichier → Options → Compléments → Atteindre (Compléments
Excel) → Parcourir → sélectionner le `.xll`.
Expected: l'onglet **PivotScope** apparaît ; un clic écrit une ligne dans
`%LOCALAPPDATA%\PivotScope\logs\pivotscope-<date>.log`.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(addin): complement Excel-DNA chargeable avec onglet de ruban"
```

---

### Task 4: SPIKE — volet WebView2 et pont `postMessage`

C'est le **point de décision GO / NO-GO** de la phase 0. Objectif : prouver que
la frappe clavier fonctionne dans un WebView2 hébergé en `CustomTaskPane`, et
qu'un aller-retour de message passe.

**Files:**
- Create: `src/PivotScope.AddIn/Pane/PaneControl.cs`
- Create: `src/PivotScope.AddIn/Pane/PaneManager.cs`
- Create: `src/PivotScope.AddIn/Pane/spike.html` (`EmbeddedResource`)
- Modify: `src/PivotScope.AddIn/Ribbon.cs`
- Modify: `src/PivotScope.AddIn/PivotScope.AddIn.csproj`

**Interfaces:**
- Consumes: `FileLog.Write`.
- Produces: `PaneManager.Show()` ; `PaneControl.NavigateToSpikeAsync()`.

- [ ] **Step 1: Ajouter WebView2 et embarquer la page de spike**

```bash
dotnet add src/PivotScope.AddIn package Microsoft.Web.WebView2
```

```xml
<ItemGroup>
  <EmbeddedResource Include="Pane\spike.html" LogicalName="spa/index.html" />
</ItemGroup>
```

- [ ] **Step 2: Écrire la page de spike**

```html
<!doctype html>
<meta charset="utf-8">
<body style="font-family:Segoe UI;background:#18181b;color:#e4e4e7;padding:12px">
  <h3>Spike PivotScope</h3>
  <p>Tapez ici — si le texte s'affiche, le focus clavier fonctionne :</p>
  <input id="t" style="width:100%;padding:6px" placeholder="test clavier">
  <button id="b" style="margin-top:8px;padding:6px 12px">Ping vers .NET</button>
  <pre id="out" style="white-space:pre-wrap"></pre>
  <script>
    const out = document.getElementById('out');
    document.getElementById('b').onclick = () =>
      window.chrome.webview.postMessage(JSON.stringify(
        { id: '1', method: 'ping', params: { text: document.getElementById('t').value } }));
    window.chrome.webview.addEventListener('message', e => {
      out.textContent += 'reçu de .NET : ' + e.data + '\n';
    });
  </script>
</body>
```

- [ ] **Step 3: Écrire le `UserControl` hôte**

```csharp
// src/PivotScope.AddIn/Pane/PaneControl.cs
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using PivotScope.AddIn.Diagnostics;

namespace PivotScope.AddIn.Pane;

/// <summary>
/// UserControl WinForms hebergeant WebView2. Le CustomTaskPane exige un
/// UserForms (support ActiveX) : WPF n'est pas exposable directement.
/// </summary>
public sealed class PaneControl : UserControl
{
    private const string VirtualHost = "pivotscope.local";
    private readonly WebView2 _web = new() { Dock = DockStyle.Fill };

    public event EventHandler<string>? MessageReceived;

    public PaneControl()
    {
        Dock = DockStyle.Fill;
        Controls.Add(_web);
    }

    public async Task InitializeAsync()
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
            core.Settings.AreDevToolsEnabled = true;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.AddWebResourceRequestedFilter($"https://{VirtualHost}/*",
                CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += OnWebResourceRequested;
            core.WebMessageReceived += (_, e) =>
                MessageReceived?.Invoke(this, e.TryGetWebMessageAsString());

            core.Navigate($"https://{VirtualHost}/index.html");
        }
        catch (Exception ex)
        {
            FileLog.Write("Echec initialisation WebView2.", ex);
            Controls.Add(new Label
            {
                Dock = DockStyle.Top,
                AutoSize = false,
                Height = 60,
                Text = "Le volet n'a pas pu demarrer. Voir %LOCALAPPDATA%\\PivotScope\\logs."
            });
        }
    }

    public void PostToWeb(string json) => _web.CoreWebView2?.PostWebMessageAsString(json);

    private void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        var path = new Uri(e.Request.Uri).AbsolutePath.TrimStart('/');
        if (path.Length == 0) path = "index.html";
        var asm = Assembly.GetExecutingAssembly();
        var stream = asm.GetManifestResourceStream("spa/" + path);
        if (stream is null)
        {
            e.Response = _web.CoreWebView2!.Environment.CreateWebResourceResponse(
                null, 404, "Not Found", string.Empty);
            return;
        }
        e.Response = _web.CoreWebView2!.Environment.CreateWebResourceResponse(
            stream, 200, "OK", $"Content-Type: {ContentType(path)}");
    }

    private static string ContentType(string path) => Path.GetExtension(path) switch
    {
        ".html" => "text/html; charset=utf-8",
        ".js" => "text/javascript; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".json" => "application/json; charset=utf-8",
        ".svg" => "image/svg+xml",
        ".woff2" => "font/woff2",
        _ => "application/octet-stream",
    };
}
```

- [ ] **Step 4: Écrire le gestionnaire de volet**

```csharp
// src/PivotScope.AddIn/Pane/PaneManager.cs
using ExcelDna.Integration.CustomUI;
using PivotScope.AddIn.Diagnostics;

namespace PivotScope.AddIn.Pane;

/// <summary>Cree le CustomTaskPane a la demande, une seule fois par session.</summary>
public static class PaneManager
{
    private static CustomTaskPane? _pane;
    private static PaneControl? _control;

    public static PaneControl? Control => _control;

    public static void Show()
    {
        try
        {
            if (_pane is null)
            {
                _pane = CustomTaskPaneFactory.CreateCustomTaskPane(
                    typeof(PaneControl), "PivotScope");
                _pane.Width = 460;
                _control = (PaneControl)_pane.ContentControl;
                _control.MessageReceived += (_, json) => FileLog.Write("web -> net : " + json);
                _ = _control.InitializeAsync();
            }
            _pane.Visible = true;
        }
        catch (Exception ex)
        {
            FileLog.Write("Echec ouverture du volet.", ex);
        }
    }
}
```

- [ ] **Step 5: Brancher le bouton du ruban**

Dans `Ribbon.cs`, remplacer le corps de `OnOpenPane` par
`Pane.PaneManager.Show();`.

- [ ] **Step 6: Vérifier — c'est le GO / NO-GO**

Run: `dotnet build -c Debug`, recharger le `.xll` dans Excel, cliquer
« Volet PivotScope ».

Critères, tous obligatoires :
1. Le volet s'ancre à droite et affiche la page.
2. **Taper dans le champ texte inscrit les caractères** (test du focus clavier).
3. Le bouton « Ping » écrit `web -> net : {"id":"1",...}` dans le log.
4. `PostToWeb` renvoie bien un message affiché dans le `<pre>` (le tester
   depuis `PaneManager` en écho immédiat).

Si le critère 2 échoue : **NO-GO**. Basculer sur le repli documenté dans la
spec — une `Form` non-modale (`Show(new WindowWrapper(handle))`) hébergeant le
même `PaneControl`. Le reste du plan est inchangé, seul `PaneManager` diffère.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat(addin): volet WebView2 et pont postMessage (spike phase 0)"
```

---

## Phase 1 — socle utilisable

### Task 5: Frontière Excel — thread et culture

**Files:**
- Create: `src/PivotScope.AddIn/Excel/ExcelThread.cs`
- Create: `src/PivotScope.Core/Globalization/InvariantFormattingScope.cs`
- Test: `tests/PivotScope.Core.Tests/InvariantFormattingScopeTests.cs`

**Interfaces:**
- Consumes: rien.
- Produces:
  - `InvariantFormattingScope.Enter()` → `IDisposable` qui bascule
    `CurrentCulture` en `en-US` et la restaure.
  - `ExcelThread.RunAsync<T>(Func<T> comWork)` → `Task<T>` exécuté sur le thread
    principal d'Excel, culture basculée.

- [ ] **Step 1: Écrire le test qui échoue**

```csharp
using System.Globalization;
using PivotScope.Core.Globalization;

public class InvariantFormattingScopeTests
{
    [Fact]
    public void Enter_SwitchesToEnUs_AndRestoresPreviousCulture()
    {
        var previous = new CultureInfo("fr-FR");
        Thread.CurrentThread.CurrentCulture = previous;

        using (InvariantFormattingScope.Enter())
        {
            Assert.Equal("en-US", Thread.CurrentThread.CurrentCulture.Name);
            Assert.Equal("1.5", 1.5d.ToString(Thread.CurrentThread.CurrentCulture));
        }

        Assert.Equal("fr-FR", Thread.CurrentThread.CurrentCulture.Name);
    }

    [Fact]
    public void Enter_RestoresCulture_EvenWhenBodyThrows()
    {
        Thread.CurrentThread.CurrentCulture = new CultureInfo("fr-FR");
        Assert.Throws<InvalidOperationException>(() =>
        {
            using (InvariantFormattingScope.Enter()) throw new InvalidOperationException();
        });
        Assert.Equal("fr-FR", Thread.CurrentThread.CurrentCulture.Name);
    }
}
```

- [ ] **Step 2: Vérifier l'échec**

Run: `dotnet test tests/PivotScope.Core.Tests --filter InvariantFormattingScopeTests`
Expected: échec de compilation, type introuvable.

- [ ] **Step 3: Implémenter**

```csharp
// src/PivotScope.Core/Globalization/InvariantFormattingScope.cs
using System.Globalization;

namespace PivotScope.Core.Globalization;

/// <summary>
/// Sur un Excel francais, les API COM qui prennent des chaines de formule
/// attendent l'anglais. On bascule la culture a la frontiere COM, uniquement.
/// </summary>
public static class InvariantFormattingScope
{
    private static readonly CultureInfo EnUs = new("en-US");

    public static IDisposable Enter() => new Scope();

    private sealed class Scope : IDisposable
    {
        private readonly CultureInfo _previous = Thread.CurrentThread.CurrentCulture;
        public Scope() => Thread.CurrentThread.CurrentCulture = EnUs;
        public void Dispose() => Thread.CurrentThread.CurrentCulture = _previous;
    }
}
```

- [ ] **Step 4: Vérifier**

Run: `dotnet test tests/PivotScope.Core.Tests --filter InvariantFormattingScopeTests`
Expected: 2 tests verts.

- [ ] **Step 5: Écrire `ExcelThread`** (non testable hors Excel)

```csharp
// src/PivotScope.AddIn/Excel/ExcelThread.cs
using ExcelDna.Integration;
using PivotScope.Core.Globalization;

namespace PivotScope.AddIn.Excel;

/// <summary>
/// Unique point de passage vers COM. Excel est STA sur son thread principal ;
/// les messages WebView2 arrivent sur le thread UI. Tout appel COM passe ici.
/// </summary>
public static class ExcelThread
{
    public static Task<T> RunAsync<T>(Func<T> comWork)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        ExcelAsyncUtil.QueueAsMacro(() =>
        {
            try
            {
                using (InvariantFormattingScope.Enter()) tcs.SetResult(comWork());
            }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        return tcs.Task;
    }

    public static Task RunAsync(Action comWork) =>
        RunAsync(() => { comWork(); return true; });
}
```

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: frontiere Excel (marshalling COM + bascule de culture)"
```

---

### Task 6: Lecture du TCD actif

**Files:**
- Create: `src/PivotScope.Core/Models/PivotContext.cs`
- Create: `src/PivotScope.AddIn/Excel/PivotTableInspector.cs`
- Test: `tests/PivotScope.Core.Tests/PivotContextTests.cs`

**Interfaces:**
- Consumes: `ExcelThread.RunAsync`.
- Produces:
  - `sealed record PivotContext(bool HasPivot, bool IsOlap, string? Server, string? Catalog, string? Cube, string? Mdx, IReadOnlyList<PivotFieldInfo> Fields, string? Diagnostic)`
  - `sealed record PivotFieldInfo(string Caption, string UniqueName, string Area)` où `Area` ∈ `row|column|filter|data|hidden`.
  - `PivotContext.None(string diagnostic)` — fabrique pour les cas dégradés.
  - `PivotTableInspector.Capture()` → `PivotContext` (à appeler via `ExcelThread`).

- [ ] **Step 1: Écrire le test qui échoue**

```csharp
using PivotScope.Core.Models;

public class PivotContextTests
{
    [Fact]
    public void None_ProducesDegradedContext_WithDiagnostic()
    {
        var ctx = PivotContext.None("Aucun tableau croisé dynamique sélectionné.");

        Assert.False(ctx.HasPivot);
        Assert.False(ctx.IsOlap);
        Assert.Null(ctx.Cube);
        Assert.Empty(ctx.Fields);
        Assert.Equal("Aucun tableau croisé dynamique sélectionné.", ctx.Diagnostic);
    }

    [Fact]
    public void Fields_AreGroupedByArea()
    {
        var ctx = new PivotContext(true, true, "SRV", "CAT", "Cube", "SELECT",
            [new PivotFieldInfo("Devise", "[Devise].[Devise]", "row"),
             new PivotFieldInfo("VL", "[Measures].[VL]", "data")], null);

        Assert.Single(ctx.Fields, f => f.Area == "row");
        Assert.Single(ctx.Fields, f => f.Area == "data");
    }
}
```

- [ ] **Step 2: Vérifier l'échec**

Run: `dotnet test tests/PivotScope.Core.Tests --filter PivotContextTests`
Expected: échec de compilation.

- [ ] **Step 3: Implémenter le modèle**

```csharp
// src/PivotScope.Core/Models/PivotContext.cs
namespace PivotScope.Core.Models;

public sealed record PivotFieldInfo(string Caption, string UniqueName, string Area);

/// <summary>Photo du TCD actif, sans aucun type Excel : traversable par le pont.</summary>
public sealed record PivotContext(
    bool HasPivot,
    bool IsOlap,
    string? Server,
    string? Catalog,
    string? Cube,
    string? Mdx,
    IReadOnlyList<PivotFieldInfo> Fields,
    string? Diagnostic)
{
    public static PivotContext None(string diagnostic) =>
        new(false, false, null, null, null, null, [], diagnostic);
}
```

- [ ] **Step 4: Vérifier**

Run: `dotnet test tests/PivotScope.Core.Tests --filter PivotContextTests`
Expected: 2 tests verts.

- [ ] **Step 5: Implémenter l'inspecteur**

```csharp
// src/PivotScope.AddIn/Excel/PivotTableInspector.cs
using ExcelDna.Integration;
using PivotScope.Core.Models;
using Excel = Microsoft.Office.Interop.Excel;

namespace PivotScope.AddIn.Excel;

/// <summary>
/// Lit le TCD sous le curseur. Ne leve jamais : tout echec devient un
/// PivotContext degrade porteur d'un diagnostic affichable.
/// </summary>
public static class PivotTableInspector
{
    public static PivotContext Capture()
    {
        var app = (Excel.Application)ExcelDnaUtil.Application;

        Excel.PivotTable? pivot = null;
        try { pivot = app.ActiveCell?.PivotTable; } catch { /* hors TCD */ }
        if (pivot is null)
            return PivotContext.None("Placez le curseur dans un tableau croisé dynamique.");

        var cache = pivot.PivotCache();
        if (!cache.OLAP)
            return PivotContext.None("Ce tableau croisé dynamique n'est pas connecté à un cube OLAP.");

        string? mdx = null;
        try { mdx = pivot.MDX; }
        catch { /* documente : erreur si aucun element de donnees */ }

        var (server, catalog) = ConnectionParts(cache);

        var fields = new List<PivotFieldInfo>();
        foreach (Excel.CubeField cf in pivot.CubeFields)
        {
            var area = cf.Orientation switch
            {
                Excel.XlPivotFieldOrientation.xlRowField => "row",
                Excel.XlPivotFieldOrientation.xlColumnField => "column",
                Excel.XlPivotFieldOrientation.xlPageField => "filter",
                Excel.XlPivotFieldOrientation.xlDataField => "data",
                _ => "hidden",
            };
            if (area == "hidden") continue;
            fields.Add(new PivotFieldInfo(cf.Caption, cf.Name, area));
        }

        return new PivotContext(true, true, server, catalog, CubeName(cache), mdx, fields, null);
    }

    private static string? CubeName(Excel.PivotCache cache)
    {
        try { return cache.WorkbookConnection?.OLEDBConnection?.CommandText as string; }
        catch { return null; }
    }

    /// <summary>Extrait Data Source et Initial Catalog de la chaine OLE DB du classeur.</summary>
    private static (string? Server, string? Catalog) ConnectionParts(Excel.PivotCache cache)
    {
        string? cs = null;
        try { cs = cache.WorkbookConnection?.OLEDBConnection?.Connection as string; }
        catch { /* connexion indisponible */ }
        if (string.IsNullOrEmpty(cs)) return (null, null);

        string? server = null, catalog = null;
        foreach (var part in cs.Split(';'))
        {
            var i = part.IndexOf('=');
            if (i <= 0) continue;
            var key = part[..i].Trim();
            var value = part[(i + 1)..].Trim();
            if (key.Equals("Data Source", StringComparison.OrdinalIgnoreCase)) server = value;
            else if (key.Equals("Initial Catalog", StringComparison.OrdinalIgnoreCase)) catalog = value;
        }
        return (server, catalog);
    }
}
```

`cache.WorkbookConnection.OLEDBConnection.CommandText` porte le nom du cube
pour une connexion `xlCmdCube` ; si la valeur est vide, l'interface affichera
un sélecteur de cube alimenté par `ICubeMetadataReader`.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(addin): capture du contexte du TCD actif"
```

---

### Task 7: Pont typé et routage

**Files:**
- Create: `src/PivotScope.Core/Bridge/BridgeMessages.cs`
- Create: `src/PivotScope.Core/Bridge/BridgeRouter.cs`
- Create: `src/PivotScope.AddIn/Pane/WebBridge.cs`
- Test: `tests/PivotScope.Core.Tests/BridgeRouterTests.cs`

**Interfaces:**
- Consumes: rien.
- Produces:
  - `sealed record BridgeRequest(string Id, string Method, JsonElement? Params)`
  - `sealed record BridgeResponse(string Id, bool Ok, JsonElement? Result, string? Error)`
  - `BridgeRouter.Register(string method, Func<JsonElement?, CancellationToken, Task<object?>> handler)`
  - `BridgeRouter.DispatchAsync(string requestJson, CancellationToken ct)` → `Task<string>` (JSON de réponse)

- [ ] **Step 1: Écrire les tests qui échouent**

```csharp
using PivotScope.Core.Bridge;

public class BridgeRouterTests
{
    [Fact]
    public async Task DispatchAsync_InvokesHandler_AndSerializesResult()
    {
        var router = new BridgeRouter();
        router.Register("ping", (_, _) => Task.FromResult<object?>(new { pong = true }));

        var json = await router.DispatchAsync("""{"id":"7","method":"ping"}""", default);

        Assert.Contains("\"id\":\"7\"", json);
        Assert.Contains("\"ok\":true", json);
        Assert.Contains("\"pong\":true", json);
    }

    [Fact]
    public async Task DispatchAsync_UnknownMethod_ReturnsErrorNotThrow()
    {
        var router = new BridgeRouter();
        var json = await router.DispatchAsync("""{"id":"1","method":"nope"}""", default);

        Assert.Contains("\"ok\":false", json);
        Assert.Contains("nope", json);
    }

    [Fact]
    public async Task DispatchAsync_HandlerThrows_ReturnsErrorWithMessage()
    {
        var router = new BridgeRouter();
        router.Register("boom", (_, _) => throw new InvalidOperationException("cube absent"));

        var json = await router.DispatchAsync("""{"id":"2","method":"boom"}""", default);

        Assert.Contains("\"ok\":false", json);
        Assert.Contains("cube absent", json);
    }

    [Fact]
    public async Task DispatchAsync_MalformedJson_ReturnsErrorNotThrow()
    {
        var router = new BridgeRouter();
        var json = await router.DispatchAsync("pas du json", default);
        Assert.Contains("\"ok\":false", json);
    }
}
```

- [ ] **Step 2: Vérifier l'échec**

Run: `dotnet test tests/PivotScope.Core.Tests --filter BridgeRouterTests`
Expected: échec de compilation.

- [ ] **Step 3: Implémenter**

```csharp
// src/PivotScope.Core/Bridge/BridgeMessages.cs
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PivotScope.Core.Bridge;

public sealed record BridgeRequest(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("method")] string Method,
    [property: JsonPropertyName("params")] JsonElement? Params);

public sealed record BridgeResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("result")] object? Result,
    [property: JsonPropertyName("error")] string? Error);
```

```csharp
// src/PivotScope.Core/Bridge/BridgeRouter.cs
using System.Text.Json;

namespace PivotScope.Core.Bridge;

/// <summary>
/// Routage des messages du volet. Ne leve jamais : toute erreur devient une
/// reponse ok=false, sinon la SPA reste bloquee sur une promesse en attente.
/// </summary>
public sealed class BridgeRouter
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly Dictionary<string, Func<JsonElement?, CancellationToken, Task<object?>>> _handlers = new(StringComparer.Ordinal);

    public void Register(string method, Func<JsonElement?, CancellationToken, Task<object?>> handler)
        => _handlers[method] = handler;

    public async Task<string> DispatchAsync(string requestJson, CancellationToken ct)
    {
        string id = "0";
        try
        {
            var request = JsonSerializer.Deserialize<BridgeRequest>(requestJson, Json)
                          ?? throw new InvalidOperationException("Message vide.");
            id = request.Id;

            if (!_handlers.TryGetValue(request.Method, out var handler))
                return Serialize(new BridgeResponse(id, false, null,
                    $"Méthode inconnue : {request.Method}"));

            var result = await handler(request.Params, ct).ConfigureAwait(false);
            return Serialize(new BridgeResponse(id, true, result, null));
        }
        catch (Exception ex)
        {
            return Serialize(new BridgeResponse(id, false, null, ex.Message));
        }
    }

    private static string Serialize(BridgeResponse response) =>
        JsonSerializer.Serialize(response, Json);
}
```

- [ ] **Step 4: Vérifier**

Run: `dotnet test tests/PivotScope.Core.Tests --filter BridgeRouterTests`
Expected: 4 tests verts.

- [ ] **Step 5: Câbler le pont côté add-in**

```csharp
// src/PivotScope.AddIn/Pane/WebBridge.cs
using PivotScope.AddIn.Diagnostics;
using PivotScope.AddIn.Excel;
using PivotScope.Core.Bridge;

namespace PivotScope.AddIn.Pane;

/// <summary>Enregistre les methodes exposees a la SPA et relaie les reponses.</summary>
public sealed class WebBridge
{
    private readonly BridgeRouter _router = new();
    private readonly PaneControl _control;

    public WebBridge(PaneControl control)
    {
        _control = control;
        _router.Register("pivot.context",
            async (_, _) => await ExcelThread.RunAsync(PivotTableInspector.Capture));
        _control.MessageReceived += OnMessage;
    }

    public BridgeRouter Router => _router;

    private async void OnMessage(object? sender, string json)
    {
        try
        {
            var response = await _router.DispatchAsync(json, CancellationToken.None);
            _control.PostToWeb(response);
        }
        catch (Exception ex) { FileLog.Write("Echec de dispatch du pont.", ex); }
    }
}
```

Instancier `new WebBridge(_control)` dans `PaneManager.Show()` après
`InitializeAsync`, et retirer l'abonnement de log du spike.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: pont typé SPA <-> .NET avec routage sans exception"
```

---

### Task 8: Résolution des clés en membres (cœur de Filter List)

**Files:**
- Create: `src/PivotScope.Core/Filtering/MemberResolver.cs`
- Test: `tests/PivotScope.Core.Tests/MemberResolverTests.cs`

**Interfaces:**
- Consumes: `IMdxExecutor.ExecuteAsync`, `CubeScope.Core.Models.QueryResult`.
- Produces:
  - `sealed record MemberResolution(IReadOnlyList<string> UniqueNames, IReadOnlyList<string> Unresolved)`
  - `MemberResolver(IMdxExecutor executor)`
  - `Task<MemberResolution> ResolveAsync(string cube, string levelUniqueName, IEnumerable<string> keys, CancellationToken ct)`

La méthode construit un nom unique candidat `{level}.&[clé]` par clé, puis
vérifie leur existence en une seule requête MDX avec un membre calculé
`StrToMember(...).Properties("MEMBER_CAPTION")` par clé — jamais
`$SYSTEM.MDSCHEMA_MEMBERS`, qui ne supporte pas `IN` et scanne toute la
dimension. Si la requête groupée échoue (une seule clé périmée fait tomber le
paquet), repli clé par clé.

- [ ] **Step 1: Écrire les tests qui échouent**

```csharp
using CubeScope.Core.Models;
using PivotScope.Core.Abstractions;
using PivotScope.Core.Filtering;

public class MemberResolverTests
{
    private sealed class FakeExecutor : IMdxExecutor
    {
        public List<string> Queries { get; } = [];
        public Func<string, QueryResult>? Responder { get; set; }

        public Task<QueryResult> ExecuteAsync(string mdx, CancellationToken ct = default)
        {
            Queries.Add(mdx);
            if (Responder is null) throw new InvalidOperationException("pas de reponse");
            return Task.FromResult(Responder(mdx));
        }
    }

    private static QueryResult Row(params string?[] captions) =>
        new([.. captions.Select((_, i) => $"__cap{i}")],
            [[.. captions.Cast<object?>()]], 0, null);

    [Fact]
    public async Task ResolveAsync_BuildsOneBatchedQuery_ForAllKeys()
    {
        var exec = new FakeExecutor { Responder = _ => Row("Euro", "Dollar") };
        var resolver = new MemberResolver(exec);

        var result = await resolver.ResolveAsync(
            "Ventes", "[Devise].[Devise].[Devise]", ["EUR", "USD"], default);

        Assert.Single(exec.Queries);
        Assert.Contains("StrToMember", exec.Queries[0]);
        Assert.Equal(
            ["[Devise].[Devise].[Devise].&[EUR]", "[Devise].[Devise].[Devise].&[USD]"],
            result.UniqueNames);
        Assert.Empty(result.Unresolved);
    }

    [Fact]
    public async Task ResolveAsync_ReportsUnresolvedKeys_WithoutFailing()
    {
        // caption nulle = membre inexistant
        var exec = new FakeExecutor { Responder = _ => Row("Euro", null) };
        var resolver = new MemberResolver(exec);

        var result = await resolver.ResolveAsync(
            "Ventes", "[Devise].[Devise].[Devise]", ["EUR", "XXX"], default);

        Assert.Equal(["[Devise].[Devise].[Devise].&[EUR]"], result.UniqueNames);
        Assert.Equal(["XXX"], result.Unresolved);
    }

    [Fact]
    public async Task ResolveAsync_FallsBackToPerKey_WhenBatchQueryFails()
    {
        var exec = new FakeExecutor();
        var calls = 0;
        exec.Responder = mdx =>
        {
            calls++;
            if (calls == 1) throw new InvalidOperationException("membre périmé");
            return Row("Euro");
        };
        var resolver = new MemberResolver(exec);

        var result = await resolver.ResolveAsync(
            "Ventes", "[Devise].[Devise].[Devise]", ["EUR", "XXX"], default);

        Assert.True(exec.Queries.Count >= 3); // 1 groupée + 2 individuelles
        Assert.Single(result.UniqueNames);
    }

    [Theory]
    [InlineData("EUR", "[D].[H].[L].&[EUR]")]
    [InlineData("A&B", "[D].[H].[L].&[A&B]")]
    [InlineData("  EUR  ", "[D].[H].[L].&[EUR]")]
    public void BuildUniqueName_AppendsKeySegment_AndTrims(string key, string expected)
        => Assert.Equal(expected, MemberResolver.BuildUniqueName("[D].[H].[L]", key));

    [Fact]
    public async Task ResolveAsync_IgnoresBlankKeys_AndDeduplicates()
    {
        var exec = new FakeExecutor { Responder = _ => Row("Euro") };
        var resolver = new MemberResolver(exec);

        var result = await resolver.ResolveAsync(
            "C", "[D].[H].[L]", ["EUR", "", "  ", "EUR"], default);

        Assert.Single(result.UniqueNames);
    }
}
```

- [ ] **Step 2: Vérifier l'échec**

Run: `dotnet test tests/PivotScope.Core.Tests --filter MemberResolverTests`
Expected: échec de compilation.

- [ ] **Step 3: Implémenter**

```csharp
// src/PivotScope.Core/Filtering/MemberResolver.cs
using System.Text;
using PivotScope.Core.Abstractions;

namespace PivotScope.Core.Filtering;

public sealed record MemberResolution(
    IReadOnlyList<string> UniqueNames,
    IReadOnlyList<string> Unresolved);

/// <summary>
/// Traduit une liste de cles metier (ISIN, codes fonds) en noms uniques de
/// membres. Piege connu : ne JAMAIS passer par $SYSTEM.MDSCHEMA_MEMBERS, qui
/// ne supporte pas IN et scanne toute la dimension. On resout par MDX
/// StrToMember, en une requete groupee, avec repli cle par cle.
/// </summary>
public sealed class MemberResolver(IMdxExecutor executor)
{
    public static string BuildUniqueName(string levelUniqueName, string key)
        => $"{levelUniqueName}.&[{key.Trim()}]";

    public async Task<MemberResolution> ResolveAsync(
        string cube, string levelUniqueName, IEnumerable<string> keys, CancellationToken ct = default)
    {
        var distinct = keys
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => k.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (distinct.Count == 0) return new MemberResolution([], []);

        var found = new List<string>();
        var missing = new List<string>();

        try
        {
            var captions = await ProbeAsync(cube, levelUniqueName, distinct, ct).ConfigureAwait(false);
            for (var i = 0; i < distinct.Count; i++)
            {
                if (i < captions.Count && captions[i] is not null)
                    found.Add(BuildUniqueName(levelUniqueName, distinct[i]));
                else
                    missing.Add(distinct[i]);
            }
        }
        catch
        {
            // Une seule reference perimee fait echouer le paquet entier : on
            // repasse cle par cle pour isoler les fautives.
            found.Clear();
            missing.Clear();
            foreach (var key in distinct)
            {
                try
                {
                    var one = await ProbeAsync(cube, levelUniqueName, [key], ct).ConfigureAwait(false);
                    if (one.Count > 0 && one[0] is not null)
                        found.Add(BuildUniqueName(levelUniqueName, key));
                    else
                        missing.Add(key);
                }
                catch { missing.Add(key); }
            }
        }

        return new MemberResolution(found, missing);
    }

    private async Task<IReadOnlyList<string?>> ProbeAsync(
        string cube, string levelUniqueName, IReadOnlyList<string> keys, CancellationToken ct)
    {
        var mdx = new StringBuilder("WITH ");
        for (var i = 0; i < keys.Count; i++)
        {
            var unique = BuildUniqueName(levelUniqueName, keys[i]);
            mdx.Append($"MEMBER [Measures].[__cap{i}] AS StrToMember(\"{unique}\").Properties(\"MEMBER_CAPTION\") ");
        }
        mdx.Append("SELECT {");
        mdx.AppendJoin(',', Enumerable.Range(0, keys.Count).Select(i => $"[Measures].[__cap{i}]"));
        mdx.Append($"}} ON 0 FROM [{cube}]");

        var result = await executor.ExecuteAsync(mdx.ToString(), ct).ConfigureAwait(false);
        if (result.Rows.Count == 0) return [];

        return [.. result.Rows[0].Select(v => v as string)];
    }
}
```

Adapter la lecture de `QueryResult` (`Rows`, `Columns`) à sa forme réelle dans
`CubeScope.Core/Models/QueryResult.cs` ; ajuster aussi le helper `Row` des
tests en conséquence.

- [ ] **Step 4: Vérifier**

Run: `dotnet test tests/PivotScope.Core.Tests --filter MemberResolverTests`
Expected: 7 tests verts.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(core): resolution des cles metier en noms uniques de membres"
```

---

### Task 9: Application du filtre au TCD

**Files:**
- Create: `src/PivotScope.AddIn/Excel/PivotFilterApplier.cs`
- Modify: `src/PivotScope.AddIn/Pane/WebBridge.cs`

**Interfaces:**
- Consumes: `MemberResolution`, `ExcelThread.RunAsync`.
- Produces: `PivotFilterApplier.Apply(string cubeFieldName, IReadOnlyList<string> uniqueNames)`.

- [ ] **Step 1: Implémenter**

```csharp
// src/PivotScope.AddIn/Excel/PivotFilterApplier.cs
using ExcelDna.Integration;
using Excel = Microsoft.Office.Interop.Excel;

namespace PivotScope.AddIn.Excel;

/// <summary>
/// Applique un filtre manuel inclusif. Pieges documentes :
/// - ClearManualFilter doit etre appele sur le CubeField, pas le PivotField
///   (erreur d'execution en OLAP sinon) ;
/// - VisibleItemsList reste vide et n'accepte rien si
///   IncludeNewItemsInFilter vaut True : il faut le forcer a False d'abord.
/// </summary>
public static class PivotFilterApplier
{
    public static void Apply(string cubeFieldName, IReadOnlyList<string> uniqueNames)
    {
        if (uniqueNames.Count == 0)
            throw new InvalidOperationException("Aucun membre résolu : filtre non appliqué.");

        var app = (Excel.Application)ExcelDnaUtil.Application;
        var pivot = app.ActiveCell?.PivotTable
                    ?? throw new InvalidOperationException(
                        "Placez le curseur dans un tableau croisé dynamique.");

        Excel.CubeField? target = null;
        foreach (Excel.CubeField cf in pivot.CubeFields)
            if (string.Equals(cf.Name, cubeFieldName, StringComparison.Ordinal)) { target = cf; break; }

        if (target is null)
            throw new InvalidOperationException($"Champ introuvable dans le TCD : {cubeFieldName}");

        target.ClearManualFilter();
        target.IncludeNewItemsInFilter = false;
        target.PivotFields[1].VisibleItemsList = uniqueNames.ToArray();
    }
}
```

- [ ] **Step 2: Exposer la méthode au pont**

Dans `WebBridge`, enregistrer `pivot.filterList` : désérialiser
`{ cubeField, level, keys[] }`, résoudre via `MemberResolver` (hors thread UI),
puis appeler `ExcelThread.RunAsync(() => PivotFilterApplier.Apply(...))` et
renvoyer `{ applied, unresolved[] }`.

- [ ] **Step 3: Vérifier dans Excel**

Sur un TCD branché au cube de test, avec une hiérarchie à clés : coller 3 clés
valides + 1 invalide.
Expected: le TCD est filtré sur les 3 membres valides ; le volet liste la clé
non résolue ; aucune exception, aucune `MessageBox`.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "feat(addin): application du filtre par liste de cles"
```

---

### Task 10: SPA Vue 3 — volet, MDX du TCD, explorateur, filtre

**Files:**
- Create: `src/PivotScope.Web/` (Vite + Vue 3 + TS)
- Create: `src/PivotScope.Web/src/bridge.ts`
- Create: `src/PivotScope.Web/src/App.vue`
- Create: `src/PivotScope.Web/src/components/{PivotHeader,MdxView,MetadataTree,FilterList}.vue`
- Modify: `src/PivotScope.AddIn/PivotScope.AddIn.csproj` (embarquement du build)
- Delete: `src/PivotScope.AddIn/Pane/spike.html`

**Interfaces:**
- Consumes: méthodes du pont `pivot.context`, `pivot.filterList`, `cube.meta`.
- Produces: `call<T>(method: string, params?: unknown): Promise<T>` dans `bridge.ts`.

- [ ] **Step 1: Créer la SPA**

```bash
cd src && npm create vite@latest PivotScope.Web -- --template vue-ts && cd PivotScope.Web && npm install
```

Dans `vite.config.ts`, poser `base: './'` pour que les chemins soient relatifs
à l'origine virtuelle.

- [ ] **Step 2: Écrire le client du pont**

```ts
// src/PivotScope.Web/src/bridge.ts
type Pending = { resolve: (v: unknown) => void; reject: (e: Error) => void }
const pending = new Map<string, Pending>()
let seq = 0

declare global {
  interface Window { chrome: { webview: {
    postMessage(m: string): void
    addEventListener(t: 'message', h: (e: { data: string }) => void): void
  } } }
}

window.chrome.webview.addEventListener('message', e => {
  const r = JSON.parse(e.data) as { id: string; ok: boolean; result?: unknown; error?: string }
  const p = pending.get(r.id)
  if (!p) return
  pending.delete(r.id)
  r.ok ? p.resolve(r.result) : p.reject(new Error(r.error ?? 'Erreur inconnue'))
})

export function call<T>(method: string, params?: unknown): Promise<T> {
  const id = String(++seq)
  return new Promise<T>((resolve, reject) => {
    pending.set(id, { resolve: resolve as (v: unknown) => void, reject })
    window.chrome.webview.postMessage(JSON.stringify({ id, method, params }))
  })
}
```

- [ ] **Step 3: Écrire les composants**

`PivotHeader` affiche serveur / catalogue / cube, ou le `diagnostic` quand
`hasPivot` est faux. `MdxView` affiche `mdx` en lecture seule avec un bouton
« copier ». `MetadataTree` appelle `cube.meta` et rend un arbre filtrable.
`FilterList` propose une zone de collage, un sélecteur de champ alimenté par
`context.fields`, et affiche `unresolved[]` après application.

Un bandeau d'erreur en haut du volet affiche le message de toute promesse
rejetée. **Aucune boîte de dialogue.**

- [ ] **Step 4: Embarquer le build dans l'assembly**

Reprendre la cible `EmbedSpa` de CubeScope, avec ses trois pièges déjà
identifiés : hooker `BeforeTargets="PrepareForBuild"` (à `CoreCompile` la liste
des ressources est déjà figée, 0 ressource embarquée) ; passer par un item
intermédiaire qualifié pour le `LogicalName` (sur un Include auto-référencé,
`%(Filename)` s'évalue vide → collision `CS1508`) ; utiliser le glob `**\*.*`
et non `**\*`, qui matche aussi les dossiers.

- [ ] **Step 5: Vérifier**

Run: `npm run build` puis `dotnet build -c Release`, recharger le `.xll`.
Expected: le volet affiche le serveur, le catalogue, le cube et le MDX du TCD
actif ; l'arbre des métadonnées se remplit ; un collage de clés filtre le TCD.
Placer le curseur hors d'un TCD : le volet affiche le message de dégradation,
sans erreur.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(web): volet Vue 3 (contexte, MDX, metadonnees, filtre par liste)"
```

---

### Task 11: CI, recette manuelle, publication

**Files:**
- Create: `.github/workflows/ci.yml`
- Create: `docs/recette.md`
- Modify: `CHANGELOG.md`

- [ ] **Step 1: Écrire la CI**

```yaml
name: CI
on: [push, pull_request]
jobs:
  build:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
        with: { submodules: recursive }
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: '10.0.x' }
      - uses: actions/setup-node@v4
        with: { node-version: '22' }
      - run: dotnet restore
      - run: dotnet build -c Release --no-restore
      - run: dotnet test -c Release --no-build --filter "Category!=Integration"
      - run: dotnet list package --vulnerable --include-transitive
```

- [ ] **Step 2: Écrire `docs/recette.md`**

Checklist de recette manuelle, une case par point : chargement du complément,
onglet visible, volet qui s'ancre, frappe clavier dans le volet, suivi du TCD
actif, MDX affiché, dégradation hors TCD, dégradation sur TCD non-OLAP, arbre
des métadonnées, filtre par liste avec clé invalide, déchargement propre.

- [ ] **Step 3: Vérifier la gate complète**

Run: `dotnet build -c Release && dotnet test -c Release --filter "Category!=Integration" && npm --prefix src/PivotScope.Web run build`
Expected: build sans avertissement, tous les tests verts, build SPA sans erreur.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "ci: build, tests et audit de vulnerabilites ; recette manuelle"
```

---

## Auto-revue du plan

**Couverture de la spec (phases 0 et 1)** — ruban : Task 3. Volet WebView2 :
Task 4. Suivi du TCD actif : Tasks 6 et 7. `PivotTable.MDX` : Task 6 puis
Task 10. Explorateur de métadonnées : Tasks 2 et 10. Filter List : Tasks 8, 9
et 10. Marshalling COM et culture : Task 5. Dégradation sans `MessageBox` :
Tasks 3, 6, 7 et 10. Log fichier : Task 3. Conventions de dépôt : Task 1.
CI et audit de vulnérabilités : Task 11. Recette manuelle : Task 11.

Hors périmètre de ce plan, par décision de la spec : calculs, bibliothèque, MDX
libre → plage, IA, « d'où vient ce chiffre », Profiler, Clear Cache,
`ShowInFieldList`, `EnableRefresh`, i18n. Ils relèvent des phases 2 à 5, qui
recevront chacune leur plan.

**Cohérence des types** — `PivotContext` / `PivotFieldInfo` (Task 6) sont
consommés tels quels par Tasks 7 et 10. `MemberResolution` (Task 8) est
consommé par Task 9. `BridgeRequest` / `BridgeResponse` (Task 7) sont la forme
exacte lue par `bridge.ts` (Task 10) : `{id, ok, result, error}`.
`ICubeMetadataReader` / `IMdxExecutor` (Task 2) sont implémentés par
`CubeScopeSession` (Task 2) et consommés par `MemberResolver` (Task 8).

**Deux points où le code réel fait foi sur ce plan** : la forme exacte de
`QueryResult` (Task 8, Step 3) et les signatures de `MetadataService` /
`StateStore` (Task 2, Step 5). Vérifier dans le sous-module avant d'écrire, et
ajuster tests et implémentation ensemble.
