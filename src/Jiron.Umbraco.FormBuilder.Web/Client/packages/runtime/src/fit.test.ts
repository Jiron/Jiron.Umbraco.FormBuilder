// Fitting a form to its page (ROADMAP 6.4l, ADR-0022): the ground check takes the first opaque background behind a form
// and leaves a site's own class alone, never darkens a theme without a dark scheme and follows a site's own switch
// (27.1); the steps fold where a word of a name is wider than its step.
import { afterEach, describe, expect, it, vi } from 'vitest';
import { checkGround, foldSteps, followGround, type ReadColour, followSteps } from './fit';

/** Reads the colours these tests write, which is all a test's browser has. */
const read: ReadColour = (css) => {
  const found = /rgba?\((\d+), (\d+), (\d+)(?:, ([\d.]+))?\)/.exec(css);
  return found === null
    ? null
    : [Number(found[1]), Number(found[2]), Number(found[3]), Math.round(Number(found[4] ?? 1) * 255)];
};

function formOn(...grounds: string[]): HTMLFormElement {
  let parent: HTMLElement = document.body;
  parent.style.backgroundColor = '';
  for (const ground of grounds) {
    const element = document.createElement('div');
    element.style.backgroundColor = ground;
    parent.append(element);
    parent = element;
  }
  const form = document.createElement('form');
  form.className = 'jfb-form';
  parent.append(form);
  return form;
}

afterEach(() => {
  document.body.innerHTML = '';
  document.body.removeAttribute('class');
  document.documentElement.removeAttribute('class');
  document.body.style.backgroundColor = '';
  vi.restoreAllMocks();
});

describe('the ground check', () => {
  it('takes the dark tokens on a dark ground', () => {
    const form = formOn('rgb(16, 20, 24)');
    checkGround(form, read);

    expect(form.classList.contains('jfb-scheme-dark')).toBe(true);
  });

  it('changes nothing on a light ground', () => {
    const form = formOn('rgb(250, 250, 250)');
    checkGround(form, read);

    expect(form.className).toBe('jfb-form');
  });

  it('changes nothing with no ground at all, which a browser shows white', () => {
    const form = formOn();
    checkGround(form, read);

    expect(form.className).toBe('jfb-form');
  });

  it('reads the first opaque ground, past what shows through', () => {
    const form = formOn('rgb(16, 20, 24)', 'rgba(255, 255, 255, 0.1)', 'rgba(0, 0, 0, 0)');
    checkGround(form, read);

    expect(form.classList.contains('jfb-scheme-dark')).toBe(true);
  });

  it('stops at the first opaque ground, whatever lies behind it', () => {
    const form = formOn('rgb(16, 20, 24)', 'rgb(250, 250, 250)');
    checkGround(form, read);

    expect(form.className).toBe('jfb-form');
  });

  it('counts a ground as dark by the contrast of white text on it', () => {
    const darkEnough = formOn('rgb(110, 110, 110)');
    checkGround(darkEnough, read);
    const lightEnough = formOn('rgb(130, 130, 130)');
    checkGround(lightEnough, read);

    expect(darkEnough.classList.contains('jfb-scheme-dark')).toBe(true);
    expect(lightEnough.classList.contains('jfb-scheme-dark')).toBe(false);
  });

  it.each([
    ['on the form', (form: HTMLElement) => form.classList.add('jfb-scheme-light')],
    ['on an ancestor', (form: HTMLElement) => form.parentElement?.classList.add('jfb-scheme-auto')],
    ['on the root', () => document.documentElement.classList.add('jfb-scheme-light')],
  ])("never changes a site's own class %s", (_where, set) => {
    const form = formOn('rgb(16, 20, 24)');
    set(form);
    const before = form.className;
    checkGround(form, read);

    expect(form.className).toBe(before);
  });

  it('leaves a form alone whose colours it cannot read', () => {
    const form = formOn('rgb(16, 20, 24)');
    checkGround(form, () => null);

    expect(form.className).toBe('jfb-form');
  });

  it('never gives the dark tokens to a form whose theme has no dark scheme, on the darkest ground', () => {
    const form = formOn('rgb(0, 0, 0)');
    form.setAttribute('data-jfb-light-only', '');
    checkGround(form, read);

    expect(form.className).toBe('jfb-form');
  });

  it("checks a form again when the site's own switch changes the ground, by a class, a style or a data attribute", async () => {
    const form = formOn();
    const check = (root: Element): void => checkGround(root, read);
    checkGround(form, read);
    followGround(form, check);
    const settled = () => new Promise((resolve) => setTimeout(resolve, 0));

    document.body.classList.add('night');
    document.body.style.backgroundColor = 'rgb(16, 20, 24)';
    await settled();
    expect(form.classList.contains('jfb-scheme-dark'), 'the switch darkened the body').toBe(true);

    document.body.style.backgroundColor = 'rgb(250, 250, 250)';
    await settled();
    expect(form.classList.contains('jfb-scheme-dark'), 'and lightened it').toBe(false);

    document.body.style.backgroundColor = 'rgb(16, 20, 24)';
    document.documentElement.setAttribute('data-theme', 'dark');
    await settled();
    expect(form.classList.contains('jfb-scheme-dark'), 'a data attribute of the document element').toBe(true);

    // A site's own class wins over what the check gave.
    document.documentElement.classList.add('jfb-scheme-dark');
    await settled();
    expect(form.classList.contains('jfb-scheme-dark')).toBe(false);
  });

  it('checks again when a background behind the form ends its transition, and for no other change', async () => {
    const form = formOn('rgb(250, 250, 250)');
    const checked: Element[] = [];
    followGround(form, (root) => checked.push(root));
    const settled = () => new Promise((resolve) => setTimeout(resolve, 0));

    document.body.setAttribute('aria-busy', 'true');
    form.parentElement?.dispatchEvent(new Event('transitionend', { bubbles: true }));
    const done = new Event('transitionend', { bubbles: true });
    Object.defineProperty(done, 'propertyName', { value: 'color' });
    form.parentElement?.dispatchEvent(done);
    await settled();
    expect(checked, 'an attribute of no switch, and a transition of another property').toEqual([]);

    const ground = new Event('transitionend', { bubbles: true });
    Object.defineProperty(ground, 'propertyName', { value: 'background-color' });
    form.parentElement?.dispatchEvent(ground);
    const own = new Event('transitionend', { bubbles: true });
    Object.defineProperty(own, 'propertyName', { value: 'background-color' });
    form.dispatchEvent(own);
    expect(checked, "behind the form, not the form's own").toEqual([form]);
  });

  it('follows nothing for a form whose theme has no dark scheme, and lets go of a form taken off the page', async () => {
    const light = formOn();
    light.setAttribute('data-jfb-light-only', '');
    const gone = formOn();
    const checked: Element[] = [];
    followGround(light, (root) => checked.push(root));
    followGround(gone, (root) => checked.push(root));
    gone.remove();
    document.body.classList.add('night');
    await new Promise((resolve) => setTimeout(resolve, 0));
    expect(checked).toEqual([]);
  });

  it('takes back what it gave when the ground turned light, and no class of the site', () => {
    const form = formOn('rgb(16, 20, 24)');
    checkGround(form, read);
    (form.parentElement as HTMLElement).style.backgroundColor = 'rgb(250, 250, 250)';
    checkGround(form, read);
    expect(form.classList.contains('jfb-scheme-dark')).toBe(false);

    form.classList.add('jfb-scheme-dark');
    checkGround(form, read);
    expect(form.classList.contains('jfb-scheme-dark'), "now it is the site's").toBe(true);
  });
});

describe('step folding', () => {
  it('folds anew when a rule changes the steps', async () => {
    document.body.innerHTML = '<div class="steps"><ol><li><span>A</span></li></ol></div>';
    const steps = document.querySelector('.steps') as HTMLElement;
    steps.setAttribute('data-jfb-compact', '');
    followSteps(steps);
    steps.setAttribute('data-jfb-compact', '');
    steps.querySelector('ol')?.append(document.createElement('li'));

    await vi.waitFor(() => expect(steps.hasAttribute('data-jfb-compact')).toBe(false));
  });

  const stepsOf = (...names: string[]): HTMLElement => {
    document.body.innerHTML = `<div class="jfb-progress-steps"><p>Step 1 of ${String(names.length)}</p><ol>${names
      .map((name) => `<li><span>${name}</span></li>`)
      .join('')}</ol></div>`;
    return document.querySelector('.jfb-progress-steps') as HTMLElement;
  };
  const roomy = (room: number): void => {
    vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockReturnValue({ width: room } as DOMRect);
  };
  /** Ten pixels a letter. */
  const measure = (word: string): number => word.length * 10;

  it('folds where one word of a name is wider than its step', () => {
    const steps = stepsOf('Kontakt', 'Veranstaltungsanmeldung', 'Fertig');
    roomy(100);
    foldSteps(steps, measure);

    expect(steps.hasAttribute('data-jfb-compact')).toBe(true);
  });

  it('stays unfolded where every word fits, however long the name is', () => {
    const steps = stepsOf('Your contact details and more of them');
    roomy(100);
    foldSteps(steps, measure);

    expect(steps.hasAttribute('data-jfb-compact')).toBe(false);
  });

  it('unfolds once it has the room', () => {
    const steps = stepsOf('Veranstaltungsanmeldung');
    roomy(100);
    foldSteps(steps, measure);
    expect(steps.hasAttribute('data-jfb-compact')).toBe(true);

    vi.restoreAllMocks();
    roomy(400);
    foldSteps(steps, measure);
    expect(steps.hasAttribute('data-jfb-compact')).toBe(false);
  });

  it('reads the room with the steps unfolded, as folded steps show no name', () => {
    const steps = stepsOf('Veranstaltungsanmeldung');
    steps.setAttribute('data-jfb-compact', '');
    const seen: boolean[] = [];
    vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockImplementation(() => {
      seen.push(steps.hasAttribute('data-jfb-compact'));
      return { width: 400 } as DOMRect;
    });
    foldSteps(steps, measure);

    expect(seen).toEqual([false]);
  });

  it('counts the name alone, not what the page adds beside it', () => {
    const steps = stepsOf('Fine<b>Veranstaltungsanmeldung</b>');
    roomy(100);
    foldSteps(steps, measure);

    expect(steps.hasAttribute('data-jfb-compact')).toBe(false);
  });

  it('leaves alone steps the theme folded by itself', () => {
    const steps = stepsOf('Veranstaltungsanmeldung');
    (steps.querySelector('span') as HTMLElement).style.position = 'absolute';
    roomy(100);
    foldSteps(steps, measure);

    expect(steps.hasAttribute('data-jfb-compact')).toBe(false);
  });

  it('leaves them unfolded where the browser cannot measure a word', () => {
    const steps = stepsOf('Veranstaltungsanmeldung');
    roomy(100);
    foldSteps(steps, () => null);

    expect(steps.hasAttribute('data-jfb-compact')).toBe(false);
  });
});
