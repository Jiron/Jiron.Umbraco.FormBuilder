// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
// Less of what the backoffice's Lit templates carry for people reading the source (ADR-0216): the bundler's minifier
// leaves a template literal's text as written, so a `css` template keeps its comments and indentation and an `html`
// template the line breaks and indentation its formatting gives it. TypeScript's parser finds each template tagged `css`
// or `html` exactly, and only the whitespace and comments that change nothing are taken out.
import ts from 'typescript';

/** The tags whose templates are made smaller. */
const TAGS = new Set(['css', 'html']);

// A run of whitespace, unless a backslash before it makes a line continuation.
const SPACES = /(?<!\\)\s+/g;
// A run of whitespace that holds a line break, unless a backslash before it makes a line continuation.
const BREAKS = /(?<!\\)[ \t]*(?:\r?\n)[\s]*/g;
// Whitespace around the characters of CSS whose neighbours never need it.
const AROUND = /\s*([{};])\s*/g;
// What makes an html template's whitespace count, so it is left as written.
const KEEPS = /<pre\b|<textarea\b|white-space/i;

/**
 * A CSS text without its comments, each run of whitespace one space, and none around braces and semicolons. Spaces
 * elsewhere stay: they part a selector's compounds (`:host ::slotted`) and a calculation's terms (`calc(1px + 2px)`).
 * @param {string} text A part of a template's raw text.
 * @param {{ first: boolean, last: boolean }} edges Whether the part starts or ends the template.
 */
export function lessCss(text, { first, last }) {
  let out = text
    .replace(/\/\*[\s\S]*?\*\//g, '')
    .replace(SPACES, ' ')
    .replace(AROUND, '$1');
  if (first) out = out.trimStart();
  if (last) out = out.trimEnd();
  return out;
}

/**
 * An HTML text with each run of whitespace that holds a line break one space, as the browser reads it outside
 * preformatted text; spaces on one line stay as they are.
 * @param {string} text A part of a template's raw text.
 */
export function lessHtml(text) {
  return text.replace(BREAKS, ' ');
}

/**
 * The parts of a tagged template's text, each with where it starts and ends in the source, its delimiters excluded.
 * @param {ts.TemplateLiteral} template
 * @param {ts.SourceFile} file
 * @returns {{ start: number, end: number }[]}
 */
function partsOf(template, file) {
  if (ts.isNoSubstitutionTemplateLiteral(template))
    return [{ start: template.getStart(file) + 1, end: template.end - 1 }];
  const parts = [{ start: template.head.getStart(file) + 1, end: template.head.end - 2 }];
  for (const span of template.templateSpans) {
    const literal = span.literal;
    parts.push({ start: literal.getStart(file) + 1, end: literal.end - (ts.isTemplateTail(literal) ? 1 : 2) });
  }
  return parts;
}

/**
 * The module's source with the text of its `css` and `html` templates made smaller; the same text when it has none.
 * @param {string} code A TypeScript or JavaScript module.
 * @param {string} fileName Its name, which tells TypeScript from JavaScript.
 */
export function lessTemplates(code, fileName) {
  if (!code.includes('css`') && !code.includes('html`')) return code;
  const kind = /\.[cm]?tsx?$/.test(fileName) ? ts.ScriptKind.TS : ts.ScriptKind.JS;
  const file = ts.createSourceFile(fileName, code, ts.ScriptTarget.Latest, true, kind);
  /** @type {{ start: number, end: number, text: string }[]} */
  const edits = [];
  const visit = (/** @type {ts.Node} */ node) => {
    if (ts.isTaggedTemplateExpression(node) && ts.isIdentifier(node.tag) && TAGS.has(node.tag.text)) {
      const parts = partsOf(node.template, file);
      const whole = parts.map((part) => code.slice(part.start, part.end)).join('');
      const css = node.tag.text === 'css';
      if (css || !KEEPS.test(whole)) {
        parts.forEach((part, index) => {
          const text = code.slice(part.start, part.end);
          const less = css ? lessCss(text, { first: index === 0, last: index === parts.length - 1 }) : lessHtml(text);
          if (less !== text) edits.push({ ...part, text: less });
        });
      }
    }
    ts.forEachChild(node, visit);
  };
  visit(file);
  let out = code;
  for (const edit of edits.sort((a, b) => b.start - a.start)) {
    out = out.slice(0, edit.start) + edit.text + out.slice(edit.end);
  }
  return out;
}

/**
 * The plugin that makes the package's own templates smaller before the bundler compiles them. A debugging build with
 * source maps (`JFB_SOURCEMAP=1`) goes without it, as its changes have no map.
 * @param {Record<string, string | undefined>} [env]
 * @returns {import('vite').Plugin}
 */
export function lessTemplateText(env = process.env) {
  return {
    name: 'jfb-less-template-text',
    enforce: 'pre',
    apply: (_config, { command }) => command === 'build' && env.JFB_SOURCEMAP !== '1',
    transform(code, id) {
      const file = id.split('?')[0] ?? id;
      if (file.includes('node_modules') || !/\.[cm]?[jt]s$/.test(file)) return null;
      const out = lessTemplates(code, file);
      return out === code ? null : { code: out, map: null };
    },
  };
}
