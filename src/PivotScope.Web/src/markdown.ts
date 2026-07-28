/**
 * Rendu Markdown minimal pour les réponses de l'IA.
 *
 * Volontairement écrit à la main plutôt que d'ajouter `marked` : on n'a besoin
 * que de six constructions, et **tout est échappé d'abord**. La sortie d'un
 * modèle est une entrée non fiable comme une autre — l'injecter en HTML brut
 * dans le volet serait une faute.
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

export function renderMarkdown(markdown: string): string {
  const out: string[] = []
  let inCode = false
  let inList = false

  const closeList = () => {
    if (inList) { out.push('</ul>'); inList = false }
  }

  for (const raw of (markdown ?? '').split('\n')) {
    const line = raw.replace(/\r$/, '')

    if (line.trimStart().startsWith('```')) {
      closeList()
      out.push(inCode ? '</code></pre>' : '<pre><code>')
      inCode = !inCode
      continue
    }

    if (inCode) { out.push(escapeHtml(line)); continue }

    const heading = /^(#{1,4})\s+(.*)$/.exec(line)
    if (heading) {
      closeList()
      const level = Math.min(heading[1].length + 2, 6)
      out.push(`<h${level}>${inline(heading[2])}</h${level}>`)
      continue
    }

    const bullet = /^\s*[-*]\s+(.*)$/.exec(line)
    if (bullet) {
      if (!inList) { out.push('<ul>'); inList = true }
      out.push(`<li>${inline(bullet[1])}</li>`)
      continue
    }

    if (line.trim() === '') { closeList(); continue }

    closeList()
    out.push(`<p>${inline(line)}</p>`)
  }

  closeList()
  if (inCode) out.push('</code></pre>')
  return out.join('\n')
}
