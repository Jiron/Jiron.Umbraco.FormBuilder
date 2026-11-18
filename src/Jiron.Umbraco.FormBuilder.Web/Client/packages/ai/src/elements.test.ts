// The AI add-on's screens (ADR-0018): the draft sidebar drafts from the editor's text into the place it was opened on,
// says a refusal by its code and says why the AI cannot work instead of offering a box that cannot; the mark shows while
// the open draft is the drafted version; Suggest writes nothing outside the default language, lists what the AI
// suggested after saving, and writes the one used into the draft; and Settings > AI saves the chosen profile.
import { UmbObjectState } from '@umbraco-cms/backoffice/observable-api';
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { html } from '@umbraco-cms/backoffice/external/lit';
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import type { JfbFormDesigner, JfbTextChange } from '../../backoffice/public/types/jiron-form-builder';
import { must, registerEnglish, settle, shadow, text } from './testing';

const api = vi.hoisted(() => ({
  status: [] as unknown[],
  drafts: [] as unknown[],
  marks: [] as unknown[],
  suggestions: [] as unknown[],
  translations: [] as unknown[],
  settings: [] as unknown[],
  saves: [] as unknown[],
  sent: [] as unknown[],
}));

vi.mock('@umbraco-cms/backoffice/resources', async (original) => ({
  ...(await original<typeof import('@umbraco-cms/backoffice/resources')>()),
  tryExecute: async (_host: unknown, request: Promise<unknown>) => request,
}));

vi.mock('@umbraco-cms/backoffice/language', async (original) => ({
  ...(await original<typeof import('@umbraco-cms/backoffice/language')>()),
  UmbLanguageCollectionRepository: class {
    async requestCollection() {
      return {
        data: {
          items: [
            { unique: 'en-US', name: 'English', isDefault: true },
            { unique: 'de-DE', name: 'Deutsch', isDefault: false },
          ],
        },
      };
    }
  },
}));

vi.mock('./api', async (original) => ({
  ...(await original<typeof import('./api')>()),
  aiStatus: async () => api.status.shift() ?? { data: { ready: true, problem: null, profileName: 'Fake chat' } },
  aiDraftCreate: async (options: { body: unknown }) => {
    api.sent.push(options.body);
    return api.drafts.shift();
  },
  aiFormMark: async () => api.marks.shift(),
  aiFormTranslate: async (options: { body: unknown }) => {
    api.sent.push(options.body);
    return api.translations.shift();
  },
  aiFormSuggest: async (options: { body: unknown }) => {
    api.sent.push(options.body);
    return api.suggestions.shift();
  },
  aiGetSettings: async () => api.settings.shift(),
  aiSaveSettings: async (options: { body: unknown }) => {
    api.sent.push(options.body);
    return api.saves.shift();
  },
}));

const refusal = (code: string, status = 409) => ({
  error: Object.assign(new Error(code), {
    name: 'UmbApiError',
    status,
    problemDetails: { code, status, title: code, type: 'about:blank' },
  }),
});

class Host extends UmbLitElement {
  override render() {
    return html`<slot></slot>`;
  }
}
if (!customElements.get('jfb-ai-test-host')) customElements.define('jfb-ai-test-host', Host);

function host(): Host {
  const element = document.createElement('jfb-ai-test-host') as Host;
  document.body.append(element);
  return element;
}

function modalContext(submit: () => void) {
  const state = new UmbObjectState<object>({});
  return {
    value: state.asObservable(),
    setValue: (next: object) => state.setValue(next),
    updateValue: (partial: object) => state.update(partial),
    getValue: () => state.getValue(),
    submit,
    reject: vi.fn(),
  };
}

beforeAll(registerEnglish);

afterEach(() => {
  document.body.replaceChildren();
  for (const list of Object.values(api)) list.splice(0);
});

describe('the draft sidebar', () => {
  async function open(folderKey: string | null) {
    const { DraftModalElement } = await import('./draft/draft.modal.element');
    const modal = new DraftModalElement();
    const submitted = vi.fn();
    const peek = vi.fn();
    const parent = host();
    parent.provideContext(UMB_NOTIFICATION_CONTEXT, { peek, getHostElement: () => parent } as never);
    modal.data = { folderKey };
    modal.modalContext = modalContext(submitted) as never;
    parent.append(modal);
    await settle(modal);
    return { modal, submitted, peek, root: shadow(modal) };
  }

  it("drafts from the editor's text into the folder it was opened on, and hands the new form on", async () => {
    api.drafts.push({ data: { key: 'new-form', name: 'Sign-up', draftVersion: 1, left: 2 } });
    const { modal, submitted, peek, root } = await open('folder-1');
    const box = must(root.querySelector('textarea'), 'the text box');
    await new Promise<void>((frame) => requestAnimationFrame(() => frame()));
    expect(root.activeElement, 'the text box takes the focus as the sidebar opens').toBe(box);
    expect(text(root.querySelector('label'))).toBe('Describe the form, or paste the text of a questionnaire');
    expect(box.getAttribute('aria-describedby')).toBe('text-help');
    const draft = must([...root.querySelectorAll('uui-button')].at(-1), 'Draft form');
    expect(draft.hasAttribute('disabled'), 'nothing to draft from yet').toBe(true);

    box.value = '  A sign-up form for the summer party  ';
    box.dispatchEvent(new Event('input'));
    await settle(modal);
    draft.click();
    await settle(modal);

    expect(api.sent).toEqual([{ text: 'A sign-up form for the summer party', folderKey: 'folder-1' }]);
    expect(modal.value).toEqual({ formKey: 'new-form' });
    expect(submitted).toHaveBeenCalledOnce();
    expect(peek.mock.calls[0]?.[1]).toEqual({ data: { message: expect.stringContaining('2 of the AI') } });
  });

  it('says a refusal above the buttons and keeps the text', async () => {
    api.drafts.push(refusal('ai_draft_invalid', 422));
    const { modal, submitted, root } = await open(null);
    const box = must(root.querySelector('textarea'), 'the text box');
    box.value = 'Something';
    box.dispatchEvent(new Event('input'));
    await settle(modal);
    must([...root.querySelectorAll('uui-button')].at(-1), 'Draft form').click();
    await settle(modal);

    expect(text(root.querySelector('[role="alert"]'))).toContain('was not a form');
    expect(box.value).toBe('Something');
    expect(submitted).not.toHaveBeenCalled();
  });

  it('says why the AI cannot draft instead of offering the box', async () => {
    api.status.push({ data: { ready: false, problem: 'ai_no_profile', profileName: null } });
    const { root } = await open(null);

    expect(root.querySelector('textarea')).toBeNull();
    expect(text(root.querySelector('.setup'))).toContain('choose a profile in Settings');
    expect(must([...root.querySelectorAll('uui-button')].at(-1), 'Draft form').hasAttribute('disabled')).toBe(true);
  });
});

describe('the drafted mark', () => {
  it('shows while the open draft is the version the add-on drafted, and goes with the first save', async () => {
    const { DraftedMarkElement } = await import('./mark/drafted-mark.element');
    const { FORM_WORKSPACE_CONTEXT } = await import('./names');
    const form = new UmbObjectState<{ key: string; alias: string; name: string } | undefined>({
      key: 'f1',
      alias: 'f1',
      name: 'F',
    });
    const draftVersion = new UmbObjectState<number | null>(3);
    const parent = host();
    parent.provideContext(FORM_WORKSPACE_CONTEXT, {
      getEntityType: () => 'jfb-form',
      getHostElement: () => parent,
      form: form.asObservable(),
      draftVersion: draftVersion.asObservable(),
    } as never);
    api.marks.push({ data: { draftVersion: 3 } });
    const mark = new DraftedMarkElement();
    parent.append(mark);
    await settle(mark);
    expect(text(shadow(mark).querySelector('uui-tag'))).toBe('Drafted by AI, please review');

    draftVersion.setValue(4);
    await settle(mark);
    expect(shadow(mark).querySelector('uui-tag')).toBeNull();
  });
});

describe('Suggest help text', () => {
  function designerOf(save = true) {
    const changes: { readonly changes: readonly JfbTextChange[]; readonly said: string }[] = [];
    const designer: JfbFormDesigner = {
      formKey: 'f1',
      getLanguage: () => '',
      getDefinition: () => ({}),
      hasUnsavedChanges: () => false,
      save: vi.fn(async () => save),
      changeTexts: (next, said) => {
        changes.push({ changes: next, said });
        return true;
      },
    };
    return { designer, changes };
  }

  async function suggestion(language: string, designer: JfbFormDesigner) {
    const { SuggestElement } = await import('./suggest/suggest.element');
    const element = new SuggestElement();
    Object.assign(element, { designer, fieldKey: 'dob', text: 'help', language });
    host().append(element);
    await settle(element);
    return element;
  }

  const open = async (element: HTMLElement) => {
    must(shadow(element).querySelector<HTMLButtonElement>('button.trigger'), 'Suggest help text').click();
    await settle(element as never);
  };

  it("is not offered in another language than the site's default", async () => {
    const element = await suggestion('de-DE', designerOf().designer);
    expect(shadow(element).querySelector('button')).toBeNull();
  });

  it('saves first, lists what the AI suggested with its reason, and writes the one used into the draft', async () => {
    const { designer, changes } = designerOf();
    api.suggestions.push({
      data: {
        draftVersion: 2,
        help: {
          setting: 'help',
          path: '$.pages[0].groups[0].fields[1].help',
          text: 'As on your passport.',
          reason: null,
        },
        improvements: [
          {
            setting: 'label',
            path: '$.pages[0].groups[0].fields[1].label',
            text: 'Date of birth',
            reason: 'An abbreviation is hard to understand.',
          },
        ],
      },
    });
    const element = await suggestion('', designer);
    const button = must(shadow(element).querySelector('button.trigger'), 'Suggest help text');
    expect(button.getAttribute('aria-expanded')).toBe('false');
    await open(element);

    expect(button.getAttribute('aria-expanded')).toBe('true');
    expect(shadow(element).querySelector('[role="group"]')?.getAttribute('aria-label')).toBe('Suggestions');
    expect(designer.save).toHaveBeenCalledOnce();
    expect(api.sent).toEqual([{ fieldKey: 'dob' }]);
    const items = [...shadow(element).querySelectorAll('li')];
    expect(items.map((item) => text(item.querySelector('.what')))).toEqual(['Help text', 'Label']);
    expect(text(items[1]?.querySelector('.reason'))).toBe('An abbreviation is hard to understand.');

    must(items[1]?.querySelector('uui-button'), 'Use').click();
    await settle(element);
    expect(changes).toEqual([
      {
        changes: [{ path: '$.pages[0].groups[0].fields[1].label', culture: '', text: 'Date of birth' }],
        said: 'Used the suggested label',
      },
    ]);
    expect(shadow(element).querySelectorAll('li'), 'the one used leaves the list').toHaveLength(1);
  });

  it('asks nothing when the save is refused', async () => {
    const { designer } = designerOf(false);
    const element = await suggestion('', designer);
    await open(element);
    expect(api.sent).toEqual([]);
  });

  it('says why the AI cannot suggest, saving nothing, when it is not set up', async () => {
    api.status.push({ data: { ready: false, problem: 'ai_no_profile', profileName: null } });
    const { designer } = designerOf();
    const element = await suggestion('', designer);
    await open(element);

    expect(designer.save).not.toHaveBeenCalled();
    expect(text(shadow(element).querySelector('[role="alert"]'))).toContain('choose a profile in Settings');
    expect(api.sent).toEqual([]);
  });

  it('closes on Escape, its list gone and its button collapsed', async () => {
    api.suggestions.push({ data: { draftVersion: 2, help: null, improvements: [] } });
    const element = await suggestion('', designerOf().designer);
    await open(element);
    const panel = must(shadow(element).querySelector('[role="group"]'), 'the list');
    expect(text(panel)).toContain('this field reads well');

    panel.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    await settle(element);
    expect(shadow(element).querySelector('[role="group"]')).toBeNull();
    expect(shadow(element).querySelector('button.trigger')?.getAttribute('aria-expanded')).toBe('false');
  });
});

describe('the translate sidebar', () => {
  async function sidebar(save = true) {
    const { TranslateModalElement } = await import('./translate/translate.modal.element');
    const designer: JfbFormDesigner = {
      formKey: 'f1',
      getLanguage: () => '',
      getDefinition: () => ({}),
      hasUnsavedChanges: () => true,
      save: vi.fn(async () => save),
      changeTexts: () => true,
    };
    const modal = new TranslateModalElement();
    modal.data = { designer };
    modal.modalContext = modalContext(vi.fn()) as never;
    host().append(modal);
    await settle(modal);
    return { modal, designer, root: shadow(modal) };
  }

  it('says why the AI cannot translate instead of offering the choice, and saves nothing', async () => {
    api.status.push({ data: { ready: false, problem: 'ai_no_profile', profileName: null } });
    const { designer, root } = await sidebar();

    expect(root.querySelector('select')).toBeNull();
    expect(text(root.querySelector('.setup'))).toContain('choose a profile in Settings');
    expect(must([...root.querySelectorAll('uui-button')].at(-1), 'Translate').hasAttribute('disabled')).toBe(true);
    expect(designer.save).not.toHaveBeenCalled();
    // The sidebar's first import, with Umbraco's language module, takes its time in a full run.
  }, 30_000);

  it('saves the draft when the editor asks for the translation, and asks nothing when the save is refused', async () => {
    const { modal, designer, root } = await sidebar(false);
    expect((root.querySelector('select') as HTMLSelectElement | null)?.value).toBe('de-DE');
    expect(designer.save).not.toHaveBeenCalled();

    must([...root.querySelectorAll('uui-button')].at(-1), 'Translate').click();
    await settle(modal);
    expect(designer.save).toHaveBeenCalledOnce();
    expect(api.sent).toEqual([]);
  }, 30_000);
});

describe('Settings > AI', () => {
  it('names the default chat profile the features use while none is chosen', async () => {
    api.settings.push({ data: { profileId: null } });
    api.status.push({ data: { ready: true, problem: null, profileName: 'Default chat' } });
    const { AISettingsScreenElement } = await import('./settings/ai-settings.screen');
    const screen = new AISettingsScreenElement();
    host().append(screen);
    await settle(screen);
    expect(text(shadow(screen).querySelector('.status'))).toBe('The AI features use Default chat.');
  });

  it('reads the chosen profile, saves the one chosen, and says what the features use', async () => {
    api.settings.push({ data: { profileId: 'p1' } });
    api.saves.push({ data: { profileId: null } });
    api.status.push({ data: { ready: true, problem: null, profileName: 'Fake chat' } });
    api.status.push({ data: { ready: false, problem: 'ai_no_profile', profileName: null } });
    const { AISettingsScreenElement } = await import('./settings/ai-settings.screen');
    const screen = new AISettingsScreenElement();
    const parent = host();
    const peek = vi.fn();
    parent.provideContext(UMB_NOTIFICATION_CONTEXT, { peek, getHostElement: () => parent } as never);
    parent.append(screen);
    await settle(screen);
    const root = shadow(screen);
    const picker = must(root.querySelector('uai-profile-picker'), 'the picker') as HTMLElement & { value?: string };
    expect(picker.getAttribute('capability')).toBe('Chat');
    expect(picker.value).toBe('p1');
    expect(text(root.querySelector('.status')), 'the picker shows the chosen profile; nothing says it again').toBe('');

    picker.value = '';
    picker.dispatchEvent(new Event('change'));
    // Named for what it saves, under Settings' tabs, which the core draws by its tag (ADR-0257).
    expect(root.querySelector('uui-button')?.getAttribute('label')).toBe('Save AI settings');
    expect(root.querySelector('umb-body-layout > jfb-place-tabs')?.getAttribute('tab')).toBe(
      'Jiron.FormBuilder.AI.MenuItem.Settings',
    );
    must(root.querySelector('uui-button'), 'Save').click();
    await settle(screen);

    expect(api.sent).toEqual([{ profileId: null }]);
    expect(text(root.querySelector('.status'))).toContain('choose a profile in Settings');
    expect(peek).toHaveBeenCalledWith('positive', { data: { message: 'Saved' } });
  });
});
