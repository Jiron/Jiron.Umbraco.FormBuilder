// What the entry panel says of an entry (ROADMAP 11.2, IA 6.3, ADR-0208): the actions of its state as the user may use
// them, its details line, a file's size, its timeline in order, an edit's problems and what a move back or an edit asks
// first. Pure, so the panel, the list and the tests say each thing alike.
import type { EntryMoveAsksResponseModel, EntryPanelDetailsResponseModel, EntryTimelineResponseModel } from '../api';
import { receivedText, stateText, type Term } from './entry-list-model';

/** An action of the panel's bar. */
export type PanelAction =
  'approve' | 'reject' | 'markSpam' | 'notSpam' | 'forward' | 'delete' | 'print' | 'rerun' | 'backToWaiting';

/** The permission each action needs on the form. */
const PANEL_NEEDS: Record<PanelAction, string> = {
  approve: 'editEntries',
  reject: 'editEntries',
  markSpam: 'editEntries',
  notSpam: 'editEntries',
  forward: 'editEntries',
  delete: 'deleteEntries',
  print: 'export',
  rerun: 'manageActions',
  backToWaiting: 'editEntries',
};

/** The state a move of the panel reaches. */
export const PANEL_MOVE: Partial<Record<PanelAction, string>> = {
  approve: 'approved',
  reject: 'rejected',
  markSpam: 'spam',
  notSpam: 'submitted',
  backToWaiting: 'submitted',
};

/** The bar of an entry: its primary action, up to three shown beside it, and the rest in the "...". */
export interface PanelBar {
  readonly primary?: PanelAction;
  readonly visible: readonly PanelAction[];
  readonly more: readonly PanelAction[];
}

/**
 * The bar of IA 6.3's table, by the entry's state: waiting for approval on a form that approves by hand, Approve with
 * Reject, Mark as spam and Forward..., then Delete... and Re-run actions; in Spam, Not spam with Delete...; any other,
 * Mark as spam, Forward... and Delete..., then Back to waiting for approval (approved or rejected, approved by hand) and
 * Re-run actions. Print, for a user who may export, waits in the "..." in every state (IA 6.3, ADR-0210). The user sees
 * only what they may use, the first three of it beside the primary. While a legal hold keeps the entry, Delete... is not
 * there: the panel says the hold instead (ADR-0222).
 */
export function panelBar(
  state: string,
  manualApproval: boolean,
  permissions: readonly string[],
  held = false,
): PanelBar {
  const may = (action: PanelAction) => permissions.includes(PANEL_NEEDS[action]) && !(held && action === 'delete');
  let primary: PanelAction | undefined;
  let order: PanelAction[];
  if (state === 'spam') {
    primary = 'notSpam';
    order = ['delete', 'print'];
  } else if (state === 'submitted' && manualApproval) {
    primary = 'approve';
    order = ['reject', 'markSpam', 'forward', 'delete', 'print', 'rerun'];
  } else {
    const back: PanelAction[] =
      manualApproval && (state === 'approved' || state === 'rejected') ? ['backToWaiting'] : [];
    const rerun: PanelAction[] = state === 'unverified' ? [] : ['rerun'];
    order = ['markSpam', 'forward', 'delete', ...back, 'print', ...rerun];
  }

  const allowed = order.filter(may);
  const visible: PanelAction[] = allowed.filter((action) => action !== 'print').slice(0, 3);
  return {
    ...(primary && may(primary) ? { primary } : {}),
    visible,
    more: allowed.filter((action) => !visible.includes(action)),
  };
}

/** The details line: the page it was sent from, the language and the time to complete, each where it is known. */
export function detailsLine(details: EntryPanelDetailsResponseModel, ui: string, term: Term, isTest = false): string[] {
  const parts: string[] = [];
  // A test entry, from the preview, is opened only through a link of its own mail: it says so first.
  if (isTest) parts.push(term('jfbEntries_details_test'));
  if (details.page) parts.push(term('jfbEntries_details_sentFrom', pathOf(details.page)));
  parts.push(languageName(details.culture, ui));
  if (details.duration !== null && details.duration !== undefined) {
    parts.push(
      details.duration < 60
        ? term('jfbEntries_details_quick')
        : term('jfbEntries_details_minutes', Math.round(details.duration / 60)),
    );
  }

  return parts;
}

/** A page's address as the panel names it: its path, which is what a person recognises. */
export function pathOf(address: string): string {
  try {
    const url = new URL(address);
    return url.pathname + url.search;
  } catch {
    return address;
  }
}

/** A culture's language in the user's own: "English", "German (Switzerland)". */
export function languageName(culture: string, ui: string): string {
  try {
    return new Intl.DisplayNames([ui], { type: 'language' }).of(culture) ?? culture;
  } catch {
    return culture;
  }
}

/** A file's size, rounded as people read it: "380 KB", "1.2 MB". */
export function sizeText(bytes: number, culture: string): string {
  const format = (value: number, unit: string, digits: number) =>
    `${new Intl.NumberFormat(culture, { maximumFractionDigits: digits }).format(value)} ${unit}`;
  if (bytes < 1024) return format(bytes, 'B', 0);
  if (bytes < 1024 * 1024) return format(bytes / 1024, 'KB', 0);
  return format(bytes / (1024 * 1024), 'MB', 1);
}

/** A line of the timeline. */
export interface TimelineLine {
  readonly id: string;
  readonly when: string;
  readonly icon: string;
  readonly text: string;
  /** What an edit changed, a line each. */
  readonly changes: readonly string[];
  readonly tone: 'default' | 'danger' | 'warning';
  /** A run a user with manageActions may retry: failed, or uncertain after asking. */
  readonly retry?: { readonly run: number; readonly uncertain: boolean; readonly name: string };
}

/** A run's icon by its state: done, not done, or still to come (a clock, as for one waiting or running). */
const RUN_ICONS: Record<string, string> = {
  succeeded: 'icon-check',
  skipped: 'icon-check',
  failed: 'icon-alert',
  uncertain: 'icon-alert',
};

const EVENT_ICONS: Record<string, string> = {
  submitted: 'icon-inbox',
  opened: 'icon-eye',
  stateChanged: 'icon-flag',
  undone: 'icon-undo',
  notSpam: 'icon-check',
  forwarded: 'icon-share',
  answersEdited: 'icon-edit',
  actionsRerun: 'icon-refresh',
  held: 'icon-lock',
  released: 'icon-unlocked',
  anonymised: 'icon-user',
};

/**
 * The day a legal hold ends, as the panel and its timeline name it (ADR-0222): its end is the start of a day in UTC, so the
 * day is read in UTC, the day it was set for wherever the reader is.
 */
export const holdEnd = (iso: string, culture: string): string =>
  new Intl.DateTimeFormat(culture, { day: 'numeric', month: 'long', year: 'numeric', timeZone: 'UTC' }).format(
    new Date(iso),
  );

const QUIET_EVENTS = new Set(['verificationExpires', 'draftExpires']);

/**
 * The timeline as the panel lists it, newest first: each event in the user's words with who did it, each action run with
 * its state and Retry where it failed or may not have arrived, and each mail with its recipient and state.
 */
export function timelineLines(
  timeline: EntryTimelineResponseModel,
  culture: string,
  now: Date,
  term: Term,
): TimelineLine[] {
  const who = (user: string | null | undefined) => user ?? term('jfbEntries_tl_someone');
  const lines: TimelineLine[] = [];
  for (const event of timeline.events) {
    if (QUIET_EVENTS.has(event.kind)) continue;
    let text: string;
    switch (event.kind) {
      case 'submitted':
        text = term('jfbEntries_tl_submitted');
        break;
      case 'opened':
        text = term('jfbEntries_tl_opened', who(event.user));
        break;
      case 'stateChanged': {
        const reached = stateText(event.to ?? '', term);
        text = event.user ? term('jfbEntries_tl_stateBy', reached, event.user) : term('jfbEntries_tl_state', reached);
        break;
      }
      case 'undone':
        text = term('jfbEntries_tl_undone', who(event.user));
        break;
      case 'notSpam':
        text = term('jfbEntries_tl_notSpam', who(event.user));
        break;
      case 'actionsRerun':
        text = term('jfbEntries_tl_actionsRerun', event.count ?? 0, who(event.user));
        break;
      case 'forwarded':
        // An erase blanks the recipient of a forward to the person it erased (ADR-0223).
        text = event.recipient
          ? term('jfbEntries_tl_forwarded', event.recipient, who(event.user))
          : term('jfbEntries_tl_forwardedNobody', who(event.user));
        break;
      case 'held':
        text = event.until
          ? term('jfbEntries_tl_held', holdEnd(event.until, culture), who(event.user))
          : term('jfbEntries_tl_heldNoEnd', who(event.user));
        break;
      case 'released':
        text = term('jfbEntries_tl_released', who(event.user));
        break;
      case 'anonymised':
        text = term('jfbEntries_tl_anonymised', who(event.user));
        break;
      case 'answersEdited':
        text = term('jfbEntries_tl_answersEdited', who(event.user));
        break;
      case 'valuesRemoved':
      case 'fileQuarantined':
      case 'verificationRequested':
      case 'verificationSent':
      case 'verified':
        text = term(`jfbEntries_tl_${event.kind}`);
        break;
      default:
        text = term('jfbEntries_tl_other');
    }

    lines.push({
      id: `event-${event.id}`,
      when: event.when,
      icon: EVENT_ICONS[event.kind] ?? 'icon-time',
      text,
      changes: event.changes.map((change) =>
        change.masked
          ? term('jfbEntries_tl_changeMasked', change.label)
          : term(
              'jfbEntries_tl_change',
              change.label,
              change.before.join(', ') || term('jfbEntries_none'),
              change.after.join(', ') || term('jfbEntries_none'),
            ),
      ),
      tone: 'default',
    });
  }

  for (const run of timeline.runs) {
    const name = run.name || run.type;
    const retryable = run.state === 'failed' || run.state === 'uncertain';
    lines.push({
      id: `run-${run.id}`,
      when: run.when,
      icon: RUN_ICONS[run.state] ?? 'icon-time',
      text: term('jfbEntries_run_line', name, term(`jfbEntries_run_${run.state}`)),
      changes: run.error && run.state === 'failed' ? [run.error] : [],
      tone: run.state === 'failed' ? 'danger' : run.state === 'uncertain' ? 'warning' : 'default',
      ...(retryable ? { retry: { run: run.id, uncertain: run.state === 'uncertain', name } } : {}),
    });
  }

  for (const mail of timeline.mail) {
    // A mail an action sent shows as its run; one of its own (a forward, a confirmation) shows here.
    if (mail.actionRun !== null && mail.actionRun !== undefined) continue;
    const kind = ['forward', 'verification', 'notice'].includes(mail.kind)
      ? term(`jfbEntries_mail_kind_${mail.kind}`)
      : term('jfbEntries_mail_kind');
    lines.push({
      id: `mail-${mail.id}`,
      when: mail.when,
      icon: 'icon-message',
      text: term('jfbEntries_mail_line', kind, mail.to, term(`jfbEntries_mail_${mail.state}`)),
      changes: [],
      tone: mail.state === 'failed' ? 'danger' : 'default',
    });
  }

  return lines
    .sort((left, right) => right.when.localeCompare(left.when))
    .map((line) => ({ ...line, when: receivedText(line.when, now, culture, term) }));
}

/** What an edit's problem says under its field. */
export function problemText(code: string, term: Term): string {
  return ['required', 'invalid', 'maxLength', 'digits'].includes(code)
    ? term(`jfbEntries_problem_${code}`)
    : term('jfbEntries_problem_other');
}

/** The field an edit's problem is at, by the answer's place in the request (`answers[2].values`). */
export function problemAnswer(path: string): number | undefined {
  const found = /^answers\[(\d+)\]/.exec(path);
  return found ? Number(found[1]) : undefined;
}

/** What a move back or an edit asks first, a sentence for each option and answer. */
export function asksLines(asks: EntryMoveAsksResponseModel, term: Term): string[] {
  return [
    ...asks.full.map((full) => term('jfbEntries_asks_full', full.optionLabel, full.fieldLabel, full.places)),
    ...asks.held.map((held) => term('jfbEntries_asks_held', held.fieldLabel)),
  ];
}
