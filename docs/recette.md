# Recette manuelle

L'interop Excel n'est pas couverte par les tests automatisés — c'est une limite
assumée, pas un oubli. Cette checklist est le contrepoids : elle se déroule
**avant chaque tag**, sur un poste avec Excel 64-bit et un cube SSAS
Multidimensional accessible.

Noter la version testée et la date en bas de page.

## Préparation

- [ ] `npm ci && npm run build` dans `src/PivotScope.Web`
- [ ] `dotnet build -c Release`
- [ ] Libérer le `.xll`. Excel est un **processus unique** : un seul classeur
      encore ouvert, même sans rapport avec PivotScope, suffit à verrouiller
      `PivotScope64.xll` et les DLL voisines, et le build échoue en
      `UnauthorizedAccessException`.
      Deux moyens :
      - fermer **tous** les classeurs — vérifier avec
        `tasklist /FI "IMAGENAME eq EXCEL.EXE" /V` qu'aucun processus ne reste ;
      - ou, sans quitter Excel, **décocher PivotScope** dans Fichier → Options
        → Compléments → *Gérer : Compléments Excel* → Atteindre. Le fichier est
        relâché aussitôt. C'est le geste à privilégier en développement.

      En cas de doute, MSBuild nomme lui-même le verrou :
      « Le fichier est verrouillé par : "Microsoft Excel (PID)" ».
- [ ] Charger `src/PivotScope.AddIn/bin/Release/net10.0-windows/PivotScope64.xll`
      via Excel → Options → Compléments → *Gérer : Compléments Excel* →
      Atteindre → Parcourir.
      **Ne pas** double-cliquer le `.xll` ni passer par Fichier → Ouvrir :
      Excel le prendrait pour un classeur et afficherait « le format et
      l'extension du fichier ne correspondent pas ».

## Chargement

- [ ] L'onglet de ruban **PivotScope** apparaît
- [ ] `%LOCALAPPDATA%\PivotScope\logs\pivotscope-<date>.log` contient
      « PivotScope chargé »
- [ ] Aucune boîte de dialogue n'est apparue au démarrage
- [ ] Excel n'a pas rangé le complément dans ses éléments désactivés
      (Options → Compléments → Gérer : Éléments désactivés)

## Volet

- [ ] Le bouton « Volet PivotScope » ancre un volet à droite
- [ ] Le volet affiche l'interface, pas une page blanche ni une erreur WebView2
- [ ] **Test du focus clavier** (critère GO/NO-GO de la phase 0) : onglet
      *Filtre*, taper dans la zone de collage — les caractères s'inscrivent.
      ⚠️ Cette zone n'apparaît **que** si un TCD OLAP est actif : faire d'abord
      la section « Contexte du TCD » ci-dessous, sinon le volet n'affiche que
      le message de dégradation et il n'y a rien où taper.
- [ ] Les touches Ctrl+A, Ctrl+C et Ctrl+V fonctionnent dans cette zone
- [ ] Le volet se ferme et se rouvre sans erreur

## Contexte du TCD

- [ ] Curseur **hors** de tout TCD → « Placez le curseur dans un tableau croisé
      dynamique. » Aucune exception.
- [ ] Curseur dans un TCD **non-OLAP** (source tableau Excel) → message
      expliquant que seul SSAS Multidimensional est pris en charge
- [ ] Curseur dans un TCD OLAP → serveur, catalogue, cube et nombre de champs
      corrects
- [ ] « Actualiser » après avoir déposé un champ reflète le changement

## MDX généré

- [ ] Le MDX du TCD s'affiche dans l'onglet *Aperçu*
- [ ] « Copier » place bien la requête dans le presse-papiers
- [ ] TCD **sans aucune mesure** → message d'invitation, pas d'erreur
      (`PivotTable.MDX` lève dans ce cas, c'est documenté)

## Métadonnées

- [ ] « Charger » remplit l'arbre des dimensions et des mesures
- [ ] Le filtre textuel réduit l'arbre
- [ ] Les niveaux d'une hiérarchie sont listés avec leur nom unique
- [ ] Le premier chargement ouvre la connexion SSAS (visible dans le log), les
      suivants sont instantanés

## Filtre par liste

- [ ] Sélectionner un champ posé en ligne, puis un niveau
- [ ] **Sur une hiérarchie à plusieurs niveaux**, filtrer sur un niveau qui
      n'est **pas** le premier. C'est le cas qui a cassé en recette le
      2026-07-27 : un CubeField expose un PivotField par niveau, et viser le
      mauvais fait répondre « Élément introuvable dans le cube OLAP ».
- [ ] Coller un **libellé** (ex. `Aurore`) dont la clé est différente
      (ex. `PRD014`) → résolu et appliqué
- [ ] Coller la **clé** directement → même résultat
- [ ] Coller **3 valeurs valides + 1 invalide** → le TCD est filtré sur les 3
      valides, et l'invalide est listée en rouge
- [ ] Coller des clés séparées par des virgules, des points-virgules et des
      tabulations → toutes reconnues
- [ ] Coller une liste avec des doublons → appliquée une seule fois
- [ ] Coller **uniquement** des clés invalides → message d'erreur clair dans le
      bandeau, TCD inchangé
- [ ] Coller une longue liste (> 100 clés) → découpage en lots transparent,
      résultat complet

## Requête MDX libre (phase 2)

- [ ] L'éditeur affiche la coloration MDX
- [ ] La complétion après `[Measures].` ne propose **que** des mesures, et
      l'insertion ne duplique pas le préfixe
- [ ] La complétion après `[Dim].` propose les hiérarchies de cette dimension
- [ ] La complétion après `[Dim].[Hier].` propose les membres
- [ ] **F5** et **Ctrl+Entrée** exécutent
- [ ] Résultat en nouvelle feuille : plage écrite, adresse et durée affichées
- [ ] Résultat à la cellule active, **curseur hors du TCD** : fonctionne (la
      connexion est mémorisée, elle n'exige pas un TCD sous le curseur)
- [ ] Cellule active **dans** un TCD : refusé avec un message explicite
- [ ] Sans en-têtes : la première ligne contient des données
- [ ] MDX invalide : bandeau d'erreur portant le message SSAS, rien d'écrit
- [ ] **Arrêter** sur une requête longue : arrêt effectif, « Requête arrêtée. »
      en gris et non en bandeau rouge

## Calculs (phase 2)

- [ ] Créer une **mesure calculée** simple (`1`) → apparaît dans le TCD
- [ ] Créer une mesure avec un **dossier d'affichage** → rangée dans ce dossier
- [ ] Créer un **membre calculé** avec une hiérarchie parente et le format
      `#,##0.00` → **formaté**, ce qu'aucune interface Excel ne permet
- [ ] Tenter un format sur une *mesure* → refusé avant d'atteindre Excel
- [ ] Tenter un dossier sur un *membre* → refusé de même
- [ ] MDX invalide → message clair, aucun calcul laissé derrière
- [ ] Recréer un calcul de même nom → remplace au lieu d'échouer
- [ ] Supprimer un calcul → disparaît du TCD et de la liste

## Bibliothèque (phase 2)

- [ ] Enregistrer un calcul → apparaît dans la bibliothèque
- [ ] Réenregistrer le même nom pour le même cube → **mis à jour**, pas dupliqué
- [ ] « Charger » remplit le formulaire avec tous les champs, format compris
- [ ] Fermer et rouvrir Excel → la bibliothèque est toujours là
- [ ] Supprimer une entrée → disparaît

## Construction (phase 2)

- [ ] « Charger » liste les champs du cube avec leur état
- [ ] Décocher un champ **non posé** → il disparaît de la liste de champs d'Excel
- [ ] Décocher un champ **posé sur le TCD** → refusé avec explication
- [ ] « Tout réafficher » restaure, le compteur revient à zéro
- [ ] Couper le rafraîchissement → le bouton du ruban se **relâche**
- [ ] Le rétablir → le bouton se **renfonce**, un seul aller-retour serveur

## Menu contextuel (phase 3)

- [ ] Clic droit dans un TCD → **trois** entrées PivotScope, pas davantage
- [ ] « D'où vient ce chiffre ? » ouvre le volet sur le bon onglet
- [ ] Décharger le complément → les entrées disparaissent du menu

## D'où vient ce chiffre (phase 3)

- [ ] Sur une **cellule de valeur** → tuple complet affiché, filtres de rapport
      compris
- [ ] Sur un **en-tête** ou un **total** → message clair, pas d'exception
- [ ] Avec un **filtre de rapport en sélection multiple** → message expliquant
      qu'il faut le réduire à un seul élément
- [ ] Sur une **mesure physique** → note « mesure physique », pas une erreur
- [ ] Sur une **mesure calculée** → expression, numéro de ligne dans le script,
      arbre des dépendances, et la liste « utilisé par »
- [ ] « Expliquer avec l'IA » bascule sur l'onglet IA avec l'expression remplie

## Assistant MDX (phase 3)

- [ ] **Sans** `ANTHROPIC_API_KEY` → message clair, boutons désactivés, aucun
      appel réseau
- [ ] Avec la clé → les quatre actions répondent
- [ ] La réponse tient compte du **contexte du tableau** (elle mentionne les
      champs réellement posés, pas seulement la requête)
- [ ] « Reprendre la requête du tableau » remplit l'éditeur
- [ ] **Arrêter** pendant une réponse → interruption propre, pas de bandeau rouge
- [ ] Une réponse contenant `<script>` ou du HTML s'affiche **en texte**, jamais
      interprétée

## Livrable autonome (phase 5)

Le test qui décide si PivotScope est distribuable. Il a une histoire : sur
CubeScope, le premier exe publié était cassé une fois déplacé, et ça ne s'est
vu qu'au téléchargement.

- [ ] `pwsh build\pack.ps1` produit `artifacts\PivotScope.zip`
- [ ] Le dossier contient **4 fichiers utiles** : le `.xll` et trois natives
      sous `runtimes\win-x64\native\`
- [ ] Extraire le zip dans un dossier **isolé**, hors du dépôt — typiquement
      `%USERPROFILE%\Téléchargements\PivotScope`
- [ ] Charger `PivotScope64.xll` **depuis ce dossier**
- [ ] Le volet s'ouvre, la SPA s'affiche (assemblies managées bien fusionnées)
- [ ] **Enregistrer un calcul dans la bibliothèque** : c'est ce geste qui
      sollicite SQLite, donc `e_sqlite3.dll`. S'il échoue, la native n'est pas
      résolue depuis cet emplacement.
- [ ] Fermer Excel, rouvrir, recharger : la bibliothèque a survécu

## Suivi automatique et navigation (refonte ergonomique)

- [ ] **Le volet suit le TCD sans qu'on le lui demande** : déplacer le curseur
      d'un TCD à un autre → l'en-tête change seul, sans cliquer « Actualiser »
- [ ] Déposer un champ sur le TCD → le MDX affiché se met à jour seul
- [ ] Parcourir rapidement beaucoup de cellules → **un seul** rechargement, pas
      un par cellule (les notifications sont regroupées)
- [ ] Changer de classeur → l'en-tête suit
- [ ] **L'en-tête reste visible depuis les cinq onglets**
- [ ] Les cinq onglets tiennent dans la largeur du volet, sans troncature
- [ ] Ouvrir *Tableau* → les champs se chargent seuls
- [ ] Ouvrir *Calculs* → calculs et bibliothèque se chargent seuls
- [ ] Ouvrir *Ce chiffre* sur une cellule de valeur → l'analyse se fait seule
- [ ] Ouvrir *Requête* → l'explorateur de métadonnées est là, replié
- [ ] Hors TCD, changer d'onglet ne déclenche **aucun** appel serveur

## Déchargement

- [ ] Fermer Excel : « PivotScope déchargé » dans le log
- [ ] Aucun processus `EXCEL.EXE` résiduel

---

| Version testée | Date | Testeur | Résultat |
|---|---|---|---|
| 0.1.0 (phases 0-1) | 2026-07-27 | David | ✅ chargement, volet, focus clavier, contexte du TCD, MDX, métadonnées, filtre par libellé — validé sur `SSAS01` / `Analytics` / `Ventes` |
| 0.2.0 (phase 2) | 2026-07-27 | David | ✅ éditeur Monaco et complétion contextuelle, requête libre → plage (nouvelle feuille et cellule active, avec et sans en-têtes), création d'une mesure calculée affichée dans le TCD |
| 0.3.0 (phase 3) | 2026-07-28 | David | ✅ provenance d'une cellule, assistant MDX, menu contextuel |
| 0.3.0 (livrable) | 2026-07-28 | David | ✅ **zip extrait dans un dossier isolé, chargé depuis là : fonctionne** — les assemblies managées sont bien fusionnées et les natives résolues |
| 0.4.0 | 2026-07-28 | David | ✅ sélecteur de niveaux, suivi automatique du TCD, refonte du volet en cinq onglets, en-tête permanent, icône du ruban |
