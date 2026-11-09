// The global settings screen (ROADMAP 12.4a, IA 8 and 9, ADR-0218): it draws a section's served fields box by box, each
// setting with its help below it, at most seven before More options, and a box of one object holding the object's
// settings; Save sends only the fields changed, with the version read, and says so; a value that can't be kept is said
// under its box and nothing is sent; the server's refusal is said there too; a save made meanwhile is said, with the
// values now saved; a section the host locks names its key and has no Save; a site switch heads a section a site can
// set, and a site's own value says so with "Use the value for all sites"; the time zone is picked from a list; Spam
// protection's level lists each captcha with its keys, greys out the consent mode the host refuses, and shows the
// puzzle and the Cookiebot category only with their choice; Email says when it can't send and sends a test mail when
// it can; Files' size is in megabytes under the host's cap; a secret reference says whether it is found; and a user who
// may not manage settings is told so.
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import { UmbApiError } from '@umbraco-cms/backoffice/resources';
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import type { FieldSettingResponseModel, SettingsSectionResponseModel } from '../api';
import { must, registerEnglish, settle, shadow, testHost, text } from '../testing';

/** The elements under a root or an element that match a selector. */
const all = <E extends Element = HTMLElement>(parent: ParentNode, selector: string): E[] => [
  ...parent.querySelectorAll<E>(selector),
];

/** What a root says, without runs of white space. */
const said = (root: ParentNode): string => (root.textContent ?? '').replace(/\s+/g, ' ').trim();

/** What the API answers: each section read, each save's answer and each test mail's, in turn. */
const api = vi.hoisted(() => ({
  reads: [] as unknown[],
  saves: [] as unknown[],
  asked: [] as unknown[],
  sent: [] as unknown[],
  mails: [] as unknown[],
}));

vi.mock('@umbraco-cms/backoffice/resources', async (original) => ({
  ...(await original<typeof import('@umbraco-cms/backoffice/resources')>()),
  tryExecute: async (_host: unknown, request: Promise<unknown>) => request,
}));

vi.mock('../api', async (original) => ({
  ...(await original<typeof import('../api')>()),
  settingsGet: async (options: unknown) => {
    api.asked.push(options);
    return api.reads.shift();
  },
  settingsSave: async (options: { body: unknown }) => {
    api.sent.push(options.body);
    return api.saves.shift();
  },
  testEmailSend: async () => api.mails.shift(),
}));

vi.mock('../designer/preview/look', async (original) => ({
  ...(await original<typeof import('../designer/preview/look')>()),
  readLook: async () => ({
    culture: 'en-US',
    languages: [
      { culture: 'en-US', name: 'English', isDefault: true },
      { culture: 'de-DE', name: 'German', isDefault: false },
    ],
  }),
}));

const { settingsScreen, MOST_IN_A_BOX } = await import('./settings-screen.element');
const { MEGABYTE } = await import('./settings-values');

const rules = (more: Partial<FieldSettingResponseModel['rules']> = {}): FieldSettingResponseModel['rules'] => ({
  required: false,
  minimumIsExclusive: false,
  maximumIsExclusive: false,
  allowedValues: [],
  ...more,
});

/** A served field, as its section's schema serves it. */
function field(
  section: string,
  name: string,
  kind: string,
  group: string,
  more: Partial<FieldSettingResponseModel> = {},
): FieldSettingResponseModel {
  const key = `#jfbGlobalSettings_${section}_${name}`;
  return {
    name,
    kind,
    choices: [],
    settings: [],
    default: null,
    label: key,
    description: `${key}Help`,
    editor: kind,
    group,
    supportsMergeTags: false,
    isPublic: false,
    rules: rules(),
    lock: null,
    unit: null,
    ...more,
  };
}

const choices = (section: string, name: string, values: readonly string[]) =>
  values.map((value) => ({ value, label: `#jfbGlobalSettings_${section}_${name}_${value}` }));

const draftDays = field('general', 'history.draftDays', 'wholeNumber', '', {
  name: 'draftDays',
  label: '#jfbGlobalSettings_general_history_draftDays',
  description: '#jfbGlobalSettings_general_history_draftDaysHelp',
  unit: '#jfbGlobalSettings_general_history_draftDaysUnit',
  rules: rules({ minimum: '1', maximum: '3650' }),
});

function general(more: Partial<SettingsSectionResponseModel> = {}): SettingsSectionResponseModel {
  return {
    section: 'general',
    site: null,
    version: 3,
    values: {
      requiredMarker: 'optional',
      timeZone: 'Europe/Berlin',
      baseUrl: null,
      history: { draftDays: 90, draftCount: 50 },
    },
    overrides: [],
    siteFields: ['requiredMarker', 'timeZone', 'baseUrl'],
    sites: [
      { key: 'site-a', name: 'Nordwind DE', hasOwnValues: true },
      { key: 'site-b', name: 'Nordwind CH', hasOwnValues: false },
    ],
    fields: [
      field('general', 'requiredMarker', 'choice', 'forms', {
        choices: choices('general', 'requiredMarker', ['required', 'optional', 'none']),
      }),
      field('general', 'baseUrl', 'text', 'site', { editor: 'baseUrl' }),
      field('general', 'timeZone', 'text', 'site', { editor: 'timeZone' }),
      field('general', 'history', 'nested', 'history', {
        settings: [
          draftDays,
          field('general', 'history.draftCount', 'wholeNumber', '', {
            name: 'draftCount',
            label: '#jfbGlobalSettings_general_history_draftCount',
            description: '#jfbGlobalSettings_general_history_draftCountHelp',
            unit: '#jfbGlobalSettings_general_history_draftCountUnit',
            rules: rules({ minimum: '0', maximum: '1000' }),
          }),
        ],
      }),
    ],
    locks: [],
    facts: {
      general: {
        defaultBaseUrl: 'https://nordwind.example',
        defaultTheme: 'default',
        themes: [{ value: 'default', label: 'Default' }],
        formBlock: { exists: true, createdAt: '2026-10-03T08:00:00Z', canCreate: true },
      },
    },
    ...more,
  };
}

function protection(implicitConsent = false): SettingsSectionResponseModel {
  return {
    section: 'protection',
    site: null,
    version: 1,
    values: {
      level: 'standard',
      captchaProvider: null,
      captchaConsent: 'explicit',
      cookiebotCategory: 'necessary',
      proofBits: 16,
    },
    overrides: [],
    siteFields: [],
    sites: [],
    fields: [
      field('protection', 'level', 'choice', 'level', {
        editor: 'protectionLevel',
        choices: choices('protection', 'level', ['standard', 'proofOfWork', 'captcha']),
      }),
      field('protection', 'captchaProvider', 'text', 'level', { editor: 'captchaProvider' }),
      field('protection', 'proofBits', 'wholeNumber', 'level', {
        unit: '#jfbGlobalSettings_protection_proofBitsUnit',
        rules: rules({ minimum: '8', maximum: '24' }),
      }),
      field('protection', 'captchaConsent', 'choice', 'consent', {
        editor: 'captchaConsent',
        choices: choices('protection', 'captchaConsent', ['explicit', 'cookiebot', 'cmp', 'implicit']),
      }),
      field('protection', 'cookiebotCategory', 'choice', 'consent', {
        choices: choices('protection', 'cookiebotCategory', ['necessary', 'marketing']),
      }),
    ],
    locks: implicitConsent
      ? []
      : [
          {
            field: 'captchaConsent',
            kind: 'restricted',
            configurationKey: 'JironFormBuilder:Privacy:AllowImplicitCaptchaConsent',
          },
        ],
    facts: {
      protection: {
        captchas: [
          {
            alias: 'recaptchaV2',
            name: 'reCAPTCHA v2',
            hasKeys: false,
            keysAt: 'JironFormBuilder:Captcha:RecaptchaV2',
          },
          { alias: 'hcaptcha', name: 'hCaptcha', hasKeys: true, keysAt: 'JironFormBuilder:Captcha:Hcaptcha' },
        ],
        implicitConsent,
      },
    },
  };
}

/** Privacy > Data collection: what entries keep of IP addresses, and insights; the host allows full addresses or not. */
function privacy(fullIp = false): SettingsSectionResponseModel {
  return {
    section: 'privacy',
    site: null,
    version: 2,
    values: { ip: 'none', insights: true },
    overrides: [],
    siteFields: [],
    sites: [],
    fields: [
      field('privacy', 'ip', 'choice', 'ip', {
        editor: 'ipHandling',
        choices: choices('privacy', 'ip', ['none', 'anonymised', 'full']),
      }),
      field('privacy', 'insights', 'checkbox', 'insights'),
    ],
    locks: fullIp
      ? []
      : [{ field: 'ip', kind: 'restricted', configurationKey: 'JironFormBuilder:Privacy:AllowFullIp' }],
    facts: { privacy: { fullIp } },
  };
}

const refusal = (code: string, errors: readonly { path: string; code: string; message: string }[] = []) => ({
  error: new UmbApiError('refused', code === 'validation_failed' ? 400 : 409, null, { code, errors } as never),
});

async function screenOf(section: string, ...reads: SettingsSectionResponseModel[]) {
  api.reads = reads.map((data) => ({ data }));
  api.saves = [];
  api.asked = [];
  api.sent = [];
  api.mails = [];
  const host = testHost();
  const peek = vi.fn();
  host.provideContext(UMB_NOTIFICATION_CONTEXT, { peek, getHostElement: () => host } as never);
  const Screen = settingsScreen(`jfb-test-settings-${section}`, section);
  const screen = new Screen();
  host.append(screen);
  await vi.waitFor(() => expect(shadow(screen).querySelector('uui-box[headline]')).not.toBeNull());
  await settle(screen);
  const root = shadow(screen);
  const box = (id: string) => must(root.getElementById(id) as HTMLInputElement | HTMLSelectElement | null, id);
  const type = async (id: string, value: string) => {
    const input = box(id);
    input.value = value;
    input.dispatchEvent(new Event('input'));
    input.dispatchEvent(new Event('change'));
    await settle(screen);
  };
  const button = (label: string) => all(root, 'uui-button').find((one) => one.getAttribute('label') === label);
  const save = async () => {
    must(button('Save'), 'Save').click();
    await vi.waitFor(() => expect(api.reads.length + api.saves.length).toBe(0));
    await settle(screen);
  };
  return { screen, root, box, type, button, save, peek };
}

beforeAll(registerEnglish);

afterEach(() => {
  document.body.replaceChildren();
});

describe('the global settings screen', () => {
  it('draws the boxes in the order the section serves them, each setting with its help below it, and no title of its own', async () => {
    const { root } = await screenOf('general', general());
    const boxes = all(root, 'uui-box[headline]');
    expect(boxes.map((one) => one.getAttribute('headline'))).toEqual([
      'Defaults for forms',
      'Address and time',
      'History',
      'Putting forms on pages',
    ]);
    expect(boxes.every((one) => one.getAttribute('headline-variant') === 'h2')).toBe(true);
    expect(text(root.querySelector('label[for="setting-requiredMarker"]'))).toBe('Fields that get a mark');
    expect(text(root.getElementById('setting-requiredMarker-help'))).toContain('marking the few optional ones');
    // A box of one object holds its settings: History's name is said once, as the box's headline.
    expect(root.querySelector('legend')).toBeNull();
    expect(text(root.getElementById('setting-history-draftDays-unit'))).toBe('days');
    expect(root.getElementById('setting-history-draftDays')?.getAttribute('aria-describedby')).toContain(
      'setting-history-draftDays-unit',
    );
    expect(text(root.getElementById('setting-baseUrl'))).toBe('');
    expect(root.getElementById('setting-baseUrl')?.getAttribute('placeholder')).toBe('https://nordwind.example');
    expect(text(boxes[3])).toContain('Pages show forms through the block type Form, created on');
  });

  it('saves only the fields changed, with the version read, and says so', async () => {
    const { type, save, peek } = await screenOf('general', general());
    await type('setting-history-draftDays', '30');
    api.saves = [{ data: general({ version: 4 }) }];
    await save();
    expect(api.sent).toEqual([{ version: 3, site: null, values: { history: { draftDays: 30, draftCount: 50 } } }]);
    expect(peek).toHaveBeenCalledWith('positive', { data: { message: 'The settings are saved.' } });
  });

  it("says why a value can't be kept under its box, and sends nothing", async () => {
    const { root, type, button } = await screenOf('general', general());
    await type('setting-history-draftDays', '0');
    expect(text(root.getElementById('setting-history-draftDays-why'))).not.toBe('');
    must(button('Save'), 'Save').click();
    await vi.waitFor(() => expect(text(root.querySelector('.alert'))).toContain('Some settings need another value'));
    expect(api.sent).toEqual([]);
  });

  it("says the server's refusal under the field it names, in the screen's own words where it has them", async () => {
    const { root, type, save } = await screenOf('general', general());
    await type('setting-history-draftDays', '30');
    await type('setting-timeZone', 'UTC');
    api.saves = [
      refusal('validation_failed', [
        { path: 'general.history.draftDays', code: 'range', message: 'Must be from 1 to 3650.' },
        { path: 'general.timeZone', code: 'timeZone', message: 'The server knows no zone UTC.' },
      ]),
    ];
    await save();
    expect(text(root.getElementById('setting-history-draftDays-why'))).toBe('Use a whole number from 1 to 3650.');
    expect(text(root.getElementById('setting-timeZone-why'))).toBe('The server knows no zone UTC.');
  });

  it('says a save made meanwhile, with the values now saved', async () => {
    const { root, type, save } = await screenOf('general', general());
    await type('setting-history-draftDays', '30');
    api.saves = [refusal('concurrency_conflict')];
    api.reads = [
      {
        data: general({
          version: 5,
          values: { ...(general().values as object), history: { draftDays: 12, draftCount: 50 } },
        }),
      },
    ];
    await save();
    expect(text(root.querySelector('.alert'))).toContain('Someone else saved these settings meanwhile');
    expect((root.getElementById('setting-history-draftDays') as HTMLInputElement).value).toBe('12');
  });

  it('names the key of a section the host locks, reads only, and has no Save', async () => {
    const locked = general({
      locks: [{ field: '*', kind: 'locked', configurationKey: 'JironFormBuilder:LockedSections:0' }],
    });
    locked.fields = locked.fields.map((one) => ({
      ...one,
      lock: {
        hidden: false,
        configurationKey: 'JironFormBuilder:LockedSections:0',
        value: (locked.values as Record<string, unknown>)[one.name],
      },
    }));
    const { root, button } = await screenOf('general', locked);
    expect(text(root.querySelector('.note[role="note"]'))).toContain('JironFormBuilder:LockedSections:0');
    expect(root.querySelector('[data-locked="requiredMarker"]')).not.toBeNull();
    expect(button('Save')).toBeUndefined();
  });

  it('starts a section a site can set with All sites; a site shows its own fields, says its own values, and can use the value for all sites again', async () => {
    const site = general({
      site: 'site-a',
      values: { ...(general().values as object), timeZone: 'Europe/Zurich' },
      globalValues: general().values,
      overrides: ['timeZone'],
    });
    const { root, box, button, save } = await screenOf('general', general(), site);
    expect(all(box('site'), 'option').map((option) => text(option))).toEqual([
      'All sites',
      'Nordwind DE',
      'Nordwind CH',
    ]);
    box('site').value = 'site-a';
    box('site').dispatchEvent(new Event('change'));
    await vi.waitFor(() => expect(said(root)).toContain('This site has its own value.'));
    expect(api.asked.at(-1)).toEqual({ path: { section: 'general' }, query: { site: 'site-a' } });
    expect(root.getElementById('setting-history-draftDays')).toBeNull();
    expect(said(root)).toContain('Settings every site shares are under All sites.');
    must(button('Use the value for all sites'), 'use all').click();
    await settle();
    expect((box('setting-timeZone') as HTMLSelectElement).value).toBe('Europe/Berlin');
    api.saves = [{ data: site }];
    await save();
    expect(api.sent).toEqual([{ version: 3, site: 'site-a', values: {}, overrides: [] }]);
  });

  it('picks the time zone from a list grouped by area, UTC first, and saves it as the list spells it', async () => {
    const { box, save } = await screenOf('general', general());
    const zones = box('setting-timeZone') as HTMLSelectElement;
    expect(zones.localName).toBe('select');
    expect(zones.querySelector('optgroup')?.getAttribute('label')).toBe('Universal');
    expect(zones.options[0]?.value).toBe('UTC');
    zones.value = 'Europe/Lisbon';
    zones.dispatchEvent(new Event('change'));
    api.saves = [{ data: general() }];
    await save();
    expect(api.sent).toEqual([{ version: 3, site: null, values: { timeZone: 'Europe/Lisbon' } }]);
  });

  it('opens More options for a box of more than seven settings', async () => {
    const many = general({
      fields: Array.from({ length: MOST_IN_A_BOX + 2 }, (_each, index) =>
        field('general', `line${index}`, 'text', 'forms'),
      ),
      values: {},
      sites: [],
    });
    const { root } = await screenOf('general', many);
    expect(all(root, 'input[type="text"]')).toHaveLength(MOST_IN_A_BOX);
    const toggle = must(root.querySelector<HTMLButtonElement>('.more-toggle'), 'toggle');
    expect(text(toggle)).toBe('More options (2)');
    toggle.click();
    await settle();
    expect(all(root, 'input[type="text"]')).toHaveLength(MOST_IN_A_BOX + 2);
    expect(toggle.getAttribute('aria-expanded')).toBe('true');
  });

  it('lists each captcha with its keys, a keyless one not to be chosen, and sets the level and the captcha together', async () => {
    const { root, save } = await screenOf('protection', protection());
    const radio = (value: string) => must(root.querySelector<HTMLInputElement>(`input[value="${value}"]`), value);
    expect(radio('captcha:recaptchaV2').disabled).toBe(true);
    expect(said(root)).toContain('No keys yet. A developer adds them in JironFormBuilder:Captcha:RecaptchaV2.');
    expect(root.getElementById('setting-proofBits')).toBeNull();
    radio('captcha:hcaptcha').click();
    await settle();
    api.saves = [{ data: protection() }];
    await save();
    expect(api.sent).toEqual([{ version: 1, site: null, values: { level: 'captcha', captchaProvider: 'hcaptcha' } }]);
  });

  it('greys out the consent mode the host refuses, with its key, and shows the Cookiebot category only with Cookiebot', async () => {
    const { root } = await screenOf('protection', protection());
    const implicit = must(root.querySelector<HTMLInputElement>('input[value="implicit"]'), 'implicit');
    expect(implicit.disabled).toBe(true);
    expect(said(root)).toContain('Locked by configuration: JironFormBuilder:Privacy:AllowImplicitCaptchaConsent');
    expect(root.getElementById('setting-cookiebotCategory')).toBeNull();
    must(root.querySelector<HTMLInputElement>('input[value="cookiebot"]'), 'cookiebot').click();
    await settle();
    expect(root.getElementById('setting-cookiebotCategory')).not.toBeNull();
    must(root.querySelector<HTMLInputElement>('input[value="proofOfWork"]'), 'proofOfWork').click();
    await settle();
    expect(text(root.getElementById('setting-proofBits-unit'))).toBe('bits');
  });

  it('keeps IP addresses as the site chooses, the full address greyed with its key until the host allows it', async () => {
    const { root, save } = await screenOf('privacy', privacy());
    const radio = (value: string) => must(root.querySelector<HTMLInputElement>(`input[value="${value}"]`), value);
    expect(said(root)).toContain('Each entry can keep the address it was sent from.');
    expect([radio('none').checked, radio('full').disabled]).toEqual([true, true]);
    expect(said(root)).toContain('Locked by configuration: JironFormBuilder:Privacy:AllowFullIp');
    radio('anonymised').click();
    await settle();
    api.saves = [{ data: privacy() }];
    await save();
    expect(api.sent).toEqual([{ version: 2, site: null, values: { ip: 'anonymised' } }]);

    document.body.replaceChildren();
    const allowed = await screenOf('privacy', privacy(true));
    expect(must(allowed.root.querySelector<HTMLInputElement>('input[value="full"]'), 'full').disabled).toBe(false);
    expect(said(allowed.root)).toContain('Only for a site that needs it whole, such as to report abuse.');
  });

  it("says Email can't send yet with the key that sets it up, and offers no test mail until it can", async () => {
    const email = (canSend: boolean): SettingsSectionResponseModel => ({
      ...protection(),
      section: 'email',
      values: { senderName: 'Nordwind' },
      fields: [field('email', 'senderName', 'text', 'sender')],
      locks: [],
      facts: { email: { canSend, sendingKey: 'Umbraco:CMS:Global:Smtp', defaultLayout: 'card', layouts: [] } },
    });
    const cannot = await screenOf('email', email(false));
    expect(text(cannot.root.querySelector('.note[role="note"]'))).toContain('Umbraco:CMS:Global:Smtp');
    expect(cannot.button('Send test email')).toBeUndefined();
    document.body.replaceChildren();
    const can = await screenOf('email', email(true));
    await can.type('setting-senderName', 'Nordwind Travel');
    api.saves = [{ data: email(true) }];
    api.mails = [{ data: { to: 'ana@example.test' } }];
    must(can.button('Send test email'), 'test').click();
    await vi.waitFor(() =>
      expect(can.peek).toHaveBeenCalledWith('positive', {
        data: { message: 'Test email sent to you (ana@example.test).' },
      }),
    );
    expect(api.sent).toEqual([{ version: 1, site: null, values: { senderName: 'Nordwind Travel' } }]);
  });

  it("keeps Files' largest file in megabytes under the host's cap, which it names", async () => {
    const files: SettingsSectionResponseModel = {
      ...protection(),
      section: 'files',
      values: { allowedTypes: ['images', 'pdf'], maxFileBytes: null },
      fields: [
        field('files', 'allowedTypes', 'list', 'types', {
          editor: 'fileTypes',
          itemKind: 'choice',
          choices: choices('files', 'allowedTypes', ['images', 'pdf']),
        }),
        field('files', 'maxFileBytes', 'wholeNumber', 'types', {
          editor: 'fileSize',
          unit: '#jfbGlobalSettings_files_maxFileBytesUnit',
        }),
      ],
      locks: [{ field: 'maxFileBytes', kind: 'clamped', configurationKey: 'JironFormBuilder:Files:MaxFileBytes' }],
      facts: {
        files: {
          hostMaxFileBytes: 50 * MEGABYTE,
          maxFileBytesKey: 'JironFormBuilder:Files:MaxFileBytes',
          blockedExtensions: ['exe'],
          blockedExtensionsKey: 'JironFormBuilder:Files:BlockedExtensions',
          groups: [
            { value: 'images', extensions: ['jpg', 'png'] },
            { value: 'pdf', extensions: ['pdf'] },
          ],
          scanners: 0,
          folder: true,
          rootKey: 'JironFormBuilder:Files:Root',
        },
      },
    };
    const { root, type, save } = await screenOf('files', files);
    expect(said(root)).toContain('JPG, PNG');
    expect(text(root.getElementById('setting-maxFileBytes-cap'))).toBe(
      'Host configuration allows at most 50 MB: JironFormBuilder:Files:MaxFileBytes',
    );
    expect(root.getElementById('setting-maxFileBytes')?.getAttribute('placeholder')).toBe('50');
    await type('setting-maxFileBytes', '60');
    expect(text(root.getElementById('setting-maxFileBytes-why'))).toContain('up to 50');
    await type('setting-maxFileBytes', '20');
    api.saves = [{ data: files }];
    await save();
    expect(api.sent).toEqual([{ version: 1, site: null, values: { maxFileBytes: 20 * MEGABYTE } }]);
    expect(said(root)).toContain("None installed, so files aren't checked for malware.");
  });

  it('takes a secret only as a reference, and says whether the saved one is found', async () => {
    const integrations: SettingsSectionResponseModel = {
      ...protection(),
      section: 'integrations',
      values: { signingSecret: '$JironFormBuilder:Secrets:Signing', slackWebhook: '$JironFormBuilder:Secrets:Slack' },
      fields: [
        field('integrations', 'signingSecret', 'text', 'webhooks', { editor: 'secretReference' }),
        field('integrations', 'slackWebhook', 'text', 'chat', { editor: 'secretReference' }),
      ],
      locks: [],
      facts: { integrations: { allowedHosts: [], allowHttp: false, found: ['signingSecret'] } },
    };
    const { root, type } = await screenOf('integrations', integrations);
    expect(text(root.getElementById('setting-signingSecret-status'))).toBe('Found in host configuration.');
    expect(text(root.getElementById('setting-slackWebhook-status'))).toBe(
      'Not in host configuration yet: a developer adds JironFormBuilder:Secrets:Slack.',
    );
    expect(said(root)).toContain('None yet, so webhook actions and JSON option lists call no address.');
    await type('setting-slackWebhook', 'https://hooks.example.test/abc');
    expect(text(root.getElementById('setting-slackWebhook-why'))).toContain('never the secret itself');
  });

  it('tells a user who may not manage settings so', async () => {
    api.reads = [refusal('forbidden')];
    const host = testHost();
    const Screen = settingsScreen('jfb-test-settings-forbidden', 'general');
    const screen = new Screen();
    api.reads = [{ error: new UmbApiError('forbidden', 403, null, { code: 'forbidden' } as never) }];
    host.append(screen);
    await vi.waitFor(() => expect(text(shadow(screen).querySelector('[role="alert"]'))).toContain('Manage settings'));
  });
});
