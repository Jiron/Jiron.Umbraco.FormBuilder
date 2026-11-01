# 0151. Fitting the page and the website's JS API

- Status: accepted; builds the ground check and step folding of [ADR-0022](0022-default-theme-and-tokens.md)'s amendment
  and the JS API of section 4.9, on [ADR-0110](0110-the-runtimes-frame-its-chunk-and-the-reading-of-the-page.md)'s frame
- Date: 2026-11-01
- Roadmap step: 6.4l (decided and built); `next()`, `back()` and `jfb:page` come with 6.5d, and `consent` with 7.2c

## Context

Both fittings show before a visitor touches the form, while the logic loads only on the first touch (ADR-0110). A site's
code may ask for a form at any time, also before that touch, and a form that came in another's place (6.4j) was not woken
by the frame.

## Options

**Where the fitting runs:** in the logic's chunk, which would show a dark site's form in light ink until it is touched; or
in the frame. The frame, whose core budget has room for it (about 1 KB of 16).

**How a colour is read:** by parsing `getComputedStyle`'s text, which a browser may write as `oklch()` or `color()`, so
a site using those would be taken for a light one; or by painting it on a canvas of one pixel, which reads any colour the
browser knows.

**How a word's width is measured:** with a hidden element, which makes a layout for each word and needs a style; or with the
same canvas and the name's computed font. A letter spacing is not counted, which is a small margin on a check of this kind.

**What `submit()` does:** runs its own send, which would be a second path past the checks; or presses the button.

## Decision

- **The frame fits every form root of the markup contract,** an outcome and a notice among them, as they stand on the same
  ground; once each, on `init`.
- **The ground check** walks from the form's parent to the first fully opaque background. No opaque one is white, as a
  browser shows it; a colour that cannot be read goes on to the next. Dark is below a luminance of 0.179, where white text
  has more contrast than black. It adds `jfb-scheme-dark` only when no `jfb-scheme-*` class is on the form or an ancestor.
  When the system's colour scheme changes, it checks again, and it takes back only a class it gave itself. An outcome and a
  form that comes in another's place keep it.
- **Step folding** sets `data-jfb-compact` when a word of a step name is wider than its step, read with the attribute off, as
  folded steps show no name; on `init`, once the fonts are in, and on each change of width, which folding cannot cause. Steps
  a theme folded by itself stay as they are. A browser that cannot measure leaves them unfolded.
- **`get(form)`** returns a frozen object for a form of the contract with model data it can read, enhancing it if it was not,
  and null for anything else. Its methods load the logic when first asked and share it with a touch's:
  - `validate()` checks as a send does and shows what stops it, and answers true when the logic cannot load, as the server
    checks every send;
  - `submit()` presses the send button, so the checks, the send and its events are a visitor's, and answers false when the
    form shows no such button now, as on a page before its last;
  - `values()` answers as the entries endpoint's `values` would hold them.
- **Each runs with the form's own logic:** the chunk keeps a form's logic by form, so a form that came in another's place is
  reached too.

## Consequences

- The core grows by about 1 KB, and every page with a form reads a colour and a font's metrics at load, a few layouts' worth.
- A site that changes its ground in code without a system change has its form checked again only through `init` of a new form;
  it should set a scheme class (ADR-0106).
- A background image is not a ground: the first opaque colour behind it counts. A site with a dark picture behind a form sets
  a scheme class.
- **Revisit** if a site's letter spacing makes a name break where the check said it fits.
