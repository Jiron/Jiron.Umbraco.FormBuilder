// The signature pad (ROADMAP 6.4n, ADR-0153): drawn by pointer, cleared and drawn again, or typed instead; nothing goes until
// a send, whose wait sends the image, the challenge first, and holds its token as the field's answer; a typed name's PNG holds
// the name as the server's does.
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { focusTarget, showFieldError } from './display';
import type { ShownError } from './messages';
import type { FormModel, ModelField } from './model';
import { canvasOfName, enhanceSignatures } from './signature-chunk';
import { ENGLISH } from './testing';
import { allOf, ON_ITS_WAY, takesUploads, type PageUploads } from './uploads';

const PNG = Uint8Array.from(
  atob('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg=='),
  (letter) => letter.charCodeAt(0),
);

const SIGNATURE: ModelField = { key: 'k-sign', type: 'signature', alias: 'sign', settings: {}, label: 'Sign' };

const MODEL: FormModel = {
  alias: 'hire',
  revision: 1,
  culture: 'en-US',
  timeZone: 'UTC',
  open: true,
  form: { key: 'f', settings: {}, pages: [{ key: 'p1', groups: [{ key: 'g1', fields: [SIGNATURE] }] }] },
  fields: { 'k-sign': { kind: 'files', canBeRequired: true, builtIn: true } },
  consents: {},
  ...ENGLISH,
};

const FIELD = (value = ''): string =>
  '<div data-jfb-field="k-sign" data-jfb-alias="sign" data-jfb-type="signature"><fieldset><legend>Sign</legend>' +
  '<div class="jfb-signature"><div class="jfb-signature-way" data-jfb-drawn><p>Sign here</p>' +
  '<canvas class="jfb-signature-pad" width="900" height="300"></canvas><div class="jfb-signature-tools">' +
  '<button type="button" data-jfb-clear data-jfb-undo="Undo clear">Clear</button>' +
  '<button type="button" data-jfb-to-typed>Type your name instead</button></div></div>' +
  '<div class="jfb-signature-way" data-jfb-typed><label for="jfb-1-sign">Your name</label>' +
  `<input id="jfb-1-sign" name="sign.typed" value="${value}"><div class="jfb-signature-tools">` +
  '<button type="button" data-jfb-to-drawn>Sign by hand instead</button></div></div></div></fieldset></div>';

/** A canvas's drawing, as far as a test needs to see it. */
interface Painting {
  strokeStyle: string;
  fillStyle: string;
  font: string;
  lineWidth: number;
  lineCap: string;
  lineJoin: string;
  readonly drawn: string[];
  readonly texts: { readonly text: string; readonly x: number; readonly y: number; readonly font: string }[];
  clearRect(): void;
  beginPath(): void;
  moveTo(x: number, y: number): void;
  lineTo(x: number, y: number): void;
  quadraticCurveTo(a: number, b: number, x: number, y: number): void;
  stroke(): void;
  measureText(text: string): { width: number };
  fillText(text: string, x: number, y: number): void;
}

const paintings = new Map<HTMLCanvasElement, Painting>();
/** A letter's width at one pixel of a font's size. */
const WIDE = 0.5;

function paintingOf(canvas: HTMLCanvasElement): Painting {
  let painting = paintings.get(canvas);
  if (painting === undefined) {
    painting = {
      strokeStyle: '',
      fillStyle: '',
      font: '',
      lineWidth: 0,
      lineCap: '',
      lineJoin: '',
      drawn: [],
      texts: [],
      clearRect() {
        this.drawn.length = 0;
      },
      beginPath() {
        this.drawn.push('begin');
      },
      moveTo(x, y) {
        this.drawn.push(`move ${String(x)} ${String(y)}`);
      },
      lineTo(x, y) {
        this.drawn.push(`line ${String(x)} ${String(y)}`);
      },
      quadraticCurveTo(a, b, x, y) {
        this.drawn.push(`curve ${String(a)} ${String(b)} ${String(x)} ${String(y)}`);
      },
      stroke() {
        this.drawn.push(`stroke ${this.strokeStyle}`);
      },
      measureText(text) {
        return { width: text.length * WIDE * Number.parseFloat(/(\d+(?:\.\d+)?)px/.exec(this.font)?.[1] ?? '0') };
      },
      fillText(text, x, y) {
        this.texts.push({ text, x, y, font: this.font });
      },
    };
    paintings.set(canvas, painting);
  }
  return painting;
}

const made: HTMLCanvasElement[] = [];

interface Sent {
  readonly url: string;
  readonly body: FormData;
  readonly signal: AbortSignal | null;
}

function serve(
  answer: (count: number) => Response | Promise<Response> = () => new Response(JSON.stringify({ token: 't1' })),
): Sent[] {
  const sent: Sent[] = [];
  vi.stubGlobal(
    'fetch',
    vi.fn((url: string, init: RequestInit) => {
      sent.push({ url, body: init.body as FormData, signal: init.signal ?? null });
      return Promise.resolve(answer(sent.length));
    }),
  );
  return sent;
}

function wake(field = FIELD(), restored?: Parameters<typeof enhanceSignatures>[2]['restored'], focusTyped = false) {
  document.body.innerHTML = `<form id="jfb-1" data-jfb-form="f">${field}<div data-jfb-live></div></form>`;
  const form = document.querySelector('form') as HTMLFormElement;
  const renew = vi.fn(() => Promise.resolve('renewed'));
  const heard: string[] = [];
  form.addEventListener('input', (event) => {
    heard.push((event.target as Element).localName);
  });
  if (focusTyped) form.querySelector<HTMLElement>('[data-jfb-typed] input')?.focus();
  const signatures = enhanceSignatures(form, MODEL, {
    api: '/jfb/api/v1',
    challenge: () => Promise.resolve('c1'),
    renew,
    ...(restored === undefined ? {} : { restored }),
  });
  const canvas = form.querySelector('canvas') as HTMLCanvasElement;
  canvas.getBoundingClientRect = () => ({ left: 10, top: 20, width: 450, height: 150 }) as DOMRect;
  canvas.setPointerCapture = vi.fn();
  const pointer = (type: string, x: number, y: number, more: Record<string, unknown> = {}): void => {
    canvas.dispatchEvent(
      Object.assign(
        new MouseEvent(type, {
          clientX: x,
          clientY: y,
          button: more['button'] === undefined ? 0 : (more['button'] as number),
          bubbles: true,
          cancelable: true,
        }),
        { pointerId: more['pointerId'] ?? 1, getCoalescedEvents: more['getCoalescedEvents'] },
      ),
    );
  };
  const draw = (): void => {
    pointer('pointerdown', 20, 30);
    pointer('pointermove', 30, 40);
    pointer('pointermove', 40, 50);
    pointer('pointermove', 50, 60);
    pointer('pointerup', 50, 60);
  };
  const part = <T extends HTMLElement>(selector: string): T => form.querySelector(selector) as T;
  return {
    form,
    signatures,
    canvas,
    pointer,
    draw,
    renew,
    heard,
    part,
    typed: part<HTMLInputElement>('[data-jfb-typed] input'),
    clear: part<HTMLButtonElement>('[data-jfb-clear]'),
    toTyped: part<HTMLButtonElement>('[data-jfb-to-typed]'),
    toDrawn: part<HTMLButtonElement>('[data-jfb-to-drawn]'),
    answer: (): string | null =>
      form.querySelector<HTMLInputElement>('input[type="hidden"][name="sign"]')?.value ?? null,
  };
}

const bytesOf = (file: Blob): Promise<Uint8Array> =>
  new Promise((resolve) => {
    const reader = new FileReader();
    reader.onload = () => resolve(new Uint8Array(reader.result as ArrayBuffer));
    reader.readAsArrayBuffer(file);
  });

beforeEach(() => {
  paintings.clear();
  made.length = 0;
  vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockImplementation(function (this: HTMLCanvasElement) {
    return paintingOf(this) as unknown as ReturnType<HTMLCanvasElement['getContext']>;
  } as never);
  HTMLCanvasElement.prototype.toBlob = function (this: HTMLCanvasElement, done: BlobCallback) {
    made.push(this);
    done({ arrayBuffer: () => Promise.resolve(PNG.slice().buffer) } as unknown as Blob);
  };
});

afterEach(() => {
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
  document.body.innerHTML = '';
});

describe('a signature on the page', () => {
  it('shows the pad and its way to typing, enhanced, with nothing to clear yet', () => {
    const { part, clear, toTyped } = wake();

    expect(part('.jfb-signature').hasAttribute('data-jfb-enhanced')).toBe(true);
    expect(part('[data-jfb-drawn]').hidden).toBe(false);
    expect(part('[data-jfb-typed]').hidden).toBe(true);
    expect(clear.hidden).toBe(true);
    expect(toTyped.hidden).toBe(false);
  });

  it('starts typed where the page already holds a typed name', () => {
    const { part, answer } = wake(FIELD('Ana Lima'));

    expect(part('[data-jfb-typed]').hidden).toBe(false);
    expect(part('[data-jfb-drawn]').hidden).toBe(true);
    expect(answer()).toBe(ON_ITS_WAY);
  });

  it('stays on the name for a visitor who came to it, the first thing the page shows without its runtime', () => {
    const { part, typed } = wake(FIELD(), undefined, true);

    expect(part('[data-jfb-typed]').hidden).toBe(false);
    expect(document.activeElement).toBe(typed);
  });

  it('tells the rules of an answer it starts with, as they decided before it could answer', () => {
    const { heard } = wake(FIELD('Ana Lima'));

    expect(heard).toEqual(['div']);
  });

  it('tells them nothing of a field that starts unanswered', () => {
    const { heard } = wake();

    expect(heard).toEqual([]);
  });

  it('leaves alone a view that writes the rest without its pad', () => {
    const field = FIELD().replace('<canvas class="jfb-signature-pad" width="900" height="300"></canvas>', '');
    document.body.innerHTML = `<form data-jfb-form="f">${field}</form>`;
    enhanceSignatures(document.querySelector('form') as HTMLFormElement, MODEL, {
      api: '/a',
      challenge: () => Promise.resolve(null),
      renew: () => Promise.resolve(null),
    });

    expect(document.querySelector('.jfb-signature')?.hasAttribute('data-jfb-enhanced')).toBe(false);
  });

  it('leaves alone a view that writes no pad', () => {
    document.body.innerHTML =
      '<form data-jfb-form="f"><div class="jfb-signature"><input name="sign.typed"></div></form>';
    const form = document.querySelector('form') as HTMLFormElement;
    const pads = enhanceSignatures(form, MODEL, {
      api: '/a',
      challenge: () => Promise.resolve(null),
      renew: () => Promise.resolve(null),
    });

    expect(form.querySelector('.jfb-signature')?.hasAttribute('data-jfb-enhanced')).toBe(false);
    expect(pads.uploaded().size).toBe(0);
  });
});

describe('drawing', () => {
  it('counts as an answer from the first stroke, and tells the rules and the errors', () => {
    const { draw, answer, heard, clear } = wake();
    expect(answer()).toBeNull();
    draw();

    expect(answer()).toBe(ON_ITS_WAY);
    expect(heard).toEqual(['div']);
    expect(clear.hidden).toBe(false);
    expect(clear.textContent).toBe('Clear');
  });

  it('draws where the pointer is, however the theme scales the pad, and takes the moves a browser gathered', () => {
    const { canvas, pointer } = wake();
    pointer('pointerdown', 10, 20);
    pointer('pointermove', 0, 0, {
      getCoalescedEvents: () => [
        { clientX: 110, clientY: 70 },
        { clientX: 210, clientY: 120 },
      ],
    });

    const drawn = paintingOf(canvas).drawn;
    expect(drawn[1]).toBe('move 0 0');
    expect(drawn.join(' ')).toContain('line 400 200');
    expect(canvas.setPointerCapture).toHaveBeenCalledWith(1);
  });

  it('draws a touch that never moved as a dot', () => {
    const { canvas, pointer } = wake();
    pointer('pointerdown', 20, 30);

    expect(paintingOf(canvas).drawn).toContain('line 20 20');
  });

  it('draws curves through the middle of each two points', () => {
    const { canvas, draw } = wake();
    draw();

    expect(paintingOf(canvas).drawn).toContain('curve 40 40 50 50');
  });

  it('takes no second finger and no other button', () => {
    const { canvas, pointer, answer } = wake();
    pointer('pointerdown', 20, 30, { button: 2 });
    expect(paintingOf(canvas).drawn.filter((each) => each === 'begin')).toEqual([]);

    pointer('pointerdown', 20, 30);
    pointer('pointerdown', 90, 90, { pointerId: 2 });
    pointer('pointermove', 90, 90, { pointerId: 2 });
    pointer('pointerup', 90, 90, { pointerId: 2 });
    expect(answer(), 'the first stroke goes on').toBeNull();
    pointer('pointerup', 20, 30);
    expect(answer()).toBe(ON_ITS_WAY);
    expect(paintingOf(canvas).drawn.filter((each) => each === 'begin')).toHaveLength(1);
    expect(paintingOf(canvas).drawn.join(' '), 'and the second draws nowhere').not.toContain('160 140');
  });

  it('ends a stroke a browser took over', () => {
    const { pointer, answer } = wake();
    pointer('pointerdown', 20, 30);
    pointer('pointercancel', 20, 30);

    expect(answer()).toBe(ON_ITS_WAY);
  });

  it('shows the strokes in the text colour of the theme', () => {
    const { canvas, draw } = wake();
    vi.spyOn(window, 'getComputedStyle').mockReturnValue({ color: 'rgb(1, 2, 3)' } as CSSStyleDeclaration);
    draw();

    expect(paintingOf(canvas).strokeStyle).toBe('rgb(1, 2, 3)');
  });
});

describe('clearing', () => {
  it('turns into "Undo clear" until the next stroke, so a slip loses nothing', () => {
    const { canvas, draw, clear, answer, pointer } = wake();
    draw();
    clear.click();
    expect(clear.textContent).toBe('Undo clear');
    expect(answer()).toBeNull();
    expect(paintingOf(canvas).drawn).toEqual([]);

    clear.click();
    expect(clear.textContent).toBe('Clear');
    expect(answer()).toBe(ON_ITS_WAY);
    expect(paintingOf(canvas).drawn).toContain('begin');

    clear.click();
    pointer('pointerdown', 20, 30);
    pointer('pointerup', 20, 30);
    expect(clear.textContent).toBe('Clear');
  });
});

describe('typing instead', () => {
  it('moves to the name, which answers once it holds anything to keep', () => {
    const { part, toTyped, typed, answer, draw } = wake();
    draw();
    toTyped.click();

    expect(part('[data-jfb-typed]').hidden).toBe(false);
    expect(part('[data-jfb-drawn]').hidden).toBe(true);
    expect(document.activeElement).toBe(typed);
    expect(answer(), 'the drawing is no answer here').toBeNull();

    typed.value = '   ';
    typed.dispatchEvent(new Event('input', { bubbles: true }));
    expect(answer()).toBeNull();

    typed.value = 'Ana';
    typed.dispatchEvent(new Event('input', { bubbles: true }));
    expect(answer()).toBe(ON_ITS_WAY);
  });

  it('reads only the way on show: a name typed before is no answer on the pad, and is one again on its way', () => {
    const { toTyped, toDrawn, typed, form, answer } = wake();
    toTyped.click();
    expect(typed.name).toBe('sign.typed');
    typed.value = 'Ana';
    typed.dispatchEvent(new Event('input', { bubbles: true }));
    toDrawn.click();

    expect(form.querySelector('[name="sign.typed"]')).toBeNull();
    expect(answer()).toBeNull();
    expect(typed.value).toBe('Ana');

    toTyped.click();
    expect(typed.name).toBe('sign.typed');
    expect(answer()).toBe(ON_ITS_WAY);
  });

  it('comes back to the drawing, kept, with the focus on what is there', () => {
    const { part, toTyped, toDrawn, clear, draw, answer } = wake();
    draw();
    toTyped.click();
    toDrawn.click();

    expect(part('[data-jfb-drawn]').hidden).toBe(false);
    expect(document.activeElement).toBe(clear);
    expect(answer()).toBe(ON_ITS_WAY);
  });

  it('comes back to an empty pad with the focus on the way to typing', () => {
    const { toTyped, toDrawn } = wake();
    toTyped.click();
    toDrawn.click();

    expect(document.activeElement).toBe(toTyped);
  });
});

describe('a send', () => {
  it('sends a drawn image to the uploads endpoint, the challenge first, and holds its token', async () => {
    const sent = serve();
    const { signatures, draw, answer } = wake();
    draw();
    expect(sent, 'nothing goes until a send').toEqual([]);

    await expect(signatures.settled()).resolves.toBe(true);

    expect(sent[0]?.url).toBe('/jfb/api/v1/forms/f/uploads?fieldKey=k-sign');
    expect([...(sent[0]?.body.keys() ?? [])]).toEqual(['challenge', 'file']);
    expect(sent[0]?.body.get('challenge')).toBe('c1');
    expect((sent[0]?.body.get('file') as File).name).toBe('signature.png');
    expect(answer()).toBe('t1');
    expect(new TextDecoder().decode(await bytesOf(sent[0]?.body.get('file') as File))).not.toContain('jfb:typed-name');
  });

  it("draws the image in the server's ink at the size of the pad, whatever the theme shows", async () => {
    serve();
    const { signatures, draw, canvas } = wake();
    vi.spyOn(window, 'getComputedStyle').mockReturnValue({ color: 'rgb(255, 255, 255)' } as CSSStyleDeclaration);
    draw();
    await signatures.settled();

    const image = made[0] as HTMLCanvasElement;
    expect(image).not.toBe(canvas);
    expect(image.height).toBe(300);
    expect(image.width).toBe(900);
    expect(paintingOf(image).strokeStyle).toBe('#1f2430');
    expect(paintingOf(image).lineWidth).toBe(3.75);
  });

  it('sends nothing without a signature, on a field the rules hid, or twice for the same one', async () => {
    const sent = serve();
    const { signatures, draw, part } = wake();
    await signatures.settled();
    expect(sent).toEqual([]);

    draw();
    part('[data-jfb-field]').setAttribute('data-jfb-hidden', '');
    await signatures.settled();
    expect(sent).toEqual([]);

    part('[data-jfb-field]').removeAttribute('data-jfb-hidden');
    await signatures.settled();
    await signatures.settled();
    expect(sent).toHaveLength(1);
  });

  it('sends anew after any change', async () => {
    const sent = serve();
    const { signatures, draw, clear, toTyped, toDrawn, typed } = wake();
    draw();
    await signatures.settled();
    draw();
    await signatures.settled();
    expect(sent).toHaveLength(2);

    clear.click();
    clear.click();
    await signatures.settled();
    expect(sent).toHaveLength(3);

    toTyped.click();
    typed.value = 'Ana';
    typed.dispatchEvent(new Event('input', { bubbles: true }));
    await signatures.settled();
    expect(sent).toHaveLength(4);

    typed.value = 'Ana Lima';
    typed.dispatchEvent(new Event('input', { bubbles: true }));
    await signatures.settled();
    expect(sent).toHaveLength(5);

    toDrawn.click();
    await signatures.settled();
    expect(sent).toHaveLength(6);
  });

  it('puts a typed name in the PNG, cleaned, as the server reads it', async () => {
    const sent = serve();
    const { signatures, toTyped, typed } = wake();
    toTyped.click();
    typed.value = '  Ana \t Lima ';
    typed.dispatchEvent(new Event('input', { bubbles: true }));
    await signatures.settled();

    const png = await bytesOf(sent[0]?.body.get('file') as File);
    const text = new TextDecoder().decode(png);
    expect(text).toContain('iTXtjfb:typed-name\0\0\0\0\0Ana Lima');
    expect(paintingOf(made[0] as HTMLCanvasElement).texts[0]?.text).toBe('Ana Lima');
  });

  it('asks for a new challenge once when its own has expired', async () => {
    const sent = serve((count) =>
      count === 1 ? new Response('{}', { status: 401 }) : new Response(JSON.stringify({ token: 't2' })),
    );
    const { signatures, draw, renew, answer } = wake();
    draw();
    await signatures.settled();

    expect(renew).toHaveBeenCalledOnce();
    expect(sent).toHaveLength(2);
    expect(answer()).toBe('t2');
  });

  it.each([
    [401, null],
    [429, '30'],
    [503, null],
    [413, null],
  ])('rejects with how %i answered, for the notice of a failed send', async (status, retryAfter) => {
    serve(
      () =>
        new Response('{}', {
          status,
          ...(retryAfter === null ? {} : { headers: { 'Retry-After': retryAfter } }),
        }),
    );
    const { signatures, draw, answer } = wake();
    draw();

    await expect(signatures.settled()).rejects.toEqual({ status, retryAfter });
    expect(answer()).toBe(ON_ITS_WAY);
  });

  it('gives up on an upload that takes too long, for the notice of a send that got no answer', async () => {
    const sent = serve();
    const { signatures, draw } = wake();
    draw();
    await signatures.settled();

    expect(sent[0]?.signal).toBeInstanceOf(AbortSignal);
  });

  it('sends what the signature is by now when it changed while an image was on its way', async () => {
    let answerFirst: (response: Response) => void = () => undefined;
    const first = new Promise<Response>((resolve) => {
      answerFirst = resolve;
    });
    const sent = serve((count) => (count === 1 ? first : new Response(JSON.stringify({ token: 't2' }))));
    const { signatures, draw, answer } = wake();
    draw();
    const settled = signatures.settled();
    await vi.waitFor(() => expect(sent).toHaveLength(1));
    draw();
    answerFirst(new Response(JSON.stringify({ token: 't1' })));
    await settled;

    expect(sent).toHaveLength(2);
    expect(answer()).toBe('t2');
  });

  it('sends nothing more, and fails nothing, when it was cleared while an image was on its way', async () => {
    let answerFirst: (response: Response) => void = () => undefined;
    const first = new Promise<Response>((resolve) => {
      answerFirst = resolve;
    });
    const sent = serve(() => first);
    const { signatures, draw, clear, answer } = wake();
    draw();
    const settled = signatures.settled();
    await vi.waitFor(() => expect(sent).toHaveLength(1));
    clear.click();
    answerFirst(new Response(JSON.stringify({ token: 't1' })));

    await expect(settled).resolves.toBe(true);
    expect(sent).toHaveLength(1);
    expect(answer()).toBeNull();
  });

  it('takes no token from a refusal', async () => {
    serve(() => new Response(JSON.stringify({ token: 't1' }), { status: 500 }));
    const { signatures, draw, answer } = wake();
    draw();

    await expect(signatures.settled()).rejects.toEqual({ status: 500, retryAfter: null });
    expect(answer()).toBe(ON_ITS_WAY);
  });

  it('rejects with an answer that holds no token', async () => {
    serve(() => new Response('{}'));
    const { signatures, draw } = wake();
    draw();

    await expect(signatures.settled()).rejects.toEqual({ status: 200, retryAfter: null });
  });

  it('rejects where a browser draws no picture', async () => {
    serve();
    HTMLCanvasElement.prototype.toBlob = (done: BlobCallback) => {
      done(null);
    };
    const { signatures, draw } = wake();
    draw();

    await expect(signatures.settled()).rejects.toThrow('no picture');
  });
});

describe("a form that came in another one's place", () => {
  const HELD = new Map([['sign', [{ token: 't9', name: 'signature.png', size: 7, strokes: [[1, 2, 3, 4]] }]]]);

  it('shows the strokes again, and holds the token, which goes no second time', async () => {
    const sent = serve();
    const { signatures, canvas, answer, clear } = wake(FIELD(), HELD);
    expect(answer()).toBe('t9');
    expect(clear.hidden).toBe(false);
    expect(paintingOf(canvas).drawn).toContain('move 1 2');

    await signatures.settled();
    expect(sent).toEqual([]);
  });

  it('hands on a drawn signature only once it went, with its strokes', async () => {
    serve();
    const { signatures, draw, toTyped } = wake();
    draw();
    expect(signatures.uploaded().size).toBe(0);

    await signatures.settled();
    const file = signatures.uploaded().get('sign')?.[0];
    expect(file?.token).toBe('t1');
    expect(file?.size).toBe(PNG.length);
    expect(file?.strokes?.[0]?.slice(0, 2)).toEqual([20, 20]);

    toTyped.click();
    expect(signatures.uploaded().size).toBe(0);
  });

  it('hands on no typed name, which is carried as the answer it is', async () => {
    serve();
    const { signatures, toTyped, typed } = wake();
    toTyped.click();
    typed.value = 'Ana';
    typed.dispatchEvent(new Event('input', { bubbles: true }));
    await signatures.settled();

    expect(signatures.uploaded().size).toBe(0);
  });
});

describe('the image of a typed name, as the server draws one', () => {
  const FAMILY = 'Inter, sans-serif';
  const LARGEST = (0.3 * 300) / 0.7;
  const drawn = (name: string, width = 900, height = 300) => {
    const canvas = canvasOfName(document, name, width, height, FAMILY);
    return { canvas, text: paintingOf(canvas).texts[0] };
  };

  it('writes a short name at the largest size, on the line, past the margin, in the ink', () => {
    const { canvas, text } = drawn('Ana');

    expect(canvas.width).toBe(900);
    expect(canvas.height).toBe(300);
    expect(text).toEqual({ text: 'Ana', x: 54, y: 225, font: `italic ${String(LARGEST)}px ${FAMILY}` });
    expect(paintingOf(canvas).fillStyle).toBe('#1f2430');
  });

  it('writes a longer name smaller, on the pad', () => {
    const name = 'a'.repeat(20);
    const { canvas, text } = drawn(name);
    const size = (LARGEST * (900 - 108)) / (20 * WIDE * LARGEST);

    expect(canvas.width).toBe(900);
    expect(text?.font).toBe(`italic ${String(size)}px ${FAMILY}`);
  });

  it('widens the image for a long name, once it is as small as it may be', () => {
    const name = 'a'.repeat(60);
    const { canvas, text } = drawn(name);
    const smallest = (0.15 * 300) / 0.7;

    expect(canvas.width).toBe(Math.ceil(60 * WIDE * smallest + 108));
    expect(text?.font).toBe(`italic ${String(smallest)}px ${FAMILY}`);
  });

  it('shrinks again past the widest the check allows', () => {
    const name = 'a'.repeat(100);
    const { canvas, text } = drawn(name);
    const size = (LARGEST * (2700 - 108)) / (100 * WIDE * LARGEST);

    expect(canvas.width).toBe(2700);
    expect(text?.font).toBe(`italic ${String(size)}px ${FAMILY}`);
    expect(100 * WIDE * size + 108).toBeLessThanOrEqual(2700);
  });
});

describe('its error', () => {
  const ERROR: ShownError = {
    field: 'k-sign',
    row: null,
    code: 'required',
    target: 'jfb-1-sign',
    message: 'Add your signature',
    link: 'Add your signature',
  };

  it('marks the typed name as the server marks it, and takes the mark away with the error', () => {
    const { form, typed, part } = wake();
    showFieldError(form, part('[data-jfb-field]'), ERROR);

    expect(typed.getAttribute('aria-invalid')).toBe('true');
    expect(part('[data-jfb-field]').querySelectorAll('[aria-invalid]')).toHaveLength(1);
  });

  it('gives the focus to the way to typing while the pad hides the name, as the keyboard signs there', () => {
    const { form, toTyped, clear } = wake();
    const shows = (element: Element): boolean => element === toTyped;
    vi.spyOn(Element.prototype, 'getClientRects').mockImplementation(function (this: Element) {
      return (shows(this) ? [{}] : []) as unknown as DOMRectList;
    });

    expect(focusTarget(form, 'jfb-1-sign')).toBe(true);
    expect(document.activeElement).toBe(toTyped);
    expect(clear.hidden).toBe(true);
  });
});

describe('uploads of each kind, as one', () => {
  const part = (settled: boolean, name: string): PageUploads => ({
    settled: () => Promise.resolve(settled),
    uploaded: () => new Map([[name, [{ token: name, name, size: 1 }]]]),
  });

  it('let a send go only when each may', async () => {
    await expect(allOf([part(true, 'a'), part(true, 'b')]).settled()).resolves.toBe(true);
    await expect(allOf([part(true, 'a'), part(false, 'b')]).settled()).resolves.toBe(false);
    await expect(allOf([part(false, 'a'), part(true, 'b')]).settled()).resolves.toBe(false);
  });

  it('hold what each holds', () => {
    expect([
      ...allOf([part(true, 'a'), part(true, 'b')])
        .uploaded()
        .keys(),
    ]).toEqual(['a', 'b']);
  });
});

describe('which forms take a signature on the page', () => {
  const form = (
    fields: string,
    pages = 1,
    templates = '<template data-jfb-templates data-jfb-sending="Sending"></template>',
  ): HTMLFormElement => {
    document.body.innerHTML = `<form>${templates}${'<section data-jfb-page></section>'.repeat(pages)}${fields}</form>`;
    return document.querySelector('form') as HTMLFormElement;
  };

  it('a form of one page with its pad', () => {
    expect(takesUploads(form(FIELD()), MODEL)).toBe(true);
  });

  it('not a form of several pages, which the server pages until 6.5d', () => {
    expect(takesUploads(form(FIELD(), 2), MODEL)).toBe(false);
  });

  it.each([
    ['a repeater, whose Add posts', '<div class="jfb-repeater"></div>'],
    ['a ranking, whose moves post', '<ol class="jfb-ranking"></ol>'],
  ])('not a form with %s, until 6.4o', (_name, markup) => {
    expect(takesUploads(form(FIELD() + markup), MODEL)).toBe(false);
  });

  it("not a form view that writes none of the send's parts, which cannot go from the page", () => {
    expect(takesUploads(form(FIELD(), 1, '<template data-jfb-templates></template>'), MODEL)).toBe(false);
    expect(takesUploads(form(FIELD(), 1, ''), MODEL)).toBe(false);
  });

  it('not a view that writes no pad, whose typed name only a post can draw', () => {
    expect(takesUploads(form('<div data-jfb-field="k-sign"><input name="sign.typed"></div>'), MODEL)).toBe(false);
  });
});
