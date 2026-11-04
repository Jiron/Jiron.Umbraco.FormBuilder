// The form workspace's views by their path (ROADMAP 9.3a): a move to another view of the open form, as its tab makes
// one, for the links between them (the outcomes in Settings, a Fix it in Design).

/** The address of a view of the form open at this address: its `/view/<name>` in place of the one shown. */
export const viewPath = (pathname: string, view: string): string =>
  pathname.replace(/\/view\/[^/]+\/?$/, '').replace(/\/$/, '') + `/view/${view}`;

/** Shows a view of the open form, unless it is the one shown. */
export function goToView(view: string) {
  const path = viewPath(window.location.pathname, view);
  if (path === window.location.pathname) return;
  history.pushState(null, '', path);
  window.dispatchEvent(new PopStateEvent('popstate'));
}
