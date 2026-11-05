// A problem's line under its box, as the Settings view and an outcome's sidebar show one (ADR-0184, ADR-0185): its
// sign, its weight in words for a screen reader where it is no error (the box's own state says an error), then its
// words; the ids that describe a box; and the problems counted where they hide, coloured by the heaviest of them.
import { html, nothing, type TemplateResult } from '@umbraco-cms/backoffice/external/lit';
import type { CheckSeverity } from '../check/form-check';

/** The sign of each weight of problem, as the form check's sidebar draws it. */
export const SIGNS: Readonly<Record<CheckSeverity, string>> = {
  error: 'icon-alert',
  warning: 'icon-info',
  hint: 'icon-lightbulb-active',
};

/** A problem shown under a box: one typed that can't be kept, or one the form check found there. */
export interface Shown {
  readonly text: string;
  readonly severity: CheckSeverity;
}

/** A problem in words, with its sign; `weight` says a warning's or a suggestion's weight in words. */
export function whyLine(id: string, problem: Shown, weight: (severity: CheckSeverity) => string): TemplateResult {
  return html`<p class="why" id=${id} data-severity=${problem.severity}>
    <uui-icon name=${SIGNS[problem.severity]} aria-hidden="true"></uui-icon>
    <span
      >${
        problem.severity === 'error' ? nothing : html`<span class="visually-hidden">${weight(problem.severity)} </span>`
      }${problem.text}</span
    >
  </p>`;
}

/** The lines of a box's problems, `<id>-why-<n>` each. */
export const whys = (id: string, problems: readonly Shown[], weight: (severity: CheckSeverity) => string) =>
  problems.map((problem, index) => whyLine(`${id}-why-${index}`, problem, weight));

/** Problems counted where they hide, on a closed group's line or an outcome's row; whether an error is among them. */
export interface Flag {
  readonly count: number;
  readonly error: boolean;
}

export const NO_FLAG: Flag = { count: 0, error: false };

/** The flag with one more problem of this weight. */
export const flagWith = (flag: Flag | undefined, severity: CheckSeverity): Flag => ({
  count: (flag?.count ?? 0) + 1,
  error: (flag?.error ?? false) || severity === 'error',
});

/** A flag's count in words, in the colour of an error where one is among them, else a warning's; then `dot`. */
export function flagLine(flag: Flag | undefined, words: (count: number) => string, dot?: string) {
  if (!flag || flag.count === 0) return nothing;
  return html`<span class="flag" data-severity=${flag.error ? 'error' : 'warning'}>${words(flag.count)}</span>${
      dot ? ` ${dot} ` : nothing
    }`;
}

/** The ids that describe a box: what it names besides, its help (`<id>-help`), then its problems. */
export function described(id: string, help: boolean, problems: readonly Shown[], besides: readonly string[] = []) {
  const ids = [...besides, help ? `${id}-help` : '', ...problems.map((_, index) => `${id}-why-${index}`)].filter(
    Boolean,
  );
  return ids.length > 0 ? ids.join(' ') : undefined;
}
