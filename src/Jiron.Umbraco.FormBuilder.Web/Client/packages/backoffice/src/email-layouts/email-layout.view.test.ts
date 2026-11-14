// An email layout's view (ROADMAP 12.1, IA 7, ADR-0215): the colours mail reads as text are checked on its surface and
// the colour a check offers is one click; the dark scheme's colours stand apart, mail's own dark ones as placeholders;
// the default language's texts are kept under the empty culture, another's under its culture; the preview asks after a
// pause with the layout as edited, shows it light or dark and waits while a value is marked; a picked logo hides the
// address; and Send test email saves first, then says where the test went or why it could not go.
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import { UmbObjectState } from '@umbraco-cms/backoffice/observable-api';
import { UmbApiError } from '@umbraco-cms/backoffice/resources';
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import type { EmailLayoutEditorResponseModel } from '../api';
import type { LibraryProblem } from '../library/items/library-items';
import { must, registerEnglish, settle, shadow, testHost, text } from '../testing';
import { EMAIL_LAYOUT_WORKSPACE_CONTEXT } from './constants';
import { NEW_DESIGN, type EmailLayoutDesign } from './email-layout-model';
import { loadEmailLayoutWords } from './email-layout-words';

const api = vi.hoisted(() => ({
  previewed: [] as { design: unknown; culture: string }[],
  previewProblems: [] as unknown[],
  tested: [] as { key: string; culture: string }[],
  testAnswer: undefined as unknown,
}));

vi.mock('@umbraco-cms/backoffice/resources', async (original) => ({
  ...(await original<typeof import('@umbraco-cms/backoffice/resources')>()),
  tryExecute: async (_host: unknown, request: Promise<unknown>) => request,
}));

const editor: EmailLayoutEditorResponseModel = {
  languages: [
    { culture: 'en-US', name: 'English', isDefault: true },
    { culture: 'de-DE', name: 'German', isDefault: false },
  ],
  light: { accent: '#0b5cad', page: '#f4f5f7', surface: '#ffffff', text: '#1d2433', muted: '#5b6372', rule: '#d8dce3' },
  dark: { accent: '#8ab4ff', page: '#111418', surface: '#1b1f24', text: '#e8eaee', muted: '#aab1bd', rule: '#3a414b' },
  defaultLayout: null,
};

vi.mock('../api', async (original) => ({
  ...(await original<typeof import('../api')>()),
  emailLayoutLibraryEditor: async () => ({ data: editor }),
  emailLayoutLibraryPreview: async (options: { body: (typeof api.previewed)[number] }) => {
    api.previewed.push(options.body);
    return {
      data: {
        html: '<!doctype html><html><head><meta name="color-scheme" content="light dark"></head><body><p>Sample</p></body></html>',
        problems: api.previewProblems,
      },
    };
  },
  emailLayoutLibraryTest: async (options: { path: { key: string }; body: { culture: string } }) => {
    api.tested.push({ key: options.path.key, culture: options.body.culture });
    return api.testAnswer;
  },
}));

const { EmailLayoutViewElement } = await import('./email-layout.view');

function contextOf(shown: EmailLayoutDesign) {
  const design = new UmbObjectState<EmailLayoutDesign | undefined>(shown);
  const problems = new UmbObjectState<readonly LibraryProblem[]>([]);
  return {
    getEntityType: () => 'jfb-email-layout',
    getHostElement: () => document.body,
    getUnique: () => '00000000-0000-4000-8000-000000000009',
    design: design.asObservable(),
    problems: problems.asObservable(),
    setDesign: vi.fn((next: EmailLayoutDesign) => design.setValue(next)),
    saveFirst: vi.fn(async () => true),
  };
}

async function viewOf(design: EmailLayoutDesign = NEW_DESIGN) {
  const context = contextOf(design);
  const host = testHost();
  const peek = vi.fn();
  host.provideContext(UMB_NOTIFICATION_CONTEXT, { peek, getHostElement: () => host } as never);
  host.provideContext(EMAIL_LAYOUT_WORKSPACE_CONTEXT, context as never);
  const view = new EmailLayoutViewElement();
  host.append(view);
  await vi.waitFor(() => expect(shadow(view).querySelector('.page')).not.toBeNull());
  await settle(view);
  return { view, root: shadow(view), context, peek };
}

const field = <E extends HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement = HTMLInputElement>(
  root: ShadowRoot,
  id: string,
) => must(root.querySelector<E>(`#${id}`), id);

async function change(
  view: { updateComplete: Promise<unknown> },
  box: HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement,
  value: string,
) {
  box.value = value;
  box.dispatchEvent(new Event('change'));
  await settle(view);
}

/** Presses a scheme's button in the switch with the name given. */
async function scheme(root: ShadowRoot, view: { updateComplete: Promise<unknown> }, group: string, name: string) {
  const buttons = root.querySelectorAll<HTMLButtonElement>(`.schemes[aria-label="${group}"] button`);
  must(
    [...buttons].find((one) => text(one) === name),
    `${group}: ${name}`,
  ).click();
  await settle(view);
}

beforeAll(async () => {
  registerEnglish();
  await loadEmailLayoutWords('en');
});

afterEach(() => {
  document.body.replaceChildren();
  api.previewed = [];
  api.previewProblems = [];
  api.tested = [];
  api.testAnswer = undefined;
});

describe("an email layout's view", () => {
  it('checks the colours mail reads as text on its surface, and sets the colour a check offers on request', async () => {
    const { view, root, context } = await viewOf();
    const accent = field(root, 'colour-light-accent');
    expect([accent.value, accent.placeholder]).toEqual(['', '#0b5cad']);
    expect(text(root.querySelector('#colour-light-accent-check'))).toBe('Contrast 6.6:1');
    await change(view, accent, '#7FB3FF');
    expect(context.setDesign).toHaveBeenLastCalledWith({ ...NEW_DESIGN, light: { accent: '#7fb3ff' } });
    const check = must(root.querySelector('#colour-light-accent-check'), 'the check');
    expect(text(check)).toMatch(/^2\.1:1 on the mail: text needs 4\.5:1\. Use #[0-9a-f]{6}$/);
    must(check.querySelector<HTMLButtonElement>('button'), 'Use').click();
    await settle(view);
    const used = context.setDesign.mock.lastCall?.[0].light.accent;
    expect(used).toMatch(/^#[0-9a-f]{6}$/);
    expect(text(root.querySelector('#colour-light-accent-check'))).toMatch(/^Contrast /);
    // A colour mail draws no text in has no check.
    expect(root.querySelector('#colour-light-page-check')?.hasAttribute('hidden')).toBe(true);
  });

  it("shows the dark scheme's colours apart, mail's own dark ones as their placeholders", async () => {
    const { view, root, context } = await viewOf({ ...NEW_DESIGN, light: { accent: '#1f4fbf' } });
    expect(root.querySelector('#colour-dark-accent')).toBeNull();
    await scheme(root, view, 'Colours to set', 'Dark');
    expect(text(root.querySelector('.group[aria-labelledby="colours-heading"] .help'))).toBe(
      'For mail apps in a dark scheme. An empty colour keeps the default.',
    );
    const accent = field(root, 'colour-dark-accent');
    expect(accent.placeholder).toBe('#8ab4ff');
    await change(view, accent, '#9cc4ff');
    expect(context.setDesign).toHaveBeenLastCalledWith({
      ...NEW_DESIGN,
      light: { accent: '#1f4fbf' },
      dark: { accent: '#9cc4ff' },
    });
  });

  it("keeps the default language's texts under the empty culture and another language's under its culture", async () => {
    const { view, root, context } = await viewOf();
    await change(view, field<HTMLTextAreaElement>(root, 'header'), '<p>Hello</p>');
    expect(context.setDesign).toHaveBeenLastCalledWith({ ...NEW_DESIGN, header: { '': '<p>Hello</p>' } });
    await change(view, field<HTMLSelectElement>(root, 'language'), 'de-DE');
    expect(field<HTMLTextAreaElement>(root, 'header').value).toBe('');
    await change(view, field<HTMLTextAreaElement>(root, 'header'), '<p>Hallo</p>');
    await change(view, field(root, 'logo-alt'), 'Marke');
    expect(context.setDesign).toHaveBeenLastCalledWith({
      ...NEW_DESIGN,
      header: { '': '<p>Hello</p>', 'de-DE': '<p>Hallo</p>' },
      logoAlt: { 'de-DE': 'Marke' },
    });
    await vi.waitFor(() => expect(api.previewed.at(-1)?.culture).toBe('de-DE'));
  });

  it('asks for the preview after a pause with the layout as edited, shows it light or dark, and waits while a value is marked', async () => {
    const { view, root } = await viewOf();
    await vi.waitFor(() => expect(root.querySelector('iframe')).not.toBeNull());
    expect(api.previewed.at(-1)).toEqual({ culture: 'en-US', design: NEW_DESIGN });
    const frame = () => must(root.querySelector('iframe'), 'the preview');
    expect([frame().title, frame().getAttribute('sandbox')]).toEqual([
      'The sample mail in the light scheme',
      'allow-same-origin',
    ]);
    expect(frame().srcdoc).toContain('content="light"');
    await scheme(root, view, 'Colour scheme of the preview', 'Dark');
    expect(frame().title).toBe('The sample mail in the dark scheme');
    expect(frame().srcdoc).toContain('content="dark"');
    api.previewProblems = [{ code: 'invalid_value', path: '$.design.light.accent', message: '' }];
    await change(view, field(root, 'colour-light-accent'), '#12');
    await vi.waitFor(() => expect(root.querySelector('iframe')).toBeNull());
    expect(text(root.querySelector('.preview [role="status"]'))).toBe(
      'The preview waits until the marked values are fixed.',
    );
    expect(field(root, 'colour-light-accent').getAttribute('aria-invalid')).toBe('true');
    expect(text(root.querySelector('.colour .why'))).toBe('A colour is a hex colour, such as #1f4fbf.');
  });

  it("hides the logo's address while a picture from the media library is the logo, and keeps a blank address as none", async () => {
    const picked = await viewOf({ ...NEW_DESIGN, logoMedia: '00000000-0000-4000-8000-0000000000aa' });
    expect(picked.root.querySelector('#logo-url')).toBeNull();
    document.body.replaceChildren();
    const { view, root, context } = await viewOf({ ...NEW_DESIGN, logoUrl: 'https://example.com/logo.png' });
    await change(view, field(root, 'logo-url'), '  /media/logo.png ');
    expect(context.setDesign).toHaveBeenLastCalledWith({ ...NEW_DESIGN, logoUrl: '/media/logo.png' });
    await change(view, field(root, 'logo-url'), '   ');
    expect(context.setDesign).toHaveBeenLastCalledWith({ ...NEW_DESIGN, logoUrl: null });
  });

  it('saves first on Send test email, then says where the test went, or why it could not go', async () => {
    const { view, root, context, peek } = await viewOf();
    const send = async () => {
      must(root.querySelector<HTMLElement>('uui-button[label="Send test email"]'), 'Send test email').click();
      await settle(view);
    };
    api.testAnswer = { data: { to: 'dana@jfb.test' } };
    await send();
    await vi.waitFor(() => expect(peek).toHaveBeenCalledOnce());
    expect(context.saveFirst).toHaveBeenCalledOnce();
    expect(api.tested).toEqual([{ key: '00000000-0000-4000-8000-000000000009', culture: 'en-US' }]);
    expect(peek).toHaveBeenLastCalledWith('positive', {
      data: { message: 'A test mail is on its way to dana@jfb.test.' },
    });

    // A save that fails sends nothing.
    context.saveFirst.mockResolvedValueOnce(false);
    await send();
    await vi.waitFor(() => expect(context.saveFirst).toHaveBeenCalledTimes(2));
    expect(api.tested).toHaveLength(1);

    api.testAnswer = {
      error: new UmbApiError('No mail', 400, {}, { status: 400, code: 'email_not_configured' } as never),
    };
    await send();
    await vi.waitFor(() => expect(peek).toHaveBeenCalledTimes(2));
    expect(peek).toHaveBeenLastCalledWith('danger', {
      data: { message: "Umbraco can't send mail yet. Set up its SMTP settings first." },
    });
  });
});
