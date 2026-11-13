# 0244. The documentation, its Learn more links and its screenshots

- Status: accepted
- Date: 2026-11-13
- Roadmap step: 17.1 (17.2a, 17.2b, 17.3); §4.13 "say it once"

## Context

Phase 17 writes the documentation for editors, site owners and developers, links the backoffice's longer help to it,
and illustrates both with screenshots, while the repository is private: nobody can look at it on GitHub from here, and
the build has no image library and no real visitors. A few choices shape how the pages stay right as the code changes.

## Decisions

1. **The pages describe what is built, checked against the code and the running demo.** Each page was written from a
   reading of the code, then read by two fresh readers, one as a new editor and one as a new developer, whose questions
   were answered in the pages or, where the product was at fault, in the code: a form's alias was shown nowhere, the form
   block's dialog sent editors to document types, the system notices promised health warnings by mail, and a form's
   opening times named the wrong clock. Features not built yet (saving a form to finish later) are said to be not yet
   available rather than described.
2. **Learn more links come from one map in the client** (`docs/learn-more.json`, a base address and a page with its
   heading per topic), drawn by one element, `jfb-learn-more`, with link text that makes sense on its own in English and
   German and a hidden "opens in a new tab". A setting or notice gets a link only where its help needs more than two
   sentences. The link check reads the same map and fails when a page or heading it names is missing or spelt in other
   letter case, so a renamed heading breaks the build, not the link.
3. **The link check holds GitHub's rules offline:** every relative link and image names a file spelt with the same
   letter case as on disk (GitHub's paths are case-sensitive, Windows is not), every anchor a heading as GitHub makes
   its anchors, and the NuGet readme no relative link at all. The READMEs are rendered in Edge by a GitHub-flavoured
   renderer (dev-only, MIT) and looked at; the owner checks them on GitHub once the repository is public (OWNER-TODO).
4. **The screenshots are taken by a script on the demo, not by hand** (`scripts/screenshots.mjs`), so they can be taken
   again after any change to a screen. The demo brings forms but no entries, so the script sends entries through the
   public API as visitors' browsers do, each with a submission id derived from its name: a second run gets the same
   receipts and adds no entry. It publishes one form once more for the history's comparison only while that form has a
   single version. Every picture is 1600 by 900, 16:9 as the Marketplace asks; the phone's three states are shot in a
   phone's window at twice its size and set side by side on one 16:9 picture. Each page is framed so the whole form fits
   where it can and the window's top edge never cuts a line.
5. **PNGs are made smaller without a library** (`scripts/png.mjs`): the pixels are re-encoded unfiltered and with each
   row's best filter, and the smaller wins; an opaque alpha channel is dropped. A screenshot with text came out 20 to 25%
   smaller unfiltered than the browser's own encoding, which the per-row filters did not beat. A PNG with a chunk that
   changes how it looks (a transparent colour, gamma, a profile) is left as it is, and the script compares every
   rewritten picture with the browser's shot pixel by pixel before writing it.

## Consequences

- A screen that changes needs `node scripts/screenshots.mjs` on the demo (or `--only <names>`), and its pictures looked
  at before they are committed; the script leaves a picture of where a step failed in `.local/`.
- Renaming a documentation heading means updating `docs/learn-more.json` or the link check fails.
- The pages make claims about behaviour that tests do not check word by word; the phase reviews read them again against
  the code.
