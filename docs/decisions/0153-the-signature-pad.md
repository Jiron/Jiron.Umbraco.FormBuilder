# 0153. The signature pad

- Status: accepted; builds the runtime's half of [ADR-0103](0103-a-signature-typed-without-javascript.md) on the uploads of
  [ADR-0152](0152-uploads-on-the-page.md)
- Date: 2026-11-02
- Roadmap step: 6.4n (decided and built); 6.5d brings it to a form of several pages, 11.2's entry panel shows the name

## Context

A signature is a PNG (ADR-0071). Without JavaScript a visitor types a name, which the server draws (ADR-0103). With the runtime a
visitor signs on a pad, or types a name, whose PNG the browser draws with its own fonts and which holds the name as the server's
does. An entry takes the signature's upload token, which names its challenge, so a post cannot claim it.

## Options

**When the image goes:** at each stroke's end, which stores a file per stroke, most of them never claimed; or at a send, once. At a
send: the page only needs to know the field is answered, which a hidden input tells it as with a file on its way.

**What a pad shows:** the image as stored, whose dark ink cannot be read on a dark theme; or the strokes in the theme's text colour,
with the image drawn apart in the ink ADR-0103 sets. The latter: strokes are data, so painting them twice costs nothing.

**How a signature that cannot go ends:** at the field, in words of its own; or as a send that failed, with the notice for how it
failed and every answer kept. The notice: the visitor's next step is the same, the words exist in every language, and the next
press sends the image again.

**A form the page cannot take** (ADR-0152's list, and a view without the pad): the pad stays off and the typed name goes by post, as
without JavaScript.

## Decision

- **A chunk of its own** (`signature-chunk`), with the cleaning and the text chunk in `@jfb/logic/signature`, held to the server by
  the shared vectors of `tests/vectors/typed-names.json`: `TypedSignature.Clean` gives the same name, and `TypedSignature.NameOf`
  and `PngHeader.Read` read the browser's bytes.
- **The markup is the server's,** which the base rules hide until the runtime marks `.jfb-signature` `data-jfb-enhanced`: two ways
  (`.jfb-signature-way`, `data-jfb-drawn` and `data-jfb-typed`), the pad a `canvas` of the field's size with `role="img"`, and
  `data-jfb-clear`, `data-jfb-to-typed` and `data-jfb-to-drawn`. A typed name already on the page starts on the typed way.
- **The pad** takes one pointer's main button at a time, by `pointerdown`, `pointermove` with the moves the browser gathered, and
  `pointerup` or `pointercancel`, captured, with `touch-action: none`; a touch that never moved is a dot.
- **Clear** turns into "Undo clear" until the next stroke, so a slip of the finger loses nothing.
- **The keyboard's and a screen reader's way is typing:** the pad is an image to them, and "Type your name instead" follows it. While
  the pad hides the name an error names, its summary link gives the focus to the first control that shows. Each way keeps its answer
  when the visitor moves to the other, but only the one on show counts.
- **The name's image** follows ADR-0103's look and fit, in the page's font, italic: the cap height from 30% of the height to 15%, then
  a wider image up to three times the pad, then smaller again.
- **Its error** marks the typed input `aria-invalid` as the server's view does, and the themes draw the pad's border from that.
- **`form_changed`** hands a drawn signature's strokes and token to the form in its place; a typed name goes as any other answer.

## Consequences

- The runtime with every feature grows by the chunk, and only a form with a signature on the page loads it.
- What the page leaves to a post after it sent (a form closed meanwhile, a captcha the server refused) cannot carry a drawing: the
  field asks for the signature again, at worst with "Sign again, or type your name instead".
- A signature changed while its image is on its way sends again, as what the visitor means by then.
- A signature is sent at each changed attempt at a send; those no entry claims go with the orphan cleanup (ADR-0148).
- A name in any script reads as itself in the browser's image and in its text chunk, so 11.2 shows the same words either way.
- **Revisit** when 6.5d pages in the browser: the page-count condition goes.
