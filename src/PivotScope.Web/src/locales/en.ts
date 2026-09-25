import type fr from './fr'

// Typed `typeof fr`: forgetting a key becomes a compile error. That is
// the whole point — otherwise an incomplete catalog only shows up in production,
// on the one screen nobody opens.
const en: typeof fr = {
  app: {
    notHosted: "This page must be opened from Excel's PivotScope pane.",
    hide: 'Hide',
    language: 'Language',
    bridgeUnavailable: 'The bridge is not available: open the pane from Excel.',
    bridgeTimeout:
      'Excel did not answer in time. A cell may be in edit mode: confirm or cancel ' +
      'the entry, then try again.',
    unknownError: 'Unknown error.',
  },

  diagnostic: {
    noPivot: 'Place the cursor inside a PivotTable.',
    notOlap:
      'This PivotTable is not connected to an OLAP cube. PivotScope only supports ' +
      'SSAS Multidimensional.',
    powerPivot:
      "This PivotTable is based on the workbook's Data Model (Power Pivot). " +
      'PivotScope only supports SSAS Multidimensional.',
    connectionUnreadable:
      "This PivotTable's connection could not be read (server or catalog missing). " +
      "Check the workbook's connection.",
  },

  areas: {
    row: 'row',
    column: 'column',
    filter: 'filter',
    data: 'values',
  },

  completion: {
    measure: 'measure',
    member: 'member',
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
    applying: 'Applying…',
    stop: 'Stop',
    copy: 'Copy',
    copied: 'Copied',
    remove: 'Delete',
    choose: '— choose —',
    noPivot: 'No OLAP PivotTable is active.',
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
    truncated:
      'The level has more than 50,000 members: the caption lookup only saw the ' +
      'first 50,000. A caption found once may exist further down — prefer keys for ' +
      'this level.',
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
    target: 'Server: {server} — catalog: {catalog}',
    noConnection:
      'Place the cursor once inside an OLAP PivotTable: PivotScope reads the server ' +
      'and catalog there, then you can pick any destination cell.',
    activeCellHint:
      'The result is written from the cell that is active when the query starts. ' +
      'A cell inside a PivotTable is refused.',
    overwrite:
      'The range {address} already holds data. Overwriting it is final: Excel ' +
      'cannot undo (Ctrl+Z) a write made by an add-in.',
    overwriteConfirm: 'Overwrite',
    writeNewSheet: 'New sheet',
    discard: 'Discard',
    discarded: 'Result discarded, nothing was written.',
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
    numberFormatDefault: 'Default',
    numberFormatNumber: 'Number',
    numberFormatPercent: 'Percentage',
    numberFormatHint:
      'Excel exposes no interface for formatting a calculated member — only a ' +
      'macro can, and only with these three formats.',
    expression: 'MDX expression',
    solveOrder: 'Solve order',
    addToPivot: 'Add to the table once created',
    create: 'Create',
    replace: 'Replace',
    replaceConfirm: 'Replace "{name}"?',
    removeConfirm: 'Remove?',
    kindMeasureShort: 'measure',
    kindMemberShort: 'member',
    kindSetShort: 'set',
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
    restorePrevious: 'Back to the previous MDX',
    copyCode: 'Copy',
    copied: 'Copied',
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
