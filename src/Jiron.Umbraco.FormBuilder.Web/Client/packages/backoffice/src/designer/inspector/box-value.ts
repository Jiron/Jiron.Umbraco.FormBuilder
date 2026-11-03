// A box's value as the inspector draws it (ROADMAP 9.4a, ADR-0170): put in while the box is neither being typed in nor
// holding a value it couldn't keep. What is typed goes into the form as it is typed, read the way the form keeps it (a
// text without its spaces at the ends, a list without its empty lines); written back into the box, that would take the
// space or the line break just typed. A box left with a value it couldn't keep keeps it, beside the words that say why.
import { AsyncDirective, directive, noChange } from '@umbraco-cms/backoffice/external/lit';

class BoxValue extends AsyncDirective {
  render(value: string, held = false): unknown {
    void value;
    void held;
    return noChange;
  }

  override update(part: Parameters<AsyncDirective['update']>[0], [value, held]: [string, boolean?]): unknown {
    const box = (part as unknown as { element: HTMLInputElement | HTMLTextAreaElement }).element;
    if (!held && !box.matches(':focus') && box.value !== value) box.value = value;
    return noChange;
  }
}

/** Binds a box's value: `.value=${boxValue(text, heldByAProblem)}`. */
export const boxValue = directive(BoxValue);
