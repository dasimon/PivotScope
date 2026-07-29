# PivotScope — plan d'implémentation, phase 4

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** savoir ce qu'Excel demande réellement au serveur — combien de
requêtes, combien de temps, et lesquelles — pour n'importe quelle opération sur
le tableau croisé dynamique.

**Architecture:** une trace SSAS ouverte autour de l'opération, corrélée par
**fenêtre temporelle et base de données**, pas par `SessionID`. C'est le point
dur : Excel rafraîchit avec **sa propre connexion**, PivotScope n'a aucun moyen
d'en connaître le `SessionID`.

**Tech Stack:** inchangé. AMO (`Microsoft.AnalysisServices`) est déjà tiré par
`CubeScope.Core`.

## Pourquoi cette phase existe maintenant

Elle était classée « coûteuse et pas quotidienne ». Une question de David lui a
donné sa raison d'être : *« on est vraiment obligé de refaire une requête vu
qu'on a déjà toutes les données ? »* — et rien dans l'API Excel ne permet d'y
répondre. Seule une trace serveur le dit sans ambiguïté.

C'est aussi la leçon d'un instrument raté : une première tentative comparait
`PivotTable.MDX` avant/après, mais confondait « requête inchangée » avec
« requête illisible », et lisait de toute façon la requête du **dernier
rafraîchissement effectué**, pas de celui en cours.

## Global Constraints

Celles des phases 1 à 3 s'appliquent. Spécifiques à celle-ci :

- **Une trace SSAS est globale au serveur.** Toujours `Stop()` + `Drop()` en
  `finally`, et nettoyer les traces orphelines d'un process mort.
- **Chaque `TraceEventClass` a sa propre liste blanche de colonnes**, validée
  côté serveur au `Update()`, pas au `Columns.Add()`. Couple invalide →
  `OperationException` « L'ID d'événement Id=X ne contient pas l'ID Id=Y ».
  Prévoir la boucle auto-corrective qui retire la colonne fautive et réessaie.
- **Ne suivre que les événements « complétés »** (`QueryEnd`,
  `QuerySubcube(Verbose)`, `GetDataFrom*`) : les « Begin » n'ont pas de durée.
- **Droits admin SSAS requis** pour créer une trace. Sans eux, la fonction se
  dégrade avec un message clair — elle ne doit jamais empêcher d'utiliser
  PivotScope.
- **Ne jamais tracer en continu.** Une trace ouverte en permanence sur un
  serveur de production est un coût imposé aux autres. Elle s'ouvre pour une
  mesure et se ferme aussitôt.

## Ce qui n'est PAS repris de CubeScope

`ProfilerService.DrainSince(session, sinceUtc)` filtre par `SessionID`, ce qui
suppose que l'appelant possède la connexion. Ici c'est Excel qui la possède :
la stratégie de corrélation est différente, donc le service l'est aussi. On ne
duplique pas par confort, on écrit autre chose parce que le problème est autre.

---

### Task 1: ~~Agrégation d'une capture~~ — supprimée

`CubeScope.Core.Profiler.ProfileAggregator.Aggregate(events, fallbackTotalMs)`
fait déjà exactement ce découpage — total = `QueryEnd`, SE = Σ `QuerySubcube`,
FE = différence, hits de cache et d'agrégation comptés — et il est déjà testé
là-bas. Écrire un second agrégateur aurait été un doublon de confort.

Mesure du 2026-07-28, qui justifie cette phase (voir Task 3) : la première
application de niveaux a coûté **301 594 ms**, les quatre suivantes ~130 ms.
La comparaison de `PivotTable.MDX` annonce « identique » dans les cinq cas —
elle ne reflète donc pas la visibilité des niveaux et **ne peut pas répondre
à la question**. Seule une trace serveur le peut.

---

### Task 2: Trace SSAS pilotée

**Files:**
- Create: `src/PivotScope.Core/Profiling/SsasTrace.cs`

**Interfaces:**
- Produces:
  - `SsasTrace(string dataSource, string database)`
  - `Task<bool> StartAsync(CancellationToken)` → faux si les droits manquent
  - `IReadOnlyList<ProfileEvent> StopAndDrain()`
  - `string? Unavailable` — raison lisible quand la trace n'a pas pu démarrer

Nommage `PivotScope_<pid>`, filtre sur `DatabaseName`, nettoyage des traces
orphelines dont le process est mort. Boucle auto-corrective sur la liste
blanche de colonnes.

- [ ] **Step 1: Implémenter**
- [ ] **Step 2: Vérifier sur le serveur** — démarrage, capture d'une requête
      connue, arrêt, absence de trace résiduelle côté serveur
- [ ] **Step 3: Commit**

---

### Task 3: Mesurer une opération du tableau

**Files:**
- Modify: `src/PivotScope.AddIn/Interop/PivotComfort.cs`
- Modify: `src/PivotScope.AddIn/Pane/WebBridge.cs`
- Create: `src/PivotScope.Web/src/components/ActivityPanel.vue`

Une case **« Mesurer l'impact serveur »** dans l'onglet Construction. Quand elle
est cochée, l'application des niveaux — et le bouton « Appliquer et actualiser »
— sont encadrés d'une trace, et le résultat s'affiche :

> 2 requêtes, 1 840 ms serveur dont 1 620 ms Storage Engine, 14 sous-cubes,
> 3 hits de cache.

Décochée par défaut : on ne trace pas un serveur de production sans l'avoir
demandé.

- [ ] **Step 1: Câbler la mesure autour de l'opération**
- [ ] **Step 2: Afficher le résultat**
- [ ] **Step 3: Vérifier dans Excel** — sur le scénario qui a soulevé la
      question : masquer deux niveaux d'une hiérarchie et lire ce que le
      serveur a réellement fait
- [ ] **Step 4: Commit**

---

### Task 4: Recette, CHANGELOG, gate

- [ ] Étendre `docs/recette.md` : mesure avec droits, sans droits (dégradation),
      absence de trace résiduelle après usage.
- [ ] Mettre à jour `CHANGELOG.md` et le statut de `CLAUDE.md`.
- [ ] Gate complète, puis commit.

---

## Reporté

**Clear PivotTable Cache** reste hors périmètre. C'est la seule fonction de la
feuille de route qui **modifie la connexion du classeur de l'utilisateur** :
suppression et recréation, refus si plusieurs TCD la partagent. Le rapport
valeur/risque ne la justifie pas tant que le reste n'est pas éprouvé au
quotidien.

## Auto-revue du plan

**Couverture** — la question « Excel requête-t-il le serveur ? » est traitée
par les tâches 1 à 3. Le découpage Formula/Storage Engine annoncé dans la spec
est dans la tâche 1.

**Cohérence des types** — `ProfileEvent` vient de `CubeScope.Core.Models` et
traverse `SsasTrace` (Task 2) vers `ActivityAggregator` (Task 1), qui produit
le `ServerActivity` affiché en Task 3.

**Point où le serveur fait foi** : les couples événement/colonne acceptés. La
boucle auto-corrective est là pour ça — journaliser le couple refusé avant de
réessayer, comme partout ailleurs dans ce projet.
