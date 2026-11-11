// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
// The backoffice's own lint rule against words written into its templates (ROADMAP 14.1): every word the backoffice
// shows comes from its dictionaries in English and German (ADR-0057), so a Lit template (html`...`) holds no letters
// of its own in the text between its tags or in an attribute a person reads or hears (a label, a title, a placeholder,
// a headline, an alternative text, a description), only what it puts in (${...}). Character references (&nbsp;), the
// keys inside <kbd> (Ctrl+Z, whose held key comes from the dictionaries), an address in a spoken value (https://), and
// what lies inside <style> and comments are no words. A rare word that is no text, such as a product's own name in a
// test page, is written through ${...} with a comment saying why.

/** The attributes whose values a person reads or hears. */
const SPOKEN = new Set([
  'label',
  'aria-label',
  'aria-description',
  'aria-roledescription',
  'aria-placeholder',
  'aria-valuetext',
  'title',
  'placeholder',
  'headline',
  'alt',
  'description',
]);

const LETTER = /\p{L}/u;
const ADDRESS = /^[a-z][a-z0-9+.-]*:\/\/\S*$/i;
const REFERENCE = /&(?:[a-z]+|#\d+|#x[0-9a-f]+);/gi;

/**
 * Whether a piece of a template, character references taken out, holds a letter.
 * @param {string} text
 */
const hasWords = (text) => LETTER.test(text.replace(REFERENCE, ''));

/**
 * Walks a template's static parts as one text, the expressions standing as a mark, and reports each place that holds
 * words: a run of text between tags, or a spoken attribute's own value.
 * @param {readonly import('estree').TemplateElement[]} quasis
 */
function wordsIn(quasis) {
  /** @type {{ at: number; what: string }[]} */
  const found = [];
  let state = 'text';
  let quote = '';
  let attribute = '';
  let value = '';
  let text = '';
  let tag = '';
  let inStyle = false;
  let inKeys = 0;
  /** @param {number} at */
  const flushText = (at) => {
    if (!inStyle && inKeys === 0 && hasWords(text)) found.push({ at, what: `text "${text.trim().slice(0, 40)}"` });
    text = '';
  };
  quasis.forEach((quasi, index) => {
    const raw = quasi.value.cooked ?? quasi.value.raw;
    for (let i = 0; i < raw.length; i++) {
      const char = raw[i];
      switch (state) {
        case 'text':
          if (raw.startsWith('<!--', i)) {
            flushText(index);
            state = 'comment';
            i += 3;
          } else if (char === '<') {
            flushText(index);
            state = 'tag';
            tag = '';
            attribute = '';
          } else text += char;
          break;
        case 'comment':
          if (raw.startsWith('-->', i)) {
            state = 'text';
            i += 2;
          }
          break;
        case 'tag':
          if (char === '>') {
            const name = tag.trim().split(/\s+/)[0]?.toLowerCase() ?? '';
            if (name === 'style') inStyle = true;
            if (name === '/style') inStyle = false;
            if (name === 'kbd') inKeys++;
            if (name === '/kbd') inKeys = Math.max(0, inKeys - 1);
            state = 'text';
          } else if (char === '"' || char === "'") {
            quote = char;
            // A property or boolean binding (.label, ?title) reads as its attribute.
            attribute = (/([^\s=]+)\s*=\s*$/.exec(tag)?.[1] ?? '').replace(/^[.?]/, '').toLowerCase();
            value = '';
            state = 'value';
          } else tag += char;
          break;
        case 'value':
          if (char === quote) {
            if (SPOKEN.has(attribute) && hasWords(value) && !ADDRESS.test(value.trim()))
              found.push({ at: index, what: `${attribute}="${value.slice(0, 40)}"` });
            tag += `${quote}${quote}`;
            state = 'tag';
          } else value += char;
          break;
      }
    }
    // The expression after this part stands in the text or the value it is in: no words of the template's own.
    if (index < quasis.length - 1) {
      if (state === 'text') text += ' ';
      else if (state === 'tag') tag += ' x';
    }
  });
  flushText(quasis.length - 1);
  return found;
}

/** @type {import('eslint').Rule.RuleModule} */
export const noHardCodedText = {
  meta: {
    type: 'problem',
    docs: { description: 'Words the backoffice shows come from its dictionaries, not from its templates.' },
    messages: {
      words:
        'A word written into the template: {{what}}. Take it from the dictionaries (this.localize.term), in English and German (ADR-0057).',
    },
    schema: [],
  },
  create(context) {
    return {
      TaggedTemplateExpression(node) {
        const tag = node.tag;
        const isHtml =
          (tag.type === 'Identifier' && tag.name === 'html') ||
          (tag.type === 'MemberExpression' && tag.property.type === 'Identifier' && tag.property.name === 'html');
        if (!isHtml) return;
        for (const { at, what } of wordsIn(node.quasi.quasis))
          context.report({ node: node.quasi.quasis[at] ?? node, messageId: 'words', data: { what } });
      },
    };
  },
};

/** The plugin that holds it, as the flat config names it: `jfb/no-hard-coded-text`. */
export default { rules: { 'no-hard-coded-text': noHardCodedText } };
