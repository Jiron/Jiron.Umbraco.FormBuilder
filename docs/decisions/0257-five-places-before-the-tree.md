# 0257. Five places before the tree, each with tabs along its top

- Status: accepted. Written while the owner had paused Phase 22 after 22.1
  ([ADR-0255](0255-phase-22-in-one-stretch.md)); the owner said to go on on 2026-11-18. It builds decision 2 of [ADR-0254](0254-a-simpler-backoffice.md). It turns round parts of
  [ADR-0021](0021-forms-section-structure.md) and [ADR-0165](0165-the-section-shell.md): the groups under headings
  below the tree, and one sidebar item for every screen
- Date: 2026-11-16
- Roadmap step: 22.2

## Context

The section's sidebar holds the tree of forms under Umbraco's own header, and below it five headings with seventeen
items, one for each screen (ADR-0021). The owner found it a list to scroll: "at the left you can only click on
settings", and "once you're in that page, you can select the sub categories". The second look added what makes it
worse: the items come after the tree, so with about twenty forms not one of them is on the screen at 900 px height,
the Inbox with its count of new entries among them
([simpler-backoffice.md](../ux/simpler-backoffice.md), D1 and D2).

The earlier reasons, which this record has to answer:

- **One item for every screen:** each screen is one click away, and the sidebar names it, so a screen needs no title
  of its own.
- **The tree first:** it sits under Umbraco's own header, where its users look for it.
- **Headings above the items:** seventeen items need grouping.

Every screen is a workspace of its own today, at its own address (`workspace/jfb-settings-email`), drawn in
Umbraco's body layout without a header. Links in mails the product has sent, in the Overview and in "Fix it" hold
those addresses.

## Options

1. **One workspace for each place, with Umbraco's workspace views** (`workspace/jfb-settings/view/email`). The tabs
   are Umbraco's own. Every screen would give up its own footer for workspace actions or sit in a second layout
   inside the first, every address would change, and each old one would need a workspace that only sends on.
2. **A section view for each place.** Umbraco shows a section's views as tabs only when the section opens on them,
   and they are the section's, not a sidebar item's.
3. **The screens stay workspaces at their addresses, and each shows its place's tabs in its own header:** Umbraco's
   tab group in Umbraco's body layout, each tab a link to its sibling's address. Chosen. On the screen it is what a
   workspace's views are; underneath nothing moves, so every address stays good by being the same address, every
   screen keeps its footer and its question on leaving, and a tab is refused to who may not open it exactly as the
   screen was.

## Decision

Option 3.

- **The sidebar starts with five places, without a heading:** Inbox, with its count of unseen entries, Library,
  Settings, Privacy, Activity. They are one navigation list, named for screen readers. The tree of forms follows
  under Umbraco's header with its "+" and "…", the Recycle Bin last. What grows comes after what is fixed (the
  fourth rule of ADR-0254): with fifty forms the five are on the screen.
- **A place shows to a user who may open any of its tabs,** leads to the first of them the user may open, and is
  marked as the open one on each of its tabs' screens and on the screens opened from them (an option list, a theme,
  an email layout, a template).
- **The tabs,** in each screen's header where a headline would stand, since the sidebar names the place and the tab
  names the screen (said once, as ADR-0021 had it):

  | Place | Tabs |
  | --- | --- |
  | Inbox | Inbox · Spam |
  | Library | Option lists · Themes · Email layouts · Templates |
  | Settings | General · Email · Spam protection · Files · Access · Integrations |
  | Privacy | People · Retention · Data collection |
  | Activity | Deliveries · Audit log |

  A tab shows only to who may open it (ADR-0013). They are Umbraco's tab group, so the keyboard and the folding of
  tabs that do not fit are Umbraco's. A place with one tab left for a user shows no tabs. Each screen has a heading
  for screen readers that names its place and tab.
- **Save saves the open tab.** Leaving a tab with unsaved changes asks first, as leaving the screen did, so no other
  tab can hold changes. The button names what it saves ("Save email settings"), since a row of tabs above it could
  be read as one form.
- **"Settings" keeps its name** in the sidebar and as a form's view. The owner's own word for the place is
  "settings", a form's view stands inside the form it belongs to, and running text always names the place with it
  ("Settings › Email", "the form's Settings"), which the copy guide records. The group "Entries" is gone, so texts
  that named "Entries › Inbox" name "Inbox".
- **In the code** the table of `section/sidebar.ts` holds the places with their tabs, and the manifests, the tabs
  and the sidebar are all drawn from it.

The earlier reasons: a screen is two clicks away instead of one, and one when it is the place's first tab; the tree
keeps Umbraco's header and loses only its place at the top; and five items need no headings.

## Consequences

- **A tab is a move to another workspace.** The header is drawn the same on each, at once, so it does not flicker;
  the permissions the tabs need are already read when the section opens.
- **The top tasks grow by at most one click** where a screen was a sidebar item and is now a place's second tab;
  they are counted again.
- **Nothing server-side changes:** the addresses `BackofficeLinks` builds are the ones the screens keep.
- The browser tests that opened a screen by its sidebar item open it by its place and tab;
  [information-architecture.md](../ux/information-architecture.md) section 1, the copy guide and the guides follow.
