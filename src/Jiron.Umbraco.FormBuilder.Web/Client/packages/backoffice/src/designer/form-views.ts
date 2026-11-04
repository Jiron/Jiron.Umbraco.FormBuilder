// The form workspace's views by their path (ROADMAP 9.3a): a move to another view of the open form, as its tab makes
// one, for the links between them (the outcomes in Settings, a Fix it in Design).

/**
 * The view the form's own address shows, without a view in it, to a user who designs: Design, the first of the views
 * (manifests.ts).
 */
export const FIRST_VIEW = 'design';

const VIEW_AT_END = /\/view\/([^/]+)\/?$/;

/** The address of a view of the form open at this address: its `/view/<name>` in place of the one shown. */
export const viewPath = (pathname: string, view: string): string =>
  pathname.replace(VIEW_AT_END, '').replace(/\/$/, '') + `/view/${view}`;

/** The view this address shows: the one it names, or the first at the form's own address. */
export const shownView = (pathname: string): string => VIEW_AT_END.exec(pathname)?.[1] ?? FIRST_VIEW;

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
