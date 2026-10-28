# 0071. The advanced types' own checks

- Status: accepted; amends [ADR-0058](0058-field-answers-and-values.md) (what a type checks: its uploads and its
  rows), [ADR-0006](0006-file-storage-and-uploads.md) (the field's limit and the type's check of a file's first
  bytes) and [ADR-0002](0002-form-definition-model-versions-and-public-projection.md) (the message kind `rowCount`);
  the host's limits in a type's settings check (`FieldSettingsCheck.Host`) and the kind of a refusal outside the type
  (`FieldUploads.Declared`) added by [ADR-0087](0087-the-upload-endpoint.md) in 6.1d
- Date: 2026-10-27
- Roadmap step: 5.4c (decided and built); 5.4d counts a repeatable group's rows, 6.1's `uploads` endpoint and 6.2's
  posts without JavaScript run the upload checks, 6.4's runtime keeps rows and signatures within these limits, 7.3
  adds the content checks every upload gets

## Context

Most field types check a visitor's answers in `Validate` (ADR-0058): the values the type read, and for a file field
the names and sizes of the files its upload tokens stand for (ADR-0062). Three things the advanced types need do not
fit there:

1. **A signature is a PNG the runtime draws** (§4.3.2), as the pad's drawing or as the typed name. By the time an
   entry is submitted, the file is stored and the check sees only its name and size. Whether it is a PNG at all, and
   how large in pixels, is in its bytes, and an upload is checked on its first bytes before it is stored (ADR-0006).
2. **A file's limit belongs to its field.** The endpoint refuses a declared length over the field's limit before it
   reads a byte, and reads through a stream that stops at it (ADR-0006), so it needs the limit before the file
   arrives.
3. **A repeatable group has no values of its own** (storage none): its child fields hold them, row by row. Its own
   limits are on the number of rows, which no value says.

Three kinds of type take no answer at all: the calculation (the server works it out, §4.3.3), the content types and
the captcha placement.

## Options

1. **Where a signature's PNG check goes.**
   - In `Validate`, over the stored file: every submission would read the file back from the store, and the browser's
     checks (D10, the vectors) know only names and sizes.
   - In 6.1's endpoint, as a special case for signatures: a site's own type could not check its files' bytes.
   - **The type's check of an upload, part of the field type contract,** which the endpoint runs on the first bytes
     before it stores the file, in the order ADR-0006 gives: the type that knows its files checks them, and a site's
     own type can do the same. Chosen.
2. **The size.**
   - Inside the type's check of an upload: the endpoint would learn the limit only once the file is read.
   - **A limit the type declares,** which the endpoint and the posts without JavaScript hold each file to, capped by
     the host's (`Files:MaxFileBytes`, §4.8). Chosen.
3. **A signature's error kinds.** `fileSize` and its kin would offer editors messages for what no visitor causes: the
   runtime draws the PNG within the limits, so only a client that skips the runtime sends another. **`invalid`** for
   every refused signature. Chosen.
4. **The rows' error kind.** `invalid` leaves an editor no way to say "add at least two guests". **A kind of its own,
   `rowCount`,** named like `fileCount`; `rows` would read as a grid's rows. Chosen.
5. **Who counts the rows.** A type sees one field, and the rows a submission gives a group depend on the whole post,
   so 5.4d counts them over the form and hands the count to the type's check.

## Decision

- **The upload contract** (`IFormBuilderFieldType`, with overloads that read the settings in
  `FormBuilderFieldTypeBase<TSettings>`):
  - `UploadLimit(field)`: the most bytes one file of the field may have; null, the default, leaves the host's limit.
  - `CheckUpload(FieldUploadCheck)`: checks an upload's name and first bytes before the file is stored and reports
    each problem by its kind, `invalid` or one the type declares, as `Validate` does; it finds nothing by default.
  - `FieldUploads` runs them for 6.1's endpoint and 6.2's posts without JavaScript, on a field whose type stores
    files: `Limit` is the type's limit or the host's, whichever is smaller (a type's limit below 1 byte is a bug,
    refused with the type's name); `CheckSize` gives `fileSize` for a file over it when the type declares that kind,
    `invalid` otherwise; `Check` hands the type the file's name and its first bytes, at most 512 (`HeadLength`: a tar
    archive names its kind at byte 257, so 7.3's content checks have what they need). A field that stores no files, a
    limit or size below 0 and a longer head are refused.
- **The file type:** its limit is `maxBytes`; its check of an upload refuses an extension the field does not take
  (`fileType`), from the same list its `Validate` holds the submitted files to (ADR-0062).
- **The signature:**
  - at most 1 MiB (`MaxBytes`), whatever more the host allows;
  - its first 33 bytes are a PNG's signature and a well-formed `IHDR` chunk (`PngHeader`): the eight signature bytes,
    the length 13, `IHDR`, a width and a height from 1 to 2,147,483,647, a bit depth its colour type allows (grey 1, 2, 4,
    8 or 16; true colour 8 or 16; indexed 1, 2, 4 or 8; grey or true colour with alpha 8 or 16), compression and
    filter method 0, interlace 0 or 1, and the chunk's CRC-32;
  - at most three times the pad's width and height in pixels, because the runtime draws at the device's pixel ratio,
    at most 3 (6.4): `width`, or 1,200 without one, and `height`, or 600 (the settings' maxima);
  - each failure is `invalid`. The image data past the header is not decoded here; 7.3's content checks and the
    scanner hook come with every upload.
- **The rows** (`ValidateRows(FieldRowsValidation)`): a type with child fields holds the number of rows a submission
  gives a field to its settings; it finds nothing by default. `FieldValues.CheckRows` runs it and refuses a type
  without child fields and a negative count. The repeatable group fails `rowCount` below `minRows` or above `maxRows`
  (at most 100, `MostRows`). `rowCount` is a message kind of `FieldMessageKinds` and `FieldMessages`, in the schema
  and the field types endpoint, and start-up refuses a type that raises it without child fields.
- **One rule for reporting:** the checks of values, uploads and rows share one list of errors (`FieldErrorList`):
  `invalid` or a kind the type declares, each once, in the order reported.
- **No answer:** the calculation, the content types and the captcha placement have no answer limits
  (`FieldAnswerLimits.None`), so whatever is posted for them is ignored: neither limited, nor read, nor checked.

## Consequences

- ADR-0058's contract gains the three members; ADR-0006's endpoint takes the field's limit from `FieldUploads.Limit`
  and runs `Check` on the sniffed bytes; ADR-0002's messages and §4.3.3's kinds gain `rowCount`; §4.9 names the
  members.
- 6.1 calls `Limit` before reading, `Check` on the first bytes and the store's stream at the limit; 6.2 calls
  `CheckSize` and `Check` on the buffered files.
- A site's own type declares a limit and checks its files' bytes through the same members.
- The browser does not decode PNGs (the vectors cover names and sizes): the runtime draws a signature within the
  limits, so only a client that skips it meets the server's refusal.
- The rows need no check in the browser while the runtime adds and removes rows only within the limits (6.4). 5.4d
  counts a submission's rows as the runtime shows them; if it ever counts them otherwise, the same check goes into
  `packages/logic` with vectors.
- **Revisit** if signatures need another format (vector paths), or if a site's types need more than a file's first
  bytes.
