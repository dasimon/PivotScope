/** Mirror of the records exposed by PivotScope.Core (camelCase serialization). */

export type PivotFieldInfo = {
  caption: string
  uniqueName: string
  area: 'row' | 'column' | 'filter' | 'data'
}

export type PivotContext = {
  hasPivot: boolean
  isOlap: boolean
  server: string | null
  catalog: string | null
  cube: string | null
  mdx: string | null
  fields: PivotFieldInfo[]
  diagnostic: string | null
  /** Workbook + sheet + name: tells one PivotTable from another. */
  pivotKey: string | null
  /** Stable code of `diagnostic`, translated by the pane. */
  diagnosticCode: 'noPivot' | 'notOlap' | 'powerPivot' | 'connectionUnreadable' | null
}

export type LevelMeta = { name: string; uniqueName: string; number: number }
export type HierarchyMeta = {
  name: string
  uniqueName: string
  levels: LevelMeta[]
  description: string
}
export type DimensionMeta = {
  name: string
  uniqueName: string
  hierarchies: HierarchyMeta[]
  description: string
}
export type MeasureMeta = {
  name: string
  uniqueName: string
  description: string
}
export type MeasureFolder = { folder: string; measures: MeasureMeta[] }

export type MemberMeta = { caption: string; uniqueName: string }

export type CubeMeta = {
  cubeName: string
  measureFolders: MeasureFolder[]
  dimensions: DimensionMeta[]
}

export type AiAction = 'Expliquer' | 'Optimiser' | 'AntiPatterns' | 'Formater'

export type AiRunResult = { cancelled: boolean; markdown: string }

export type DependencyNode = {
  name: string
  kind: string
  dependencies: DependencyNode[]
}

export type DependencyGraph = {
  root: DependencyNode
  usedBy: string[]
}

export type CellProvenance = {
  tuple: string
  measure: string | null
  coordinates: string[]
  expression: string | null
  startLine: number | null
  dependencies: DependencyGraph | null
  /** Response that is not an error: physical measure, unreadable script… */
  note: string | null
}

export type CalculationKind = 'Measure' | 'Member' | 'Set'

export type ExistingCalculation = {
  name: string
  formula: string
  kind: string
  isValid: boolean
  displayFolder: string | null
}

export type CalculationDefinition = {
  name: string
  expression: string
  kind: CalculationKind
  displayFolder: string | null
  numberFormat: string | null
  parentHierarchy: string | null
  solveOrder: number
}

export type StoredCalculation = {
  id: number
  definition: CalculationDefinition
  cube: string | null
  savedUtc: string
}

export type LevelVisibility = {
  name: string
  caption: string
  shown: boolean
}

export type FieldVisibility = {
  name: string
  caption: string
  shownInFieldList: boolean
  area: string
}

export type QueryRunResult = {
  /** True if the user stopped the query: this is not an error. */
  cancelled: boolean
  /** The destination holds data: nothing written yet, the user decides. */
  pendingOverwrite: boolean
  address: string
  rows: number
  columns: number
  durationMs: number
  server: string
  catalog: string
}

export type WriteMode = 'overwrite' | 'newSheet' | 'discard'

export type ConfirmWriteResult = { written: boolean; address: string }

export type FilterListResult = {
  applied: number
  unresolved: string[]
  /** Captions borne by several members: deliberately left unresolved. */
  ambiguous: string[]
  /** The caption lookup only saw the first members of the level. */
  truncated: boolean
}
