# PivotScope — plan d'implémentation, phase 2

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** écrire du MDX dans le volet avec un vrai éditeur, l'exécuter vers une
plage Excel, et créer des mesures et membres calculés sur le TCD — avec une
bibliothèque pour les réutiliser.

**Architecture:** Monaco est repris du sous-module CubeScope (grammaire MDX,
complétion, thème) plutôt que réécrit. La logique testable (projection d'un
`QueryResult` en plage, validation des noms de calculs, bibliothèque SQLite)
vit dans `PivotScope.Core` ; l'interop reste confinée à `PivotScope.AddIn`.

**Tech Stack:** inchangé, plus `monaco-editor` 0.56 côté SPA et
`Microsoft.Data.Sqlite` (déjà tiré par `CubeScope.Core`).

## Global Constraints

Celles de la phase 1 s'appliquent intégralement. Rappels qui mordent ici :

- `PivotScope.Core` ne référence **jamais** `Microsoft.Office.Interop.Excel`.
- Tout appel COM passe par `ExcelThread` ; toute requête SSAS reste hors du
  thread UI.
- **Aucune `MessageBox`** : bandeau dans le volet, et log fichier.
- Cible `net10.0-windows` x64, `TreatWarningsAsErrors`, versions centralisées.
- Le `.xll` est verrouillé tant qu'Excel est ouvert : fermer Excel avant tout
  `dotnet build`.

## Acquis de la phase 1 à ne pas réapprendre

- Un `CubeField` de hiérarchie expose **un `PivotField` par niveau**
  (`PivotFilterApplier.FindPivotFieldForLevel`).
- `StrToMember` sur un membre inexistant **ne lève pas**, il renvoie null.
- Énumérer un niveau en MDX coûte ~79 ms pour 3 157 membres ; c'est
  `$SYSTEM.MDSCHEMA_MEMBERS` qui est interdit, pas le MDX.
- Le contrôle du volet doit rester `[ComVisible]` avec une interface COM par
  défaut et **aucun membre public supplémentaire**.

---

## Structure des fichiers ajoutés

| Fichier | Responsabilité |
|---|---|
| `src/PivotScope.Core/Query/RangeProjection.cs` | `QueryResult` → tableau rectangulaire prêt à coller |
| `src/PivotScope.Core/Calculations/CalculationDefinition.cs` | définition d'un calcul (nom, MDX, type, dossier, format) |
| `src/PivotScope.Core/Calculations/CalculationValidator.cs` | validation et normalisation du nom et de l'expression |
| `src/PivotScope.Core/Calculations/CalculationLibrary.cs` | persistance SQLite des calculs réutilisables |
| `src/PivotScope.AddIn/Interop/SheetWriter.cs` | écriture d'une plage dans la feuille active |
| `src/PivotScope.AddIn/Interop/CalculationApplier.cs` | `AddCalculatedMember` + affichage dans le TCD |
| `src/PivotScope.AddIn/Interop/PivotComfort.cs` | `ShowInFieldList`, `EnableRefresh` |
| `src/PivotScope.Web/src/monaco-*.ts` | repris du sous-module CubeScope |
| `src/PivotScope.Web/src/components/MdxEditor.vue` | éditeur Monaco réutilisable |
| `src/PivotScope.Web/src/components/QueryPanel.vue` | MDX libre → plage |
| `src/PivotScope.Web/src/components/CalcPanel.vue` | calculs du TCD + bibliothèque |
| `src/PivotScope.Web/src/components/ComfortPanel.vue` | confort de construction du TCD |

---

### Task 1: Projection d'un résultat MDX en plage

**Files:**
- Create: `src/PivotScope.Core/Query/RangeProjection.cs`
- Test: `tests/PivotScope.Core.Tests/RangeProjectionTests.cs`

**Interfaces:**
- Consumes: `CubeScope.Core.Models.QueryResult`, `GridColumn`.
- Produces: `RangeProjection.ToGrid(QueryResult result, bool includeHeaders)` →
  `object?[,]` (indexé `[ligne, colonne]`, 0-based).

- [ ] **Step 1: Écrire les tests qui échouent**

```csharp
using CubeScope.Core.Models;
using PivotScope.Core.Query;

namespace PivotScope.Core.Tests;

public class RangeProjectionTests
{
    private static QueryResult Result() => new(
        [new GridColumn("c0", "Devise", true), new GridColumn("c1", "VL", false)],
        [
            new Dictionary<string, object?> { ["c0"] = "EUR", ["c1"] = 1.5d },
            new Dictionary<string, object?> { ["c0"] = "USD", ["c1"] = null },
        ],
        2, 2, 12);

    [Fact]
    public void ToGrid_WithHeaders_PutsHeadersOnFirstRow()
    {
        var grid = RangeProjection.ToGrid(Result(), includeHeaders: true);

        Assert.Equal(3, grid.GetLength(0));
        Assert.Equal(2, grid.GetLength(1));
        Assert.Equal("Devise", grid[0, 0]);
        Assert.Equal("VL", grid[0, 1]);
        Assert.Equal("EUR", grid[1, 0]);
        Assert.Equal(1.5d, grid[2 - 1, 1]);
    }

    [Fact]
    public void ToGrid_WithoutHeaders_StartsAtData()
    {
        var grid = RangeProjection.ToGrid(Result(), includeHeaders: false);

        Assert.Equal(2, grid.GetLength(0));
        Assert.Equal("EUR", grid[0, 0]);
    }

    [Fact]
    public void ToGrid_KeepsNullCells_RatherThanEmptyStrings()
    {
        // Une cellule vide et un zéro ne veulent pas dire la même chose :
        // écrire "" dans Excel casserait les formules en aval.
        var grid = RangeProjection.ToGrid(Result(), includeHeaders: false);

        Assert.Null(grid[1, 1]);
    }

    [Fact]
    public void ToGrid_EmptyResult_ReturnsHeadersOnly()
    {
        var empty = new QueryResult([new GridColumn("c0", "Devise", true)], [], 0, 1, 0);

        var grid = RangeProjection.ToGrid(empty, includeHeaders: true);

        Assert.Equal(1, grid.GetLength(0));
        Assert.Equal("Devise", grid[0, 0]);
    }

    [Fact]
    public void ToGrid_NoColumnsAtAll_ReturnsEmptyGrid()
    {
        var nothing = new QueryResult([], [], 0, 0, 0);

        var grid = RangeProjection.ToGrid(nothing, includeHeaders: true);

        Assert.Equal(0, grid.Length);
    }
}
```

- [ ] **Step 2: Vérifier l'échec**

Run: `dotnet test tests/PivotScope.Core.Tests --filter RangeProjectionTests`
Expected: échec de compilation, `RangeProjection` introuvable.

- [ ] **Step 3: Implémenter**

```csharp
using CubeScope.Core.Models;

namespace PivotScope.Core.Query;

/// <summary>
/// Met un résultat MDX à plat pour Excel. Un tableau rectangulaire écrit en une
/// seule affectation vaut mille écritures cellule par cellule : c'est la
/// différence entre instantané et interminable sur un gros crossjoin.
/// </summary>
public static class RangeProjection
{
    public static object?[,] ToGrid(QueryResult result, bool includeHeaders)
    {
        var columns = result.Columns.Count;
        if (columns == 0) return new object?[0, 0];

        var offset = includeHeaders ? 1 : 0;
        var grid = new object?[result.Rows.Count + offset, columns];

        if (includeHeaders)
            for (var c = 0; c < columns; c++)
                grid[0, c] = result.Columns[c].Header;

        for (var r = 0; r < result.Rows.Count; r++)
        {
            var row = result.Rows[r];
            for (var c = 0; c < columns; c++)
            {
                // On laisse les null tels quels : une cellule vide et un zéro
                // ne sont pas la même chose pour les formules en aval.
                row.TryGetValue(result.Columns[c].Field, out var value);
                grid[r + offset, c] = value;
            }
        }

        return grid;
    }
}
```

- [ ] **Step 4: Vérifier**

Run: `dotnet test tests/PivotScope.Core.Tests --filter RangeProjectionTests`
Expected: 5 tests verts.

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat(core): projection d'un resultat MDX en plage rectangulaire"
```

---

### Task 2: Écriture dans la feuille et exécution MDX libre

**Files:**
- Create: `src/PivotScope.AddIn/Interop/SheetWriter.cs`
- Modify: `src/PivotScope.AddIn/Pane/WebBridge.cs`

**Interfaces:**
- Consumes: `RangeProjection.ToGrid`, `ExcelThread.RunAsync`, `IMdxExecutor`.
- Produces: `SheetWriter.Write(object?[,] grid, bool newSheet)` →
  `string` (adresse de la plage écrite, ex. `Feuil2!A1:C42`).
- Méthode du pont : `query.run` → `{ mdx, newSheet, includeHeaders }` →
  `{ address, rows, columns, durationMs }`.

- [ ] **Step 1: Écrire `SheetWriter`**

```csharp
using ExcelDna.Integration;
using Xl = Microsoft.Office.Interop.Excel;

namespace PivotScope.AddIn.Interop;

/// <summary>
/// Écrit un tableau rectangulaire dans la feuille. Une seule affectation à
/// Range.Value2 : écrire cellule par cellule via COM est des ordres de
/// grandeur plus lent. À appeler via <see cref="ExcelThread"/>.
/// </summary>
public static class SheetWriter
{
    public static string Write(object?[,] grid, bool newSheet)
    {
        var rows = grid.GetLength(0);
        var columns = grid.GetLength(1);
        if (rows == 0 || columns == 0)
            throw new InvalidOperationException("La requête n'a produit aucune donnée.");

        var app = (Xl.Application)ExcelDnaUtil.Application;
        var book = app.ActiveWorkbook
            ?? throw new InvalidOperationException("Aucun classeur ouvert.");

        Xl.Range anchor;
        if (newSheet)
        {
            var sheet = (Xl.Worksheet)book.Worksheets.Add();
            anchor = (Xl.Range)sheet.Cells[1, 1];
        }
        else
        {
            anchor = app.ActiveCell
                ?? throw new InvalidOperationException("Aucune cellule active.");
        }

        var target = anchor.Resize[rows, columns];
        target.Value2 = grid;
        target.Worksheet.Activate();
        return $"{target.Worksheet.Name}!{target.Address[false, false]}";
    }
}
```

- [ ] **Step 2: Exposer la méthode au pont**

Dans `WebBridge`, enregistrer `query.run` : lire `mdx`, `newSheet`,
`includeHeaders` des paramètres ; exécuter via la session (hors thread UI) ;
projeter avec `RangeProjection.ToGrid` ; écrire via
`ExcelThread.RunAsync(() => SheetWriter.Write(grid, newSheet))` ; renvoyer
l'adresse, le nombre de lignes et de colonnes, et la durée mesurée.

- [ ] **Step 3: Vérifier dans Excel**

Exécuter `SELECT {[Measures].DefaultMember} ON 0 FROM [Ventes]`.
Expected: une plage écrite, adresse renvoyée, feuille activée. Puis une requête
invalide : bandeau d'erreur portant le message SSAS, aucune écriture.

- [ ] **Step 4: Commit**

```bash
git add -A && git commit -m "feat: execution MDX libre vers une plage Excel"
```

---

### Task 3: Éditeur Monaco dans le volet

**Files:**
- Create: `src/PivotScope.Web/src/monaco-core.ts`, `monaco-mdx.ts`,
  `mdx-completion.ts`, `mdxFunctions.ts` (repris de `external/CubeScope/CubeScope.Web/src/`)
- Create: `src/PivotScope.Web/src/components/MdxEditor.vue`
- Create: `src/PivotScope.Web/src/components/QueryPanel.vue`
- Modify: `src/PivotScope.Web/package.json`, `src/PivotScope.Web/src/App.vue`

**Interfaces:**
- Produces: `<MdxEditor v-model="mdx" :readonly="false" :meta="meta" />`,
  émettant `run` sur F5 et Ctrl+Entrée.

- [ ] **Step 1: Ajouter Monaco et copier les fichiers du sous-module**

```bash
npm --prefix src/PivotScope.Web install monaco-editor@^0.56.0
cp external/CubeScope/CubeScope.Web/src/monaco-core.ts   src/PivotScope.Web/src/
cp external/CubeScope/CubeScope.Web/src/monaco-mdx.ts    src/PivotScope.Web/src/
cp external/CubeScope/CubeScope.Web/src/mdx-completion.ts src/PivotScope.Web/src/
cp external/CubeScope/CubeScope.Web/src/mdxFunctions.ts  src/PivotScope.Web/src/
```

Ces fichiers sont du code MIT de David : la copie est légitime. En tête de
`monaco-core.ts`, ajouter un commentaire indiquant l'origine et le rappel de la
resynchronisation à chaque montée de version de monaco.

Adapter les imports de `mdx-completion.ts` : il appelle l'API HTTP de CubeScope
pour les membres ; ici il doit appeler `call('cube.members', …)` du pont.

- [ ] **Step 2: Écrire `MdxEditor.vue`**

Monter Monaco sur un `div`, langage `mdx`, thème sombre, `automaticLayout: true`.
Lier `modelValue` au contenu, émettre `update:modelValue` sur changement, et
`run` sur `F5` et `Ctrl+Entrée` via `addCommand`. Détruire l'instance à
`onBeforeUnmount` — sinon chaque réouverture du volet fuit un éditeur.

- [ ] **Step 3: Écrire `QueryPanel.vue`**

Éditeur + case « nouvelle feuille » + case « avec en-têtes » + bouton
« Exécuter (F5) » + zone de résultat affichant adresse, lignes, colonnes, durée.

- [ ] **Step 4: Ajouter l'onglet « Requête » dans `App.vue`**

- [ ] **Step 5: Vérifier**

Run: `npm --prefix src/PivotScope.Web run build`
Expected: build sans erreur. Puis dans Excel : coloration MDX, F5 exécute,
la complétion propose les mesures du cube après `[`.

Surveiller la taille du bundle : Monaco dégraissé pèse ~6 Mo côté CubeScope.
S'il dépasse, resynchroniser la liste d'imports de `monaco-core.ts`.

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "feat(web): editeur Monaco MDX et onglet Requete"
```

---

### Task 4: Validation d'une définition de calcul

**Files:**
- Create: `src/PivotScope.Core/Calculations/CalculationDefinition.cs`
- Create: `src/PivotScope.Core/Calculations/CalculationValidator.cs`
- Test: `tests/PivotScope.Core.Tests/CalculationValidatorTests.cs`

**Interfaces:**
- Produces:
  - `enum CalculationKind { Measure, Member, Set }`
  - `sealed record CalculationDefinition(string Name, string Expression, CalculationKind Kind, string? DisplayFolder, string? NumberFormat, string? ParentHierarchy, int SolveOrder)`
  - `CalculationValidator.Validate(CalculationDefinition)` → `IReadOnlyList<string>` (messages, vide si valide)
  - `CalculationValidator.QualifiedName(CalculationDefinition)` → nom unique MDX

- [ ] **Step 1: Écrire les tests qui échouent**

```csharp
using PivotScope.Core.Calculations;

namespace PivotScope.Core.Tests;

public class CalculationValidatorTests
{
    private static CalculationDefinition Measure(string name, string expr = "1") =>
        new(name, expr, CalculationKind.Measure, null, null, null, 0);

    [Theory]
    [InlineData("Marge")]
    [InlineData("Marge nette")]
    [InlineData("VL pondérée")]
    public void Validate_AcceptsPlainNames(string name)
        => Assert.Empty(CalculationValidator.Validate(Measure(name)));

    [Fact]
    public void Validate_RejectsEmptyName()
        => Assert.Contains(
            CalculationValidator.Validate(Measure("  ")),
            m => m.Contains("nom", StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void Validate_RejectsEmptyExpression()
        => Assert.Contains(
            CalculationValidator.Validate(Measure("Marge", "   ")),
            m => m.Contains("expression", StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void Validate_RejectsBracketInName()
    {
        // Un crochet dans le nom casse le nom unique MDX qu'on construit.
        var messages = CalculationValidator.Validate(Measure("Ma[rge"));
        Assert.NotEmpty(messages);
    }

    [Fact]
    public void Validate_MemberRequiresParentHierarchy()
    {
        var member = new CalculationDefinition(
            "Total zone euro", "1", CalculationKind.Member, null, null, null, 0);

        Assert.Contains(
            CalculationValidator.Validate(member),
            m => m.Contains("hiérarchie", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void QualifiedName_Measure_LivesUnderMeasures()
        => Assert.Equal("[Measures].[Marge]",
            CalculationValidator.QualifiedName(Measure("Marge")));

    [Fact]
    public void QualifiedName_Member_LivesUnderItsHierarchy()
    {
        var member = new CalculationDefinition(
            "Zone euro", "1", CalculationKind.Member, null, null, "[Devise].[Devise]", 0);

        Assert.Equal("[Devise].[Devise].[Zone euro]",
            CalculationValidator.QualifiedName(member));
    }
}
```

- [ ] **Step 2: Vérifier l'échec**

Run: `dotnet test tests/PivotScope.Core.Tests --filter CalculationValidatorTests`
Expected: échec de compilation.

- [ ] **Step 3: Implémenter**

```csharp
namespace PivotScope.Core.Calculations;

public enum CalculationKind { Measure, Member, Set }

/// <summary>
/// Un calcul tel que l'utilisateur le définit. NumberFormat mérite un mot :
/// Excel ne propose AUCUNE interface pour le poser sur un membre calculé
/// (« can only be set by macros »), donc PivotScope sait faire ce qu'Excel ne
/// sait pas.
/// </summary>
public sealed record CalculationDefinition(
    string Name,
    string Expression,
    CalculationKind Kind,
    string? DisplayFolder,
    string? NumberFormat,
    string? ParentHierarchy,
    int SolveOrder);

public static class CalculationValidator
{
    public static IReadOnlyList<string> Validate(CalculationDefinition definition)
    {
        var messages = new List<string>();

        if (string.IsNullOrWhiteSpace(definition.Name))
            messages.Add("Le nom du calcul est obligatoire.");
        else if (definition.Name.Contains('[') || definition.Name.Contains(']'))
            messages.Add("Le nom ne peut pas contenir de crochets : ils délimitent " +
                         "les identifiants MDX.");

        if (string.IsNullOrWhiteSpace(definition.Expression))
            messages.Add("L'expression MDX est obligatoire.");

        if (definition.Kind is CalculationKind.Member &&
            string.IsNullOrWhiteSpace(definition.ParentHierarchy))
            messages.Add("Un membre calculé doit indiquer sa hiérarchie parente.");

        return messages;
    }

    /// <summary>Nom unique MDX du calcul, tel qu'Excel devra le connaître.</summary>
    public static string QualifiedName(CalculationDefinition definition) =>
        definition.Kind switch
        {
            CalculationKind.Measure => $"[Measures].[{definition.Name}]",
            _ => $"{definition.ParentHierarchy}.[{definition.Name}]",
        };
}
```

- [ ] **Step 4: Vérifier**

Run: `dotnet test tests/PivotScope.Core.Tests --filter CalculationValidatorTests`
Expected: 9 tests verts.

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat(core): definition et validation d'un calcul MDX"
```

---

### Task 5: Création du calcul dans le TCD

**Files:**
- Create: `src/PivotScope.AddIn/Interop/CalculationApplier.cs`
- Modify: `src/PivotScope.AddIn/Pane/WebBridge.cs`

**Interfaces:**
- Produces:
  - `CalculationApplier.Apply(CalculationDefinition definition, bool addToPivot)` → `string` (nom unique créé)
  - `CalculationApplier.List()` → `IReadOnlyList<ExistingCalculation>` avec
    `sealed record ExistingCalculation(string Name, string Formula, string Kind, bool IsValid)`
  - `CalculationApplier.Delete(string name)`

- [ ] **Step 1: Implémenter**

Points à respecter, tous documentés :

- utiliser `AddCalculatedMember` (API Excel 2013+), pas `Add`, sauf pour un
  ensemble nommé (`xlCalculatedSet`) qui exige `Add` puis `CubeFields.AddSet` ;
- `XlCalculatedMemberType` : `xlCalculatedMember`=0, `xlCalculatedSet`=1,
  `xlCalculatedMeasure`=2 ;
- `DisplayFolder` n'est valide que pour une **mesure** calculée ;
  `NumberFormat` et `ParentHierarchy` seulement pour un **membre** calculé ;
- vérifier la validité avec `CalculatedMember.IsValid` **après**
  `PivotCache.MakeConnection()` — sinon `IsValid` renvoie `True` par défaut sur
  un TCD déconnecté, et on croit valide un calcul cassé ;
- pour afficher une mesure calculée : la retrouver dans `pivot.CubeFields` par
  son nom unique puis `pivot.AddDataField(cubeField, caption)`. `GetMeasure`
  ne convient pas — il ne sert qu'aux mesures implicites d'une hiérarchie
  d'attribut, et seulement pour Count/Sum/Average/Max/Min.
- si le cube field n'est pas trouvé après création, **journaliser l'inventaire**
  des `CubeFields` (nom, type, sous-type) plutôt que d'échouer en aveugle :
  c'est ce réflexe qui a résolu deux bugs en phase 1.

- [ ] **Step 2: Exposer trois méthodes au pont**

`calc.list`, `calc.apply`, `calc.delete`, toutes via `ExcelThread`.

- [ ] **Step 3: Vérifier dans Excel**

Créer une mesure calculée `Test` valant `1`, avec le format `#,##0.00`.
Expected: elle apparaît dans le TCD, formatée — un format qu'aucune interface
Excel ne permet de poser sur un membre calculé. Puis créer une mesure au MDX
invalide : message clair, rien créé.

- [ ] **Step 4: Commit**

```bash
git add -A && git commit -m "feat: creation, liste et suppression des calculs du TCD"
```

---

### Task 6: Bibliothèque de calculs

**Files:**
- Create: `src/PivotScope.Core/Calculations/CalculationLibrary.cs`
- Test: `tests/PivotScope.Core.Tests/CalculationLibraryTests.cs`

**Interfaces:**
- Produces:
  - `CalculationLibrary(string dbPath)` — crée le schéma si absent
  - `Task<IReadOnlyList<StoredCalculation>> ListAsync(CancellationToken ct)`
  - `Task<int> SaveAsync(CalculationDefinition definition, string? cube, CancellationToken ct)` → id
  - `Task DeleteAsync(int id, CancellationToken ct)`
  - `sealed record StoredCalculation(int Id, CalculationDefinition Definition, string? Cube, DateTime SavedUtc)`

- [ ] **Step 1: Écrire les tests qui échouent**

Tests sur une base temporaire (`Path.GetTempFileName()`), nettoyée en
`IDisposable` : le schéma se crée tout seul ; un aller-retour préserve tous les
champs y compris `NumberFormat` et `DisplayFolder` ; sauvegarder deux fois le
même nom pour le même cube **met à jour** au lieu de dupliquer ; `ListAsync`
trie par date décroissante ; `DeleteAsync` sur un id absent ne lève pas.

- [ ] **Step 2: Vérifier l'échec**

Run: `dotnet test tests/PivotScope.Core.Tests --filter CalculationLibraryTests`
Expected: échec de compilation.

- [ ] **Step 3: Implémenter**

`Microsoft.Data.Sqlite`, une table `Calculation(Id, Name, Expression, Kind,
DisplayFolder, NumberFormat, ParentHierarchy, SolveOrder, Cube, SavedUtc)`,
index unique `(Name, Cube)`, `PRAGMA user_version = 1` pour préparer les
migrations comme dans le `StateStore` de CubeScope.

- [ ] **Step 4: Vérifier**

Run: `dotnet test tests/PivotScope.Core.Tests --filter CalculationLibraryTests`
Expected: tous verts.

- [ ] **Step 5: Exposer `library.list`, `library.save`, `library.delete` au pont**

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "feat(core): bibliotheque SQLite de calculs reutilisables"
```

---

### Task 7: Confort de construction du TCD

**Files:**
- Create: `src/PivotScope.AddIn/Interop/PivotComfort.cs`
- Modify: `src/PivotScope.AddIn/Ribbon.cs`, `WebBridge.cs`
- Create: `src/PivotScope.Web/src/components/ComfortPanel.vue`

**Interfaces:**
- Produces:
  - `PivotComfort.SetAutoRefresh(bool enabled)` → `bool` (état effectif)
  - `PivotComfort.IsAutoRefreshEnabled()` → `bool`
  - `PivotComfort.SetFieldVisibility(string cubeFieldName, bool visible)`
  - `PivotComfort.ListFields()` → `IReadOnlyList<(string Name, string Caption, bool ShownInFieldList)>`

- [ ] **Step 1: Implémenter**

`PivotCache.EnableRefresh` + `Application.Calculation` pour le rafraîchissement
(mécanique réelle de l'add-in d'origine, vérifiée en phase 0) ;
`CubeField.ShowInFieldList` pour la visibilité.

- [ ] **Step 2: Indicateur permanent dans le ruban**

Ajouter un `toggleButton` « Rafraîchissement auto » dont le `getPressed`
reflète l'état réel, et invalider le ruban après chaque bascule
(`IRibbonUI.Invalidate`). L'add-in d'origine laisse oublier que le
rafraîchissement est coupé, et l'utilisateur croit ensuite son TCD faux.

- [ ] **Step 3: Vérifier dans Excel**

Couper le rafraîchissement, déposer trois champs, le rétablir.
Expected: le bouton du ruban reste enfoncé tant que c'est coupé ; un seul
aller-retour serveur au rétablissement.

- [ ] **Step 4: Commit**

```bash
git add -A && git commit -m "feat: confort de construction du TCD avec indicateur au ruban"
```

---

### Task 8: Recette, CHANGELOG, gate complète

- [ ] **Step 1: Étendre `docs/recette.md`**

Une section par nouveauté : éditeur (coloration, complétion, F5), requête libre
(plage écrite, erreur SSAS propre), calculs (mesure formatée, MDX invalide
refusé, suppression), bibliothèque (aller-retour, mise à jour sans doublon),
confort (indicateur ruban cohérent).

- [ ] **Step 2: Mettre à jour `CHANGELOG.md`**

- [ ] **Step 3: Gate**

```bash
dotnet build -c Release
dotnet test -c Release --filter "Category!=Integration"
npm --prefix src/PivotScope.Web run build
npm --prefix src/PivotScope.Web audit --audit-level=high
```

Expected: 0 avertissement, tous les tests verts, build SPA propre, 0 vulnérabilité.

- [ ] **Step 4: Commit**

```bash
git add -A && git commit -m "docs: recette et changelog de la phase 2"
```

---

## Auto-revue du plan

**Couverture de la phase 2 telle que définie dans la spec** — calculs MDX :
Tasks 4 et 5. Bibliothèque : Task 6. MDX libre → plage : Tasks 1, 2 et 3.
`ShowInFieldList` et `EnableRefresh` : Task 7. L'éditeur Monaco, non listé
explicitement dans la spec mais indispensable à l'écriture de MDX, est Task 3.

**Cohérence des types** — `CalculationDefinition` (Task 4) est consommé tel quel
par `CalculationApplier` (Task 5) et `CalculationLibrary` (Task 6).
`RangeProjection.ToGrid` (Task 1) produit l'`object?[,]` que `SheetWriter`
(Task 2) écrit. `CalculationValidator.QualifiedName` sert à Task 5 pour
retrouver le cube field après création.

**Points où le comportement d'Excel fait foi sur ce plan**, à instrumenter dès
l'écriture plutôt qu'après coup : le nom sous lequel une mesure calculée
apparaît dans `pivot.CubeFields` (Task 5), et la combinaison exacte de
paramètres acceptée par `AddCalculatedMember` selon le type de calcul (Task 5).
Dans les deux cas, journaliser l'inventaire réel avant de lever.
