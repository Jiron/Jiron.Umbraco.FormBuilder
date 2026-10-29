# 0102. Files in a post without JavaScript

- Status: accepted; builds [ADR-0006](0006-file-storage-and-uploads.md)'s no-JS posts with files and changes how the
  post is read (part by part, so a file that is too large keeps every other answer); extends
  [ADR-0100](0100-the-post-without-javascript.md) (the post's files, kept across a re-render) and
  [ADR-0087](0087-the-upload-endpoint.md) (its token, posted back as the field's answer); a signature typed without
  JavaScript stored as a file is, by [ADR-0103](0103-a-signature-typed-without-javascript.md) in 6.2k
- Date: 2026-10-29
- Roadmap step: 6.2j (decided and built); 6.4's runtime uploads before sending, 7.1 verifies the challenge, 7.3
  hardens the rules over HTTP/2

## Context

ADR-0006 decided that a form with a file field names itself in its action (`?jfb-post=<key>.<instance>`), that a
middleware before routing sets the form's limits and reads the post, with a drain allowance so a browser sees the page
instead of a reset connection, and that the surface action holds the files to the upload's rules before storing them.
It expected a post over the limit to lose its answers, "because the post was not read", and asked for the message to
be designed once and for a checked file to be kept across a re-render through a signed token.

What 6.2j found in the code: the upload's rules are one service already (`FileUploads.StoreAsync`: the name, the first
512 bytes against the executables, the host's blocklist, the type's own check and the field's extensions, then the
store at the field's limit); its token (`UploadTokens`, 24 hours, the form, the field and the file) is what a file
field's answer holds in the pipeline (ADR-0087); and the surface path gave the pipeline no token reader, so any value
of a file field was `invalid`.

Open were: how the post is read; what a file that is too large, of a kind the field does not take, or one too many
does to the post and to the button pressed; how a stored file comes back on the page; and files in a repeatable
group's rows.

## Options

**Reading the post:**

1. **ASP.NET's form reader** with a per-part limit. A part over it throws, and the whole post is lost: the page shows
   again empty, which a visitor who filled in a long form takes as the form eating their work.
2. **The middleware reads the post itself, part by part,** with `MultipartReader`: a value as a value, a file part as
   a file buffered up to its field's limit. A file past it is not kept: the reader moves on to the next part, which
   drains the rest, and notes the field. The whole post is still bounded by the drain allowance, so the server never
   reads more than ADR-0006 lets Kestrel drain anyway, and every other answer is kept.

**A file the post cannot take** (too large, a kind the field does not take, one too many):

1. **Send without it.** An optional file field would be stored empty without a word.
2. **The posted page again** with the error at the field and every answer, whatever the button: nothing is sent and
   no page is left, as for a failed Next.

**A file that passed its checks, when the page shows again:**

1. **A hidden input with its token.** The visitor would see an empty file input and choose the file again.
2. **A ticked box per stored file** named like the field and carrying the token as its value, "Keep invoice.pdf
   (120 KB)": the page says the file is kept, and the visitor can let it go by unticking it. The token is the
   field's answer as the runtime posts it (ADR-0087), so the pipeline reads it as it reads the runtime's.

## Decision

Option 2 for each question.

- **The marker:** a form whose definition stores files (a file field or a signature, ADR-0071) posts to
  `<page>?jfb-post=<key>.<instance>#<instance>`, the key as `D`; every button's `formaction` keeps it (ADR-0101).
  The address is the same for every visitor.
- **The middleware** (`FormPostFilesMiddleware`, an Umbraco pipeline filter before routing) takes a `POST` with a
  multipart body and a marker that names a published form and an instance id as the render gives them; anything else
  passes untouched.
  - **The limits:** the form's limit is, for each field that stores files, its limit (`FileUploads.LimitOf`) times
    the files it takes (`maxFiles`, one for a signature) times the rows its group may have, plus 1 MB for the rest;
    Kestrel's limit for the request is twice that, the drain allowance. A declared length over the allowance is not
    read at all.
  - **Reading:** at most 1,024 parts, 8 headers and 4 KB of headers each; a value part is read as UTF-8 up to 1 MB. A
    file part named by a field that stores files, by its alias now or one it had and in a row the post counts, is
    buffered (memory up to 64 KB, then a temporary file that is deleted when the request ends) up to its field's
    limit, at most as many as the field takes; an empty part is no file. A file past its field's limit is noted as
    `fileSize`, one past the field's count as `fileCount`, a file part of no such field is skipped. The post then
    goes on as a form whose values and files are what was read.
  - **Past the allowance,** or a post that breaks these limits otherwise, nothing can be kept: the post goes on with
    no form, so Umbraco renders the page, and the form shows the notice "Your answers weren't sent" in the error
    summary's place, saying that the files were too large to send and that the answers could not be kept (`too_large`).
- **The surface action,** before any button's work: each buffered file goes through `FileUploads.StoreAsync`, the
  same rules as the upload endpoint, and a stored file's token joins its field's answer (for a field that takes one
  file it replaces a kept one). A refused file is noted with its kind (`fileType`, `fileSize`, `invalid`). The request
  carries the token reader, so the pipeline reads the tokens as it reads the runtime's.
- **When a file is noted,** nothing is sent and no page is left: Back, Next and Send show the posted page again with
  the noted errors at their fields, together with what checking the page finds; a row's or a ranking's button still
  makes its change. Every answer is kept, the stored files with them.
- **The message** is the field's own message for the kind (ADR-0099: the editor's, else the package's, which names
  the field's limit, its formats or its count) at the field, linked from the error summary like every other error; it
  is never repeated in a notice.
- **A kept file:** above the file input, one ticked box per stored file of the field, named like the field (or its row's
  path) with the token as its value and labelled "Keep {name} ({size})", the size 1,024 based in the page's culture,
  in KB until it rounds to a megabyte, with one place only when it is not whole and a file of a few bytes as 0.1 KB;
  unticked, it is not posted and the file is let go. The page resolves each token to the stored file's name and size, and leaves out one that no longer names a
  file of this form and field that no entry holds.
- **Rows:** a file field in a repeatable group's row is named and kept by its row's path, and a row removed takes its
  kept files with it.

## Consequences

- A visitor without JavaScript can attach files and never loses a filled-in form to a file that is too large, unless
  the post is larger than the drain allowance, where the browser may show a reset connection (ADR-0006).
- Files a visitor stores and never sends are left to the abandoned-upload clean-up (D8), as the runtime's are.
- The page shown after a post keeps the marker in its address. Opened again, it is a `GET`, which the middleware never
  reads, so it shows the form as any visitor sees it.
- The page holds a token per kept file; the token is a bearer token for 24 hours, as the runtime's is (ADR-0087).
- A form with a file field posts to an address with a query, which the output cache never serves (posts are never
  cached, 6.2l).
- **Revisit** if browsers come to keep a file input's file across a page load, or if Umbraco stops reading forms
  while it routes.
