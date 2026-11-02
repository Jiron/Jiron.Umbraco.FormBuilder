// Uploads on the page (ROADMAP 6.4m, ADR-0152): each file checked before anything is sent, its progress and states, the
// token as the field's answer, what the server refuses, a stopped upload's "Try again", the settings' say on thumbnails,
// and what a send waits for.
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import { wordsFor } from './messages';
import type { FormModel, ModelField } from './model';
import { entriesOf } from './page';
import { ENGLISH } from './testing';
import { ON_ITS_WAY, takesUploads } from './uploads';
import { clearFieldError, showsErrors } from './display';
import { enhanceUploads } from './uploads-chunk';

const PDF = [0x25, 0x50, 0x44, 0x46, 0x2d, 0x31];
const PNG = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];
const PROGRAM = [0x4d, 0x5a, 0x90, 0x00];

const FILE: ModelField = {
  key: 'k-file',
  type: 'file',
  alias: 'plan',
  settings: { types: ['pdf', 'images'], maxBytes: 1000, maxFiles: 2 },
  label: 'Plan',
};

const modelOf = (fields: ModelField[] = [FILE]): FormModel => ({
  alias: 'hire',
  revision: 1,
  culture: 'en-US',
  timeZone: 'UTC',
  open: true,
  form: { key: 'f', settings: {}, pages: [{ key: 'p1', groups: [{ key: 'g1', fields }] }] },
  fields: Object.fromEntries(
    fields.map((each) => [
      each.key,
      {
        kind: each.type === 'file' || each.type === 'signature' ? 'files' : 'text',
        canBeRequired: true,
        builtIn: true,
      },
    ]),
  ),
  consents: {},
  ...ENGLISH,
});

const FIELD = (
  attributes = 'data-jfb-dropzone data-jfb-thumbnails',
  accept = '.pdf,.png',
  multiple = 'multiple',
): string =>
  `<div data-jfb-field="k-file" data-jfb-alias="plan" data-jfb-type="file"><div class="jfb-upload" ${attributes}>` +
  `<div class="jfb-upload-actions"><input type="file" id="f1" name="plan" accept="${accept}" ${multiple}>` +
  '<label for="f1">Choose files</label><input type="file" class="jfb-upload-camera" id="f1-camera" accept="image/*"></div></div>' +
  '<template data-jfb-file data-jfb-list="Your files" data-jfb-uploading="{0}% uploaded" data-jfb-uploaded="{0} is uploaded." ' +
  'data-jfb-remove="Remove {0}" data-jfb-stop="Stop uploading {0}" data-jfb-again=" to upload {0}" ' +
  'data-jfb-stopped="It stopped." data-jfb-later="Try later." data-jfb-kb="{0} KB" data-jfb-mb="{0} MB">' +
  '<ul class="jfb-files" aria-label="Your files"></ul><li class="jfb-file"><span class="jfb-icon jfb-icon-file"></span><strong></strong>' +
  '<p class="jfb-help"></p><p class="jfb-error"></p><progress max="100" value="0"></progress>' +
  '<button type="button" data-jfb-retry>Try again<span class="jfb-visually-hidden"></span></button>' +
  '<button type="button" data-jfb-drop-file><span class="jfb-visually-hidden"></span></button></li></template></div>';

/** A request as the page makes it, which a test answers. */
class FakeRequest {
  static made: FakeRequest[] = [];
  readonly upload = new EventTarget();
  readonly listeners = new Map<string, () => void>();
  url = '';
  body: FormData | null = null;
  aborted = false;
  status = 0;
  responseText = '';

  constructor() {
    FakeRequest.made.push(this);
  }

  addEventListener(type: string, listener: () => void): void {
    this.listeners.set(type, listener);
  }

  open(_method: string, url: string): void {
    this.url = url;
  }

  send(body: FormData): void {
    this.body = body;
  }

  abort(): void {
    this.aborted = true;
    this.listeners.get('abort')?.();
  }

  answer(status: number, body: unknown): void {
    this.status = status;
    this.responseText = JSON.stringify(body);
    this.listeners.get('load')?.();
  }

  progress(loaded: number, total: number): void {
    this.upload.dispatchEvent(Object.assign(new Event('progress'), { lengthComputable: true, loaded, total }));
  }

  fail(): void {
    this.listeners.get('error')?.();
  }
}

const fileOf = (name: string, bytes: number[], type = 'application/pdf', size?: number): File => {
  const file = new File([new Uint8Array(bytes)], name, { type });
  if (size !== undefined) Object.defineProperty(file, 'size', { value: size });
  // A test's browser reads no bytes of a Blob's slice, so the page's read gives them.
  Object.defineProperty(file, 'slice', {
    value: () => ({ arrayBuffer: () => Promise.resolve(new Uint8Array(bytes).buffer) }),
  });
  return file;
};

function wake(
  field = FIELD(),
  model = modelOf(),
  restored?: ReadonlyMap<string, readonly { token: string; name: string; size: number }[]>,
  challenge: () => Promise<string | null> = () => Promise.resolve('c1'),
) {
  document.body.innerHTML = `<form id="jfb-1" data-jfb-form="f"><section data-jfb-page="p1">${field}</section><div data-jfb-live aria-live="polite"></div></form>`;
  const form = document.querySelector('form') as HTMLFormElement;
  const renew = vi.fn(() => Promise.resolve('renewed'));
  const uploads = enhanceUploads(form, model, {
    api: '/jfb/api/v1',
    challenge,
    renew,
    ...(restored === undefined ? {} : { restored }),
  });
  const input = form.querySelector('input[name="plan"]') as HTMLInputElement;
  const choose = (...files: File[]): void => {
    Object.defineProperty(input, 'files', { value: files, configurable: true });
    input.dispatchEvent(new Event('change', { bubbles: true }));
  };
  return { form, uploads, input, choose, renew };
}

const until = (check: () => void): Promise<void> => vi.waitFor(check, { timeout: 2000, interval: 2 });
const row = (at = 0): HTMLElement => document.querySelectorAll<HTMLElement>('.jfb-file')[at] as HTMLElement;
const liveText = (): string => document.querySelector('[data-jfb-live]')?.textContent ?? '';

beforeAll(async () => {
  await wordsFor('en-US');
});

beforeEach(() => {
  FakeRequest.made = [];
  vi.stubGlobal('XMLHttpRequest', FakeRequest);
});

afterEach(() => {
  vi.unstubAllGlobals();
  document.body.innerHTML = '';
});

describe('a field on the page', () => {
  it('shows it is enhanced, with no list until a file comes', () => {
    wake();

    expect(document.querySelector('.jfb-upload')?.hasAttribute('data-jfb-enhanced')).toBe(true);
    expect(document.querySelector('.jfb-files')).toBeNull();
  });

  it('leaves a field alone whose view writes no template', () => {
    wake(FIELD().replace(/<template[\s\S]*<\/template>/, ''));

    expect(document.querySelector('.jfb-upload')?.hasAttribute('data-jfb-enhanced')).toBe(false);
  });
});

describe('a file that may go', () => {
  it('goes to the uploads endpoint, the challenge first, with its progress, and its token is the answer', async () => {
    const { form, uploads, choose, input } = wake();
    choose(fileOf('plan.pdf', PDF, 'application/pdf', 500));
    await until(() => expect(FakeRequest.made[0]?.body).toBeInstanceOf(FormData));
    const request = FakeRequest.made[0] as FakeRequest;

    expect(request.url).toBe('/jfb/api/v1/forms/f/uploads?fieldKey=k-file');
    expect([...(request.body as FormData).keys()]).toEqual(['challenge', 'file']);
    expect((request.body as FormData).get('challenge')).toBe('c1');
    expect(input.value, 'the post carries no file twice').toBe('');
    expect(row().hasAttribute('data-jfb-busy')).toBe(true);
    expect(row().querySelector('strong')?.textContent).toBe('plan.pdf');
    expect(row().querySelector('.jfb-help')?.textContent).toBe('0% uploaded');
    expect(row().querySelector('[data-jfb-drop-file]')?.textContent).toBe('Stop uploading plan.pdf');
    expect(row().querySelector('[data-jfb-retry]')?.hasAttribute('hidden')).toBe(true);

    request.progress(250, 500);
    expect(row().querySelector('progress')?.value).toBe(50);
    expect(row().querySelector('.jfb-help')?.textContent).toBe('50% uploaded');

    request.answer(200, { token: 'tok-1', name: 'plan.pdf', size: 500 });
    expect(row().hasAttribute('data-jfb-busy')).toBe(false);
    expect(row().querySelector('.jfb-help')?.textContent).toBe('0.5 KB');
    expect(row().querySelector('progress')?.hasAttribute('hidden')).toBe(true);
    expect(row().querySelector('[data-jfb-drop-file]')?.textContent).toBe('Remove plan.pdf');
    expect(entriesOf(form)).toContainEqual(['plan', 'tok-1']);
    expect(liveText()).toBe('plan.pdf is uploaded.');
    expect([...uploads.uploaded()]).toEqual([['plan', [{ token: 'tok-1', name: 'plan.pdf', size: 500 }]]]);
  });

  it('shows a size in the larger unit from a mebibyte', async () => {
    const model = modelOf([{ ...FILE, settings: {} }]);
    const { choose } = wake(FIELD(), model);
    choose(fileOf('big.pdf', PDF, 'application/pdf', 3 * 1024 * 1024));
    await until(() => expect(FakeRequest.made[0]?.body).toBeInstanceOf(FormData));
    FakeRequest.made[0]?.answer(200, { token: 't', size: 3 * 1024 * 1024 });

    expect(row().querySelector('.jfb-help')?.textContent).toBe('3 MB');
  });

  it('holds a send until it is done', async () => {
    const { uploads, choose } = wake();
    choose(fileOf('plan.pdf', PDF));
    await until(() => expect(FakeRequest.made[0]?.body).toBeInstanceOf(FormData));
    let settled = false;
    void uploads.settled().then(() => (settled = true));
    await new Promise((resolve) => setTimeout(resolve, 5));
    expect(settled).toBe(false);

    FakeRequest.made[0]?.answer(200, { token: 't' });
    await until(() => expect(settled).toBe(true));
  });

  it('asks for a new challenge once when its own has expired, and sends again', async () => {
    const { choose, renew } = wake();
    choose(fileOf('plan.pdf', PDF));
    await until(() => expect(FakeRequest.made[0]?.body).toBeInstanceOf(FormData));
    FakeRequest.made[0]?.answer(401, { code: 'challenge_required' });
    await until(() => expect(FakeRequest.made[1]?.body).toBeInstanceOf(FormData));

    expect(renew).toHaveBeenCalledTimes(1);
    FakeRequest.made[1]?.answer(401, { code: 'challenge_required' });
    expect(renew, 'once').toHaveBeenCalledTimes(1);
    expect(row().hasAttribute('data-jfb-failed')).toBe(true);
  });

  it('takes several, in their own rows', async () => {
    const { choose } = wake();
    choose(fileOf('a.pdf', PDF), fileOf('b.pdf', PDF));
    await until(() => expect(FakeRequest.made).toHaveLength(2));

    expect(document.querySelectorAll('.jfb-files')).toHaveLength(1);
    expect(document.querySelectorAll('.jfb-file')).toHaveLength(2);
  });

  it('replaces the one file a field for one holds', async () => {
    const { choose } = wake(FIELD(undefined, undefined, ''), modelOf([{ ...FILE, settings: { maxFiles: 1 } }]));
    choose(fileOf('a.pdf', PDF));
    await until(() => expect(FakeRequest.made[0]?.body).toBeInstanceOf(FormData));
    choose(fileOf('b.pdf', PDF));
    await until(() => expect(row().querySelector('strong')?.textContent).toBe('b.pdf'));

    expect(FakeRequest.made[0]?.aborted, 'the first stops').toBe(true);
    expect(document.querySelectorAll('.jfb-file')).toHaveLength(1);
  });

  it('reads its own input, wherever a view writes the camera', async () => {
    const camera = '<input type="file" class="jfb-upload-camera" id="f1-camera" accept="image/*">';
    const field = FIELD().replace(camera, '').replace('<div class="jfb-upload-actions">', `$&${camera}`);
    const { choose } = wake(field);
    choose(fileOf('plan.pdf', PDF));
    await until(() => expect(FakeRequest.made[0]?.body).toBeInstanceOf(FormData));
    FakeRequest.made[0]?.answer(200, { token: 't1', name: 'plan.pdf', size: 9 });

    await until(() => expect(row().querySelector('input[type="hidden"]')?.getAttribute('name')).toBe('plan'));
  });

  it('empties the input it read, so the same file can be chosen again and a post never holds it', () => {
    const { choose, input } = wake();
    const set = vi.fn();
    Object.defineProperty(input, 'value', { set, get: () => '', configurable: true });
    choose(fileOf('plan.pdf', PDF));

    expect(set).toHaveBeenCalledWith('');
  });

  it('shows no more than 99% before the server answers, and nothing of a length it cannot know', async () => {
    const { choose } = wake();
    choose(fileOf('plan.pdf', PDF));
    await until(() => expect(FakeRequest.made[0]?.body).toBeInstanceOf(FormData));
    FakeRequest.made[0]?.upload.dispatchEvent(
      Object.assign(new Event('progress'), { lengthComputable: false, loaded: 1, total: 0 }),
    );
    expect(row().querySelector('.jfb-help')?.textContent).toBe('0% uploaded');

    FakeRequest.made[0]?.progress(9, 9);
    expect(row().querySelector('.jfb-help')?.textContent).toBe('99% uploaded');
  });

  it('sends no challenge when none came, for the server to answer', async () => {
    const { choose } = wake(undefined, undefined, undefined, () => Promise.resolve(null));
    choose(fileOf('plan.pdf', PDF));
    await until(() => expect(FakeRequest.made[0]?.body).toBeInstanceOf(FormData));

    expect(FakeRequest.made[0]?.body?.has('challenge')).toBe(false);
  });

  it('holds a send from the moment it is chosen, past the check and the upload', async () => {
    const { choose, uploads } = wake();
    choose(fileOf('plan.pdf', PDF));
    let settled = false;
    void uploads.settled().then(() => {
      settled = true;
    });
    await until(() => expect(FakeRequest.made[0]?.body).toBeInstanceOf(FormData));
    await new Promise((resolve) => setTimeout(resolve, 10));
    expect(settled).toBe(false);

    FakeRequest.made[0]?.answer(200, { token: 't1', name: 'plan.pdf', size: 9 });
    await until(() => expect(settled).toBe(true));
  });

  it('takes a file from the camera too', async () => {
    const { form } = wake();
    const camera = form.querySelector('.jfb-upload-camera') as HTMLInputElement;
    Object.defineProperty(camera, 'files', { value: [fileOf('shot.png', PNG, 'image/png')], configurable: true });
    camera.dispatchEvent(new Event('change', { bubbles: true }));

    await until(() => expect(FakeRequest.made[0]?.body).toBeInstanceOf(FormData));
  });
});

describe('a file checked before it is sent', () => {
  it.each([
    ['too large', fileOf('big.pdf', PDF, 'application/pdf', 1001)],
    ['of a type the field does not take', fileOf('notes.dat', PDF)],
    ['a program renamed', fileOf('invoice.pdf', PROGRAM)],
    ['bytes of another kind', fileOf('picture.png', PDF, 'image/png')],
    [
      'a page behind another name',
      fileOf('photo.png', [...PNG, ...[...'<script>'].map((c) => c.charCodeAt(0))], 'image/png'),
    ],
  ])('%s, sends nothing and says why', async (_name, file) => {
    const { choose, form } = wake();
    choose(file);
    await until(() => expect(row().hasAttribute('data-jfb-failed')).toBe(true));

    expect(FakeRequest.made).toEqual([]);
    expect(row().querySelector('.jfb-error')?.textContent).not.toBe('');
    expect(liveText()).toContain(file.name);
    expect(row().querySelector('[data-jfb-retry]')?.hasAttribute('hidden'), 'nothing to try again').toBe(true);
    expect(entriesOf(form).filter(([name]) => name === 'plan')).toEqual([]);
    expect(row().querySelector('[data-jfb-drop-file]')?.textContent).toBe(`Remove ${file.name}`);
  });

  it('takes a program by its bytes, under an extension that names no kind', async () => {
    const { choose } = wake(FIELD(undefined, '.dat'));
    choose(fileOf('tool.dat', PROGRAM));
    await until(() => expect(row().hasAttribute('data-jfb-failed')).toBe(true));

    expect(FakeRequest.made).toEqual([]);
  });

  it("reads as many of a file's first bytes as the server", async () => {
    const { choose } = wake();
    const file = new File([new Uint8Array(PDF)], 'plan.pdf');
    const slice = vi.fn(() => ({ arrayBuffer: () => Promise.resolve(new Uint8Array(PDF).buffer) }));
    Object.defineProperty(file, 'slice', { value: slice });
    choose(file);
    await until(() => expect(FakeRequest.made).toHaveLength(1));

    expect(slice).toHaveBeenCalledWith(0, 512);
  });

  it('takes a file as large as the limit', async () => {
    const { choose } = wake();
    choose(fileOf('full.pdf', PDF, 'application/pdf', 1000));
    await until(() => expect(FakeRequest.made).toHaveLength(1));
  });

  it('takes any extension on a field that names none', async () => {
    const { choose } = wake(FIELD(undefined, ''));
    choose(fileOf('anything.dat', PDF));
    await until(() => expect(FakeRequest.made).toHaveLength(1));
  });

  it('counts no file already refused', async () => {
    const { choose } = wake();
    choose(fileOf('a.exe.pdf', [0x4d, 0x5a, 0x90, 0]));
    await until(() => expect(row().hasAttribute('data-jfb-failed')).toBe(true));
    choose(fileOf('b.pdf', PDF), fileOf('c.pdf', PDF));
    await until(() => expect(FakeRequest.made).toHaveLength(2));

    expect(row(1).hasAttribute('data-jfb-failed')).toBe(false);
    expect(row(2).hasAttribute('data-jfb-failed')).toBe(false);
  });

  it('counts how many the field takes', async () => {
    const { choose } = wake();
    choose(fileOf('a.pdf', PDF), fileOf('b.pdf', PDF), fileOf('c.pdf', PDF));
    await until(() => expect(row(2).hasAttribute('data-jfb-failed')).toBe(true));

    expect(FakeRequest.made).toHaveLength(2);
  });
});

describe('what the server answers', () => {
  it.each([
    [429, 'Try later.'],
    [503, 'Try later.'],
  ])('%i says to try later, with no retry at once', async (status, text) => {
    const { choose } = wake();
    choose(fileOf('plan.pdf', PDF));
    await until(() => expect(FakeRequest.made[0]?.body).toBeInstanceOf(FormData));
    FakeRequest.made[0]?.answer(status, {});
    await until(() => expect(row().querySelector('.jfb-error')?.textContent).toBe(text));

    expect(row().querySelector('[data-jfb-retry]')?.hasAttribute('hidden')).toBe(true);
    expect(liveText()).toBe(`plan.pdf: ${text}`);
  });

  it('a 200 that holds no token is a failed upload', async () => {
    const { choose } = wake();
    choose(fileOf('plan.pdf', PDF));
    await until(() => expect(FakeRequest.made[0]?.body).toBeInstanceOf(FormData));
    FakeRequest.made[0]?.answer(200, {});

    expect(row().querySelector('.jfb-error')?.textContent).toBe('It stopped.');
    expect(row().querySelector('input[type="hidden"]')).toBeNull();
  });

  it.each([
    [413, 'fileSize', /KB/],
    [415, 'fileType', /PDF/],
  ])("%i words the field's own error", async (status, code, words) => {
    const { choose } = wake();
    choose(fileOf('plan.pdf', PDF));
    await until(() => expect(FakeRequest.made[0]?.body).toBeInstanceOf(FormData));
    FakeRequest.made[0]?.answer(status, { errors: [{ code, path: 'file' }] });
    await until(() => expect(row().hasAttribute('data-jfb-failed')).toBe(true));
    await until(() => expect(row().querySelector('.jfb-error')?.textContent).toMatch(words));

    expect(row().querySelector('[data-jfb-retry]')?.hasAttribute('hidden')).toBe(true);
  });

  it('a request cut off is a failed upload the visitor can try again, by the same row', async () => {
    const { choose, form } = wake();
    choose(fileOf('plan.pdf', PDF));
    await until(() => expect(FakeRequest.made[0]?.body).toBeInstanceOf(FormData));
    FakeRequest.made[0]?.fail();

    expect(row().hasAttribute('data-jfb-failed')).toBe(true);
    expect(row().querySelector('.jfb-error')?.textContent).toBe('It stopped.');
    const retry = row().querySelector<HTMLButtonElement>('[data-jfb-retry]') as HTMLButtonElement;
    expect(retry.hasAttribute('hidden')).toBe(false);
    expect(retry.textContent).toBe('Try again to upload plan.pdf');
    expect(liveText()).toBe('plan.pdf: It stopped.');

    retry.click();
    await until(() => expect(FakeRequest.made[1]?.body).toBeInstanceOf(FormData));
    expect(row().hasAttribute('data-jfb-failed')).toBe(false);
    expect(document.activeElement, 'the focus stays in the row').toBe(row().querySelector('[data-jfb-drop-file]'));
    FakeRequest.made[1]?.answer(200, { token: 'again' });
    expect(entriesOf(form)).toContainEqual(['plan', 'again']);
  });

  it('a server error is a failed upload too', async () => {
    const { choose } = wake();
    choose(fileOf('plan.pdf', PDF));
    await until(() => expect(FakeRequest.made[0]?.body).toBeInstanceOf(FormData));
    FakeRequest.made[0]?.answer(500, {});

    expect(row().querySelector('.jfb-error')?.textContent).toBe('It stopped.');
  });
});

describe('what the page reads while a file is on its way', () => {
  const answers = (form: HTMLFormElement): string[] =>
    [...form.querySelectorAll<HTMLInputElement>('input[type="hidden"][name="plan"]')].map((each) => each.value);

  it('counts the field as answered from the moment it is chosen, and tells the rules and the errors each time', async () => {
    const { choose, form } = wake();
    const heard = vi.fn();
    form.addEventListener('input', heard);
    choose(fileOf('plan.pdf', PDF));

    expect(answers(form)).toEqual([ON_ITS_WAY]);
    expect(heard).toHaveBeenCalled();
    await until(() => expect(FakeRequest.made[0]?.body).toBeInstanceOf(FormData));
    heard.mockClear();
    FakeRequest.made[0]?.answer(200, { token: 't1', name: 'plan.pdf', size: 9 });

    expect(answers(form)).toEqual(['t1']);
    expect(heard).toHaveBeenCalled();
  });

  it.each([
    ['refused by the page', () => fileOf('invoice.pdf', PROGRAM), null],
    ['cut off', () => fileOf('plan.pdf', PDF), 500],
    ['refused by the server', () => fileOf('plan.pdf', PDF), 415],
    ['refused for now', () => fileOf('plan.pdf', PDF), 503],
  ])('holds nothing once it is %s, and tells them', async (_name, file, status) => {
    const { choose, form } = wake();
    choose(file());
    if (status !== null) {
      await until(() => expect(FakeRequest.made[0]?.body).toBeInstanceOf(FormData));
      FakeRequest.made[0]?.answer(status, { errors: [{ code: 'fileType' }] });
    }
    await until(() => expect(answers(form)).toEqual([]));
    await until(() => expect(row().hasAttribute('data-jfb-failed')).toBe(true));
  });

  it('counts it again while it is tried again', async () => {
    const { choose, form } = wake();
    choose(fileOf('plan.pdf', PDF));
    await until(() => expect(FakeRequest.made[0]?.body).toBeInstanceOf(FormData));
    FakeRequest.made[0]?.fail();
    expect(answers(form)).toEqual([]);
    row().querySelector<HTMLElement>('[data-jfb-retry]')?.click();

    expect(answers(form)).toEqual([ON_ITS_WAY]);
  });

  it('tells them when a file goes off the list', async () => {
    const { choose, form } = wake();
    choose(fileOf('plan.pdf', PDF));
    await until(() => expect(FakeRequest.made[0]?.body).toBeInstanceOf(FormData));
    const heard = vi.fn();
    form.addEventListener('input', heard);
    row().querySelector<HTMLElement>('[data-jfb-drop-file]')?.click();

    expect(answers(form)).toEqual([]);
    expect(heard).toHaveBeenCalled();
  });
});

describe("a failed row beside the field's own errors", () => {
  it('is no error of the field, which a check would take away with its row', async () => {
    const { choose, form } = wake();
    choose(fileOf('plan.pdf', PDF));
    await until(() => expect(FakeRequest.made[0]?.body).toBeInstanceOf(FormData));
    FakeRequest.made[0]?.fail();

    expect(showsErrors(form)).toBe(false);
    clearFieldError(form.querySelector('[data-jfb-field]') as HTMLElement);
    expect(row()).toBeDefined();
    expect(row().isConnected).toBe(true);
  });
});

describe('a send with a file that failed', () => {
  it('stops at a file that can be tried again, whose retry takes the focus, and says why', async () => {
    const { choose, uploads } = wake();
    choose(fileOf('plan.pdf', PDF));
    await until(() => expect(FakeRequest.made[0]?.body).toBeInstanceOf(FormData));
    FakeRequest.made[0]?.fail();

    await expect(uploads.settled()).resolves.toBe(false);
    expect(document.activeElement).toBe(row().querySelector('[data-jfb-retry]'));
    expect(liveText()).toBe('plan.pdf: It stopped.');
  });

  it('stops at a refused one, whose only way is off the list', async () => {
    const { choose, uploads } = wake();
    choose(fileOf('invoice.pdf', PROGRAM));

    await expect(uploads.settled()).resolves.toBe(false);
    expect(document.activeElement).toBe(row().querySelector('[data-jfb-drop-file]'));
  });

  it('goes on once it is off the list', async () => {
    const { choose, uploads } = wake();
    choose(fileOf('invoice.pdf', PROGRAM));
    await uploads.settled();
    row().querySelector<HTMLElement>('[data-jfb-drop-file]')?.click();

    await expect(uploads.settled()).resolves.toBe(true);
  });
});

describe('taking a file off the list', () => {
  it('stops one on its way, and moves the focus to the field', async () => {
    const { choose, input } = wake();
    choose(fileOf('plan.pdf', PDF));
    await until(() => expect(FakeRequest.made[0]?.body).toBeInstanceOf(FormData));
    row().querySelector<HTMLElement>('[data-jfb-drop-file]')?.click();

    expect(FakeRequest.made[0]?.aborted).toBe(true);
    expect(document.querySelector('.jfb-files'), 'the list goes with its last').toBeNull();
    expect(document.activeElement).toBe(input);
  });

  it('holds a send no longer', async () => {
    const { choose, uploads } = wake();
    choose(fileOf('plan.pdf', PDF));
    await until(() => expect(FakeRequest.made[0]?.body).toBeInstanceOf(FormData));
    row().querySelector<HTMLElement>('[data-jfb-drop-file]')?.click();

    await expect(uploads.settled()).resolves.toBe(true);
  });

  it('sends nothing of one taken off while its challenge is on its way', async () => {
    let give: (challenge: string) => void = () => undefined;
    const challenge = new Promise<string>((resolve) => {
      give = resolve;
    });
    const { choose } = wake(undefined, undefined, undefined, () => challenge);
    choose(fileOf('plan.pdf', PDF));
    await until(() => expect(FakeRequest.made).toHaveLength(1));
    row().querySelector<HTMLElement>('[data-jfb-drop-file]')?.click();
    give('c1');
    await new Promise((resolve) => setTimeout(resolve, 5));

    expect(FakeRequest.made[0]?.body).toBeNull();
  });

  it("takes a stored file's token with it", async () => {
    const { choose, form, uploads } = wake();
    choose(fileOf('plan.pdf', PDF));
    await until(() => expect(FakeRequest.made[0]?.body).toBeInstanceOf(FormData));
    FakeRequest.made[0]?.answer(200, { token: 'tok-1' });
    row().querySelector<HTMLElement>('[data-jfb-drop-file]')?.click();

    expect(entriesOf(form).filter(([name]) => name === 'plan')).toEqual([]);
    expect(uploads.uploaded().get('plan')).toEqual([]);
  });
});

describe('dropping a file', () => {
  const dragged = (type: string, files: File[] = []): Event =>
    Object.assign(new Event(type, { bubbles: true, cancelable: true }), { dataTransfer: { files } });

  it('takes the file, and shows the zone while it is over', async () => {
    wake();
    const zone = document.querySelector('.jfb-upload') as HTMLElement;
    const over = dragged('dragover');
    zone.dispatchEvent(over);
    expect(over.defaultPrevented).toBe(true);
    expect(zone.hasAttribute('data-jfb-drop')).toBe(true);
    zone.dispatchEvent(dragged('dragleave'));
    expect(zone.hasAttribute('data-jfb-drop')).toBe(false);

    zone.dispatchEvent(dragged('dragenter'));
    zone.dispatchEvent(dragged('drop', [fileOf('plan.pdf', PDF)]));
    expect(zone.hasAttribute('data-jfb-drop')).toBe(false);
    await until(() => expect(FakeRequest.made[0]?.body).toBeInstanceOf(FormData));
  });

  it('takes none on a field without a drop zone', () => {
    wake(FIELD(''));
    const zone = document.querySelector('.jfb-upload') as HTMLElement;
    const over = dragged('dragover');
    zone.dispatchEvent(over);

    expect(over.defaultPrevented).toBe(false);
    expect(zone.hasAttribute('data-jfb-drop')).toBe(false);
  });

  it('takes none on a field the rules turned off', () => {
    const { input } = wake();
    input.disabled = true;
    const zone = document.querySelector('.jfb-upload') as HTMLElement;
    const over = dragged('dragover');
    zone.dispatchEvent(over);
    const drop = dragged('drop');
    zone.dispatchEvent(drop);

    expect(over.defaultPrevented).toBe(false);
    expect(drop.defaultPrevented).toBe(false);
    expect(document.querySelector('.jfb-file')).toBeNull();
  });
});

describe('a thumbnail', () => {
  beforeEach(() => {
    vi.stubGlobal('URL', Object.assign(globalThis.URL, { createObjectURL: () => 'blob:x', revokeObjectURL: vi.fn() }));
  });

  it('shows for an image with the setting on', async () => {
    const { choose } = wake();
    choose(fileOf('shot.png', PNG, 'image/png'));
    await until(() => expect(FakeRequest.made[0]?.body).toBeInstanceOf(FormData));

    expect(row().querySelector('img')?.getAttribute('src')).toBe('blob:x');
    expect(row().querySelector('img')?.getAttribute('alt')).toBe('');
    expect(row().querySelector('.jfb-icon-file')).toBeNull();
  });

  it('gives its memory back when the file goes', async () => {
    const { choose } = wake();
    choose(fileOf('shot.png', PNG, 'image/png'));
    await until(() => expect(FakeRequest.made[0]?.body).toBeInstanceOf(FormData));
    row().querySelector<HTMLElement>('[data-jfb-drop-file]')?.click();

    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:x');
  });

  it('shows no picture with the setting off', async () => {
    const { choose } = wake(FIELD('data-jfb-dropzone'));
    choose(fileOf('shot.png', PNG, 'image/png'));
    await until(() => expect(FakeRequest.made[0]?.body).toBeInstanceOf(FormData));

    expect(row().querySelector('img')).toBeNull();
    expect(row().querySelector('.jfb-icon-file')).not.toBeNull();
  });

  it('shows no picture for a file that is none', async () => {
    const { choose } = wake();
    choose(fileOf('plan.pdf', PDF));
    await until(() => expect(FakeRequest.made[0]?.body).toBeInstanceOf(FormData));

    expect(row().querySelector('img')).toBeNull();
  });

  it('goes back to the icon where a policy takes no picture from memory', async () => {
    const { choose } = wake();
    choose(fileOf('shot.png', PNG, 'image/png'));
    await until(() => expect(FakeRequest.made[0]?.body).toBeInstanceOf(FormData));
    row().querySelector('img')?.dispatchEvent(new Event('error'));

    expect(row().querySelector('img')).toBeNull();
    expect(row().querySelector('.jfb-icon-file')).not.toBeNull();
  });
});

describe("a form that came in another one's place", () => {
  it('lists the files the server holds, with their tokens', () => {
    const { form, uploads } = wake(
      FIELD(),
      modelOf(),
      new Map([['plan', [{ token: 'kept', name: 'plan.pdf', size: 500 }]]]),
    );

    expect(row().querySelector('strong')?.textContent).toBe('plan.pdf');
    expect(entriesOf(form)).toContainEqual(['plan', 'kept']);
    expect(uploads.uploaded().get('plan')).toEqual([{ token: 'kept', name: 'plan.pdf', size: 500 }]);
    row().querySelector<HTMLElement>('[data-jfb-drop-file]')?.click();
    expect(entriesOf(form).filter(([name]) => name === 'plan')).toEqual([]);
  });
});

describe('which forms take their files on the page', () => {
  const formWith = (pages: number): HTMLFormElement => {
    document.body.innerHTML = `<form><template data-jfb-templates data-jfb-sending="Sending"></template>${'<section data-jfb-page="p"></section>'.repeat(pages)}</form>`;
    return document.querySelector('form') as HTMLFormElement;
  };

  it('one page of file fields', () => {
    expect(takesUploads(formWith(1), modelOf())).toBe(true);
  });

  it('not a form of several pages, which the server pages until 6.5d', () => {
    expect(takesUploads(formWith(2), modelOf())).toBe(false);
  });

  it('not a view that writes no template for a file, whose file only a post can hold', () => {
    const form = document.createElement('form');
    form.innerHTML =
      '<template data-jfb-templates data-jfb-sending="Sending"></template><div data-jfb-field="k-file"><input type="file" name="plan"></div>';

    expect(takesUploads(form, modelOf())).toBe(false);
  });

  it('not a form with a field of a kind that stores files and that the page cannot read', () => {
    const other: ModelField = { key: 'k-other', type: 'album', alias: 'album', settings: {} };
    const model = modelOf([FILE, other]);
    const stores = {
      ...model,
      fields: { ...model.fields, 'k-other': { kind: 'files', canBeRequired: true, builtIn: true } },
    } satisfies FormModel;
    expect(takesUploads(formWith(1), stores)).toBe(false);
  });

  it('not a form with no file field', () => {
    const text: ModelField = { key: 'k-text', type: 'text', alias: 'name', settings: {} };
    expect(takesUploads(formWith(1), modelOf([text]))).toBe(false);
  });
});
