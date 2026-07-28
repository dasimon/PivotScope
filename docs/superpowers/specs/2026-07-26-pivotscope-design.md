# PivotScope — design

> Statut : validé par David le 2026-07-26. Document de référence pour le plan
> d'implémentation.

## 1. Intention

PivotScope est un complément Excel qui donne à un développeur SSAS
**Multidimensional** les moyens de comprendre, écrire et mesurer le MDX **là où
il travaille réellement** : dans le tableau croisé dynamique qu'il a sous les
yeux.

C'est le pendant Excel de [CubeScope](https://github.com/dasimon/CubeScope), et
il en partage la doctrine : un développeur seul, un moteur unique, aucune
abstraction spéculative.

**Hors périmètre définitif** : Tabular, Power BI, DAX, Power Pivot (modèle de
données Excel). Ne jamais introduire d'abstraction multi-moteurs « au cas où ».

### Filiation

Le projet est **inspiré** de
[OLAP PivotTable Extensions](https://olappivottableextensions.github.io/)
(Greg Galloway, Ms-PL) mais n'en reprend **aucune ligne de code**. La licence
Ms-PL, clause 3(D), impose que toute portion redistribuée sous forme source
reste sous Ms-PL ; PivotScope est sous **MIT**, ce qui exclut toute reprise.
Le README crédite explicitement le projet d'origine.

Cette réécriture est réaliste parce que l'abandon de Power Pivot supprime la
plus grosse masse de code de l'original : les 18 classes `AdomdClientWrappers/`
(~2 500 lignes) n'existaient que pour faire cohabiter deux implémentations
ADOMD. Le reste repose sur des API Excel documentées.

## 2. Décisions d'architecture (actées)

- **Hôte : Excel-DNA, cible `net10.0-windows`.** VSTO est écarté : Microsoft
  documente que « VSTO Add-Ins can't be created with .NET » et que .NET
  Framework 4.8 en est la dernière version majeure — ce qui interdirait tout
  partage avec `CubeScope.Core`. Office.js est écarté par la doc Microsoft :
  « PivotTables created with OLAP are not currently supported ».
- **UI : volet Office (CustomTaskPane) hébergeant WebView2**, qui affiche une
  SPA Vue 3 + TypeScript + Monaco. Réutilise la grammaire MDX, le thème sombre
  et les patrons UI de CubeScope.
- **SPA embarquée en ressources** dans l'assembly, servie à WebView2 par
  interception de `WebResourceRequested` sur l'origine virtuelle
  `https://pivotscope.local/`. Aucun fichier extrait sur disque. Même principe
  que l'`EmbeddedSpaFileProvider` de CubeScope.
- **Pont SPA ↔ .NET : `postMessage`**, pas `AddHostObjectToScript` (marshalling
  COM difficile à déboguer). Enveloppe minimale :
  `{id, method, params}` → `{id, ok, result | error}`.
- **Source de données : SSAS Multidimensional uniquement**, via
  `Microsoft.AnalysisServices.AdomdClient.NetCore.retail.amd64`. Sécurité
  intégrée Windows. Aucun credential stocké nulle part.
- **Réutilisation de CubeScope.Core derrière des interfaces.**
  `PivotScope.Core` déclare les abstractions dont il a besoin
  (`ICubeMetadataReader`, `IMdxExecutor`, `IMdxAssistant`) ; une couche
  d'adaptation les branche sur `CubeScope.Core`. Le mécanisme de partage est
  ainsi remplaçable sans toucher au reste du code.
  Mécanisme retenu : **sous-module git épinglé sur un commit**
  (`external/CubeScope`) + `ProjectReference` — reproductible en CI,
  contrairement à un chemin relatif, et sans perte d'historique, contrairement
  à une copie. Un paquet NuGet reste envisageable une fois l'API stabilisée ;
  l'API de `CubeScope.Core` n'a jamais été conçue comme une bibliothèque
  publique, il serait prématuré de la figer maintenant.
- **État local : SQLite** propre à PivotScope (bibliothèque de calculs,
  historique, préférences). Pas de partage concurrent avec la base CubeScope ;
  une action explicite « importer depuis CubeScope » à la place.
- **Distribution : dossier zippé** contenant `PivotScope.xll` (x64) et les DLL
  natives non fusionnables. Ni MSI, ni InstallShield, ni droits admin, ni
  enregistrement COM, ni clé de signature forte.

## 3. Structure de la solution

| Projet | Cible | Rôle | Dépend d'Excel |
|---|---|---|---|
| `PivotScope.AddIn` | `net10.0-windows` | Excel-DNA : ruban, menu contextuel, volet WebView2, pont, **toute** l'interop COM | oui |
| `PivotScope.Core` | `net10.0-windows` | Construction du MDX, résolution de membres, bibliothèque de calculs, orchestration des services CubeScope | **non** |
| `PivotScope.Web` | Vue 3 + TS + Vite | SPA du volet, buildée puis embarquée | non |
| `PivotScope.Core.Tests` | xUnit | Tout ce qui est testable hors Excel | non |
| `external/CubeScope` | sous-module | `CubeScope.Core` en `ProjectReference` | non |

La frontière `AddIn` / `Core` est la garantie de testabilité : `Core` ne
référence jamais `Microsoft.Office.Interop.Excel`.

### Règles de frontière

- **Threading.** Excel COM est STA sur le thread principal ; les messages
  WebView2 arrivent sur le thread UI. Tout appel Excel passe par
  `ExcelAsyncUtil.QueueAsMacro`. Toute requête SSAS part sur un thread de fond
  et reposte son résultat. Cette règle est appliquée dans **une seule classe
  frontière** — la disperser conduit à chasser des
  `RPC_E_SERVERCALL_RETRYLATER`.
- **Culture.** Sur un Excel français, les API COM qui prennent des chaînes de
  formule attendent l'anglais. La bascule de culture est appliquée à la
  frontière COM uniquement, pas dispersée dans l'UI comme dans l'original.

## 4. API Excel utilisées (vérifiées sur Microsoft Learn)

| API | Usage | Contrainte documentée |
|---|---|---|
| `PivotCache.OLAP` | détecter un TCD OLAP | — |
| `PivotTable.MDX` | requête générée par le TCD | OLAP uniquement ; erreur si aucun élément de données |
| `PivotCell.MDX` | tuple complet d'une cellule de valeur, filtres de rapport inclus | erreur hors zone de valeurs ; erreur si un filtre de rapport est en sélection multiple |
| `PivotField.VisibleItemsList` | filtre manuel inclusif (lecture/écriture) | OLAP uniquement ; vide si `CubeField.IncludeNewItemsInFilter = True` |
| `CubeField.ClearManualFilter` | réinitialiser le filtre avant d'appliquer une liste | erreur si appelé sur un `PivotField` en OLAP — il faut le `CubeField` |
| `CubeField.IncludeNewItemsInFilter` | doit valoir `False` pour que `VisibleItemsList` soit utilisable | à `True`, `VisibleItemsList` est vide et n'accepte aucun élément |
| `CalculatedMembers.AddCalculatedMember` | créer mesure / membre / ensemble | API Excel 2013+ ; expose `DisplayFolder`, `ParentHierarchy`, `ParentMember`, `NumberFormat` |
| `CalculatedMember.IsValid` | valider un calcul | renvoie `True` si le TCD n'est pas connecté → appeler `PivotCache.MakeConnection()` avant |
| `XlCalculatedMemberType` | `xlCalculatedMember`=0, `xlCalculatedSet`=1, `xlCalculatedMeasure`=2 | — |
| `CubeField.ShowInFieldList` | masquer un champ de la liste de champs | — |
| `PivotCache.EnableRefresh` + `Application.Calculation` | couper le rafraîchissement automatique | — |
| `CustomTaskPaneFactory.CreateCustomTaskPane` | volet Office | exige un `UserControl` WinForms (support ActiveX) |

`PivotCache.MissingItemsLimit` est **inutilisable** ici : la doc précise qu'elle
ne fonctionne que sur les TCD **non**-OLAP.

`NumberFormat` sur un membre calculé n'a **aucune interface utilisateur dans
Excel** (« can only be set by macros ») : PivotScope pourra formater un membre
calculé, ce qu'Excel ne sait pas faire nativement.

## 5. Ergonomie

Le défaut principal de l'original est ergonomique : une **boîte de dialogue
modale** ouverte par clic droit, à refermer et rouvrir sans cesse, et **8
entrées** injectées dans le menu contextuel.

PivotScope :

- **Un onglet de ruban « PivotScope »** — point d'entrée visible, absent de
  l'original.
- **Le volet reste ouvert** et suit le TCD actif : déposer un champ met à jour
  le MDX affiché. C'est le principal gain d'usage du projet.
- **Trois entrées** au menu contextuel : *Ouvrir le volet*, *Filtrer par une
  liste*, *D'où vient ce chiffre*.

## 6. Fonctionnalités

### 6.1 Reprises de l'original

| Fonction | Mécanique |
|---|---|
| **MDX du TCD** | `PivotTable.MDX` dans un Monaco en lecture seule ; formatage par l'IA à la demande ; copie. |
| **Filter List** | `PivotField.VisibleItemsList = string[]`, précédé de `CubeField.ClearManualFilter()` pour repartir d'un filtre propre, et après avoir forcé `CubeField.IncludeNewItemsInFilter = False` — sinon la doc précise que `VisibleItemsList` reste vide et n'accepte aucun élément. L'utilisateur colle des **clés ou des libellés** (voir 6.4), ou sélectionne une **plage Excel**. Les valeurs non résolues sont **rapportées**, pas fatales. |
| **Explorateur de métadonnées** | DMV via `CubeScope.Core`, arbre filtrable ; double-clic = insertion dans l'éditeur ou ajout au TCD. |
| **Calculs MDX** | `AddCalculatedMember` avec `DisplayFolder`, `ParentHierarchy`, `NumberFormat`. Éditeur Monaco + autocomplétion des mesures / hiérarchies / membres. Validation par `IsValid` après `MakeConnection()`. |
| **Bibliothèque de calculs** | SQLite ; import explicite depuis CubeScope. |
| **Choose Fields to Show** | `CubeField.ShowInFieldList`. |
| **Disable Auto Refresh** | `PivotCache.EnableRefresh = false` + `Application.Calculation = xlCalculationManual`, avec **indicateur permanent dans le ruban** (l'original laisse oublier que c'est actif). |
| **Clear PivotTable Cache** | Suppression / recréation de la connexion du classeur. **Modifie le classeur.** Garde-fous : refus si plusieurs TCD partagent la connexion, refus si `CommandType ≠ xlCmdCube`, confirmation explicite, invitation à enregistrer d'abord. |

### 6.2 Abandonnées

Show Property as Caption, icônes KPI SSAS → `XlIconSet`, impersonation /
identifiants alternatifs, formateur MDX par service SOAP externe
(`formatmdx.azurewebsites.net` — remplacé par l'IA), support Excel 2003–2013,
shim COM `ManagedAggregator`, actions personnalisées MSI.

### 6.3 Nouvelles

| Fonction | Mécanique |
|---|---|
| **Panneau IA** | Expliquer / Optimiser / Anti-patterns / Formater / **langage naturel → MDX**. Contexte injecté : métadonnées filtrées sur les références du MDX (service `CubeScope.Core`) **+ l'état du TCD** (champs en ligne / colonne / filtre, mesures affichées). Clé par variable d'environnement `ANTHROPIC_API_KEY` uniquement ; UI dégradée avec message clair si absente. |
| **MDX libre → plage Excel** | Monaco + exécution ADOMD + collage du `CellSet` à partir de la cellule active ou dans une feuille neuve. Réutilise `CellSetMapper` de CubeScope et ses replis éprouvés (`Axes.Count`, `FormattedValue` vide, `axis.Set.Hierarchies` qui lève). Annulable. |
| **D'où vient ce chiffre** | `Range.PivotCell.MDX` → tuple ; identification de la mesure ; expression trouvée dans le MDX Script du cube (AMO, `CubeScope.Core`) ; dépendances récursives (graphe CubeScope) ; bouton « expliquer ». Les deux cas d'erreur documentés sont traités par un message, pas par une exception. |
| **Profiler FE/SE** | Trace SSAS autour du refresh du TCD. Excel rafraîchit avec **sa propre connexion** : la corrélation par `SessionID` de CubeScope ne s'applique pas. Méthode retenue : fenêtre temporelle du refresh + `DatabaseName` + rapprochement de `QueryEnd.TextData` avec `PivotTable.MDX`. Trace scopée au PID, `Stop()` + `Drop()` en `finally`, nettoyage des traces orphelines. Dégradable si les droits admin SSAS manquent. |

### 6.4 Résolution des valeurs collées (mesuré sur le cube réel, 2026-07-27)

La conception initiale ne résolvait que par **clé**, via `StrToMember`. Le
premier essai sur le cube `Ventes` l'a invalidée : David colle `Aurore`,
qui est le **libellé** du produit dont la clé est `PRD014`. Personne n'a
les clés techniques sous la main.

Sondage du cube (`SSAS01` / `Analytics` / `Ventes`) :

| Constat | Valeur |
|---|---|
| Clés réelles du niveau `Magasin` | `ANC001`, `PRD020`, `782792M`, `CIBTPIDF1`… |
| Libellés réels | libellés métier lisibles, sans rapport avec la clé |
| Cardinalité du niveau | 3 157 membres |
| **Énumération complète du niveau en MDX** | **79 ms** |
| `StrToMember` sur un membre inexistant | **ne lève pas** — renvoie une cellule nulle |

Ces deux dernières lignes tranchent deux incertitudes de conception :

- Le repli par libellé est **abordable**. Le piège documenté sur CubeScope
  (« ne jamais scanner ») vise `$SYSTEM.MDSCHEMA_MEMBERS`, qui parcourt la
  dimension entière ; énumérer un seul niveau en MDX n'a pas le même ordre de
  grandeur. L'interdiction du DMV reste, elle.
- Le repli valeur-par-valeur du résolveur n'est pas le chemin nominal : comme
  `StrToMember` ne lève pas, une clé inconnue se détecte à la caption nulle,
  sans faire tomber le lot.

**Ordre de résolution retenu**, appliqué à chaque valeur collée :

1. **Nom unique complet** (`[Dim].[Hier].[Niveau].&[X]`) → repris tel quel,
   aucun appel serveur ;
2. **Clé** → `niveau.&[valeur]`, sondé par lots de 100, sans scan ;
3. **Libellé** → une seule énumération du niveau (plafond 50 000 membres,
   résultat mis en cache par cube et niveau), déclenchée uniquement s'il reste
   des valeurs non résolues.

Un libellé porté par **plusieurs** membres n'est pas tranché au hasard : il est
rapporté comme **ambigu**, distinct de « introuvable », et l'interface invite à
coller la clé. Filtrer sur le mauvais membre produirait un chiffre faux sans
que personne ne s'en aperçoive.

## 7. Robustesse

Contrainte numéro un : **ne jamais casser Excel**. Une exception au démarrage et
Excel range le complément dans ses « éléments désactivés ».

- **Démarrage minimal** : enregistrer le ruban, rien d'autre. Métadonnées,
  SSAS, SQLite et WebView2 sont initialisés paresseusement à la première
  ouverture du volet.
- **Détection du contexte** par `PivotCache.OLAP` : pas de TCD ou TCD non-OLAP
  → boutons grisés et une phrase explicite dans le volet.
- **Aucune `MessageBox` bloquante** : bandeau d'erreur dans le volet, toast
  pour le reste.
- **Log fichier tournant** dans `%LOCALAPPDATA%\PivotScope\logs`.

## 8. Tests

- **`PivotScope.Core.Tests` (xUnit)** : construction du MDX, résolution d'une
  liste de clés en noms uniques de membres (exécuteur MDX bouchonné),
  bibliothèque de calculs, sérialisation du pont.
- **Tests d'intégration `[Category=Integration]`** contre le cube réel, en
  réutilisant le helper `TestTarget.cs` et les variables d'environnement
  `CUBESCOPE_TEST_*` déjà en place.
- **Checklist de recette manuelle versionnée** (`docs/recette.md`), déroulée
  avant chaque tag : seul moyen honnête de couvrir l'interop Excel.
- `ExcelDna.Testing` (tests exécutés dans Excel) n'est pas retenu au plan ; à
  réévaluer si la recette manuelle devient pénible.

## 9. Distribution

- Livrable : **dossier zippé** = `PivotScope.xll` (x64, assemblies managées
  fusionnées par `ExcelDnaPack`) + DLL natives non fusionnables.
- Installation : dézipper, puis Excel → Options → Compléments, ou `install.ps1`
  écrivant `HKCU\Software\Microsoft\Office\Excel\Addins`.
- Prérequis, à écrire dans le README : Excel **64-bit** 2016+, **.NET Desktop
  Runtime 10 (x64)**, WebView2 Evergreen, SSAS Multidimensional, et droits
  admin SSAS pour le seul Profiler.
- CI GitHub Actions calquée sur CubeScope : `ci.yml` (build + tests hors
  intégration sur `windows-latest`), `release.yml` (tag `v*` → zip attaché à la
  Release).
- **Langue** : SPA bilingue FR/EN dès le départ (patron `vue-i18n` de
  CubeScope) ; ruban et menu contextuel localisés par un dictionnaire d'une
  quinzaine de chaînes, selon la langue d'Excel.

## 9 bis. Conventions de dépôt

- **`Directory.Build.props`** à la racine : `Nullable=enable`,
  `TreatWarningsAsErrors=true`, `LangVersion=latest`, version unique.
- **`Directory.Packages.props`** : gestion centralisée des versions de paquets
  (`ManagePackageVersionsCentrally`), pour éviter la dérive entre projets.
- **`.editorconfig`** et **`.gitattributes`** (normalisation des fins de ligne).
- **Audit de vulnérabilités dans la CI** : `dotnet list package --vulnerable
  --include-transitive` en échec bloquant. CubeScope a déjà rencontré le cas
  (`Microsoft.Identity.Client` tiré en transitive par ADOMD) — le même pin
  direct sera appliqué ici, et resynchronisé à chaque montée d'ADOMD.
- **SemVer** + `CHANGELOG.md` au format Keep a Changelog ; les tags `v*`
  déclenchent la Release.
- **Identité visuelle** : même cube isométrique émeraude que CubeScope,
  différencié par une grille de tableau croisé. Les deux README se citent
  mutuellement — une famille d'outils se reconnaît, elle ne se devine pas.

## 10. Phases

Chaque phase se termine par un binaire utilisable au quotidien.

| Phase | Contenu | Sortie |
|---|---|---|
| **0** | **Spike WebView2 en volet Office** : frappe clavier, Monaco, aller-retour du pont. **GO / NO-GO.** Repli si NO-GO : fenêtre non-modale hébergeant le même WebView2. | verdict |
| **1** | Ruban + volet suivant le TCD actif + `PivotTable.MDX` + explorateur de métadonnées + Filter List | installable ; Filter List justifie seul l'usage quotidien |
| **2** | Calculs + bibliothèque SQLite + MDX libre → plage + `ShowInFieldList` + `EnableRefresh` | remplace l'original |
| **3** | Panneau IA + « D'où vient ce chiffre » | dépasse l'original |
| **4** | Profiler FE/SE + Clear PivotTable Cache | les deux fonctions coûteuses et risquées, en dernier |
| **5** | README, captures, CI, première Release taguée | publication |

**Portée du premier plan d'implémentation : phases 0 et 1 uniquement.** Le
verdict de la phase 0 peut changer l'UI de la phase 1, et la phase 1 tranche le
risque n° 3 (`CubeScope.Core` réutilisable ou non). Les phases 2 à 5 recevront
chacune leur propre plan, écrit une fois la précédente livrée.

## 10 bis. Phase 0 : GO (2026-07-27)

Le spike a été mené non pas sur une page jetable mais directement sur la SPA de
la phase 1, ce qui a validé les deux phases d'un coup, sur
`SSAS01` / `Analytics` / `Ventes` :

- le complément se charge, l'onglet de ruban apparaît ;
- le `CustomTaskPane` s'ancre et `EnsureCoreWebView2Async` réussit — la seconde
  moitié du risque n° 1 (ExcelDna #682) tombe ;
- **la frappe clavier fonctionne dans le WebView2 hébergé en volet Office** —
  critère GO/NO-GO principal. Le repli « fenêtre non-modale » n'est pas nécessaire ;
- le pont fait ses aller-retours ; contexte du TCD, MDX généré, arbre des
  métadonnées et filtre par liste fonctionnent sur données réelles.

Trois défauts ont été trouvés à cette occasion et corrigés — ils sont décrits
au risque n° 1 et en 6.4, et couverts par la checklist de recette.

## 11. Risques suivis

1. **WebView2 dans un volet Office** — historique de bugs de focus clavier
   ([WebView2Feedback #951](https://github.com/MicrosoftEdge/WebView2Feedback/issues/951))
   et d'échecs de `EnsureCoreWebView2Async` en contexte Excel-DNA
   ([ExcelDna #682](https://github.com/Excel-DNA/ExcelDna/issues/682)).
   *Levé ou non en phase 0, avant tout investissement.*

   **Constaté 2026-07-27, premier chargement réel** : `CreateCTP` échoue avec
   `COMException 0x80004005` « Impossible de créer le contrôle ActiveX
   spécifié » si le `UserControl` n'a pas d'**interface COM par défaut**.
   Office instancie le contrôle d'un CustomTaskPane par COM ; il faut
   `[ComVisible(true)]` + `[ComDefaultInterface(typeof(IPaneControl))]` sur une
   interface (même vide), comme dans l'exemple officiel Excel-DNA. Corollaire :
   tout membre public du contrôle est candidat à l'exposition COM — un
   événement générique (`EventHandler<string>`) n'y est pas représentable. On
   garde donc la surface publique du contrôle vide et tout le reste `internal`.

   **Statut : levé.** Une fois ce point corrigé, le volet s'ancre, WebView2
   s'initialise et la frappe clavier fonctionne. Voir 10 bis.

   Deux autres pièges trouvés au même moment, sans rapport avec WebView2 :
   - Excel-DNA produit par défaut un `.xll` **32 bits** *et* un 64 bits ;
     charger le 32 bits dans un Excel 64 bits donne « le format et l'extension
     du fichier ne correspondent pas », qui fait croire à un fichier corrompu.
     `ExcelDnaCreate32BitAddIn=false` : un seul livrable.
   - Un `CubeField` de hiérarchie expose **un `PivotField` par niveau**. Écrire
     `VisibleItemsList` sur le mauvais niveau fait répondre « Élément
     introuvable dans le cube OLAP ». Le nommage de ces `PivotField` n'est pas
     documenté : `PivotFilterApplier` essaie le nom unique du niveau, son
     dernier segment et la caption, puis journalise tous les candidats.
2. **Corrélation de la trace du Profiler** — la méthode de repli (fenêtre
   temporelle + `DatabaseName` + rapprochement du `TextData`) n'est pas
   vérifiée sur le terrain. *Phase 4.*
3. **`CubeScope.Core` réellement réutilisable** — s'il est trop soudé à
   ASP.NET Core (DI, `IOptions`, `ILogger`), un assainissement sera nécessaire.
   *Verdict en phase 1, à la première utilisation réelle.*
4. **Un seul runtime .NET Core par process Excel** — conflit possible avec un
   autre complément .NET chargé simultanément ; `RollForward=Major` limite la
   casse sans l'éliminer.
5. **Excel 32-bit** — hors périmètre v1, assumé.

## 12. Sources

- [VSTO Runtime lifecycle & support statement](https://learn.microsoft.com/en-us/visualstudio/vsto/visual-studio-tools-for-office-runtime?view=visualstudio)
- [Excel-DNA — .NET runtime support](https://excel-dna.net/docs/guides-basic/dotnet-runtime-support/)
- [Excel-DNA — Custom Task Panes](https://docs.excel-dna.net/reference-various/)
- [Work with PivotTables using the Excel JavaScript API](https://learn.microsoft.com/office/dev/add-ins/excel/excel-add-ins-pivottables)
- [PivotTable.MDX](https://learn.microsoft.com/office/vba/api/excel.pivottable.mdx) ·
  [PivotCell.MDX](https://learn.microsoft.com/office/vba/api/excel.pivotcell.mdx) ·
  [PivotField.VisibleItemsList](https://learn.microsoft.com/office/vba/api/excel.pivotfield.visibleitemslist) ·
  [CalculatedMembers.AddCalculatedMember](https://learn.microsoft.com/office/vba/api/excel.calculatedmembers.addcalculatedmember) ·
  [CubeField](https://learn.microsoft.com/office/vba/api/excel.cubefield) ·
  [PivotCache.MissingItemsLimit](https://learn.microsoft.com/office/vba/api/excel.pivotcache.missingitemslimit)
- Licence du projet d'origine : Ms-PL, clauses 3(A), 3(C), 3(D).
