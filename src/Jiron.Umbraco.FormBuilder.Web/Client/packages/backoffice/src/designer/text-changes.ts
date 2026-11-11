// A change of the draft's texts by their paths (ADR-0018), which a designer action or a field text action asks for:
// the paths Jiron Form Builder's texts walker gives (`$.pages[0].groups[0].fields[1].help`), each to one of the
// draft's texts in one language. Pure: the next definition, or null when a change is not one of a text, and nothing
// changes then. A language other than the default needs the text's default, as a definition does.
import type { DesignDefinition } from './definition';

/** One text to change, as the public declarations name it. */
export interface TextChange {
  readonly path: string;
  readonly culture: string;
  readonly text: string;
}

/** The names a text has where the walker finds one: what a new default text may be written as. */
export const TEXT_NAMES: ReadonlySet<string> = new Set([
  'label',
  'help',
  'placeholder',
  'title',
  'description',
  'message',
  'submitLabel',
  'nextLabel',
  'backLabel',
  'closedMessage',
  'text',
  'html',
  'statement',
  'lowLabel',
  'highLabel',
]);

// A culture name (`de`, `de-CH`, `sr-Latn-RS`), or '' for the default text.
const CULTURE = /^(?:[A-Za-z]{2,3}(?:-[A-Za-z0-9]{2,8})*)?$/;

// `$` then `.name` and `[n]` steps; anything else is no path.
const STEP = /\.([A-Za-z_][A-Za-z0-9_]*)|\[(\d{1,4})\]/y;

/** The steps of a path, or null for one that is not of the walker's shape. */
export function stepsOf(path: string): (string | number)[] | null {
  if (!path.startsWith('$')) return null;
  const steps: (string | number)[] = [];
  STEP.lastIndex = 1;
  while (STEP.lastIndex < path.length) {
    const at = STEP.lastIndex;
    const match = STEP.exec(path);
    if (!match || match.index !== at) return null;
    steps.push(match[1] ?? Number(match[2]));
  }
  return steps.length > 0 ? steps : null;
}

const isPlain = (value: unknown): value is Record<string, unknown> =>
  typeof value === 'object' && value !== null && !Array.isArray(value);

// A localised text: an object whose every value is a string.
const isText = (value: unknown): value is Record<string, string> =>
  isPlain(value) && Object.values(value).every((text) => typeof text === 'string');

/** The definition with the texts changed, or null when one change cannot be made. */
export function withTexts(definition: DesignDefinition, changes: readonly TextChange[]): DesignDefinition | null {
  if (changes.length === 0) return null;
  const next = structuredClone(definition) as unknown as Record<string, unknown>;
  for (const change of changes) {
    if (!CULTURE.test(change.culture) || typeof change.text !== 'string' || change.text.trim() === '') return null;
    const steps = stepsOf(change.path);
    const name = steps?.at(-1);
    if (!steps || typeof name !== 'string') return null;
    let parent: unknown = next;
    for (const step of steps.slice(0, -1)) {
      parent =
        typeof step === 'number'
          ? Array.isArray(parent)
            ? parent[step]
            : undefined
          : isPlain(parent)
            ? parent[step]
            : undefined;
      if (parent === undefined || parent === null) return null;
    }
    if (!isPlain(parent)) return null;
    const current = parent[name];
    if (current === undefined || current === null) {
      // A new text: only a default one, under a name a text has.
      if (change.culture !== '' || !TEXT_NAMES.has(name)) return null;
      parent[name] = { '': change.text };
      continue;
    }
    if (!isText(current)) return null;
    // A translation needs the default text beside it; the culture is written as the site names it, once.
    if (change.culture !== '' && typeof current[''] !== 'string') return null;
    const others = Object.entries(current).filter(([key]) => key.toLowerCase() !== change.culture.toLowerCase());
    const written: Record<string, string> = Object.fromEntries(others);
    written[change.culture] = change.text;
    parent[name] = written;
  }
  return next as unknown as DesignDefinition;
}
