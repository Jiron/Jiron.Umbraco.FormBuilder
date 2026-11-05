// The form's settings as the Settings view edits them (ROADMAP 10.1a, ADR-0184): each group of the definition's
// `settings` read with the model's defaults, a setting written back as the server writes it (a value kept even where it
// is the default, an empty text, list or object left out), where the form check's problems and a Fix it lead in the
// view, what a typed list of names or a reference holds, and the line a closed group says. Pure.
import type { CaptchaProviderResponseModel, FormSettingsChoicesResponseModel } from '../../api';
import { parsePath } from '../check/form-check';
import type { DesignDefinition, DesignField, LocalizedText } from '../definition';
import { without } from '../inspector/setting-values';
import { isInstant } from './time-zone';

/** The view's groups in their order (IA 5.2); After sending comes with 10.1b, Privacy with 11.5. */
export const GROUPS = ['submission', 'availability', 'protection', 'appearance', 'developer'] as const;
export type GroupId = (typeof GROUPS)[number];

type Json = Readonly<Record<string, unknown>>;

const isObject = (value: unknown): value is Json =>
  value !== null && typeof value === 'object' && !Array.isArray(value);

/** A group of the definition's settings as stored; empty where the definition has none. */
export function groupOf(definition: DesignDefinition, group: GroupId | 'privacy'): Json {
  const settings = definition['settings'];
  const value = isObject(settings) ? settings[group] : undefined;
  return isObject(value) ? value : {};
}

/** A word a setting holds, or its default where it holds none. */
export const wordIn = (group: Json, name: string, standard: string): string =>
  typeof group[name] === 'string' ? (group[name] as string) : standard;

/** A switch a setting holds, or its default where it holds none. */
export const flagIn = (group: Json, name: string, standard: boolean): boolean =>
  typeof group[name] === 'boolean' ? (group[name] as boolean) : standard;

/** A text a setting holds, or '' where it holds none. */
export const lineIn = (group: Json, name: string): string =>
  typeof group[name] === 'string' ? (group[name] as string) : '';

/** The names a list setting holds. */
export const namesIn = (group: Json, name: string): string[] =>
  Array.isArray(group[name]) ? (group[name] as unknown[]).filter((one): one is string => typeof one === 'string') : [];

/** A whole number a setting holds, or undefined. */
export const numberIn = (value: unknown): number | undefined =>
  typeof value === 'number' && Number.isInteger(value) ? value : undefined;

/** The texts in each language a setting holds, or undefined. */
export const textsIn = (group: Json, name: string): LocalizedText | undefined =>
  isObject(group[name]) ? (group[name] as LocalizedText) : undefined;

/** An object a setting holds, or undefined. */
export const objectIn = (group: Json, name: string): Json | undefined =>
  isObject(group[name]) ? group[name] : undefined;

// An object with the value at the steps set, or taken away for undefined; null where nothing is left in it.
function setAt(object: Json, steps: readonly string[], value: unknown): Json | null {
  const [first, ...rest] = steps;
  if (first === undefined) return object;
  const inner = isObject(object[first]) ? (object[first] as Json) : {};
  const next = rest.length === 0 ? value : (setAt(inner, rest, value) ?? undefined);
  const empty = next === undefined || next === null || (Array.isArray(next) && next.length === 0);
  const kept = empty ? without(object, first) : { ...object, [first]: next };
  return Object.keys(kept).length > 0 ? kept : null;
}

/**
 * The definition with the setting at these steps under `settings` set, where it was: undefined, null and an empty list
 * take it away, and an object left with nothing in it goes too, as the server writes its canonical form.
 */
export function withSettingAt(
  definition: DesignDefinition,
  steps: readonly string[],
  value: unknown,
): DesignDefinition {
  const settings = setAt(isObject(definition['settings']) ? definition['settings'] : {}, steps, value);
  return settings ? { ...definition, settings } : (without(definition, 'settings') as unknown as DesignDefinition);
}

/** The setting's value at these steps under `settings`, as stored. */
export function settingAt(definition: DesignDefinition, steps: readonly string[]): unknown {
  let value: unknown = definition['settings'];
  for (const step of steps) value = isObject(value) ? value[step] : undefined;
  return value;
}

// ---- Fix it and the form check's problems -----------------------------------------------------------------------------

/** Where a setting is in the view: its group, whether under More options, and its boxes in the order focus tries them. */
export interface Anchor {
  readonly group: GroupId;
  readonly more: boolean;
  readonly boxes: readonly string[];
}

const SUBMISSION: Readonly<Record<string, readonly [box: string, more: boolean]>> = {
  submitLabel: ['submit-label', false],
  store: ['store', false],
  storeDays: ['store', false],
  approval: ['approval', false],
  entryTitle: ['entry-title', false],
  nextLabel: ['next-label', true],
  backLabel: ['back-label', true],
  summaryPage: ['summary-page', true],
  progress: ['progress', true],
};

const AVAILABILITY: Readonly<Record<string, string>> = {
  opensAt: 'opens-at',
  closesAt: 'closes-at',
  maxEntries: 'max-entries',
  closedMessage: 'closed-message',
  members: 'access',
  memberGroups: 'member-groups',
};

const APPEARANCE: Readonly<Record<string, string>> = {
  theme: 'theme',
  requiredMarker: 'required-marker',
  validationSummary: 'validation-summary',
  choiceLayout: 'choice-layout',
  cssClass: 'css-class',
};

const DEVELOPER: Readonly<Record<string, string>> = {
  headless: 'headless',
  queryValues: 'query-values',
  contextValues: 'context-values',
};

/** The box id of a captcha provider's own setting, as the inspector's editors name a box by its path. */
export const captchaBox = (name: string) => `setting-captcha-${name.replace(/[^a-zA-Z0-9]/g, '-')}`;

const at = (group: GroupId, boxes: readonly string[], more = false): Anchor => ({ group, more, boxes });
const own = (table: Readonly<Record<string, string>>, name: string | number | undefined): string | undefined =>
  typeof name === 'string' && Object.hasOwn(table, name) ? table[name] : undefined;

/**
 * Where the setting a path of the definition names is in the view (`$.settings.availability.closesAt`), with the boxes
 * the focus tries; null for a path outside the settings, or for a setting this view doesn't show yet.
 */
export function anchorOf(path: string): Anchor | null {
  const steps = parsePath(path);
  if (!steps || steps[0] !== 'settings') return null;
  const [, group, name, inner, deeper] = steps;
  switch (group) {
    case 'submission': {
      if (name === 'verification') {
        const box = inner === 'emailField' ? 'verification-field' : inner === 'days' ? 'verification-days' : null;
        return at('submission', box ? [box, 'verification'] : ['verification']);
      }
      if (name === 'entryReference') {
        const box = inner === 'year' ? 'reference-year' : inner === 'digits' ? 'reference-digits' : 'reference-prefix';
        return at('submission', [box], true);
      }
      const found = typeof name === 'string' && Object.hasOwn(SUBMISSION, name) ? SUBMISSION[name] : undefined;
      return found ? at('submission', [found[0]], found[1]) : null;
    }
    case 'availability': {
      const box = own(AVAILABILITY, name);
      return box ? at('availability', [box]) : null;
    }
    case 'protection':
      if (name === 'contentRules') return at('protection', ['content-rules']);
      if (name === 'captcha' && inner === 'settings' && typeof deeper === 'string')
        return at('protection', [captchaBox(deeper), 'protection']);
      return name === 'level' || name === 'captcha' ? at('protection', ['protection']) : null;
    case 'appearance': {
      const box = own(APPEARANCE, name);
      return box ? at('appearance', [box]) : null;
    }
    case 'developer': {
      const box = own(DEVELOPER, name);
      return box ? at('developer', [box]) : null;
    }
    default:
      return null;
  }
}

// ---- What the boxes hold --------------------------------------------------------------------------------------------

/**
 * The email fields that email verification can confirm, in the form's order: those outside repeatable groups whose
 * address is kept until someone confirms it, past any run of the actions.
 */
export function verificationFields(definition: DesignDefinition): DesignField[] {
  return definition.pages.flatMap((page) =>
    page.groups.flatMap((group) =>
      group.fields.filter((field) => field.type === 'email' && field['keep'] !== 'untilActionsRun'),
    ),
  );
}

/** Why a list of names can't be kept: one that is no name, one the package keeps, or one twice. */
export type NamesProblem =
  | { readonly kind: 'notAName'; readonly name: string }
  | { readonly kind: 'reserved'; readonly name: string }
  | { readonly kind: 'twice'; readonly name: string };

/** The names a box holds, one a line, as the server checks them: letters, digits, hyphens and underscores. */
export function parseNames(
  raw: string,
  longest: number,
  reserved: string | null,
): { readonly ok: true; readonly names: string[] } | { readonly ok: false; readonly problem: NamesProblem } {
  const names = raw
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter((line) => line !== '');
  const seen = new Set<string>();
  for (const name of names) {
    if (name.length > longest || !/^[A-Za-z0-9_-]+$/.test(name))
      return { ok: false, problem: { kind: 'notAName', name } };
    if (reserved && name.toLowerCase().startsWith(reserved.toLowerCase()))
      return { ok: false, problem: { kind: 'reserved', name } };
    if (seen.has(name.toLowerCase())) return { ok: false, problem: { kind: 'twice', name } };
    seen.add(name.toLowerCase());
  }
  return { ok: true, names };
}

/** A whole number typed into a box, within its bounds; undefined for an empty box, null for one that is no such number. */
export function parseWhole(raw: string, least: number, most: number): number | undefined | null {
  const text = raw.trim();
  if (text === '') return undefined;
  if (!/^\d+$/.test(text)) return null;
  const value = Number(text);
  return value >= least && value <= most ? value : null;
}

/** An entry reference as visitors get it: the prefix, the year and a hyphen where it has one, then the padded number. */
export const referenceOf = (prefix: string, year: number | null, digits: number, number: number): string =>
  `${prefix}${year === null ? '' : `${year}-`}${String(number).padStart(Math.min(10, Math.max(1, digits)), '0')}`;

/** The protection a form chooses: the site's, a level, or a captcha provider by its name. */
export type ProtectionChoice =
  | { readonly kind: 'level'; readonly level: 'inherit' | 'standard' | 'proofOfWork' }
  | { readonly kind: 'captcha'; readonly provider: string };

/** The protection the definition chooses. */
export function protectionOf(definition: DesignDefinition): ProtectionChoice {
  const protection = groupOf(definition, 'protection');
  const level = wordIn(protection, 'level', 'inherit');
  if (level === 'captcha')
    return { kind: 'captcha', provider: lineIn(objectIn(protection, 'captcha') ?? {}, 'provider') };
  return { kind: 'level', level: level === 'standard' || level === 'proofOfWork' ? level : 'inherit' };
}

/** A choice's value in the view's list of choices. */
export const choiceValue = (choice: ProtectionChoice) =>
  choice.kind === 'captcha' ? `captcha:${choice.provider}` : choice.level;

/** The definition with a protection chosen: a provider keeps its settings while it stays the one chosen. */
export function withProtection(definition: DesignDefinition, choice: ProtectionChoice): DesignDefinition {
  const before = protectionOf(definition);
  const leveled = withSettingAt(
    definition,
    ['protection', 'level'],
    choice.kind === 'captcha' ? 'captcha' : choice.level,
  );
  if (choice.kind === 'level') return withSettingAt(leveled, ['protection', 'captcha'], undefined);
  if (before.kind === 'captcha' && before.provider === choice.provider) return leveled;
  return withSettingAt(leveled, ['protection', 'captcha'], { provider: choice.provider });
}

// ---- The closed groups' lines ---------------------------------------------------------------------------------------

/** What a group's line needs from the view: its words, and a date and a number as the user reads them. */
export interface SummaryWords {
  /** A term of the Settings view's dictionary area, without its `jfbSettings_`. */
  readonly term: (key: string, ...args: unknown[]) => string;
  /** An instant as the user reads it, on the site's clock. */
  readonly date: (instant: string) => string;
  /** A number as the user's language writes it. */
  readonly number: (value: number) => string;
}

/** The dot between the parts of a closed group's line. */
export const DOT = String.fromCharCode(0xb7);

/** The site's protection, as "Site default" names it: a level, or the provider's label. */
export function siteProtection(
  choices: FormSettingsChoicesResponseModel,
  providers: readonly CaptchaProviderResponseModel[],
  term: SummaryWords['term'],
): string {
  const { protection, captchaProvider } = choices.defaults;
  if (protection === 'captcha') {
    const provider = providers.find((one) => one.name === captchaProvider);
    return provider?.label ?? captchaProvider ?? term('protection_captcha');
  }
  return term(protection === 'proofOfWork' ? 'protection_proofOfWork' : 'protection_standard');
}

/** A theme's name by its alias; the alias where the site has no such theme. */
export const themeName = (choices: FormSettingsChoicesResponseModel, alias: string): string =>
  choices.themes.find((theme) => theme.alias === alias)?.label ?? alias;

/** The line a closed group says of what is set: a few words for each setting that tells the form apart. */
export function summaryOf(
  group: GroupId,
  definition: DesignDefinition,
  choices: FormSettingsChoicesResponseModel,
  providers: readonly CaptchaProviderResponseModel[],
  words: SummaryWords,
): string {
  const { term } = words;
  const parts: string[] = [];
  switch (group) {
    case 'submission': {
      const submission = groupOf(definition, 'submission');
      parts.push(term(wordIn(submission, 'store', 'always') === 'never' ? 'summary_notStored' : 'summary_stored'));
      if (wordIn(submission, 'approval', 'automatic') === 'manual') parts.push(term('summary_approval'));
      if (objectIn(submission, 'verification')) parts.push(term('summary_verification'));
      break;
    }
    case 'availability': {
      const availability = groupOf(definition, 'availability');
      const opens = isInstant(availability['opensAt']) ? words.date(availability['opensAt']) : null;
      const closes = isInstant(availability['closesAt']) ? words.date(availability['closesAt']) : null;
      parts.push(
        opens && closes
          ? term('summary_openBetween', opens, closes)
          : opens
            ? term('summary_opens', opens)
            : closes
              ? term('summary_openUntil', closes)
              : term('summary_alwaysOpen'),
      );
      const limit = numberIn(availability['maxEntries']);
      parts.push(limit === undefined ? term('summary_noLimit') : term('summary_limit', words.number(limit), limit));
      const access = wordIn(availability, 'members', 'anyone');
      if (access === 'members') parts.push(term('summary_members'));
      if (access === 'groups') parts.push(term('summary_groups', namesIn(availability, 'memberGroups').length));
      break;
    }
    case 'protection': {
      const choice = protectionOf(definition);
      if (choice.kind === 'captcha')
        parts.push(providers.find((one) => one.name === choice.provider)?.label ?? choice.provider);
      else if (choice.level === 'inherit') parts.push(term('siteDefault', siteProtection(choices, providers, term)));
      else parts.push(term(`protection_${choice.level}`));
      const rules = wordIn(groupOf(definition, 'protection'), 'contentRules', 'inherit');
      if (rules !== 'inherit') parts.push(term(`summary_content_${rules}`));
      break;
    }
    case 'appearance': {
      const appearance = groupOf(definition, 'appearance');
      const theme = lineIn(appearance, 'theme');
      parts.push(term('summary_theme', themeName(choices, theme || choices.defaults.theme)));
      const marker = wordIn(appearance, 'requiredMarker', 'inherit');
      parts.push(term(`summary_marker_${marker === 'inherit' ? choices.defaults.requiredMarker : marker}`));
      break;
    }
    case 'developer': {
      const developer = groupOf(definition, 'developer');
      parts.push(term(flagIn(developer, 'headless', false) ? 'summary_headlessOn' : 'summary_headlessOff'));
      const query = namesIn(developer, 'queryValues').length;
      const context = namesIn(developer, 'contextValues').length;
      if (query > 0) parts.push(term('summary_queryValues', query));
      if (context > 0) parts.push(term('summary_contextValues', context));
      break;
    }
  }
  return parts.join(` ${DOT} `);
}
