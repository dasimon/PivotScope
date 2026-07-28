# PivotScope

Complément Excel pour développeur SSAS **Multidimensional** : écrire, exécuter
et comprendre du MDX là où on travaille vraiment — dans le tableau croisé
dynamique qu'on a sous les yeux. Frère de [CubeScope](https://github.com/dasimon/CubeScope),
dont il réutilise le moteur.

**Hors périmètre définitif : Tabular, Power BI, DAX, Power Pivot.** Ne jamais
introduire d'abstraction multi-moteurs « au cas où ».

Inspiré de OLAP PivotTable Extensions (Greg Galloway, Ms-PL), **sans aucune
reprise de code** : PivotScope est sous MIT.

## Décisions d'architecture (actées — ne pas rouvrir sans raison forte)

- **Excel-DNA**, cible `net10.0-windows`, **x64 uniquement**. VSTO est exclu
  (Microsoft : « VSTO Add-Ins can't be created with .NET »), Office.js aussi
  (« PivotTables created with OLAP are not currently supported »).
- **Volet Office (CustomTaskPane) hébergeant WebView2**, qui affiche une SPA
  Vue 3 + Monaco. Validé sur poste réel : le focus clavier fonctionne.
- **SPA embarquée en ressources**, servie sur l'origine virtuelle
  `https://pivotscope.local/` par interception de `WebResourceRequested`.
  Aucun fichier extrait sur disque.
- **Pont `postMessage`** (`{id, method, params}` → `{id, ok, result|error}`),
  pas `AddHostObjectToScript`. Le routeur **ne lève jamais** : une exception
  laisserait une promesse pendante côté SPA.
- **`PivotScope.Core` ne référence jamais `Microsoft.Office.Interop.Excel`.**
  C'est ce qui rend la logique testable sans Excel.
- **CubeScope.Core en sous-module git épinglé**, derrière `ICubeMetadataReader`
  / `IMdxExecutor` / `ILevelMemberReader` — le mécanisme de partage reste donc
  remplaçable.
- **Aucune `MessageBox`** : bandeau dans le volet, plus log fichier dans
  `%LOCALAPPDATA%\PivotScope\logs`.

## Pièges connus (payés une fois, ne pas les redécouvrir)

### Excel-DNA et COM

- **Un `CustomTaskPane` instancie son contrôle par COM.** Sans
  `[ComVisible(true)]` + `[ComDefaultInterface(typeof(I…))]` sur une interface
  (même vide), `CreateCTP` échoue en `COMException 0x80004005` « Impossible de
  créer le contrôle ActiveX spécifié ». Corollaire : tout membre public du
  contrôle est candidat à l'exposition COM, et un événement générique
  (`EventHandler<string>`) n'y est pas représentable → garder la surface
  publique vide, tout le reste `internal`.
- **Excel-DNA produit par défaut un `.xll` 32 bits ET un 64 bits.** Charger le
  32 bits dans un Excel 64 bits donne « le format et l'extension du fichier ne
  correspondent pas », qui fait croire à un fichier corrompu.
  `ExcelDnaCreate32BitAddIn=false` : un seul livrable.
- **Excel verrouille le `.xll` et ses DLL** tant que le processus vit — un seul
  classeur ouvert, même sans rapport, suffit. Pour builder sans quitter Excel :
  décocher PivotScope dans Options → Compléments → Atteindre. MSBuild nomme le
  verrou : « Le fichier est verrouillé par : "Microsoft Excel (PID)" ».
- **Threading** : Excel est STA sur son thread principal, les messages WebView2
  arrivent sur le thread UI. Tout appel COM passe par `ExcelThread` — sinon
  `RPC_E_SERVERCALL_RETRYLATER` intermittent.
- **Culture** : sur un Excel français, les API COM qui prennent des chaînes de
  formule attendent l'anglais. Bascule confinée à `InvariantFormattingScope`,
  appliquée à la frontière COM uniquement.

### Objet PivotTable

- **Un `CubeField` de hiérarchie expose UN `PivotField` PAR NIVEAU.** Écrire
  `VisibleItemsList` sur le mauvais niveau fait répondre « Élément introuvable
  dans le cube OLAP ». Le nommage de ces `PivotField` n'est pas documenté :
  `PivotFilterApplier` essaie le nom unique du niveau, son dernier segment et
  la caption, puis **journalise tous les candidats**.
- **`CubeField.IncludeNewItemsInFilter` doit valoir `False`** avant d'écrire
  `VisibleItemsList`, sinon l'affectation est **silencieusement sans effet**.
  Et `ClearManualFilter` s'appelle sur le `CubeField`, pas le `PivotField`.
- **Excel ne matérialise le `CubeField` d'une mesure de session qu'après un
  rafraîchissement.** Créer une mesure calculée puis chercher son cube field
  échoue : il faut `RefreshTable()` d'abord. (C'est à quoi servait le « Refresh
  data by default » de l'add-in d'origine.)
- **`CalculatedMember.IsValid` renvoie `True` sur un TCD déconnecté** :
  appeler `PivotCache.MakeConnection()` avant de s'y fier.
- **`PivotCache.MissingItemsLimit` ne marche que sur les TCD NON-OLAP.** Ce
  n'est pas la mécanique du « Clear Cache » ; l'original recrée la connexion du
  classeur.
- `NumberFormat` n'est valide que pour un **membre** calculé, `DisplayFolder`
  que pour une **mesure**. Hors de ces cas, le réglage est accepté puis ignoré.
- `PivotTable.MDX` lève si le TCD n'a aucun élément de données.
- `PivotCell.MDX` lève hors zone de valeurs et sur un filtre de rapport en
  sélection multiple.
- `CubeFields.GetMeasure` **ne sert pas** à afficher une mesure calculée : il ne
  concerne que les mesures implicites d'une hiérarchie d'attribut, et seulement
  pour Count/Sum/Average/Max/Min. Utiliser `AddDataField(cubeField, …)`.

### SSAS et MDX

- **Ne JAMAIS passer par `$SYSTEM.MDSCHEMA_MEMBERS`** : pas de support de `IN`,
  et un filtre sur `MEMBER_UNIQUE_NAME` scanne la dimension entière. Tout passe
  par MDX.
- **`StrToMember` sur un membre inexistant ne lève pas**, il renvoie `null`
  (constaté sur cube réel).
- L'utilisateur colle des **libellés**, pas des clés (« Aurore » quand la clé est
  « PRD014 »). `MemberResolver` essaie : nom unique complet → clé → libellé, et
  l'énumération d'un niveau ne coûte que ~79 ms pour 3 157 membres.
- Un libellé porté par **plusieurs** membres est signalé comme ambigu, jamais
  résolu au hasard : filtrer le mauvais membre produirait un chiffre faux.

### Front

- **Ne pas enfermer Monaco dans un `<label>`** : un label intercepte les clics
  et redirige le focus vers son premier contrôle, Monaco ne peut plus le
  prendre. Utiliser `.field` / `.field-label`.
- **Monaco auto-ferme les crochets** : taper `[` écrit `[]`. Une complétion qui
  insère son propre `]` produit `]]` — la plage remplacée doit avaler le
  crochet qui traîne.
- **`%(RecursiveDir)` rend un ANTISLASH** sous Windows : sans normalisation, le
  worker Monaco s'embarque en `spa/assets\x.js` alors que l'URL demande des
  slashes → 404 muet et éditeur mort. Vérifier avec
  `GetManifestResourceNames()`, pas à l'œil.
- Les trois pièges MSBuild d'embarquement hérités de CubeScope : hooker
  `PrepareForBuild` (à `CoreCompile` la liste est figée, 0 ressource) ; passer
  par un item intermédiaire qualifié pour le `LogicalName` (sinon `%(Filename)`
  s'évalue vide → `CS1508`) ; glob `**\*.*` et non `**\*`.
- **`System.Text.Json` sérialise les enums en nombres** par défaut : la SPA
  comparerait `2` à `"Measure"`. `JsonStringEnumConverter` posé sur le pont, et
  un test le verrouille.
- **`monaco-editor` 0.56 tire un `dompurify` vulnérable.** `npm audit fix
  --force` propose de rétrograder Monaco en 0.53, ce qui casserait l'exports map
  utilisée par `monaco-core` : forcer la transitive par `overrides` à la place.
- `monaco-core.ts` est la liste d'imports de Monaco dégraissé, reprise de
  CubeScope : **à resynchroniser à chaque montée de version de monaco**.

## Conventions de travail

- Chaque phase se termine par un binaire utilisable au quotidien.
- Messages d'interface en français, code et symboles en anglais.
- L'interop Excel n'est pas testable automatiquement : la contrepartie est
  [`docs/recette.md`](docs/recette.md), déroulée avant chaque tag.
- **Quand l'interop résiste, journaliser l'inventaire réel** (les `CubeFields`,
  les `PivotFields`, leurs noms et types) avant de lever. Ce réflexe a résolu
  trois bugs que la documentation seule ne permettait pas de trancher.

## Statut

**Phases 0 et 1 (2026-07-27)** — volet suivant le TCD actif, MDX généré,
explorateur de métadonnées, filtre par liste de clés ou de libellés.

**Phase 2 (2026-07-27)** — éditeur Monaco MDX avec complétion contextuelle,
requête libre → plage Excel (annulable jusqu'au serveur), calculs MDX
(mesures, membres, ensembles) avec format de nombre, bibliothèque SQLite,
confort de construction du TCD avec indicateur au ruban.

**Phase 3 (2026-07-27)** — « d'où vient ce chiffre » (tuple complet, expression,
graphe de dépendances), assistant MDX enrichi du contexte du TCD, menu
contextuel limité à trois entrées.

Phases 0 à 2 validées bout en bout sur `SSAS01` / `Analytics` /
`Ventes` ; phase 3 en attente de recette.

Limitation connue de l'assistant : `AiService.RunAsync` bâtit son contexte cube
à partir de `cubes[0]` du catalogue, pas du cube courant. Sur un catalogue
multi-cubes le contexte injecté est **appauvri**, pas faux. Si la qualité des
réponses en souffre, ajouter un paramètre `cube` à `RunAsync` dans le
sous-module — ce serait aussi un correctif pour CubeScope.

**Packaging (2026-07-27)** — `build\pack.ps1` produit un dossier de 4 fichiers
(le `.xll` empaqueté + trois natives) et son zip, au lieu des 76 fichiers du
dossier de build. Workflow `release.yml` sur tag `v*`.

**Reste en dette** : le dépôt n'a **pas de remote** et rien n'est poussé.

### Le packaging, en pratique

- `ExcelDnaPack` ne tourne qu'au **`dotnet publish`**, pas au `build` : pour un
  projet SDK, `ExcelDnaPublishPath` reste vide et l'empaquetage vise le dossier
  de publication. Le `.xll` empaqueté sort dans `bin\<conf>\<tfm>\publish\`.
- `ExcelDnaPackNativeLibraryDependencies=true` est posé dans le `.csproj` mais
  **reste sans effet observable** avec ExcelDna.AddIn 1.9 : le `.xll` fait la
  même taille et ne contient ni `e_sqlite3` ni `WebView2Loader`. D'où les
  natives livrées à côté, dans `runtimes\win-x64\native\` — l'emplacement où
  .NET les résout. À réessayer à la prochaine montée d'Excel-DNA.
- **Un `.ps1` contenant des accents doit avoir un BOM UTF-8** : Windows
  PowerShell 5.1 lit sinon le fichier en ANSI et échoue à l'analyse. `pwsh` n'a
  pas ce défaut, mais on ne choisit pas l'interpréteur de celui qui lance.
- Le seul test qui vaut : extraire le zip dans un dossier **isolé** et charger
  le `.xll` de là. Enregistrer un calcul dans la bibliothèque exerce SQLite,
  donc la résolution des natives.

**Phases suivantes** : 3 = panneau IA et « d'où vient ce chiffre » ;
4 = Profiler FE/SE et Clear PivotTable Cache ; 5 = packaging et publication.
