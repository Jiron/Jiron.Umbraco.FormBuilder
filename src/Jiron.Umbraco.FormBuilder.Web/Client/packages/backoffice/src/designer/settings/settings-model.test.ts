// The form's settings as the Settings view edits them (ROADMAP 10.1a, ADR-0184): a setting written where it was, as the
// server writes its canonical form; where each path of the definition's settings is in the view; the email fields
// verification can confirm; names, whole numbers and references as the save checks hold them; the protection chosen;
// and the line each closed group says.
import { describe, expect, it } from 'vitest';
import type { CaptchaProviderResponseModel, FormSettingsChoicesResponseModel } from '../../api';
import type { DesignDefinition } from '../definition';
import {
  DOT,
  anchorOf,
  captchaBox,
  groupOf,
  parseNames,
  parseWhole,
  protectionOf,
  referenceOf,
  settingAt,
  summaryOf,
  verificationFields,
  withProtection,
  withSettingAt,
  type SummaryWords,
} from './settings-model';

/** A definition as the server writes a new form's: every setting with a value, in the model's order. */
const DEFINITION: DesignDefinition = {
  pages: [
    {
      key: 'p1',
      groups: [
        {
          key: 'g1',
          fields: [
            { key: 'name', type: 'text' },
            { key: 'mail', type: 'email' },
            { key: 'passing', type: 'email', keep: 'untilActionsRun' },
            { key: 'people', type: 'repeater', fields: [{ key: 'other', type: 'email' }] },
          ],
        },
      ],
    },
    { key: 'p2', groups: [{ key: 'g2', fields: [{ key: 'work', type: 'email' }] }] },
  ],
  settings: {
    submission: { summaryPage: false, progress: 'steps', store: 'always', approval: 'automatic' },
    availability: { members: 'anyone', onePerMember: false },
    protection: { level: 'inherit', contentRules: 'inherit' },
    privacy: { ip: 'inherit', insights: true },
    appearance: { requiredMarker: 'inherit', validationSummary: true, choiceLayout: 'vertical' },
    developer: { headless: false },
  },
};

const CHOICES: FormSettingsChoicesResponseModel = {
  timeZone: 'Europe/Berlin',
  defaults: { protection: 'standard', contentRules: 'normal', requiredMarker: 'optional', theme: 'default' },
  themes: [
    { alias: 'default', label: 'Default' },
    { alias: 'brand', label: 'Brand' },
  ],
  memberGroups: [],
  limits: { lineLength: 500, htmlLength: 50000, nameLength: 64, verificationDays: 90, referenceDigits: 10 },
};

const PROVIDERS: CaptchaProviderResponseModel[] = [
  { name: 'recaptchaV3', label: 'reCAPTCHA v3', isConfigured: true, settings: [] },
  { name: 'hcaptcha', label: 'hCaptcha', isConfigured: false, settings: [] },
];

/** Words that say each term by its key and arguments, so a line shows what it is made of. */
const WORDS: SummaryWords = {
  term: (key, ...args) => (args.length > 0 ? `${key}(${args.join(',')})` : key),
  date: (instant) => `[${instant}]`,
  number: (value) => `#${value}`,
};

const line = (definition: DesignDefinition, group: Parameters<typeof summaryOf>[0], choices = CHOICES) =>
  summaryOf(group, definition, choices, PROVIDERS, WORDS).split(` ${DOT} `);

describe('a setting written back', () => {
  it('goes where it was, so a change and its undo leave the settings as the server wrote them', () => {
    const on = withSettingAt(DEFINITION, ['submission', 'summaryPage'], true);
    expect(Object.keys(groupOf(on, 'submission'))).toEqual(['summaryPage', 'progress', 'store', 'approval']);
    expect(withSettingAt(on, ['submission', 'summaryPage'], false)).toEqual(DEFINITION);
  });

  it('comes in at the end of its group, and an empty text, list or object goes', () => {
    const titled = withSettingAt(DEFINITION, ['submission', 'entryTitle'], 'x');
    expect(Object.keys(groupOf(titled, 'submission')).at(-1)).toBe('entryTitle');
    expect(withSettingAt(titled, ['submission', 'entryTitle'], undefined)).toEqual(DEFINITION);
    const listed = withSettingAt(DEFINITION, ['developer', 'queryValues'], ['ref']);
    expect(withSettingAt(listed, ['developer', 'queryValues'], [])).toEqual(DEFINITION);
    const deep = withSettingAt(DEFINITION, ['protection', 'captcha', 'settings', 'threshold'], '0.7');
    expect(settingAt(deep, ['protection', 'captcha', 'settings', 'threshold'])).toBe('0.7');
    expect(withSettingAt(deep, ['protection', 'captcha', 'settings', 'threshold'], undefined)).toEqual(DEFINITION);
  });

  it('makes the settings of a definition that has none', () => {
    const bare: DesignDefinition = { pages: [] };
    const set = withSettingAt(bare, ['appearance', 'cssClass'], 'wide');
    expect(set).toEqual({ pages: [], settings: { appearance: { cssClass: 'wide' } } });
    expect(withSettingAt(set, ['appearance', 'cssClass'], undefined)).toEqual(bare);
  });
});

describe('where a path is in the view', () => {
  it('names the group, whether under More options, and the boxes the focus tries', () => {
    const cases: [string, ReturnType<typeof anchorOf>][] = [
      ["$.settings.submission.submitLabel['de-DE']", { group: 'submission', more: false, boxes: ['submit-label'] }],
      ['$.settings.submission.storeDays', { group: 'submission', more: false, boxes: ['store'] }],
      [
        '$.settings.submission.verification.emailField',
        { group: 'submission', more: false, boxes: ['verification-field', 'verification'] },
      ],
      [
        '$.settings.submission.verification.days',
        { group: 'submission', more: false, boxes: ['verification-days', 'verification'] },
      ],
      ['$.settings.submission.verification', { group: 'submission', more: false, boxes: ['verification'] }],
      ['$.settings.submission.backLabel', { group: 'submission', more: true, boxes: ['back-label'] }],
      ['$.settings.submission.entryReference.digits', { group: 'submission', more: true, boxes: ['reference-digits'] }],
      ['$.settings.submission.entryReference', { group: 'submission', more: true, boxes: ['reference-prefix'] }],
      ['$.settings.availability.memberGroups[1]', { group: 'availability', more: false, boxes: ['member-groups'] }],
      ['$.settings.availability.members', { group: 'availability', more: false, boxes: ['access'] }],
      ['$.settings.protection.captcha.provider', { group: 'protection', more: false, boxes: ['protection'] }],
      [
        '$.settings.protection.captcha.settings.threshold',
        { group: 'protection', more: false, boxes: [captchaBox('threshold'), 'protection'] },
      ],
      ['$.settings.protection.contentRules', { group: 'protection', more: false, boxes: ['content-rules'] }],
      ['$.settings.appearance.theme', { group: 'appearance', more: false, boxes: ['theme'] }],
      ['$.settings.developer.contextValues[0]', { group: 'developer', more: false, boxes: ['context-values'] }],
    ];
    for (const [path, anchor] of cases) expect(anchorOf(path), path).toEqual(anchor);
  });

  it('is nowhere for a setting this view does not show yet, or a path outside the settings', () => {
    for (const path of [
      '$.settings.submission.resume.days',
      '$.settings.availability.onePerMember',
      '$.settings.privacy.retentionDays.spam',
      '$.settings',
      '$.outcomes[0]',
      '$.pages[0].label',
      'nonsense',
    ])
      expect(anchorOf(path), path).toBeNull();
  });
});

describe('what the boxes hold', () => {
  it('offers the email fields outside repeatable groups, on every page, kept until confirmed, for verification', () => {
    expect(verificationFields(DEFINITION).map((field) => field.key)).toEqual(['mail', 'work']);
  });

  it('reads names one a line as the server checks them', () => {
    expect(parseNames(' ref \n\nutm_source\ncampaign-2026 ', 64, null)).toEqual({
      ok: true,
      names: ['ref', 'utm_source', 'campaign-2026'],
    });
    expect(parseNames('', 64, null)).toEqual({ ok: true, names: [] });
    expect(parseNames('ref\nwith space', 64, null)).toEqual({
      ok: false,
      problem: { kind: 'notAName', name: 'with space' },
    });
    expect(parseNames('a'.repeat(65), 64, null)).toMatchObject({ ok: false, problem: { kind: 'notAName' } });
    expect(parseNames('Ref\nref', 64, null)).toEqual({ ok: false, problem: { kind: 'twice', name: 'ref' } });
    expect(parseNames('JFB_server', 64, 'jfb')).toEqual({
      ok: false,
      problem: { kind: 'reserved', name: 'JFB_server' },
    });
    expect(parseNames('jfbserver', 64, null)).toEqual({ ok: true, names: ['jfbserver'] });
  });

  it('reads a whole number within its bounds, nothing from an empty box', () => {
    expect(parseWhole(' 7 ', 1, 90)).toBe(7);
    expect(parseWhole('', 1, 90)).toBeUndefined();
    for (const text of ['0', '91', '1.5', '-3', 'seven']) expect(parseWhole(text, 1, 90), text).toBeNull();
  });

  it('writes a reference as visitors get it', () => {
    expect(referenceOf('REQ-', 2026, 5, 42)).toBe('REQ-2026-00042');
    expect(referenceOf('', null, 3, 42)).toBe('042');
    expect(referenceOf('', 2026, 1, 42)).toBe('2026-42');
  });
});

describe('the protection chosen', () => {
  it("reads the site's, a level, or a provider", () => {
    expect(protectionOf(DEFINITION)).toEqual({ kind: 'level', level: 'inherit' });
    const captcha = withProtection(DEFINITION, { kind: 'captcha', provider: 'hcaptcha' });
    expect(protectionOf(captcha)).toEqual({ kind: 'captcha', provider: 'hcaptcha' });
    expect(groupOf(captcha, 'protection')).toEqual({
      level: 'captcha',
      contentRules: 'inherit',
      captcha: { provider: 'hcaptcha' },
    });
  });

  it('keeps a provider its settings while it stays chosen, and drops them with another choice', () => {
    const scored = withSettingAt(
      withProtection(DEFINITION, { kind: 'captcha', provider: 'recaptchaV3' }),
      ['protection', 'captcha', 'settings', 'threshold'],
      '0.7',
    );
    const same = withProtection(scored, { kind: 'captcha', provider: 'recaptchaV3' });
    expect(settingAt(same, ['protection', 'captcha', 'settings', 'threshold'])).toBe('0.7');
    const other = withProtection(scored, { kind: 'captcha', provider: 'hcaptcha' });
    expect(settingAt(other, ['protection', 'captcha'])).toEqual({ provider: 'hcaptcha' });
    expect(withProtection(scored, { kind: 'level', level: 'inherit' })).toEqual(DEFINITION);
  });
});

describe("a closed group's line", () => {
  it('says how entries are kept and approved', () => {
    expect(line(DEFINITION, 'submission')).toEqual(['summary_stored']);
    let changed = withSettingAt(DEFINITION, ['submission', 'store'], 'never');
    changed = withSettingAt(changed, ['submission', 'approval'], 'manual');
    changed = withSettingAt(changed, ['submission', 'verification'], { emailField: 'mail', days: 7 });
    expect(line(changed, 'submission')).toEqual(['summary_notStored', 'summary_approval', 'summary_verification']);
  });

  it('says when the form is open, how many entries it takes and for whom', () => {
    expect(line(DEFINITION, 'availability')).toEqual(['summary_alwaysOpen', 'summary_noLimit']);
    const opens = withSettingAt(DEFINITION, ['availability', 'opensAt'], '2026-12-01T08:00:00Z');
    expect(line(opens, 'availability')[0]).toBe('summary_opens([2026-12-01T08:00:00Z])');
    const closes = withSettingAt(DEFINITION, ['availability', 'closesAt'], '2026-12-24T17:00:00Z');
    expect(line(closes, 'availability')[0]).toBe('summary_openUntil([2026-12-24T17:00:00Z])');
    let both = withSettingAt(opens, ['availability', 'closesAt'], '2026-12-24T17:00:00Z');
    both = withSettingAt(both, ['availability', 'maxEntries'], 1200);
    both = withSettingAt(both, ['availability', 'members'], 'groups');
    both = withSettingAt(both, ['availability', 'memberGroups'], ['a', 'b']);
    expect(line(both, 'availability')).toEqual([
      'summary_openBetween([2026-12-01T08:00:00Z],[2026-12-24T17:00:00Z])',
      'summary_limit(#1200,1200)',
      'summary_groups(2)',
    ]);
    expect(line(withSettingAt(DEFINITION, ['availability', 'members'], 'members'), 'availability').at(-1)).toBe(
      'summary_members',
    );
  });

  it("names the protection, the site's by what it stands for, and content checks of the form's own", () => {
    expect(line(DEFINITION, 'protection')).toEqual(['siteDefault(protection_standard)']);
    const site = { ...CHOICES, defaults: { ...CHOICES.defaults, protection: 'captcha', captchaProvider: 'hcaptcha' } };
    expect(line(DEFINITION, 'protection', site)).toEqual(['siteDefault(hCaptcha)']);
    const own = withSettingAt(
      withProtection(DEFINITION, { kind: 'level', level: 'proofOfWork' }),
      ['protection', 'contentRules'],
      'strict',
    );
    expect(line(own, 'protection')).toEqual(['protection_proofOfWork', 'summary_content_strict']);
    expect(line(withProtection(DEFINITION, { kind: 'captcha', provider: 'recaptchaV3' }), 'protection')).toEqual([
      'reCAPTCHA v3',
    ]);
  });

  it("names the theme and the marks, the site's where the form has none", () => {
    expect(line(DEFINITION, 'appearance')).toEqual(['summary_theme(Default)', 'summary_marker_optional']);
    let own = withSettingAt(DEFINITION, ['appearance', 'theme'], 'brand');
    own = withSettingAt(own, ['appearance', 'requiredMarker'], 'none');
    expect(line(own, 'appearance')).toEqual(['summary_theme(Brand)', 'summary_marker_none']);
  });

  it('says whether headless sites may read it, and the values it takes', () => {
    expect(line(DEFINITION, 'developer')).toEqual(['summary_headlessOff']);
    let own = withSettingAt(DEFINITION, ['developer', 'headless'], true);
    own = withSettingAt(own, ['developer', 'queryValues'], ['ref', 'utm']);
    own = withSettingAt(own, ['developer', 'contextValues'], ['crm']);
    expect(line(own, 'developer')).toEqual([
      'summary_headlessOn',
      'summary_queryValues(2)',
      'summary_contextValues(1)',
    ]);
  });
});
