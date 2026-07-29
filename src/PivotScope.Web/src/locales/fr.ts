// Catalogue de référence. `en.ts` est typé `typeof fr` : toute clé oubliée
// devient une erreur de compilation, pas un texte vide découvert en production.
//
// Piège vue-i18n : ne JAMAIS écrire « | » dans un message, c'est le séparateur
// de pluriel — le texte serait coupé en morceaux silencieusement.
export default {
  app: {
    notHosted: "Cette page doit être ouverte depuis le volet PivotScope d'Excel.",
    hide: 'Masquer',
    language: 'Langue',
  },

  tabs: {
    table: 'Tableau',
    query: 'Requête',
    calc: 'Calculs',
    provenance: 'Ce chiffre',
    ai: 'IA',
  },

  common: {
    load: 'Charger',
    reload: 'Recharger',
    loading: 'Chargement…',
    refresh: 'Actualiser',
    reading: 'Lecture…',
    apply: 'Appliquer',
    applying: 'Application…',
    stop: 'Arrêter',
    copy: 'Copier',
    copied: 'Copié',
    remove: 'Supprimer',
    choose: '— choisir —',
    noPivot: 'Aucun tableau croisé dynamique OLAP actif.',
    placeCursor: 'Placez le curseur dans un tableau croisé dynamique.',
  },

  header: {
    title: 'Tableau croisé dynamique',
    server: 'Serveur',
    catalog: 'Catalogue',
    cube: 'Cube',
    fields: 'Champs',
    readingContext: 'Lecture du contexte…',
  },

  mdx: {
    title: 'MDX généré',
    empty:
      'Aucune requête à afficher : déposez au moins une mesure dans le tableau ' +
      'croisé dynamique.',
  },

  metadata: {
    title: 'Métadonnées du cube',
    filter: 'Filtrer dimensions et mesures…',
    notLoaded:
      'Les métadonnées ne sont pas chargées. Elles nécessitent une connexion au ' +
      'cube, qui n’est ouverte qu’à la demande.',
    measures: 'Mesures ({count} dossiers)',
    dimensions: 'Dimensions ({count})',
    root: '(racine)',
    level: 'niveau {number}',
  },

  filter: {
    title: 'Filtrer par une liste',
    field: 'Champ du tableau croisé dynamique',
    level: 'Niveau contenant les clés',
    loadMetaFirst:
      'Chargez les métadonnées du cube pour lister les niveaux de ce champ.',
    values: 'Valeurs à conserver ({count})',
    placeholder: 'Une valeur par ligne — clé (PRD014) ou libellé (Aurore)',
    hint:
      'Clés et libellés sont acceptés. La clé est essayée d’abord ; à défaut, le ' +
      'libellé est recherché parmi les membres du niveau choisi.',
    action: 'Appliquer le filtre',
    applied: '{count} membre(s) appliqué(s).',
    unresolved:
      '{count} valeur(s) introuvable(s) à ce niveau, ni comme clé ni comme libellé :',
    ambiguous:
      '{count} libellé(s) porté(s) par plusieurs membres — non appliqué(s), collez ' +
      'la clé pour lever le doute :',
  },

  query: {
    title: 'Requête MDX',
    newSheet: 'Nouvelle feuille',
    headers: 'En-têtes',
    run: 'Exécuter (F5)',
    running: 'Exécution…',
    template: 'Modèle',
    cancelled: 'Requête arrêtée.',
    written: '{rows} ligne(s) × {columns} colonne(s) écrites en {ms} ms.',
  },

  calc: {
    title: 'Calculs du tableau',
    kind: 'Nature',
    kindMeasure: 'Mesure calculée',
    kindMember: 'Membre calculé',
    kindSet: 'Ensemble nommé',
    name: 'Nom',
    namePlaceholder: 'Marge nette',
    parentHierarchy: 'Hiérarchie parente',
    displayFolder: 'Dossier d’affichage',
    displayFolderPlaceholder: 'Rentabilité (facultatif)',
    numberFormat: 'Format de nombre',
    numberFormatPlaceholder: '#,##0.00 (facultatif)',
    numberFormatHint:
      'Excel ne propose aucune interface pour formater un membre calculé — seule ' +
      'une macro peut le faire. PivotScope le fait ici.',
    expression: 'Expression MDX',
    solveOrder: 'Ordre de résolution',
    addToPivot: 'Ajouter au tableau après création',
    create: 'Créer / remplacer',
    saveToLibrary: 'Enregistrer dans la bibliothèque',
    onThisTable: 'Sur ce tableau',
    none: 'Aucun calcul sur ce tableau.',
    invalid: 'invalide',
    library: 'Bibliothèque',
    libraryEmpty:
      'La bibliothèque est vide. Enregistrez un calcul pour le retrouver dans un ' +
      'autre classeur.',
    allCubes: 'tous cubes',
  },

  provenance: {
    title: 'D’où vient ce chiffre ?',
    analyse: 'Analyser la cellule',
    intro:
      'Placez le curseur sur une cellule de valeur du tableau, puis lancez ' +
      'l’analyse. Excel donnera ses coordonnées complètes — filtres de rapport ' +
      'compris — et PivotScope remontera jusqu’à l’expression qui la produit.',
    coordinates: 'Coordonnées complètes de la cellule',
    measure: 'Mesure',
    context: 'Contexte',
    expression: 'Expression',
    atLine: '— ligne {line} du script du cube',
    explainWithAi: 'Expliquer avec l’IA',
    uses: 'Ce que ce calcul utilise',
    usedBy: 'Utilisé par {count} autre(s) calcul(s)',
    kindCalculatedMember: 'membre calculé',
    kindNamedSet: 'ensemble nommé',
    kindMeasure: 'mesure',
    kindHierarchy: 'hiérarchie',
  },

  ai: {
    title: 'Assistant MDX',
    notConfigured:
      'L’assistant n’est pas configuré. Définissez la variable d’environnement ' +
      'ANTHROPIC_API_KEY, puis relancez Excel. La clé n’est jamais enregistrée ' +
      'par PivotScope.',
    source: 'MDX à analyser — le contexte du tableau est joint automatiquement',
    useTableQuery: 'Reprendre la requête du tableau',
    running: 'Analyse en cours…',
    explain: 'Expliquer',
    explainHint: 'Que fait cette requête ?',
    optimise: 'Optimiser',
    optimiseHint: 'Comment la rendre plus rapide ?',
    antiPatterns: 'Anti-patterns',
    antiPatternsHint: 'Quels pièges MDX s’y trouvent ?',
    format: 'Formater',
    formatHint: 'La réécrire lisiblement.',
  },

  comfort: {
    title: 'Construction du tableau',
    defer: 'Différer la mise en page',
    deferHint:
      'Activez-le pour déposer plusieurs champs sans attendre le serveur à chaque ' +
      'geste, puis appliquez tout d’un coup. L’état reste visible dans le ruban : ' +
      'on ne peut pas l’oublier et croire ensuite que le tableau est faux.',
    refreshNow: 'Appliquer et actualiser',
    refreshing: 'Actualisation…',
    levels: 'Niveaux affichés',
    levelsHint:
      'Une hiérarchie à quatre ou cinq niveaux les impose tous. Cochez ceux que ' +
      'vous voulez voir : Excel n’offre nulle part ce choix.',
    hierarchy: 'Hiérarchie posée sur le tableau',
    noHierarchy:
      'Aucune hiérarchie en ligne ou en colonne. Chargez les champs, ou posez-en ' +
      'une sur le tableau.',
    applyLevels: 'Appliquer les niveaux',
    keepOneLevel: 'Gardez au moins un niveau.',
    pendingChanges: 'Modifications non appliquées.',
    fieldList: 'Champs de la liste',
    fieldListEmpty:
      'Chargez les champs pour choisir ceux qui restent visibles dans la liste de ' +
      'champs du tableau croisé dynamique.',
    filterFields: 'Filtrer les champs…',
    hiddenCount: '{hidden} masqué(s) sur {total}',
    showAll: 'Tout réafficher',
    laidOutHint:
      'Un champ posé sur le tableau ne peut pas être masqué de la liste : ' +
      'retirez-le d’abord de la disposition.',
  },
}
