// The text still being typed is taken in before a save (ROADMAP 21.1): the box with the focus tells of it as leaving
// it would, through the shadow roots, and keeps the focus.
import { afterEach, describe, expect, it, vi } from 'vitest';
import { commitTyping } from './commit-typing';

afterEach(() => {
  document.body.replaceChildren();
});

/** A host with a shadow root that holds the markup, as an element of the backoffice does. */
function inShadow(markup: string): ShadowRoot {
  const host = document.createElement('div');
  document.body.append(host);
  const shadow = host.attachShadow({ mode: 'open' });
  shadow.innerHTML = markup;
  return shadow;
}

describe('the text being typed', () => {
  it('is told of by the box that holds the focus, inside the shadow roots, and the focus stays', () => {
    const outer = inShadow('<div id="inner"></div>');
    const inner = (outer.getElementById('inner') as HTMLElement).attachShadow({ mode: 'open' });
    inner.innerHTML = '<textarea></textarea><input id="other">';
    const box = inner.querySelector('textarea') as HTMLTextAreaElement;
    const heard = vi.fn();
    const other = vi.fn();
    box.addEventListener('change', heard);
    inner.getElementById('other')?.addEventListener('change', other);
    box.focus();
    box.value = 'Kind regards';

    commitTyping();

    expect([heard.mock.calls.length, other.mock.calls.length]).toEqual([1, 0]);
    expect(inner.activeElement).toBe(box);
  });

  it('asks nothing of a tick box, a button or a page without a focus', () => {
    const shadow = inShadow('<input type="checkbox"><button>Save</button>');
    const heard = vi.fn();
    shadow.addEventListener('change', heard);
    for (const control of shadow.querySelectorAll<HTMLElement>('input, button')) {
      control.focus();
      commitTyping();
    }
    (shadow.activeElement as HTMLElement | null)?.blur();
    commitTyping();
    expect(heard).not.toHaveBeenCalled();
  });
});
