/**
 * The signature pad (ROADMAP 6.4n, ADR-0153, ADR-0103), a chunk that only a form with a signature loads. A visitor signs on
 * the pad by pointer, touch or pen, or types a name instead, the keyboard's and a screen reader's way. Nothing goes until a
 * send: the page counts the field as answered through a hidden input, and the send's wait draws the PNG, sends it to the
 * `uploads` endpoint, the challenge first, and puts its token there. A typed name's PNG is drawn with the browser's fonts and
 * holds the name in a text chunk, as the server's does. The pad shows the strokes in the theme's text colour; the image
 * always holds the server's ink on nothing, so it reads the same on any ground.
 */
import { cleanName, pngWithName } from '@jfb/logic/signature';
import type { FormModel } from './model';
import { fieldsOf } from './page';
import { ON_ITS_WAY, type PageUploads, type UploadedFile, type UploadOptions } from './uploads';

const INK = '#1f2430';
const FILE_NAME = 'signature.png';
/** How long an image's upload may take before its send counts as one that got no answer. */
const WAIT = 40_000;

// The image of a name, as the server draws one (ADR-0103): the margin of the width, the baseline of the height, the cap
// height at best and at least of the height, and how far the image may widen for a long name.
const MARGIN = 0.06;
const BASELINE = 0.75;
const LARGEST_CAPS = 0.3;
const SMALLEST_CAPS = 0.15;
const MOST_WIDENING = 3;
/** A font's cap height, of its size, near enough for a size to choose. */
const CAPS_OF_SIZE = 0.7;

type Stroke = readonly number[];

/** Paints strokes, a pair of numbers per point, as curves through the middle of each two points. */
function paint(canvas: HTMLCanvasElement, strokes: readonly Stroke[], ink: string): void {
  const context = canvas.getContext('2d');
  if (context === null) return;
  context.clearRect(0, 0, canvas.width, canvas.height);
  context.strokeStyle = ink;
  context.lineWidth = Math.max(2, canvas.height / 80);
  context.lineCap = 'round';
  context.lineJoin = 'round';
  for (const stroke of strokes) {
    context.beginPath();
    context.moveTo(stroke[0] as number, stroke[1] as number);
    // A touch that never moved is a dot.
    if (stroke.length === 2) context.lineTo(stroke[0] as number, stroke[1] as number);
    for (let at = 2; at + 3 < stroke.length; at += 2) {
      const x = stroke[at] as number;
      const y = stroke[at + 1] as number;
      context.quadraticCurveTo(x, y, (x + (stroke[at + 2] as number)) / 2, (y + (stroke[at + 3] as number)) / 2);
    }
    if (stroke.length > 2) context.lineTo(stroke[stroke.length - 2] as number, stroke[stroke.length - 1] as number);
    context.stroke();
  }
}

/**
 * A name on a canvas of the pad's size, as the server draws one: a long name smaller, then on a wider image, then smaller
 * again.
 */
export function canvasOfName(
  document: Document,
  name: string,
  width: number,
  height: number,
  family: string,
): HTMLCanvasElement {
  const canvas = document.createElement('canvas');
  canvas.width = width;
  canvas.height = height;
  const context = canvas.getContext('2d');
  if (context === null) return canvas;
  const fontOf = (size: number): string => `italic ${String(size)}px ${family}`;
  const largest = (LARGEST_CAPS * height) / CAPS_OF_SIZE;
  const smallest = (SMALLEST_CAPS * height) / CAPS_OF_SIZE;
  context.font = fontOf(largest);
  const long = context.measureText(name).width;
  const margins = 2 * MARGIN * width;
  let size = Math.min(largest, (largest * (width - margins)) / long);
  if (size < smallest) {
    const widest = MOST_WIDENING * width;
    const needed = Math.ceil((long * smallest) / largest + margins);
    canvas.width = Math.min(needed, widest);
    size = needed > widest ? (largest * (widest - margins)) / long : smallest;
  }
  context.font = fontOf(size);
  context.fillStyle = INK;
  context.fillText(name, MARGIN * width, BASELINE * height);
  return canvas;
}

const pngOf = (canvas: HTMLCanvasElement): Promise<Uint8Array> =>
  new Promise<Blob | null>((resolve) => {
    canvas.toBlob(resolve, 'image/png');
  }).then(async (blob) => {
    if (blob === null) throw new Error('The browser draws no picture.');
    return new Uint8Array(await blob.arrayBuffer());
  });

/** Enhances each signature of the form: its pad, its tools and the way between drawing and typing. */
export function enhanceSignatures(form: HTMLFormElement, model: FormModel, options: UploadOptions): PageUploads {
  const key = form.dataset['jfbForm'] ?? '';
  const fields = fieldsOf(model).flatMap((field) => [field, ...(field.fields ?? [])]);
  const sends: (() => Promise<void>)[] = [];
  const held: (() => readonly [string, UploadedFile] | null)[] = [];

  /** Sends an image; a challenge that has expired is renewed once. A refusal rejects with what the send's notice words. */
  async function upload(fieldKey: string, image: Uint8Array, renewed = false): Promise<string> {
    const challenge = await options.challenge();
    const body = new FormData();
    if (challenge !== null) body.append('challenge', challenge);
    body.append('file', new Blob([image as BlobPart], { type: 'image/png' }), FILE_NAME);
    const response = await fetch(`${options.api}/forms/${key}/uploads?fieldKey=${encodeURIComponent(fieldKey)}`, {
      method: 'POST',
      body,
      signal: AbortSignal.timeout(WAIT),
    });
    if (response.status === 401 && !renewed) {
      await options.renew();
      return upload(fieldKey, image, true);
    }
    const token = response.ok ? ((await response.json()) as { token?: string }).token : undefined;
    if (token === undefined) {
      throw { status: response.status, retryAfter: response.headers.get('Retry-After') };
    }
    return token;
  }

  const enhance = (box: HTMLElement): void => {
    const host = box.closest<HTMLElement>('[data-jfb-field]');
    const field = fields.find((each) => each.key === host?.dataset['jfbField']);
    const canvas = box.querySelector('canvas');
    const drawnWay = box.querySelector<HTMLElement>('[data-jfb-drawn]');
    const typedWay = box.querySelector<HTMLElement>('[data-jfb-typed]');
    const typed = typedWay?.querySelector('input');
    const clear = box.querySelector<HTMLButtonElement>('[data-jfb-clear]');
    const toTyped = box.querySelector<HTMLButtonElement>('[data-jfb-to-typed]');
    const toDrawn = box.querySelector<HTMLButtonElement>('[data-jfb-to-drawn]');
    if (!host || field === undefined || !canvas || !drawnWay || !typedWay || !typed || !clear || !toTyped || !toDrawn)
      return;
    // In an attribute, as a row added or moved in place names its controls anew.
    typed.dataset['jfbName'] = typed.name;
    const typedName = (): string => typed.dataset['jfbName'] ?? '';
    const name = (): string => typedName().replace(/\.typed$/, '');
    const clearWord = clear.textContent;
    const restored = options.restored?.get(name())?.[0];
    let strokes: number[][] = restored?.strokes?.map((stroke) => [...stroke]) ?? [];
    let cleared: number[][] | null = null;
    // A visitor who came to the name, the first thing the page shows without its runtime, stays there.
    let drawing = typed.value.trim() === '' && form.ownerDocument.activeElement !== typed;
    let token: string | null = restored?.token ?? null;
    let answer: HTMLInputElement | null = null;
    let pointer: number | null = null;
    let current: number[] = [];
    let size = restored?.size ?? 0;
    let changes = 0;

    const show = (): void => {
      paint(canvas, strokes, getComputedStyle(canvas).color || INK);
      clear.hidden = strokes.length === 0 && cleared === null;
      clear.textContent = cleared === null ? clearWord : (clear.dataset['jfbUndo'] ?? clearWord);
      drawnWay.hidden = !drawing;
      typedWay.hidden = drawing;
      // Only the way on show answers: a name typed before stays in its input, but the page does not read it as one.
      if (drawing) typed.removeAttribute('name');
      else typed.name = typedName();
    };

    /** What the page reads as the field's answer: the token, or that one is on its way. */
    const sync = (): void => {
      const answered = drawing ? strokes.length > 0 : cleanName(typed.value) !== null;
      if (!answered) {
        answer?.remove();
        answer = null;
        return;
      }
      if (answer === null) {
        answer = form.ownerDocument.createElement('input');
        answer.type = 'hidden';
        box.append(answer);
      }
      answer.name = name();
      answer.value = token ?? ON_ITS_WAY;
    };

    /** The signature changed: the image sent, if any, is no more its answer, and the rules and the errors look again. */
    const changed = (): void => {
      token = null;
      changes += 1;
      sync();
      box.dispatchEvent(new Event('input', { bubbles: true }));
    };

    const pointOf = (event: PointerEvent): readonly [number, number] => {
      const room = canvas.getBoundingClientRect();
      return [
        ((event.clientX - room.left) * canvas.width) / (room.width || canvas.width),
        ((event.clientY - room.top) * canvas.height) / (room.height || canvas.height),
      ];
    };

    canvas.addEventListener('pointerdown', (event) => {
      // A second finger draws nothing, and only the main button of a mouse does.
      if (pointer !== null || event.button !== 0) return;
      event.preventDefault();
      pointer = event.pointerId;
      canvas.setPointerCapture?.(pointer);
      current = [...pointOf(event)];
      strokes.push(current);
      cleared = null;
      show();
    });
    canvas.addEventListener('pointermove', (event) => {
      if (event.pointerId !== pointer) return;
      const moves = event.getCoalescedEvents?.() ?? [];
      for (const each of moves.length > 0 ? moves : [event]) current.push(...pointOf(each));
      show();
    });
    const end = (event: PointerEvent): void => {
      if (event.pointerId !== pointer) return;
      pointer = null;
      changed();
    };
    canvas.addEventListener('pointerup', end);
    canvas.addEventListener('pointercancel', end);

    clear.addEventListener('click', () => {
      if (cleared === null) {
        cleared = strokes;
        strokes = [];
      } else {
        strokes = cleared;
        cleared = null;
      }
      show();
      changed();
    });
    toTyped.addEventListener('click', () => {
      drawing = false;
      show();
      changed();
      typed.focus();
    });
    toDrawn.addEventListener('click', () => {
      drawing = true;
      show();
      changed();
      (clear.hidden ? toTyped : clear).focus();
    });
    typed.addEventListener('input', () => {
      token = null;
      changes += 1;
      sync();
    });

    sends.push(async () => {
      // What changes while an image is on its way is what the visitor means: that goes instead.
      for (;;) {
        if (answer === null || token !== null || !box.isConnected || host.closest('[data-jfb-hidden]') !== null) return;
        const at = changes;
        let image: Uint8Array;
        if (drawing) {
          const clean = form.ownerDocument.createElement('canvas');
          clean.width = canvas.width;
          clean.height = canvas.height;
          paint(clean, strokes, INK);
          image = await pngOf(clean);
        } else {
          const written = cleanName(typed.value) as string;
          const family = getComputedStyle(typed).fontFamily || 'sans-serif';
          const picture = canvasOfName(form.ownerDocument, written, canvas.width, canvas.height, family);
          image = pngWithName(await pngOf(picture), new TextEncoder().encode(written));
        }
        const sent = await upload(field.key, image);
        if (at === changes) {
          size = image.length;
          token = sent;
          sync();
        }
      }
    });
    held.push(() =>
      drawing && token !== null && box.isConnected ? [name(), { token, name: FILE_NAME, size, strokes }] : null,
    );

    sync();
    show();
    box.setAttribute('data-jfb-enhanced', '');
    // The rules decided before the field could answer.
    if (answer !== null) box.dispatchEvent(new Event('input', { bubbles: true }));
  };

  for (const box of form.querySelectorAll<HTMLElement>('.jfb-signature')) enhance(box);

  return {
    async settled(): Promise<boolean> {
      await Promise.all(sends.map((each) => each()));
      return true;
    },
    uploaded(): ReadonlyMap<string, readonly UploadedFile[]> {
      const files = held.flatMap((each) => {
        const file = each();
        return file === null ? [] : [file];
      });
      return new Map(files.map(([name, file]) => [name, [file]]));
    },
    add(root: Element): void {
      for (const box of root.querySelectorAll<HTMLElement>('.jfb-signature')) enhance(box);
    },
  };
}
