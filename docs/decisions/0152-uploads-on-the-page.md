# 0152. Uploads on the page

- Status: accepted; builds the uploads of [ADR-0006](0006-file-storage-and-uploads.md) and the file field's design in
  [ADR-0022](0022-default-theme-and-tokens.md) on the endpoint of [ADR-0087](0087-the-upload-endpoint.md) and the checks of
  [ADR-0143](0143-what-an-uploads-bytes-and-name-may-hide.md)
- Date: 2026-11-02
- Roadmap step: 6.4m (decided and built); 6.5d brings it to a form of several pages

## Context

A file field posts its files with the form without JavaScript (ADR-0102). With the runtime, a file goes ahead of the
entry to the `uploads` endpoint, so a visitor sees its progress, and a file the server would refuse costs no upload.
An upload's token names its challenge (ADR-0138), which only an entry with the same challenge can claim, and a post has none.

## Options

**How a file's progress is read:** `fetch` tells nothing of what it sent; `XMLHttpRequest` does, so it is the one place the
runtime uses it.

**Where the token lives:** in the upload's memory, which the send adds to its body; or in a hidden input of the file's row,
named as the field. The input: the page reads it as it reads every other answer, so the send, `values()` and the rules need
no second path.

**A form of several pages,** whose Next posts until 6.5d: its tokens could not be claimed by the post, so that form keeps the
browser's own input until the pages move to the browser. So does a form with a repeater or a ranking, whose buttons post until
6.4o, a view without the template, whose file only a post can hold, and a form view without the send's parts, which cannot go from
the page.

**How the browser's checks stay the server's:** by hand, or by a port held to the server by shared vectors, as the logic is
(ADR-0005). The vectors.

## Decision

- **A chunk of its own** (`uploads-chunk`), with the signatures in `@jfb/logic/files`, which only a form with a file field
  loads. A form that cannot load it posts as without JavaScript.
- **The checks before anything is sent,** in the server's order: the extensions the field takes (`accept`), its size, how many it
  takes, a program or a script, bytes of another kind than the extension names, and a page behind another name, on the first 512
  bytes, each in the field's own words. A blocked extension and the host's limits are the server's alone, which answers as ever.
- **A row per file,** from the template the field's view writes, with its name and state: busy with its progress, held with its
  size, refused, or stopped. What the server answers: `200` holds the token; `401` renews the challenge once and sends again;
  `413` and `415` word the error it names; `429` and `503` say to try later and offer no retry; anything else, a cut connection
  among them, is "The upload stopped before it finished", with "Try again" in the same row.
- **The buttons are the server's markup,** which the base rules hide until the runtime marks the zone `data-jfb-enhanced`, so a
  page without JavaScript shows the browser's own input alone, and a site's view can word them. "Take a photo" is written only with
  the field's `camera` on, and shows only on a touch screen. An image shows its thumbnail only with `thumbnails` on, from the
  browser's memory (`blob:`); a site whose policy does not take that keeps the icon.
- **A field for one file replaces its file** when another is chosen; a field for several refuses what is past its `maxFiles`.
- **A file on its way counts as an answer:** its row's hidden input says so from the moment it is chosen, and goes when the file
  fails, and each change tells the rules and the errors. `values()` shows that marker for as long as the file is on its way.
- **The page checks a field whose files it takes** (added in 6.4n): it reads them all, so a required one shows its error with the
  others at the first press, not after the server's answer; a field the post takes stays the server's to check.
- **A send waits** for the uploads on their way, busy as ever, and stops at a file that failed, whose row takes the focus, until
  the visitor tries again or takes it off: a file left behind in silence would be worse than a press to answer (changed in 6.4n).
  Taking a file off the list drops its token, and stops it when
  it is still on its way. A stored upload no entry claims goes with the orphan cleanup (ADR-0148).
- **`form_changed`** carries a form's held files to the form in its place (ADR-0120), with the same challenge, which their tokens
  name.

## Consequences

- The runtime with every feature grows by the uploads' chunk, and only a form with a file field loads it.
- A site's strict policy needs `connect-src` for the API as for every send, and `img-src blob:` for thumbnails.
- A failed file's row is marked `data-jfb-failed`: `data-jfb-error` is the error paragraph, which a field's check takes away.
- An upload's token names its challenge: one held past the challenge's renewal (a page left open for a day) cannot be claimed, and
  the server's `invalid` at the field asks for the file again.
- A fallback post after an upload cannot claim the token, so its field asks again. That takes a refusal the page does not answer,
  with a file already up, and only asks the visitor to choose the file again.
- **Revisit** when 6.5d pages in the browser: the page-count condition goes.
