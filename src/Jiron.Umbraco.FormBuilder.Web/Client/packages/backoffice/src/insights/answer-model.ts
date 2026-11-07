// What the Answers of a form's Insights view work out before they draw (ROADMAP 11.5b, IA 5.5, ADR-0212): which fields
// get a box and which one line names as unanswered, each line's share of the answers, a ranking's bars, whether several
// choices add up past the answers, a grid's columns across its rows, and the free-text answers with the next ones added.
// Pure.
import type {
  InsightAnswerGridRowResponseModel,
  InsightAnswerOptionResponseModel,
  InsightAnswerResponseModel,
  InsightAnswerTextResponseModel,
  InsightAnswerTextsResponseModel,
} from '../api';

/** The fields with answers, which get a box each, and those nobody answered, which share one line. */
export function splitAnswered(fields: readonly InsightAnswerResponseModel[]): {
  answered: InsightAnswerResponseModel[];
  unanswered: InsightAnswerResponseModel[];
} {
  return {
    answered: fields.filter((field) => field.answered > 0),
    unanswered: fields.filter((field) => field.answered <= 0),
  };
}

/** A count's share of a whole, from 0 to 1; none of nothing. */
export const shareOf = (count: number, whole: number): number => (whole > 0 ? Math.max(0, count) / whole : 0);

/** Whether a field's options add up past its answers, as they can where an answer chooses several. */
export const addsUp = (field: Pick<InsightAnswerResponseModel, 'several' | 'options' | 'answered'>): boolean =>
  field.several && field.options.reduce((sum, option) => sum + option.count, 0) > field.answered;

/**
 * A ranking's bars: how near the top each option stands on average, the first place a whole bar and the last the
 * shortest; an option without a position has none.
 */
export function rankBars(options: readonly InsightAnswerOptionResponseModel[]): {
  values: number[];
  whole: number;
} {
  const places = options.filter((option) => option.position !== null && option.position !== undefined).length;
  return {
    values: options.map((option) =>
      option.position === null || option.position === undefined ? 0 : Math.max(0, places + 1 - option.position),
    ),
    whole: places,
  };
}

/** One line of an option or a grid's column, by what it shows: the visitors' own texts, an older option, or its label. */
export interface LineName {
  readonly label: string;
  readonly older: boolean;
  readonly other: boolean;
}

/** The key a line is found by across a grid's rows. */
export const lineKey = (line: LineName): string =>
  line.other ? 'other' : `${line.older ? 'older' : 'live'}:${line.label}`;

/** A grid's columns across its rows, in the order they first come: the live ones, then older ones a row still has. */
export function gridColumns(rows: readonly InsightAnswerGridRowResponseModel[]): LineName[] {
  const columns = new Map<string, LineName>();
  for (const row of rows)
    for (const option of row.options) {
      const key = lineKey(option);
      if (!columns.has(key)) columns.set(key, { label: option.label, older: option.older, other: option.other });
    }
  return [...columns.values()];
}

/** The free-text answers of a field as shown, and where the next ones start. */
export interface TextsShown {
  readonly texts: readonly InsightAnswerTextResponseModel[];
  readonly more: string | null;
}

/** The answers shown with the next page added after them. */
export const withPage = (shown: TextsShown, page: InsightAnswerTextsResponseModel): TextsShown => ({
  texts: [...shown.texts, ...page.texts],
  more: page.more ?? null,
});
