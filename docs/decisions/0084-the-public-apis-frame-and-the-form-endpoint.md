# 0084. The public API's frame and the form endpoint

- Status: accepted; amends [ADR-0012](0012-anonymous-endpoints-spam-protection-and-challenges.md) (another
  origin's requests other than `GET`, the site's own pages behind a proxy that ends TLS, a body for an endpoint that
  takes none, the declarations), [ADR-0044](0044-form-service.md) (the published-form cache as built),
  [ADR-0043](0043-backoffice-settings-sections.md) (a stored time zone this server cannot resolve) and
  [ADR-0002](0002-form-definition-model-versions-and-public-projection.md) (the one sanitiser); the answer's
  `mergeTags` and `consents` extended by [ADR-0085](0085-visitor-texts-and-consent-wordings-on-the-form-endpoint.md)
  in 6.1b; the `options` endpoint, `no-cache` with an `ETag` as the form endpoint, added by
  [ADR-0086](0086-the-options-endpoint.md) in 6.1c
- Date: 2026-10-28
- Roadmap step: 6.1a (decided and built); 6.1b gives the consent wordings' hashes, 6.1d and 6.1e count a body without
  a declared length, 6.1e adds the entry limit to `open`, 7.1 enforces the rate-limit policies

## Context

6.1a builds the first endpoint visitors' browsers and headless sites call, and with it everything the later ones stand
on: where the API lives, which origins may call it, what every answer carries, how an endpoint says what it takes and
how long it may run, and how a published form gets from the database to a visitor in their culture. ADR-0012 decided
most of the frame in 1.7b; building it left a few questions open:

1. ADR-0012 sends another origin's requests no CORS headers, so the browser hands them nothing. A browser sends some
   posts without asking first (a form post, a `text/plain` body), so such a post would run and change data, and only
   its answer would be hidden.
2. Behind a proxy that ends TLS, the site sees `http://` while its own pages send `Origin: https://…`. Compared as
   written, the site's own pages would count as another origin.
3. ADR-0012 counts a body without a declared length while it is read. An endpoint that takes no body need not read
   anything to know that any body is too long.
4. ADR-0044 left the published-form cache to 6.1: what it keeps, for how long, and how a change on one server reaches
   the others.
5. The public projection (ADR-0035) holds every text per culture. A visitor's page needs one string per text, with the
   dictionary's items resolved and rich text sanitised, and the answer's `ETag` has to follow all of it.
6. A stored time zone the server cannot resolve (ADR-0043) must not stop a form, and must not fill the log.

## Decision

### Where it lives

- **The base path** is `JironFormBuilder:PublicApi:BasePath`, `/jfb/api` by default, with the version after it
  (`/jfb/api/v1`). It is `/` and segments of letters, digits, `.`, `-`, `_` and `~`, at most 100 characters, never a
  segment of dots only, and neither `/umbraco`, `/App_Plugins` or `/jfb/assets` nor a path below or above one of them;
  the start-up check names the key it refuses.
- **The controllers are internal,** below an abstract base that maps them to the document `jfb-public`, allows
  anonymous calls and puts the declarations' filter in front of them. A feature provider adds them to MVC, and a
  convention puts the base path and version before each controller's route, so moving the base path moves every
  endpoint and the catch-all together.
- **An address below the base path that names no endpoint** answers `404 not_found` as ProblemDetails (a `HEAD` gets
  the status alone), never a page of the site. The catch-all has no route order of its own: an endpoint's route is more
  precise and wins, and its literal base path is more precise than Umbraco's route for content, which an order would let
  win.

### What every request meets

A middleware runs before Umbraco's routing (an Umbraco pipeline filter, `Jiron.FormBuilder.PublicApi`), for the base
path only, in any letter case:

- the form feature with no form content (ADR-0006), `Cache-Control: no-store`, which only the form endpoint replaces,
  and `Vary: Origin`;
- **the origin check:** a request without an `Origin` passes. The site's own origin passes, compared with
  `{scheme}://{host}` of the request; when the request came over `http`, an `https` origin on the same host passes too,
  since a proxy ended TLS before the site, but an `http` origin never passes as an `https` site's own. An origin in
  `AllowedOrigins`, written as browsers send it (lowercase, the host in its ASCII form, the port only when it is not
  the scheme's), gets `Access-Control-Allow-Origin` and sees `ETag` and `Retry-After`. Any other origin, `null`, `*`,
  something that is no origin and several `Origin` headers count as denied: a `GET` or `HEAD` runs without CORS
  headers, and **any other method answers `403 origin_denied` before anything runs** (amends ADR-0012; the preview's
  `null` with a preview ticket comes with the preview);
- **a preflight** (`OPTIONS` with an `Origin` and `Access-Control-Request-Method`) answers itself: `204` with
  `GET, POST`, `Content-Type` and ten minutes for an allowed origin, `403 origin_denied` for any other, never
  credentials;
- **what ends a request early:** a cancellation after the visitor left gets no answer and is no error; one after the
  time limit ran out answers `504 timeout`; anything else unexpected answers `500 server_error` and is logged at Error
  with the endpoint's name and the exception, never the request's values. Each drops the `ETag` an endpoint set before
  it failed. A failure after the answer started goes on to the site.

### What each endpoint declares

- **Every action declares** its rate-limit policy (`read`, `challenge`, `submit`, `upload`, `events`, `verify`), its
  body limit in bytes (0 or more) and its time limit (1 to 300 seconds), as attributes that refuse a value out of
  range. The document `jfb-public` carries them as `x-jfb-rate-limit`, `x-jfb-body-limit` and `x-jfb-time-limit`, and a
  test fails for an operation without them and for an action without them, the catch-all's included.
- **The filter** applies them before the body is read or bound: a declared length above the limit, or **any body for
  an action that takes none** (a declared length above 0, or a body the server sees without one), answers
  `413 payload_too_large` and closes the connection on HTTP/1.x (amends ADR-0012; counting a body without a declared
  length while it is read comes with the first endpoints that take one, 6.1d and 6.1e). The time limit runs on the
  site's `TimeProvider` and cancels the token the action gets, linked to the visitor's; the request's own token is put
  back when the action ends. While the migrations are pending every action answers `503 migrations_pending` with
  `Retry-After: 10`. An action without declarations gets no body and ten seconds.
- **The rate-limit policies are declared from the first endpoint and enforced from 7.1.**
- **The document** describes what is sent: `application/json` for answers, `application/problem+json` for refusals, the
  frame's own refusals (`413`, `500`, `504`, and `403` for a method another origin may not send) on every operation,
  operation names from the controller and action (`FormDefinition`), and no backoffice sign-in, since the API is
  anonymous. `docs/api/jfb-public.json` is its reviewed snapshot.

### The form endpoint

`GET forms/{keyOrAlias}?culture=&contentKey=`:

- **The form:** a key with hyphens, or an alias in any letter case; anything else is answered as missing without a
  read. Missing, unpublished, in the trash, not served to headless sites, or with a published version that cannot be
  read: the same `404 form_not_found`, so the answer tells nothing about a form it does not serve.
- **The culture** is a culture name of at most 85 characters, or `400 invalid_request`. The site's language it names in
  any letter case answers, spelled as Umbraco spells it; any other answers in the default language, so no culture a
  visitor makes up reaches a cache or the dictionary.
- **The page** (`contentKey`) is any GUID, or `400 invalid_request`. Its site is the root of the published content it
  names; a page that is not published, or no page, gives the values every site shares.
- **The answer** is `{alias, revision, culture, timeZone, open, form}`: `revision` is the public revision a submission
  posts; `timeZone` is the site's IANA zone, for the browser's `today`; `open` says whether the form takes entries now
  by its opening and closing times (the entry limit joins in 6.1e); `form` is the visitor's form below.
- **Caching:** `Cache-Control: no-cache` with an `ETag` of the first 32 hex digits of the SHA-256 of the answer's
  bytes, so it follows the publish, the culture, the dictionary, the site's zone, the sanitiser and the host's designer
  locks (ADR-0060) without a version of its own. `If-None-Match` compares weakly and answers `304`.

### The visitor's form

- The public projection with **each text in the culture** (ADR-0002's fallback), a text that names a dictionary item
  (`#key`) as the item's text, all the items of one answer read at once, and the texts in the fields' public settings
  by their types' schemas (nested objects included; a settings list holds no texts). A setting that is no proper text
  per culture shows as the empty text, so no object goes empty and nothing unchecked shows.
- **Every rich text passes the one sanitiser**, wherever it comes from (the editor's text or a dictionary item):
  HtmlSanitizer with Jiron Form Builder's allowlist, the first runtime dependency shipped to sites besides Umbraco's
  (MIT, with AngleSharp and AngleSharp.Css, both MIT; THIRD-PARTY-NOTICES). It keeps text formatting, lists, headings
  from `h2` down, quotes, code and links to `http`, `https`, `mailto`, `tel` and the site's own paths, with `dir`,
  `href`, `lang`, `target` and `title`; a tag it does not keep goes with its content; a link to a new tab gets
  `rel="noopener noreferrer"`, and any other target goes. Plain text is left for the page to escape.
- A lone surrogate, which a dictionary item may hold, becomes U+FFFD, so the form always writes.

### Published forms on each server

- **What is kept:** per form key, the published form (through `GetPublishedAsync`, so designer locks apply) with its
  projection, or a mark that an existing form is not served; per alias, the form's key. One load per miss is shared by
  every request that misses while it runs, outside each request's cancellation, so a request that gives up or times
  out leaves the read to the others. A key or alias that names no form is not kept, nor is its load, so visitors'
  guesses never fill the cache. A published version that cannot be read is logged at Error once per load and answered
  as missing until it is published again. Entries are kept for twelve hours, which only bounds what a server keeps of
  forms nobody asks for.
- **What drops it:** the form service's publish, unpublish, rename, trash, restore and delete (the delete also when it
  fails partway), the folder service's trash and restore (every form at once) and its delete of each form, each through
  a payload cache refresher (`FormCacheChange(key)`, the empty key for every form): this server forgets at once, the
  others on their next sync. Any change drops every alias entry, since a rename moves an alias between forms.

### The site's time zone

`general.timeZone` of the request's site through the settings service. A stored zone this server cannot resolve counts
as UTC, and the site logs a warning that names the setting, the site and the zone, at most once an hour per site and
zone on each server, in a list it empties before it holds more than 256 (amends ADR-0043). The allowlist test of
ADR-0008 fills every field of every settings section and shows that only the time zone reaches the answer, and a guard
fails when a section or field appears that the test does not fill.

## Options considered

- **Another origin's posts: run them and hide the answer, as ADR-0012 said,** or refuse them first. A browser sends a
  form post or a `text/plain` body to another origin without a preflight, so hiding the answer would still let any page
  submit to the site through its visitors. Refusing every method but `GET` and `HEAD` costs nothing a legitimate caller
  needs: allowed origins and servers pass. Chosen: refuse.
- **The site's own origin: the request's scheme and host only,** or also `https` on the same host when the request came
  over `http`. A site behind a proxy that ends TLS without forwarded headers would refuse its own pages' posts with the
  first; the second accepts only the same host over the stronger scheme. Chosen: the second; an `http` origin on an
  `https` site stays another origin.
- **Unknown keys in the cache: keep a "missing" mark,** or keep nothing. A mark saves a read for a repeated guess, but
  visitors choose the keys, so the marks would grow without bound. Chosen: nothing for keys and aliases of no form; a
  mark only for forms that exist and are not served, whose number the site's forms bound.
- **The `ETag`: a version number, or a hash of the answer.** A version number would need to change with the dictionary,
  the zone, the sanitiser and the locks too. Chosen: the hash, which follows everything the answer holds.
- **The sanitiser: written here, or an established library.** Rich text from editors and the dictionary is
  security-critical parsing, which CLAUDE.md rule 4 and §4.10 give to an established library. Chosen: HtmlSanitizer,
  MIT, over AngleSharp's HTML5 parser, configured once and safe to share between threads.
- **Malformed text settings: drop the setting, or show the empty text.** Dropping can leave an object or the settings
  empty, which a settings object cannot hold, so the form would fail to write. Chosen: the empty text.

## Consequences

- The later endpoints of 6.1 to 11.5 get the frame by deriving from the base controller and declaring their limits;
  the declarations test and the snapshot cover each.
- A headless site renders a form from one call per culture and page, and revalidates it cheaply with `If-None-Match`.
- A rename, trash or publish reaches every server's visitors within one sync, and an unknown key costs one read each
  time and nothing kept.
- The first runtime dependency besides Umbraco's ships to sites; the notices check holds its licence and those of its
  dependencies.
- **Revisit** if browsers start sending other methods without a preflight, if Umbraco offers a public API base with
  CORS per path, or if HtmlSanitizer stops being maintained.
