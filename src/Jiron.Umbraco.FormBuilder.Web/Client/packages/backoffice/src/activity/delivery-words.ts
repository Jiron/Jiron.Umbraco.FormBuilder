// A delivery in words (ROADMAP 12.4b, IA 9, ADR-0219): its state and tone, whom it went to, what it is, when, and what its
// details say happened. A run's failure is said as the Actions view says it (ADR-0195); a mail held back names its limit.
import type { DeliveryDetailResponseModel, DeliveryResponseModel } from '../api';
import { atTime, happenedOf, triesOf } from '../designer/actions/run-words';
import { dateOf, whenOf } from './activity-words';

type Term = (key: string, ...args: unknown[]) => string;

/** How a state's tag looks: a problem in the danger or warning colour, a delivery in the positive one. */
export type Tone = 'default' | 'positive' | 'warning' | 'danger';

/** The states a list counts as problems (ADR-0219). */
const PROBLEMS = new Set(['failed', 'tryingAgain', 'uncertain', 'limited']);

/** Whether a delivery is a problem: failed, trying again, uncertain or limited. */
export const isProblem = (delivery: DeliveryResponseModel): boolean => PROBLEMS.has(delivery.state);

/** The tone of a state's tag. */
export function toneOf(state: string): Tone {
  switch (state) {
    case 'failed':
    case 'tryingAgain':
      return 'danger';
    case 'uncertain':
    case 'limited':
      return 'warning';
    case 'delivered':
      return 'positive';
    default:
      return 'default';
  }
}

/** Whom it went to: a mail's first recipient with how many more, a webhook's host or chat, or '' for another action. */
export function toOf(delivery: DeliveryResponseModel, term: Term): string {
  if (delivery.kind === 'email') {
    const [first = '', ...more] = delivery.recipients;
    return more.length === 0 ? first : term('toMore', first, more.length);
  }
  const target = delivery.target;
  if (!target) return delivery.actionType === 'webhook' ? term('to_unknown') : '';
  switch (target.kind) {
    case 'host':
      return target.text ?? term('to_unknown');
    case 'reference':
      return target.text ? term('to_secret', target.text) : term('to_unknown');
    case 'slack':
      return term('to_slack');
    case 'teams':
      return term('to_teams');
    default:
      return term('to_unknown');
  }
}

/**
 * What it is: the action's name, or its type's where it has none, or for a mail of no action what kind of mail it is.
 * `typeName` turns a type's name, which may be a `#key`, into words.
 */
export function whatOf(delivery: DeliveryResponseModel, term: Term, typeName: (name: string) => string): string {
  if (delivery.actionName) return delivery.actionName;
  if (delivery.kind === 'email') {
    const kind = delivery.emailKind ?? 'action';
    return term(`mail_${kind}`) === `mail_${kind}` ? term('mail_action') : term(`mail_${kind}`);
  }
  return delivery.typeName ? typeName(delivery.typeName) : term('otherType');
}

/** When it was queued, and when it is tried next where it waits for another try after a failed one. */
export function whenOfDelivery(delivery: DeliveryResponseModel, now: Date, culture: string, term: Term): string {
  const queued = dateOf(delivery.createdUtc);
  const next = dateOf(delivery.nextAttemptUtc);
  const said = queued ? whenOf(queued, now, culture) : '';
  return delivery.state === 'tryingAgain' && next ? term('whenNext', said, whenOf(next, now, culture)) : said;
}

/** A delivery's details' headline: a mail to its recipient, a webhook to its target, or the action itself. */
export function headlineOf(delivery: DeliveryResponseModel, term: Term, typeName: (name: string) => string): string {
  const to = toOf(delivery, term);
  if (delivery.kind === 'email') return term('headlineEmail', to);
  if (delivery.actionType === 'webhook') return term('headlineWebhook', to);
  return whatOf(delivery, term, typeName);
}

/** What a delivery's details say happened, line by line, and its tries; nothing for one that went or waits. */
export interface DeliveryStory {
  readonly lines: readonly string[];
  readonly tries: string;
}

/**
 * The story of a delivery: `term` is the activity area's, `runTerm` the Actions view's, whose words say what happened to a
 * run.
 */
export function storyOf(
  detail: DeliveryDetailResponseModel,
  now: Date,
  culture: string,
  term: Term,
  runTerm: Term,
): DeliveryStory {
  const delivery = detail.delivery;
  const tries = () => triesOf(delivery, now, culture, runTerm);
  switch (delivery.state) {
    case 'failed':
      return { lines: [happenedOf(delivery, runTerm)], tries: tries() };
    case 'tryingAgain': {
      const next = dateOf(delivery.nextAttemptUtc);
      const then =
        next && next > now ? term('tryingAgain', atTime(next, now, culture, runTerm)) : term('tryingAgainSoon');
      return { lines: [happenedOf(delivery, runTerm), then], tries: tries() };
    }
    case 'uncertain':
      return {
        lines: [term(delivery.kind === 'email' ? 'uncertainEmail' : 'uncertainRun'), happenedOf(delivery, runTerm)],
        tries: tries(),
      };
    case 'limited': {
      const limit = detail.limit;
      if (!limit) return { lines: [], tries: '' };
      const held =
        limit.kind === 'recipient'
          ? term('limitedRecipient', limit.count, delivery.recipients[0] ?? '')
          : term('limitedSite', limit.count);
      return { lines: [held, term('limitedWhy', limit.key)], tries: '' };
    }
    case 'skipped': {
      const code = delivery.errorCode ?? '';
      const known = ['stopped', 'undone', 'superseded'].includes(code);
      return { lines: [term(known ? `skipped_${code}` : 'skipped_other')], tries: '' };
    }
    default:
      return { lines: [], tries: '' };
  }
}
