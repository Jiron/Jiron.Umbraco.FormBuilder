// The form workspace's views by their path (ROADMAP 9.3a): a move to another view of the open form, as its tab makes
// one, for the links between them (a Fix it in Design, Settings or Actions).
import type { FixTarget } from './check/form-check';

/**
 * The view the form's own address shows, without a view in it, to a user who designs: Design, the first of the views
 * (manifests.ts).
 */
export const FIRST_VIEW = 'design';

/**
 * The views besides Design that take up a Fix it handed on to them once they are shown (ADR-0184): only these are
 * handed one, so none waits for a view still to come: the Settings view (10.1) and the Actions view (10.2a).
 */
export const FIXING_VIEWS: ReadonlySet<string> = new Set(['settings', 'actions']);

/** The views that change the form, which the form's Preview, Save draft and Publish... act on (ADR-0258). */
const CHANGING_VIEWS: ReadonlySet<string> = new Set([FIRST_VIEW, 'settings', 'actions']);

/** Whether a view changes the form: the footer's buttons show there, and the footer is drawn. */
export const changesForm = (view: string): boolean => CHANGING_VIEWS.has(view);

const VIEW_AT_END = /\/view\/([^/]+)\/?$/;

/** The address of a view of the form open at this address: its `/view/<name>` in place of the one shown. */
export const viewPath = (pathname: string, view: string): string =>
  pathname.replace(VIEW_AT_END, '').replace(/\/$/, '') + `/view/${view}`;

/**
 * The view this address shows: the one it names, or the first at the form's own address. What a view adds after its
 * own name (a dialog Umbraco opens by an address) leaves it the view shown.
 */
export const shownView = (pathname: string): string => /\/view\/([^/]+)/.exec(pathname)?.[1] ?? FIRST_VIEW;

/**
 * Shows a view of the open form, unless it is the one shown. Umbraco's router builds the view anew at a new address,
 * even one that shows the same view, so the view shown stays as it is.
 */
export function goToView(view: string) {
  const { pathname } = window.location;
  if (shownView(pathname) === view) return;
  history.pushState(null, '', viewPath(pathname, view));
  window.dispatchEvent(new PopStateEvent('popstate'));
}

/**
 * Leads a Fix it to the view that makes it: the one it names for the form's settings or actions, Design for any other
 * (an outcome's too, ADR-0256). It is
 * handed on (`request`) first, to be taken up once that view is shown, only where that view takes it up.
 */
export function followFix(fix: FixTarget, request: (fix: FixTarget) => void) {
  if (fix.kind !== 'view' || FIXING_VIEWS.has(fix.view)) request(fix);
  goToView(fix.kind === 'view' ? fix.view : 'design');
}

/**
 * How long the router stays still before a sidebar opens for a Fix it handed on from another view (ADR-0227). Umbraco
 * closes every sidebar not opened by an address as a navigation ends, and the router's last move comes a few ms after
 * the view shows (its modal routes, 11 to 36 ms in the browser tests under load): several times that, and below the
 * sidebar's own 250 ms slide.
 */
export const ROUTER_QUIET = 200;

/** The longest wait, for a router that never stays still: the Fix it is not held up for good. */
export const ROUTER_WAIT = 2000;

/** What the router does, on `window`: the history's own moves and Umbraco's navigations. */
const ROUTER_MOVES = [
  'popstate',
  'pushstate',
  'replacestate',
  'changestate',
  'navigationstart',
  'navigationsuccess',
  'navigationcancel',
  'navigationerror',
  'navigationend',
] as const;

/**
 * Resolves once the router has made no move for `quiet` ms, or after `wait` ms. A sidebar opened for a Fix it that
 * came from another view waits for it: opened while the router still moves, it would be closed at once.
 */
export function routerSettled(target: EventTarget = window, quiet = ROUTER_QUIET, wait = ROUTER_WAIT): Promise<void> {
  return new Promise((done) => {
    let still: ReturnType<typeof setTimeout> | undefined;
    const finish = () => {
      for (const move of ROUTER_MOVES) target.removeEventListener(move, moved);
      clearTimeout(still);
      clearTimeout(longest);
      done();
    };
    const moved = () => {
      clearTimeout(still);
      still = setTimeout(finish, quiet);
    };
    const longest = setTimeout(finish, wait);
    for (const move of ROUTER_MOVES) target.addEventListener(move, moved);
    moved();
  });
}
