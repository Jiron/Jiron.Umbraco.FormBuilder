/**
 * Answer recall on the page (ROADMAP 6.4g, ADR-0115), a chunk of its own with the merge tags: the visitor texts that
 * recall answers, rendered as the server renders them for a page's answers (ADR-0068, ADR-0095). Each text of the form
 * endpoint's `mergeTags` is parsed once, from the form's property its name says, in its context and with its owner's
 * path check, and only one with a `field.*` tag is rendered again: over the page's reading (each field's values as its
 * type read them, a row's child the rules hide in that row left out), the elements the rules hide, the culture's words
 * and formats, and the endpoint's values for its other tags.
 */
import { own, type FormState, type RecallAnswer, type RecallAnswers } from '@jfb/logic';
import { recallAnswers } from '@jfb/logic/recall';
import { parsePageTemplate, renderPageTemplate, type TemplatePart } from '@jfb/logic/template';
import type { TextRenderer } from './engine';
import type { FormModel, ModelField, ModelText } from './model';
import type { FieldRead, PageReading } from './page';

export { fillDefaults } from './defaults';

/** The renderer of a form's visitor texts that recall answers; it renders none for a form without merge tags. */
export function textsOf(model: FormModel): TextRenderer {
  const tags = model.mergeTags;
  if (tags == null) return () => new Map();
  const recalled = (owner: string, answers: RecallAnswers, hidden: readonly string[]) =>
    recallAnswers(tags.recall, owner, answers, hidden, model.words, model.formats);
  const texts = tags.texts.flatMap((text) => {
    const template = text.text === 'defaultValue' ? null : templateOf(model, text);
    if (template === null) return [];
    // Which fields a text may name depends on where it stands, never on the answers; a source its place refuses is
    // written as it stands, as the server writes it (ADR-0118).
    const refused = text.refused ?? [];
    const problem = recalled(text.owner, {}, []).pathProblem;
    const parsed = parsePageTemplate(template, text.context, (path) =>
      refused.includes(path) ? 'source_not_for_place' : problem(path),
    );
    return parsed.parts.some(isFieldTag) ? [{ text, parsed }] : [];
  });
  return (reading, state) => {
    const answers = answersOf(reading, state);
    return new Map(
      texts.map(({ text, parsed }) => {
        const found = recalled(text.owner, answers, state.hidden).values;
        const given = own(tags.values, text.context) ?? {};
        const values = (path: string) => found(path) ?? own(given, path);
        return [`${text.owner} ${text.text}`, renderPageTemplate(parsed, values, model.formats).text];
      }),
    );
  };
}

const isFieldTag = (part: TemplatePart): boolean => part.kind === 'tag' && part.path?.startsWith('field.') === true;

/** A text of the endpoint's form: the property its name says, such as `settings.text`, of its owner. */
function templateOf(model: FormModel, { owner, text }: ModelText): string | null {
  const fields = (list: readonly ModelField[]): { readonly key: string }[] =>
    list.flatMap((field) => [field, ...fields(field.fields ?? [])]);
  let value: unknown = model.form.pages
    .flatMap((page) => [page, ...page.groups.flatMap((group) => [group, ...fields(group.fields)])])
    .find((each) => each.key === owner);
  for (const name of text.split('.'))
    value = typeof value === 'object' && value !== null ? own(value as Record<string, unknown>, name) : undefined;
  return typeof value === 'string' ? value : null;
}

/**
 * The page's answers as recall reads them, as the server's render gives them: each field's values as its type read
 * them (a site's own type's cleaned texts), and each repeatable group's rows while their number keeps its limits, a
 * child the rules hide in its row left out of that row.
 */
function answersOf(reading: PageReading, state: FormState): RecallAnswers {
  const answerOf = (read: FieldRead): RecallAnswer => ({
    values: read.reading?.values ?? read.values.map((value) => ({ text: value })),
  });
  return {
    fields: Object.fromEntries([...reading.fields].map(([key, read]) => [key, answerOf(read)])),
    rows: Object.fromEntries(
      [...reading.groups].map(([key, group]) => [
        key,
        group.rows.map((row, at) => {
          const hidden = own(state.rows, key)?.[at]?.hidden ?? [];
          return Object.fromEntries(
            [...row].filter(([child]) => !hidden.includes(child)).map(([child, read]) => [child, answerOf(read)]),
          );
        }),
      ]),
    ),
  };
}
