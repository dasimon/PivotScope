# PivotScope — plan d'implémentation, phase 3

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** répondre à « d'où vient ce chiffre ? » sur n'importe quelle cellule
du TCD, et brancher l'assistant IA sur le contexte du tableau.

**Architecture:** `PivotCell.MDX` donne le tuple complet d'une cellule ; on en
extrait la mesure, on retrouve son expression dans le MDX Script du cube
(`ScriptService`) et ses dépendances récursives (`DependencyService`). L'IA
réutilise `AiService` de CubeScope, avec en plus l'état du TCD — que CubeScope
n'a pas.

**Tech Stack:** inchangé. Aucune nouvelle dépendance.

## Global Constraints

Celles des phases 1 et 2 s'appliquent. Rappels qui mordent ici :

- `PivotScope.Core` ne référence **jamais** `Microsoft.Office.Interop.Excel`.
- Tout appel COM passe par `ExcelThread` ; tout appel SSAS reste hors thread UI.
- **Aucune `MessageBox`.** Bandeau dans le volet, plus log fichier.
- Clé IA par `ANTHROPIC_API_KEY` uniquement, **jamais stockée**. UI dégradée avec
  message clair si absente.
- Fermer Excel — ou décocher le complément — avant tout `dotnet build`.

## Acquis à ne pas réapprendre

- `PivotCell.MDX` lève **hors zone de valeurs** et sur un **filtre de rapport en
  sélection multiple** : deux cas à traiter par un message, pas par une exception.
- Quand l'interop résiste, **journaliser l'inventaire réel** avant de lever.
- `AiService.RunAsync` construit son contexte cube à partir de `cubes[0]` du
  catalogue, pas du cube courant. Sur un catalogue multi-cubes, le contexte
  injecté sera **appauvri** (les références ne matcheront pas), pas faux.
  Limitation acceptée pour cette phase ; si la qualité des réponses en souffre,
  ajouter un paramètre `cube` à `RunAsync` dans le sous-module — ce serait aussi
  un correctif pour CubeScope lui-même.

---

## Structure des fichiers ajoutés

| Fichier | Responsabilité |
|---|---|
| `src/PivotScope.Core/Provenance/TupleParser.cs` | extraire mesure et coordonnées d'un tuple MDX |
| `src/PivotScope.Core/Provenance/CellProvenance.cs` | modèle de réponse « d'où vient ce chiffre » |
| `src/PivotScope.Core/Provenance/ProvenanceService.cs` | tuple → expression + dépendances |
| `src/PivotScope.Core/Ai/PivotAiContext.cs` | état du TCD mis en forme pour le prompt |
| `src/PivotScope.AddIn/Interop/PivotCellReader.cs` | `Range.PivotCell.MDX` de la cellule active |
| `src/PivotScope.Web/src/components/ProvenancePanel.vue` | onglet « Ce chiffre » |
| `src/PivotScope.Web/src/components/AiPanel.vue` | onglet « IA » |

---

### Task 1: Lecture d'un tuple MDX

**Files:**
- Create: `src/PivotScope.Core/Provenance/TupleParser.cs`
- Test: `tests/PivotScope.Core.Tests/TupleParserTests.cs`

**Interfaces:**
- Produces:
  - `sealed record MdxTuple(string? Measure, IReadOnlyList<string> Coordinates)`
  - `TupleParser.Parse(string tuple)` → `MdxTuple`

`PivotCell.MDX` renvoie une chaîne de la forme
`([Measures].[Chiffre d'affaires],[Devise].[Devise].&[EUR],[Temps].[Année].&[2026])`.
On en extrait la mesure (le membre sous `[Measures]`) et les autres coordonnées.

- [ ] **Step 1: Écrire les tests qui échouent**

```csharp
using PivotScope.Core.Provenance;

namespace PivotScope.Core.Tests;

public class TupleParserTests
{
    [Fact]
    public void Parse_SepareLaMesureDesCoordonnees()
    {
        var tuple = TupleParser.Parse(
            "([Measures].[Chiffre d'affaires],[Devise].[Devise].&[EUR])");

        Assert.Equal("[Measures].[Chiffre d'affaires]", tuple.Measure);
        Assert.Equal(["[Devise].[Devise].&[EUR]"], tuple.Coordinates);
    }

    [Fact]
    public void Parse_SansParenthesesEnglobantes()
    {
        var tuple = TupleParser.Parse("[Measures].[VL]");

        Assert.Equal("[Measures].[VL]", tuple.Measure);
        Assert.Empty(tuple.Coordinates);
    }

    [Fact]
    public void Parse_NeCoupePasSurUneVirguleDansUnCrochet()
    {
        // Un libellé de membre peut contenir une virgule : découper naïvement
        // sur « , » casserait la coordonnée en deux.
        var tuple = TupleParser.Parse(
            "([Measures].[VL],[Fonds].[Fonds].&[Actions, Europe])");

        Assert.Equal(["[Fonds].[Fonds].&[Actions, Europe]"], tuple.Coordinates);
    }

    [Fact]
    public void Parse_SansMesure_RendMeasureNull()
    {
        var tuple = TupleParser.Parse("([Devise].[Devise].&[EUR])");

        Assert.Null(tuple.Measure);
        Assert.Single(tuple.Coordinates);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("()")]
    public void Parse_EntreeVide_NeLevePas(string input)
    {
        var tuple = TupleParser.Parse(input);

        Assert.Null(tuple.Measure);
        Assert.Empty(tuple.Coordinates);
    }

    [Fact]
    public void Parse_ToleLesEspacesAutourDesVirgules()
    {
        var tuple = TupleParser.Parse("( [Measures].[VL] , [Devise].[Devise].&[EUR] )");

        Assert.Equal("[Measures].[VL]", tuple.Measure);
        Assert.Equal(["[Devise].[Devise].&[EUR]"], tuple.Coordinates);
    }
}
```

- [ ] **Step 2: Vérifier l'échec**

Run: `dotnet test tests/PivotScope.Core.Tests --filter TupleParserTests`
Expected: échec de compilation, `TupleParser` introuvable.

- [ ] **Step 3: Implémenter**

```csharp
namespace PivotScope.Core.Provenance;

/// <summary>Un tuple MDX décomposé : la mesure d'un côté, les coordonnées de l'autre.</summary>
public sealed record MdxTuple(string? Measure, IReadOnlyList<string> Coordinates);

/// <summary>
/// Lit la chaîne rendue par PivotCell.MDX.
///
/// Le découpage se fait à la profondeur zéro de crochets : un libellé de membre
/// peut contenir une virgule (« [Actions, Europe] »), et découper naïvement
/// couperait la coordonnée en deux.
/// </summary>
public static class TupleParser
{
    private const string MeasuresPrefix = "[Measures].";

    public static MdxTuple Parse(string tuple)
    {
        var text = (tuple ?? string.Empty).Trim();
        if (text.StartsWith('(') && text.EndsWith(')')) text = text[1..^1];
        if (string.IsNullOrWhiteSpace(text)) return new MdxTuple(null, []);

        string? measure = null;
        var coordinates = new List<string>();

        foreach (var part in SplitTopLevel(text))
        {
            var member = part.Trim();
            if (member.Length == 0) continue;

            if (measure is null &&
                member.StartsWith(MeasuresPrefix, StringComparison.OrdinalIgnoreCase))
                measure = member;
            else
                coordinates.Add(member);
        }

        return new MdxTuple(measure, coordinates);
    }

    private static IEnumerable<string> SplitTopLevel(string text)
    {
        var depth = 0;
        var start = 0;

        for (var i = 0; i < text.Length; i++)
        {
            switch (text[i])
            {
                case '[': depth++; break;
                case ']': depth--; break;
                case ',' when depth == 0:
                    yield return text[start..i];
                    start = i + 1;
                    break;
            }
        }

        yield return text[start..];
    }
}
```

- [ ] **Step 4: Vérifier**

Run: `dotnet test tests/PivotScope.Core.Tests --filter TupleParserTests`
Expected: 8 tests verts.

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat(core): lecture d'un tuple MDX rendu par PivotCell.MDX"
```

---

### Task 2: Provenance d'une cellule

**Files:**
- Create: `src/PivotScope.Core/Provenance/CellProvenance.cs`
- Create: `src/PivotScope.Core/Provenance/ProvenanceService.cs`
- Create: `src/PivotScope.Core/Abstractions/IScriptReader.cs`
- Test: `tests/PivotScope.Core.Tests/ProvenanceServiceTests.cs`

**Interfaces:**
- Consumes: `CubeScript`, `CubeMeta`, `DependencyGraph`, `DependencyService.Resolve`.
- Produces:
  - `interface IScriptReader { Task<CubeScript> GetScriptAsync(string cube, CancellationToken ct); }`
  - `sealed record CellProvenance(string Tuple, string? Measure, IReadOnlyList<string> Coordinates, string? Expression, int? StartLine, DependencyGraph? Dependencies, string? Note)`
  - `ProvenanceService(IScriptReader scripts, ICubeMetadataReader metadata)`
  - `Task<CellProvenance> DescribeAsync(string cube, string tuple, CancellationToken ct)`

Comportement : si la mesure n'est pas calculée (absente du script), `Expression`
et `Dependencies` restent nuls et `Note` explique que c'est une mesure physique
— ce n'est pas une erreur, c'est une réponse.

- [ ] **Step 1: Écrire les tests qui échouent**

Couvrir : mesure calculée trouvée dans le script → expression, ligne et graphe ;
mesure physique → `Note` renseignée, pas d'exception ; tuple sans mesure →
coordonnées seules ; échec de lecture du script → provenance partielle plutôt
que rien (le tuple reste affichable). Les doubles implémentent `IScriptReader`
et `ICubeMetadataReader` avec des `CubeScript` / `CubeMeta` construits à la main.

- [ ] **Step 2: Vérifier l'échec**

Run: `dotnet test tests/PivotScope.Core.Tests --filter ProvenanceServiceTests`
Expected: échec de compilation.

- [ ] **Step 3: Implémenter**

`DescribeAsync` : parser le tuple ; si pas de mesure, rendre les coordonnées
seules ; sinon chercher dans `script.Commands` la commande dont le `Name`
correspond au nom unique de la mesure (comparaison insensible à la casse, en
tolérant la présence ou l'absence du préfixe `[Measures].`) ; si trouvée,
appeler `DependencyService.Resolve(script, meta, name)`. Toute exception de
lecture du script devient une `Note`, jamais une exception remontée.

- [ ] **Step 4: Vérifier**

Run: `dotnet test tests/PivotScope.Core.Tests --filter ProvenanceServiceTests`
Expected: tous verts.

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat(core): provenance d'une cellule (expression + dependances)"
```

---

### Task 3: Lecture de la cellule active et adaptateur script

**Files:**
- Create: `src/PivotScope.AddIn/Interop/PivotCellReader.cs`
- Modify: `src/PivotScope.Core/Adapters/CubeScopeSession.cs` (implémenter `IScriptReader`)
- Modify: `src/PivotScope.AddIn/Pane/WebBridge.cs`

**Interfaces:**
- Produces:
  - `PivotCellReader.ReadTuple()` → `string` (le tuple), ou lève un message clair
  - méthode du pont `cell.provenance` → `CellProvenance`

- [ ] **Step 1: Implémenter la lecture COM**

```csharp
using ExcelDna.Integration;
using Xl = Microsoft.Office.Interop.Excel;

namespace PivotScope.AddIn.Interop;

/// <summary>
/// Lit le tuple MDX complet de la cellule active.
///
/// Deux limites documentées par Microsoft, à traduire en messages plutôt qu'en
/// exceptions : PivotCell.MDX lève hors de la zone de valeurs, et lève aussi
/// quand un filtre de rapport est en sélection multiple.
/// </summary>
public static class PivotCellReader
{
    public static string ReadTuple()
    {
        var app = (Xl.Application)ExcelDnaUtil.Application;
        var cell = app.ActiveCell
            ?? throw new InvalidOperationException("Aucune cellule active.");

        Xl.PivotCell pivotCell;
        try { pivotCell = cell.PivotCell; }
        catch
        {
            throw new InvalidOperationException(
                "Cette cellule n'appartient pas à un tableau croisé dynamique.");
        }

        if (pivotCell.PivotCellType != Xl.XlPivotCellType.xlPivotCellValue)
            throw new InvalidOperationException(
                "Sélectionnez une cellule de valeur : les en-têtes et les totaux " +
                "n'ont pas de coordonnées complètes.");

        try { return pivotCell.MDX; }
        catch
        {
            throw new InvalidOperationException(
                "Excel ne peut pas donner les coordonnées de cette cellule. " +
                "C'est le cas lorsqu'un filtre de rapport a plusieurs éléments " +
                "sélectionnés : réduisez-le à un seul.");
        }
    }
}
```

- [ ] **Step 2: Exposer `IScriptReader` sur `CubeScopeSession`**

Ajouter `ScriptService` au champ, et
`Task<CubeScript> GetScriptAsync(string cube, CancellationToken ct)` déléguant
à `_script.GetScriptAsync(cube, ct: ct)`.

- [ ] **Step 3: Brancher `cell.provenance` sur le pont**

Lire le tuple via `ExcelThread`, puis appeler `ProvenanceService` hors thread UI.

- [ ] **Step 4: Ajouter l'entrée au menu contextuel**

Une entrée « D'où vient ce chiffre ? » dans le menu contextuel du TCD, qui ouvre
le volet sur l'onglet correspondant. Le menu contextuel n'a pas encore été
posé : le faire ici, en se limitant à **trois** entrées maximum comme prévu au
design (Ouvrir le volet, Filtrer par une liste, D'où vient ce chiffre).

- [ ] **Step 5: Vérifier dans Excel**

Cellule de valeur → tuple, expression et dépendances. En-tête → message clair.
Filtre de rapport multi-sélection → message clair.

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "feat: d'ou vient ce chiffre, sur la cellule active"
```

---

### Task 4: Onglet « Ce chiffre »

**Files:**
- Create: `src/PivotScope.Web/src/components/ProvenancePanel.vue`
- Modify: `src/PivotScope.Web/src/App.vue`, `types.ts`

Affiche : le tuple (monospace, copiable), la mesure, les coordonnées en puces,
l'expression MDX en lecture seule dans `MdxEditor`, et l'arbre des dépendances
(composant récursif). Un bouton « Expliquer avec l'IA » qui bascule sur l'onglet
IA en pré-remplissant l'expression.

- [ ] **Step 1: Écrire le composant récursif d'arbre**
- [ ] **Step 2: Écrire le panneau**
- [ ] **Step 3: Vérifier** — `npm run build`, puis dans Excel sur une mesure
      calculée réelle du cube
- [ ] **Step 4: Commit**

---

### Task 5: Panneau IA

**Files:**
- Create: `src/PivotScope.Core/Ai/PivotAiContext.cs`
- Create: `src/PivotScope.Web/src/components/AiPanel.vue`
- Test: `tests/PivotScope.Core.Tests/PivotAiContextTests.cs`
- Modify: `WebBridge.cs`, `CubeScopeSession.cs`, `App.vue`, `types.ts`

**Interfaces:**
- Produces:
  - `PivotAiContext.Describe(PivotContext context)` → `string`
  - méthodes du pont `ai.status` → `{ configured }` et `ai.run` → `{ markdown }`

`PivotAiContext.Describe` met en forme l'état du TCD pour le prompt : cube,
champs en ligne, en colonne, en filtre, mesures affichées. C'est ce que
CubeScope ne peut pas fournir, et ce qui rend l'assistant pertinent ici.

- [ ] **Step 1: Écrire les tests de mise en forme du contexte**

Couvrir : contexte sans TCD → chaîne vide ; champs groupés par zone dans
l'ordre ligne/colonne/filtre/valeurs ; cube nommé ; aucun champ → mention
explicite plutôt qu'une section vide.

- [ ] **Step 2: Vérifier l'échec, implémenter, vérifier**

- [ ] **Step 3: Brancher `ai.status` et `ai.run`**

`ai.status` renvoie `AiService.IsConfigured` — l'UI se dégrade proprement si
`ANTHROPIC_API_KEY` est absente, au lieu d'échouer à l'usage.
`ai.run` prend `{ action, mdx }`, préfixe le MDX du contexte TCD, et appelle
`RunAsync`. Annulable comme `query.run`, avec le même patron de jeton.

- [ ] **Step 4: Écrire `AiPanel.vue`**

Quatre actions (Expliquer, Optimiser, Anti-patterns, Formater), la source du
MDX au choix (requête du TCD, éditeur de l'onglet Requête, expression courante),
rendu Markdown, bouton Arrêter. Message clair et boutons désactivés si l'IA
n'est pas configurée.

- [ ] **Step 5: Vérifier dans Excel** — une action réelle avec la clé posée

- [ ] **Step 6: Commit**

---

### Task 6: Recette, CHANGELOG, gate

- [ ] **Step 1: Étendre `docs/recette.md`** — provenance (cellule de valeur,
      en-tête, filtre multi-sélection, mesure physique, mesure calculée), IA
      (sans clé, avec clé, annulation).
- [ ] **Step 2: Mettre à jour `CHANGELOG.md` et le statut de `CLAUDE.md`.**
- [ ] **Step 3: Gate**

```bash
dotnet build -c Release
dotnet test -c Release --filter "Category!=Integration"
npm --prefix src/PivotScope.Web run build
npm --prefix src/PivotScope.Web audit --audit-level=high
dotnet list package --vulnerable --include-transitive
```

- [ ] **Step 4: Commit**

---

## Auto-revue du plan

**Couverture** — « D'où vient ce chiffre » : Tasks 1 à 4. Panneau IA : Task 5.
Les deux fonctions listées en phase 3 de la spec sont couvertes.

**Cohérence des types** — `MdxTuple` (Task 1) est consommé par
`ProvenanceService` (Task 2), qui produit le `CellProvenance` lu par le pont
(Task 3) et affiché par `ProvenancePanel` (Task 4). `IScriptReader` (Task 2) est
implémenté par `CubeScopeSession` (Task 3). `PivotContext` (phase 1) alimente
`PivotAiContext.Describe` (Task 5).

**Points où le comportement d'Excel fait foi** : la forme exacte de la chaîne
rendue par `PivotCell.MDX` sur ce cube. Le parseur est écrit pour être tolérant
(parenthèses optionnelles, espaces, virgules dans les libellés) ; au premier
essai réel, journaliser le tuple brut avant de conclure.
