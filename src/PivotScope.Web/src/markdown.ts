/**
 * Minimal Markdown rendering for the AI's answers.
 *
 * Deliberately hand-written rather than adding `marked`: we only need a
 * handful of constructs, and **everything is escaped first**. A model's output
 * is untrusted input like any other — injecting it as raw HTML
 * into the pane would be a mistake.
 */

function escapeHtml(text: string): string {
  return text
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
}

function inline(text: string): string {
  return escapeHtml(text)
    .replace(/`([^`]+)`/g, '<code>$1</code>')
    .replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>')
    .replace(/(^|[^*])\*([^*]+)\*/g, '$1<em>$2</em>')
}

/** Cells of a Markdown table row: "| a | b |" → ["a", "b"]. */
function cells(line: string): string[] {
  return line.trim().replace(/^\|/, '').replace(/\|$/, '').split('|').map(c => c.trim())
}

const isTableRow = (line: string) => /^\s*\|.*\|\s*$/.test(line)
const isTableSeparator = (line: string) => /^\s*\|?\s*:?-{3,}:?\s*(\|\s*:?-{3,}:?\s*)*\|?\s*$/.test(line)

/**
 * The raw text of each fenced code block, in order. The rendered HTML only
 * carries an index: copying goes back to this text, never to the escaped HTML.
 */
export function codeBlocks(markdown: string): string[] {
  const blocks: string[] = []
  let current: string[] | null = null
  for (const raw of (markdown ?? '').split('\n')) {
    const line = raw.replace(/\r$/, '')
    if (line.trimStart().startsWith('```')) {
      if (current) { blocks.push(current.join('\n')); current = null }
      else current = []
      continue
    }
    current?.push(line)
  }
  if (current) blocks.push(current.join('\n'))
  return blocks
}

/**
 * @param copyLabel label of the copy button placed on each code block; the
 *   button carries `data-code="<index>"`, matching {@link codeBlocks}.
 */
export function renderMarkdown(markdown: string, copyLabel = 'Copier'): string {
  const out: string[] = []
  const lines = (markdown ?? '').split('\n').map(l => l.replace(/\r$/, ''))
  let inCode = false
  let codeIndex = 0
  let list: 'ul' | 'ol' | null = null

  const closeList = () => {
    if (list) { out.push(`</${list}>`); list = null }
  }
  const openList = (kind: 'ul' | 'ol') => {
    if (list === kind) return
    closeList()
    out.push(`<${kind}>`)
    list = kind
  }

  for (let i = 0; i < lines.length; i++) {
    const line = lines[i]

    if (line.trimStart().startsWith('```')) {
      closeList()
      if (inCode) {
        out.push('</code></pre></div>')
      } else {
        out.push(
          `<div class="code"><button type="button" class="secondary copy" data-code="${codeIndex++}">` +
          `${escapeHtml(copyLabel)}</button><pre><code>`,
        )
      }
      inCode = !inCode
      continue
    }

    if (inCode) { out.push(escapeHtml(line)); continue }

    // A table: a row of cells followed by a separator line.
    if (isTableRow(line) && i + 1 < lines.length && isTableSeparator(lines[i + 1])) {
      closeList()
      out.push('<div class="table"><table><thead><tr>')
      for (const cell of cells(line)) out.push(`<th>${inline(cell)}</th>`)
      out.push('</tr></thead><tbody>')
      i += 2
      while (i < lines.length && isTableRow(lines[i])) {
        out.push('<tr>')
        for (const cell of cells(lines[i])) out.push(`<td>${inline(cell)}</td>`)
        out.push('</tr>')
        i++
      }
      i--
      out.push('</tbody></table></div>')
      continue
    }

    const heading = /^(#{1,4})\s+(.*)$/.exec(line)
    if (heading) {
      closeList()
      const level = Math.min(heading[1].length + 2, 6)
      out.push(`<h${level}>${inline(heading[2])}</h${level}>`)
      continue
    }

    const bullet = /^\s*[-*]\s+(.*)$/.exec(line)
    if (bullet) {
      openList('ul')
      out.push(`<li>${inline(bullet[1])}</li>`)
      continue
    }

    const numbered = /^\s*\d+[.)]\s+(.*)$/.exec(line)
    if (numbered) {
      openList('ol')
      out.push(`<li>${inline(numbered[1])}</li>`)
      continue
    }

    if (line.trim() === '') { closeList(); continue }

    closeList()
    out.push(`<p>${inline(line)}</p>`)
  }

  closeList()
  if (inCode) out.push('</code></pre></div>')
  return out.join('\n')
}
