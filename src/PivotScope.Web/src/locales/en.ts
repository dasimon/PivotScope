import type fr from './fr'

// Typé `typeof fr` : oublier une clé devient une erreur de compilation. C'est
// tout l'intérêt — un catalogue incomplet ne se voit sinon qu'en production,
// sur le seul écran que personne n'ouvre.
const en: typeof fr = {
  app: {
    notHosted: "This page must be opened from Excel's PivotScope pane.",
    hide: 'Hide',
    language: 'Language',
  },

  tabs: {
    table: 'Table',
    query: 'Query',
    calc: 'Calculations',
    provenance: 'This figure',
    ai: 'AI',
  },

  common: {
    load: 'Load',
    reload: 'Reload',
    loading: 'Loading…',
    refresh: 'Refresh',
    reading: 'Reading…',
    apply: 'Apply',
    applying: 'Applying…',
    stop: 'Stop',
    copy: 'Copy',
    copied: 'Copied',
    remove: 'Delete',
    choose: '— choose —',
    noPivot: 'No OLAP PivotTable is active.',
    placeCursor: 'Place the cursor inside a PivotTable.',
  },

  header: {
    title: 'PivotTable',
    server: 'Server',
    catalog: 'Catalog',
    cube: 'Cube',
    fields: 'Fields',
    readingContext: 'Reading context…',
  },

  mdx: {
    title: 'Generated MDX',
    empty: 'Nothing to show: drop at least one measure into the PivotTable.',
  },

  metadata: {
    title: 'Cube metadata',
    filter: 'Filter dimensions and measures…',
    notLoaded:
      'Metadata is not loaded. It needs a connection to the cube, which is only ' +
      'opened on demand.',
    measures: 'Measures ({count} folders)',
    dimensions: 'Dimensions ({count})',
    root: '(root)',
    level: 'level {number}',
  },

  filter: {
    title: 'Filter by a list',
    field: 'PivotTable field',
    level: 'Level holding the keys',
    loadMetaFirst: "Load the cube's metadata to list this field's levels.",
    values: 'Values to keep ({count})',
    placeholder: 'One value per line — key (PRD014) or caption (Aurore)',
    hint:
      'Keys and captions are both accepted. The key is tried first; failing that, ' +
      'the caption is looked up among the members of the chosen level.',
    action: 'Apply filter',
    applied: '{count} member(s) applied.',
    unresolved:
      '{count} value(s) not found at this level, neither as a key nor as a caption:',
    ambiguous:
      '{count} caption(s) borne by several members — not applied, paste the key to ' +
      'disambiguate:',
  },

  query: {
    title: 'MDX query',
    newSheet: 'New sheet',
    headers: 'Headers',
    run: 'Run (F5)',
    running: 'Running…',
    template: 'Template',
    cancelled: 'Query stopped.',
    written: '{rows} row(s) × {columns} column(s) written in {ms} ms.',
  },

  calc: {
    title: 'PivotTable calculations',
    kind: 'Kind',
    kindMeasure: 'Calculated measure',
    kindMember: 'Calculated member',
    kindSet: 'Named set',
    name: 'Name',
    namePlaceholder: 'Net margin',
    parentHierarchy: 'Parent hierarchy',
    displayFolder: 'Display folder',
    displayFolderPlaceholder: 'Profitability (optional)',
    numberFormat: 'Number format',
    numberFormatPlaceholder: '#,##0.00 (optional)',
    numberFormatHint:
      'Excel exposes no interface for formatting a calculated member — only a ' +
      'macro can. PivotScope does it here.',
    expression: 'MDX expression',
    solveOrder: 'Solve order',
    addToPivot: 'Add to the table once created',
    create: 'Create / replace',
    saveToLibrary: 'Save to library',
    onThisTable: 'On this table',
    none: 'No calculation on this table.',
    invalid: 'invalid',
    library: 'Library',
    libraryEmpty:
      'The library is empty. Save a calculation to find it again in another ' +
      'workbook.',
    allCubes: 'all cubes',
  },

  provenance: {
    title: 'Where does this figure come from?',
    analyse: 'Analyse the cell',
    intro:
      'Place the cursor on a value cell of the table, then run the analysis. ' +
      'Excel will give its full coordinates — report filters included — and ' +
      'PivotScope will trace back to the expression that produces it.',
    coordinates: 'Full cell coordinates',
    measure: 'Measure',
    context: 'Context',
    expression: 'Expression',
    atLine: "— line {line} of the cube's script",
    explainWithAi: 'Explain with AI',
    uses: 'What this calculation uses',
    usedBy: 'Used by {count} other calculation(s)',
    kindCalculatedMember: 'calculated member',
    kindNamedSet: 'named set',
    kindMeasure: 'measure',
    kindHierarchy: 'hierarchy',
  },

  ai: {
    title: 'MDX assistant',
    notConfigured:
      'The assistant is not configured. Set the ANTHROPIC_API_KEY environment ' +
      'variable, then restart Excel. PivotScope never stores the key.',
    source: 'MDX to analyse — the table context is attached automatically',
    useTableQuery: "Use the table's query",
    running: 'Analysing…',
    explain: 'Explain',
    explainHint: 'What does this query do?',
    optimise: 'Optimise',
    optimiseHint: 'How can it be made faster?',
    antiPatterns: 'Anti-patterns',
    antiPatternsHint: 'Which MDX pitfalls does it contain?',
    format: 'Format',
    formatHint: 'Rewrite it readably.',
  },

  comfort: {
    title: 'Building the table',
    defer: 'Defer layout update',
    deferHint:
      'Turn this on to drop several fields without waiting for the server each ' +
      'time, then apply everything at once. The state stays visible in the ribbon, ' +
      'so you cannot forget it and conclude the table is wrong.',
    refreshNow: 'Apply and refresh',
    refreshing: 'Refreshing…',
    levels: 'Displayed levels',
    levelsHint:
      'A hierarchy with four or five levels imposes all of them. Tick the ones you ' +
      'want to see: Excel offers this choice nowhere.',
    hierarchy: 'Hierarchy on the table',
    noHierarchy:
      'No hierarchy on rows or columns. Load the fields, or drop one onto the table.',
    applyLevels: 'Apply levels',
    keepOneLevel: 'Keep at least one level.',
    pendingChanges: 'Unapplied changes.',
    fieldList: 'Fields in the list',
    fieldListEmpty:
      "Load the fields to choose which ones stay visible in the PivotTable's field " +
      'list.',
    filterFields: 'Filter fields…',
    hiddenCount: '{hidden} hidden out of {total}',
    showAll: 'Show all again',
    laidOutHint:
      'A field laid out on the table cannot be hidden from the list: remove it from ' +
      'the layout first.',
  },
}

export default en
