// Autocomplétion MDX, sensible au contexte de frappe :
//   « [Measures].            » → mesures seules
//   « [Dim].[Hier].          » → membres de la hiérarchie (chargés en lazy)
//   « [Dim].                 » → hiérarchies de cette dimension
//   ailleurs                  → mots-clés, fonctions, dimensions et mesures
//
// Version courte de celle de CubeScope : branchée sur le pont postMessage,
// sans store global ni API HTTP.
import { monaco } from './monaco-mdx'
import { mdxFunctions } from './mdxFunctions'
import { call } from './bridge'
import type { CubeMeta, MemberMeta } from './types'

const KEYWORDS = [
  'SELECT', 'FROM', 'WHERE', 'ON COLUMNS', 'ON ROWS', 'NON EMPTY',
  'WITH MEMBER', 'WITH SET', 'MEMBER', 'SET', 'AS', 'DIMENSION PROPERTIES',
  'CELL PROPERTIES', 'CURRENTMEMBER', 'CHILDREN', 'MEMBERS', 'PARENT',
]

const MEASURES_PREFIX = '[Measures]'

let meta: CubeMeta | null = null
const memberCache = new Map<string, MemberMeta[]>()
let disposable: monaco.IDisposable | null = null

/** La complétion suit le cube du TCD actif. */
export function setCubeMeta(next: CubeMeta | null): void {
  meta = next
  memberCache.clear()
}

async function membersOf(hierarchy: string): Promise<MemberMeta[]> {
  const cached = memberCache.get(hierarchy)
  if (cached) return cached
  try {
    const fetched = await call<MemberMeta[]>('cube.members', { hierarchy })
    memberCache.set(hierarchy, fetched)
    return fetched
  } catch {
    // Une complétion qui échoue ne doit jamais interrompre la frappe.
    return []
  }
}

/**
 * Ce que l'utilisateur est en train de taper depuis le dernier séparateur.
 * Sert à calculer la plage à remplacer : sans ça, insérer après « [Measures]. »
 * dupliquerait le préfixe déjà saisi.
 */
function typedFragment(before: string): string {
  return /[^\s(){},.]*$/.exec(before)?.[0] ?? ''
}

function item(
  label: string,
  insert: string,
  kind: monaco.languages.CompletionItemKind,
  range: monaco.IRange | ((insert: string) => monaco.IRange),
  detail?: string,
  documentation?: string,
  sortText?: string,
): monaco.languages.CompletionItem {
  return {
    label,
    insertText: insert,
    kind,
    range: typeof range === 'function' ? range(insert) : range,
    detail,
    documentation,
    sortText,
  }
}

export function registerMdxCompletion(): void {
  disposable?.dispose()

  disposable = monaco.languages.registerCompletionItemProvider('mdx', {
    triggerCharacters: ['[', '.', '&'],

    async provideCompletionItems(model, position) {
      const before = model.getValueInRange({
        startLineNumber: position.lineNumber,
        startColumn: 1,
        endLineNumber: position.lineNumber,
        endColumn: position.column,
      })

      const fragment = typedFragment(before)

      // Monaco auto-ferme les crochets : taper « [ » écrit « [] » et laisse le
      // curseur au milieu. Si notre insertion apporte déjà son « ] », il faut
      // que la plage remplacée avale celui qui traîne, sinon on obtient « ]] ».
      const after = model.getLineContent(position.lineNumber).slice(position.column - 1)
      const danglingClose = after.startsWith(']') ? 1 : 0

      const rangeFor = (insert: string): monaco.IRange => ({
        startLineNumber: position.lineNumber,
        endLineNumber: position.lineNumber,
        startColumn: position.column - fragment.length,
        endColumn: position.column + (insert.endsWith(']') ? danglingClose : 0),
      })


      const K = monaco.languages.CompletionItemKind
      const context = before.slice(0, before.length - fragment.length)

      // 1. « [Measures]. » → les mesures, et rien d'autre.
      if (/\[Measures\]\.$/i.test(context)) {
        const suggestions: monaco.languages.CompletionItem[] = []
        for (const folder of meta?.measureFolders ?? [])
          for (const measure of folder.measures)
            suggestions.push(item(
              measure.name,
              // Le préfixe [Measures]. est déjà tapé : n'insérer que le nom.
              measure.uniqueName.startsWith(MEASURES_PREFIX + '.')
                ? measure.uniqueName.slice(MEASURES_PREFIX.length + 1)
                : `[${measure.name}]`,
              K.Field, rangeFor, folder.folder || 'mesure', measure.description))
        return { suggestions }
      }

      // 2. « [Dim].[Hier]. » → membres de la hiérarchie.
      const afterHierarchy = /(\[[^\]]+\]\.\[[^\]]+\])\.$/.exec(context)
      if (afterHierarchy) {
        const hierarchy = afterHierarchy[1]
        const members = await membersOf(hierarchy)
        return {
          suggestions: members.slice(0, 1000).map(m => item(
            m.caption,
            m.uniqueName.startsWith(hierarchy + '.')
              ? m.uniqueName.slice(hierarchy.length + 1)
              : m.uniqueName,
            K.Value, rangeFor, 'membre', m.uniqueName)),
        }
      }

      // 3. « [Dim]. » → hiérarchies de cette dimension.
      const afterDimension = /(\[[^\]]+\])\.$/.exec(context)
      if (afterDimension) {
        const dimensionName = afterDimension[1].slice(1, -1)
        const dimension = meta?.dimensions.find(
          d => d.name.toLowerCase() === dimensionName.toLowerCase())
        if (dimension) {
          return {
            suggestions: dimension.hierarchies.map(h => item(
              h.name, `[${h.name}]`, K.Class, rangeFor, dimension.name, h.description)),
          }
        }
      }

      // 4. Contexte libre : on trie pour que le métier passe avant la syntaxe.
      const suggestions: monaco.languages.CompletionItem[] = []

      for (const folder of meta?.measureFolders ?? [])
        for (const measure of folder.measures)
          suggestions.push(item(measure.uniqueName, measure.uniqueName, K.Field, rangeFor,
                                folder.folder || 'mesure', measure.description, '1'))

      for (const dimension of meta?.dimensions ?? [])
        for (const hierarchy of dimension.hierarchies) {
          suggestions.push(item(hierarchy.uniqueName, hierarchy.uniqueName, K.Class, rangeFor,
                                dimension.name, hierarchy.description, '2'))
          for (const level of hierarchy.levels)
            suggestions.push(item(level.uniqueName, level.uniqueName, K.Property, rangeFor,
                                  `niveau ${level.number}`, undefined, '3'))
        }

      for (const [name, doc] of Object.entries(mdxFunctions))
        suggestions.push(item(name, name, K.Function, rangeFor, doc.signature, doc.doc, '4'))

      for (const keyword of KEYWORDS)
        suggestions.push(item(keyword, keyword, K.Keyword, rangeFor, undefined, undefined, '5'))

      return { suggestions }
    },
  })
}

export function disposeMdxCompletion(): void {
  disposable?.dispose()
  disposable = null
}
