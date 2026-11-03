// A draft's rich text as the designer shows it (ADR-0168). The server cleans an editor's markup when it publishes the
// form; a draft holds what the editor gave, so the designer never parses it into the page as it is. It copies the text
// and the plain structure of a short allowlist of elements, without a single attribute, and drops scripts, styles and
// embedded content with what they hold. What it gives back is markup it wrote itself: escaped text in known tags.

/** The elements kept, each written as its own bare tag. */
const KEPT = new Set([
  'p',
  'br',
  'strong',
  'b',
  'em',
  'i',
  'u',
  's',
  'sub',
  'sup',
  'ul',
  'ol',
  'li',
  'a',
  'h2',
  'h3',
  'h4',
  'blockquote',
]);

/** The elements dropped with everything in them. */
const DROPPED = new Set([
  'script',
  'style',
  'template',
  'iframe',
  'object',
  'embed',
  'noscript',
  'svg',
  'math',
  'img',
  'video',
  'audio',
  'picture',
  'canvas',
  'form',
  'input',
  'button',
  'select',
  'textarea',
  'head',
  'title',
  'link',
  'meta',
]);

/** A text as markup: the five characters that mean something there escaped. */
export function escapeHtml(text: string): string {
  return text
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;');
}

/**
 * A rich text rewritten from its text and its kept elements: any other element gives its content, without its own tag,
 * except those that are dropped whole. Parsing happens in a document of its own, which runs nothing and loads nothing.
 */
export function safeHtml(html: string | undefined | null): string {
  if (!html) return '';
  const parsed = new DOMParser().parseFromString(html, 'text/html');
  return written(parsed.body);
}

/** Whether a rich text shows anything: some text that is not white space, or a line break. */
export function showsText(html: string): boolean {
  return (
    /<br>/.test(html) ||
    html
      .replace(/<[^>]*>/g, '')
      .replace(/&nbsp;|&#160;/g, ' ')
      .trim().length > 0
  );
}

function written(node: Node): string {
  let out = '';
  for (const child of node.childNodes) {
    if (child.nodeType === Node.TEXT_NODE) {
      out += escapeHtml(child.textContent ?? '');
      continue;
    }
    if (child.nodeType !== Node.ELEMENT_NODE) continue;
    const tag = (child as Element).localName;
    if (DROPPED.has(tag)) continue;
    if (!KEPT.has(tag)) {
      out += written(child);
      continue;
    }
    out += tag === 'br' ? '<br>' : `<${tag}>${written(child)}</${tag}>`;
  }
  return out;
}

/** Markup `safeHtml` wrote, as nodes to put in a template: it holds known tags and escaped text only. */
export function fragmentOf(safe: string): DocumentFragment {
  const template = document.createElement('template');
  template.innerHTML = safe;
  return template.content;
}
