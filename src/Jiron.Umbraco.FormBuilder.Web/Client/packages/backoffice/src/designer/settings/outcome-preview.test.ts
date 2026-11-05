// An outcome's live preview (ROADMAP 10.1c, ADR-0187): asked for at once, then once the changes pause, the newest answer
// winning and the last one kept while the next is awaited; the server's markup drawn with nothing in it that runs, loads
// or sends, the countdown from the runtime's least seconds and the print button shown, as after a send.
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { CHECK_PAUSE } from '../check/form-checker';
import type { DesignDefinition } from '../definition';
import { OutcomePreviewer, previewMarkup, type OutcomePreview, type PreviewState } from './outcome-preview';

const draft = (url: string): DesignDefinition => ({
  pages: [],
  outcomes: [{ key: 'o1', kind: 'message', showAnswers: false, redirect: { url, delaySeconds: 5 } }],
});

const shown = (html: string): OutcomePreview => ({ html, address: null, delaySeconds: 0, direction: 'ltr' });

beforeEach(() => vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] }));

afterEach(() => vi.useRealTimers());

describe("an outcome's previewer", () => {
  it('asks at once, then once the changes pause, and shows the newest answer', async () => {
    const answers: ((preview: OutcomePreview) => void)[] = [];
    const states: PreviewState[] = [];
    const previewer = new OutcomePreviewer(
      () => new Promise<OutcomePreview>((done) => answers.push(done)),
      (state) => states.push(state),
    );
    previewer.changed(draft('/a'));
    await vi.advanceTimersByTimeAsync(0);
    expect(answers).toHaveLength(1);
    expect(previewer.state).toEqual({ status: 'asking', preview: null });
    answers[0]?.(shown('<p>A</p>'));
    await vi.advanceTimersByTimeAsync(0);
    expect(previewer.state).toEqual({ status: 'shown', preview: shown('<p>A</p>') });

    // Two changes in a row ask once, after the pause; the last answer stays while the next is awaited.
    previewer.changed(draft('/b'));
    await vi.advanceTimersByTimeAsync(CHECK_PAUSE / 2);
    previewer.changed(draft('/c'));
    await vi.advanceTimersByTimeAsync(CHECK_PAUSE - 1);
    expect(answers).toHaveLength(1);
    await vi.advanceTimersByTimeAsync(1);
    expect(answers).toHaveLength(2);
    expect(previewer.state).toEqual({ status: 'asking', preview: shown('<p>A</p>') });

    // An answer to an earlier ask that comes after a later one's is dropped.
    previewer.changed(draft('/d'));
    await vi.advanceTimersByTimeAsync(CHECK_PAUSE);
    answers[2]?.(shown('<p>D</p>'));
    await vi.advanceTimersByTimeAsync(0);
    answers[1]?.(shown('<p>C</p>'));
    await vi.advanceTimersByTimeAsync(0);
    expect(previewer.state.preview).toEqual(shown('<p>D</p>'));
  });

  it('asks nothing for a change that leaves the draft as it was, and again after an ask that failed', async () => {
    let asked = 0;
    const reply: { preview?: OutcomePreview } = {};
    const previewer = new OutcomePreviewer(
      async () => {
        asked++;
        return reply.preview;
      },
      () => undefined,
    );
    previewer.changed(draft('/a'));
    await vi.advanceTimersByTimeAsync(0);
    expect(previewer.state).toEqual({ status: 'failed', preview: null });
    reply.preview = shown('<p>A</p>');
    previewer.changed(draft('/a'));
    await vi.advanceTimersByTimeAsync(CHECK_PAUSE);
    expect(asked).toBe(2);
    expect(previewer.state.status).toBe('shown');
    previewer.changed(draft('/a'));
    await vi.advanceTimersByTimeAsync(CHECK_PAUSE);
    expect(asked).toBe(2);
  });

  it('asks again with Try again only after an ask that got no answer', async () => {
    let asked = 0;
    const reply: { preview?: OutcomePreview } = {};
    const previewer = new OutcomePreviewer(
      async () => {
        asked++;
        return reply.preview;
      },
      () => undefined,
    );
    previewer.changed(draft('/a'));
    await vi.advanceTimersByTimeAsync(0);
    reply.preview = shown('<p>A</p>');
    previewer.retry();
    await vi.advanceTimersByTimeAsync(0);
    expect(asked).toBe(2);
    expect(previewer.state).toEqual({ status: 'shown', preview: shown('<p>A</p>') });
    previewer.retry();
    await vi.advanceTimersByTimeAsync(0);
    expect(asked).toBe(2);
  });

  it('stops waiting, and drops an answer still awaited', async () => {
    let answer: (preview: OutcomePreview) => void = () => undefined;
    const previewer = new OutcomePreviewer(
      () => new Promise<OutcomePreview>((done) => (answer = done)),
      () => undefined,
    );
    previewer.changed(draft('/a'));
    await vi.advanceTimersByTimeAsync(0);
    previewer.stop();
    answer(shown('<p>A</p>'));
    await vi.advanceTimersByTimeAsync(0);
    expect(previewer.state.status).toBe('asking');
  });
});

describe("an outcome's preview markup", () => {
  const drawn = (html: string, delay = 0) => {
    const holder = document.createElement('div');
    holder.append(previewMarkup(document, html, delay));
    return holder;
  };

  it("counts down from the runtime's least seconds, or the outcome's own where they are more", () => {
    const html =
      '<div data-jfb-outcome tabindex="-1"><p data-jfb-countdown data-jfb-one="In {0} second." data-jfb-other="In {0} seconds."></p></div>';
    expect(drawn(html, 5).querySelector('[data-jfb-countdown]')?.textContent).toBe('In 10 seconds.');
    expect(drawn(html, 30).querySelector('[data-jfb-countdown]')?.textContent).toBe('In 30 seconds.');
    expect(drawn(html, 5).querySelector('[data-jfb-outcome]')?.hasAttribute('tabindex')).toBe(false);
  });

  it('shows the print button, as the runtime does after a send', () => {
    const holder = drawn('<div data-jfb-copy><button type="button" data-jfb-print hidden>Print</button></div>');
    expect(holder.querySelector<HTMLButtonElement>('[data-jfb-print]')?.hidden).toBe(false);
  });

  it('keeps its links and buttons for a screen reader, none of them a stop of Tab', () => {
    const holder = drawn(
      '<div data-jfb-outcome tabindex="-1"><a href="/thanks" data-jfb-continue>Go now</a><button type="button">Stay here</button></div>',
    );
    expect(holder.querySelector('a')?.getAttribute('href')).toBe('/thanks');
    expect([...holder.querySelectorAll('a, button')].map((one) => one.getAttribute('tabindex'))).toEqual(['-1', '-1']);
    expect(holder.querySelector('[data-jfb-outcome]')?.hasAttribute('tabindex')).toBe(false);
  });

  it('leaves out what could run, load or send', () => {
    const holder = drawn(
      '<p onclick="alert(1)">Hi</p><script>alert(2)</script><a href="javascript:alert(3)">Go</a>' +
        '<img src="x" onerror="alert(4)"><style>p{}</style><link rel="stylesheet" href="/x.css"><form><input></form>' +
        '<iframe src="/x"></iframe><a href="java&#9;script:alert(5)">Tab</a><a href=" JAVASCRIPT:alert(6)">Case</a>',
    );
    expect(holder.innerHTML).toBe('<p>Hi</p><a>Go</a><img src="x"><a>Tab</a><a>Case</a>');
  });
});
