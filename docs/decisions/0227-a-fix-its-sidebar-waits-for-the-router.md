# 0227. A sidebar opened for a Fix it from another view waits until the router stays still

- Status: accepted; amends [ADR-0184](0184-the-form-settings-view.md) (a Fix it handed on to the view that makes it) and
  [ADR-0188](0188-the-actions-view.md) (the Actions view's Fix it)
- Date: 2026-11-10
- Roadmap step: 13.1, found by its phase-review gate

## Context

A Fix it in the form check or the publish dialog can lead to another view: an action's setting to the Actions view, an
outcome's to the Settings view, a rule to the Design view. The view it leads to takes the Fix it up once it has read
what it shows, and then opens the sidebar on the setting: the action's, the outcome's, or the rules builder.

Umbraco's modal manager closes every sidebar that no address opened as soon as a navigation ends (`navigationsuccess`),
and the router's last move comes after the new view is already on the page: the workspace editor's slot shows the view,
and then its modal routes settle, 11 to 36 ms later in the browser tests. A view that reads quickly, as a machine under
load lets it, opened its sidebar inside that gap, and the router's last move closed it again at once. The phase-review
gate on 688d643 failed on it twice (`webhook-editors.spec.ts`, `content-editors.spec.ts`); one copy in three of a probe
run on three workers failed the same way, and every copy passed on one worker.

## Decision

1. **A sidebar opened for a Fix it handed on from another view waits until the router has made no move for 200 ms**
   (`routerSettled` in `form-views.ts`): a history move, or a navigation's start, end, success, cancel or error, starts
   the wait again. 200 ms is several times the longest gap measured, and below the sidebar's own 250 ms slide.
2. **The wait ends after 2 s at the longest,** so a router that keeps moving never holds a Fix it for good.
3. **Only those sidebars wait:** the Actions view's action sidebar, the Settings view's outcome sidebar and the Design
   view's rules builder, each only for a Fix it handed on. A Fix it in the view that is shown opens at once, as no
   navigation follows it; a Fix it that only moves the focus to a box needs no wait.
4. **A view that is gone when the wait ends opens nothing,** as the user went elsewhere meanwhile.

The other ways were weaker. Umbraco's routable modals survive navigation, but each sidebar would need an address of its
own and the views' state in it; waiting for a fixed number of `navigationsuccess` events breaks with any change to how
many slots the backoffice nests; reopening a sidebar that a navigation closed would make it slide out and in again.

## Consequences

- A Fix it from another view opens its sidebar about 200 ms later than before, after the check's own sidebar has slid
  away and the new view has drawn.
- Unit tests check the wait's quiet time, its restart on each kind of move, its longest wait and that it stops
  listening; each of the three views has a test that fails without its wait. The browser tests of every Fix it ran three
  times on four workers.
