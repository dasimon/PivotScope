/** Miroir des records exposés par PivotScope.Core (sérialisation camelCase). */

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
  /** Réponse qui n'est pas une erreur : mesure physique, script illisible… */
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
  /** Vrai si l'utilisateur a arrêté la requête : ce n'est pas une erreur. */
  cancelled: boolean
  address: string
  rows: number
  columns: number
  durationMs: number
}

export type FilterListResult = {
  applied: number
  unresolved: string[]
  /** Libellés portés par plusieurs membres : non résolus volontairement. */
  ambiguous: string[]
}
