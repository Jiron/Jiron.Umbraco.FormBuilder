# 0103. A signature typed without JavaScript

- Status: accepted; builds [ADR-0097](0097-the-part-date-and-output-controls.md)'s typed name as the stored signature
  and extends [ADR-0071](0071-the-advanced-types-own-checks.md) (a signature's PNG may also be drawn by the server, and
  may carry the typed name) and [ADR-0102](0102-files-in-a-post-without-javascript.md) (the surface action stores it
  as it stores a file)
- Date: 2026-10-29
- Roadmap step: 6.2k (decided and built); 6.4's runtime writes the same name into the PNGs it draws, 11.2's entry
  panel shows it

## Context

A signature is a PNG (ADR-0071). With JavaScript the runtime draws it on its pad, or draws the name a visitor types
instead (§4.3.2, 6.4). Without JavaScript the page shows only the name's input, `<alias>.typed` (ADR-0097), and
6.2k must turn that name into the stored signature, held to the signature's checks.

The server has nothing to draw text with:

- no image library may be added (CLAUDE.md rule 4, no direct ImageSharp);
- fonts that could ship with the package are almost all under the SIL Open Font License, which is not one of the
  licences allowed for what ships to sites;
- the operating system's fonts differ from host to host, and a Linux container often has none; `System.Drawing`
  runs on Windows only.

Names come in every script. Whatever the server cannot draw must not stop a visitor from signing: without
JavaScript, typing the name is the only way to sign.

## Options

**Drawing the name:**

1. **The operating system's fonts** through `System.Drawing`. Windows only, and the result would differ per host.
2. **A font file and a font reader of our own.** No font may ship, and a TrueType reader and rasterizer is a large
   parser of untrusted-looking data to maintain.
3. **A hand of our own:** each letter written as pen strokes (data in the code), drawn by a small rasterizer with a
   round pen, and written by a small PNG writer over .NET's own zlib. Nothing to license, the same on every host,
   and the same pixels for the same name, which tests can compare.

**A name the hand cannot draw** (a script it does not cover, a symbol):

1. **Refuse it.** A visitor without JavaScript whose name is written in another script could not sign at all.
2. **Store text instead of a PNG.** Every reader of signatures (checks, files, exports, the entry panel) would need
   a second shape.
3. **Store the PNG with the name in it.** The image draws what the hand covers and an open box for each character it
   does not; the exact name travels with the image in a PNG text chunk, which the entry panel and the exports can
   read.

**When to draw:**

1. **On every post.** A row's Add or a ranking's move would store a new file each time.
2. **On the page buttons (Back, Next, Send).** Every check the page can run sees the signature, and a row's or a
   ranking's button stores nothing.
3. **On Send only.** Next would report a required signature as missing although the name is typed.

## Decision

Option 3, option 3 and option 2.

- **The name:** the typed text is normalised to NFC; control characters (Unicode `Cc`) and format characters (`Cf`,
  such as the direction marks that can disguise a text) are removed, except the zero-width joiner and non-joiner,
  which some scripts need; each run of white space becomes one space, and the ends are trimmed. Nothing left is no
  signature. A name longer than 100 UTF-16 code units is `invalid`; the input carries `maxlength="100"`, so only a
  forged post reaches that.
- **When:** a post pressed with Back, Next or Send draws each signature field's typed name, outside groups and in
  rows. A row's or a ranking's button only keeps the name. The page keeps the name in its input, never a token, so the
  next post with a page button draws it again; the PNG an earlier post stored is left to the abandoned-upload
  clean-up (D8), as the runtime's unused uploads are.
- **The checks:** the drawn PNG goes through `FileUploads.StoreAsync` as `signature.png`, the same rules as an upload
  (the name, the executables, the type's PNG check of ADR-0071, the 1 MB limit). Its token replaces any value posted
  for the field. A refusal (the form trashed meanwhile on another server, or a host whose blocklist names `png`) is a
  problem at the field with its kind.
- **The pipeline** never sees the typed part: the post's reader leaves it out of what it hands the pipeline, whose
  signature takes one token (ADR-0058), and keeps it for the page shown again.
- **The image:**
  - **Size:** the field's pad (`width` × `height`), else 900 × 300, the default theme's pad at 3:1.
  - **Look:** transparent, ink `#1f2430`, the baseline at 75% of the height where the pad draws its line, starting
    at 6% of the width.
  - **Fit:** a long name shrinks from a cap height of 30% of the height to 15%. Past that the image widens up to
    three times the pad's width, which the check allows (ADR-0071), and only then shrinks further.
- **The hand:** a slanted print hand (10°) with a round pen whose width follows the size.
  - **Covered:** the Latin letters, digits, and the marks names use (space, hyphen, apostrophes, full stop, comma,
    ampersand).
  - **Accents:** every letter that decomposes (NFD) into a Latin base and the combining marks names use (acute,
    grave, circumflex, tilde, diaeresis, ring, caron, breve, macron, dot above, double acute, cedilla, ogonek, comma
    below, and for Vietnamese the dot below, the hook above and the horn), drawn as the base with its marks, stacked
    when there are several above, an `i` or `j` without its dot under a mark above.
  - **Letters of their own:** ß, æ, ø, œ, ð, þ, ł, đ, ħ, ŋ, ŧ, ĳ, ŀ and ı, small and capital where there are both,
    and ĸ, ŉ and ſ, so every letter from U+00C0 to U+017F is drawn.
  - **Anything else:** an open box per character.
- **The PNG:** true colour with alpha, 8 bits, the ink's colour in every pixel and its cover in the alpha; one `iTXt`
  chunk after `IHDR` with the keyword `jfb:typed-name`, no language tag and the name as UTF-8; then the image in one
  `IDAT` (zlib, rows unfiltered) and `IEND`. The CRC-32 is `PngHeader`'s, now shared (`Png`). `TypedSignature.NameOf`
  reads the name back from a stored PNG.

## Consequences

- A visitor without JavaScript can sign in any script. A name in Latin letters shows as a drawn signature; one in
  another script is stored exactly, and shows as boxes until the hand learns that script.
- The hand is data: adding a script means adding its glyphs, and the tests check each glyph stays inside its box.
- 6.4's runtime draws a typed name with the browser's fonts and adds the same `iTXt` chunk, so both ways keep the
  name, and 11.2's entry panel shows it as text beside the image (both added to the roadmap in this step).
- Each page button press with a typed name stores a PNG of a few kilobytes, cleaned up with abandoned uploads.
- **Revisit** if sites need another script drawn, or if a font with an allowed licence and a reader become
  available.
