# 0100. The post without JavaScript

- Status: accepted; extends [ADR-0004](0004-rendering-and-submission-paths.md) (what the form carries, the page after
  the redirect, a message with a redirect) and [ADR-0093](0093-the-razor-rendering.md) (the form's hidden inputs, the
  page shown after a post); extended by [ADR-0101](0101-rows-and-moves-without-javascript.md) in 6.2i (the rows'
  and the rankings' buttons) and by [ADR-0102](0102-files-in-a-post-without-javascript.md) in 6.2j (the post's files)
- Completed by: [ADR-0231](0231-members-entries.md), which builds what this left to step 14.3 (members)
- Date: 2026-10-29; amended 2026-10-31 (6.8a: another origin's post while a member is signed in; 6.8g: the origin "null")
- Roadmap step: 6.2h (decided and built); 6.8a (another origin's post as a member); 6.2i gives a row's Add and Remove and a ranking's moves their effect
  (ADR-0101), 6.2j takes files (ADR-0102), 6.2k the typed signature, 6.2l adds the cache rules, 7.1 the honeypot, the challenge and
  the rate limits, 6.5 the summary page and the copy of the answers

## Context

ADR-0004 decided the shape of a post without JavaScript: the form posts to its own page with Umbraco's route token, a
surface action reads it and runs the submission pipeline (ADR-0088), the page renders again with the answers, the errors
and any notice handed to the form through the request's items (ADR-0093), and a sent form redirects to
`?jfb-sent=<instance>#<instance>` so a reload sends nothing again. Every answer of the action is `no-store`.

Open were: what the form carries so that the action knows the form, its instance, its revision and the page shown; how
the posted names become the pipeline's request (parts, rows, consents, switches, line breaks, query and context values);
how Back, Next and Send move through the pages; what each refusal shows; what the page after the redirect shows when an
outcome's message recalls answers ("Thank you, {{ field.name.first }}"), which the redirect's address cannot hold; and
a message that is followed by a redirect, which a page without JavaScript cannot count down.

## Options

**What the page after the redirect shows:**

1. **`?jfb-sent=<instance>` alone,** and the outcome rendered without the entry. A message that recalls answers cannot
   be rendered, and nothing says which outcome the rules chose.
2. **The rendered message in the address,** protected. Long addresses, and the visitor's answers in the browser's
   history, in server logs and in the `Referer` of every link on the page, if only encrypted.
3. **A short-lived signed token** in `jfb-sent` that names the form, the entry's submission id and the instance; the page
   renders the outcome the `receipt` endpoint renders for that entry (ADR-0089), from the version it was submitted with
   and its stored answers. No answer is in the address.

**The page's query values,** which rules and hidden fields read (ADR-0014, ADR-0088):

1. **In the action's address.** Umbraco's output cache ignores the query string (ADR-0004), so a cached page would give
   every visitor the first visitor's values.
2. **An action of the fragment alone** (`#jfb-1`), which posts to the address the visitor sees, query string and all. A
   site's `<base href>` sends the post to another page, and a file form's `?jfb-post` marker (ADR-0006) replaces the
   query string.
3. **The address of the page from the first post's `Referer`,** on the site's own origin only, and carried in hidden
   inputs on every page rendered again after a post. A first render stays the same for every visitor; a site whose
   referrer policy sends no address loses them on a first post without JavaScript, as a page without it loses nothing
   else.

**Moving through the pages:**

1. **One form with every page, posted together.** The markup already holds every page, those not shown `hidden` but not
   disabled, so every post carries the answers of every page. Next checks the page shown as `validate` checks it
   (ADR-0089), Back moves without a check.
2. **Each page posted alone,** the earlier pages' answers kept in a signed token or on the server. State per visitor,
   and a token that grows with the form.

**A message followed by a redirect:**

1. **A `Refresh` header after the delay.** The visitor cannot stop it, which fails WCAG 2.2.1 (failure F40).
2. **The message with a link to go on.** The runtime (6.4) counts down with "Go now" and "Stay here", as the copy guide
   shows; without JavaScript the visitor follows the link.

## Decision

Option 3, option 3, option 1 and option 2.

- **The surface action:** `FormBuilderSurfaceController` with one action, `Post`, `[HttpPost, IgnoreAntiforgeryToken]`.
  It is public, because MVC takes only public controllers and Umbraco routes surface controllers through MVC, and holds
  no logic: it hands the request to an internal handler and turns the handler's answer into the page again
  (`CurrentUmbracoPage()`), a redirect or `400`. The handler's first check is Umbraco's route values: a post that
  reaches the action without a page (the route `/umbraco/surface/…`) answers `400`. Every answer of the action is
  `Cache-Control: no-store`, set before anything runs and again when the response starts.
- **What the form carries,** after its parts, as hidden inputs that no first render makes per visitor:
  - `ufprt`, Umbraco's route token for the action, made as `Html.SurfaceControllerHiddenInput` makes it
    (`EncryptionHelper.CreateEncryptedRouteString`) and given in the model, so a site's own Form view writes it too;
  - `jfb-form` (the form's key), `jfb-instance` (its instance id), `jfb-revision` (the public revision it shows) and
    `jfb-page` (the key of the page shown);
  - `jfb-rows-<alias>` for each repeatable group, the number of rows shown, so a row whose controls post nothing (boxes
    left unticked) keeps its place;
  - on a page rendered again after a post, `jfb-query-<name>` for each query value the post carried, of the names the
    form lists.
- **Reading the post,** into the request the JSON endpoint reads (ADR-0088), so both paths store the same answers:
  - `<alias>` and `<alias>.<part>` are a field's texts and its parts' (ADR-0058); `<group>[<row>].<child>` and
    `<group>[<row>].<child>.<part>` a repeatable group's rows, as many as its `jfb-rows-` says and never more than the
    pipeline takes (ADR-0098); a name of no field of the form, a row past the count and an unknown `jfb-` name are left
    out, since a site's markup may add inputs of its own; a name is a field's when the form names it so now or named
    it so in a published version, as the pipeline reads a page of an older revision (ADR-0002), and the first name of a
    field that the post holds is the one it is read by;
  - `jfb-consent-<alias>` (and `jfb-consent-<group>[<row>].<alias>`) is the consent's `wording` part (ADR-0009); the
    pipeline reads it only with the tick;
  - a yes or no switch that posts nothing is `no`, as the runtime sends it (ADR-0096): the page holds every page of the
    form, so every switch was on the posted page; one a rule hides is stripped by the pipeline. The switches are those
    of the live version, so a page of an older revision that lacked one meets `form_changed`, and its answers are kept;
  - every value's line breaks as the runtime sends them: a browser posts CRLF, which becomes LF;
  - `jfb-context-<name>` are the context values (ADR-0093); the query values are the posted `jfb-query-` inputs, or on
    a first post the query of the `Referer` when it is an address of the site's own origin;
  - the culture and the page come from Umbraco's published request for the page the post came to, as the form's render
    took them; a post carries no submission id, so the server makes one (a page that could carry one would carry the
    same for every visitor of a cached page).
- **Moving through the pages** (`jfb-nav`):
  - `back`: the page before the posted one that the rules show, without a check;
  - `next`: the posted page checked as `validate` checks it; with errors, the posted page again with them, else the
    next page the rules show;
  - `submit`, and a post without `jfb-nav` (a site's own view without the buttons' names): the pipeline; errors after Send show on the first page
    that holds one (an option that filled up meanwhile, an answer another entry took);
  - a row's Add or Remove and a ranking's moves (`add:`, `remove:`, `up:`, `down:`): the posted page again with the
    change and every answer, without a check (ADR-0101); any other value: the posted page again with every answer.

  The handler names the posted page and the move; the form's render works out which page the rules show, with the
  posted answers, as it works out every other state (ADR-0093). A page rendered again after a post works out its rules
  with the query values the post carried, as the pipeline does (ADR-0088), so a field a rule shows for a query value
  is shown where the pipeline asks for it; visitor texts never read the query (ADR-0095).
- **What the visitor sees:**

  | The pipeline answers | The page shows |
  | --- | --- |
  | `validation_failed` | the page with the errors in the messages of ADR-0099; an empty submission's message in the summary |
  | `form_changed` | the current form with the answers kept by alias and every consent whose wording changed unticked, with the notice "This form was updated while you filled it in" in the error summary's place, saying that the answers are kept and, when a consent changed, that it must be read again |
  | `form_closed`, `entry_limit_reached` | the form's closed or full notice in its place, as for any visitor now |
  | `member_required` | until 14.3 binds a post to its member, the form again with the answers and a notice that they were not sent; a visitor the form does not admit gets its sign-in notice instead |
  | `invalid_request`, `duplicate_submission` | the form again with the answers and a notice that they were not sent |
  | `form_not_found` | the page without the form, as the form's render shows one that is gone |
  | an entry, with a message or no outcome | `302` to the page with `?jfb-sent=<instance>.<token>#<instance>` |
  | an entry, with a redirect at once | `302` to the redirect's address, checked by the publish rule (ADR-0035) |
  | an entry, with a message and a redirect | the page with the message, as for a message, and a "Continue" link to the address, marked `data-jfb-continue` for the runtime's countdown (6.4) |

  A notice after a post has its heading, `role="alert"` and `tabindex="-1"`, as the error summary has, and the page
  opens at the form through the action's fragment. Without JavaScript nothing moves the focus, so both notices stand
  in the error summary's place, where the page opens; a send that did not go through is the red variant
  (`jfb-notice jfb-notice-error`), which the runtime (6.4) puts just above the buttons and moves the focus to, as the
  website specimens show.
- **The sent token:** Data Protection under the purpose `Jiron.FormBuilder.Sent.v1`: a version, the form's key, the
  entry's submission id, the instance and the time it expires on the site's clock, 30 minutes on, as base64url. The
  form's render reads `jfb-sent` for its own instance and form only, and shows the outcome `ReceiptAsync` answers for
  that submission id; a token that is malformed, changed, expired or another form's or instance's shows the form, as a
  page without one does. A render that shows an outcome is `no-store` (6.2l adds the rest of the cache rules).
- **Forms for members, and any form while a member is signed in:** before anything else, a post whose `Origin` is
  not the site's own (as ADR-0012 counts it, `http` and `https` alike for its host) answers `403` and renders nothing,
  so another site never sees the form again with answers; a post without an `Origin` passes, since every browser sends
  one with a post.

  *Why any form (6.8a):* the member's cookie goes with another site's post too, and the entry would be stored as the
  member's, with the values their prefill hides from the page (ADR-0124). The public API refuses another origin's post
  for the same reason (ADR-0012). A visitor who is not signed in carries nothing another site could borrow, so their
  post passes from anywhere, as a plain HTML form on another site may post to the page.

## Consequences

- A page without JavaScript keeps every answer through Back, Next, errors and changes of the form, and stores what the
  runtime's post to `entries` stores for the same answers; the integration tests compare the two entries' values.
- A double click without JavaScript can store two entries: a page without script carries no submission id, and 7.1's
  challenge is fetched by script. The runtime's post is idempotent.
- The query values reach a post without JavaScript only through the `Referer` on its first post, so a site that sends
  no referrer loses them there; the docs say so.
- 6.2i reads `add:`, `remove:`, `up:` and `down:` in the same handler (ADR-0101); 6.2j adds the `?jfb-post` marker
  and files, 6.2k the typed signature; 7.1 adds the honeypot, the fill time and the rate limits, whose refusal keeps
  every answer and says when to send again (ADR-0012); 6.5 adds the summary page and the copy of the answers, whose
  token is single-use.
- **The origin "null"** (6.8g): a page with `Referrer-Policy: no-referrer` posts to itself with `Origin: null`, as a
  sandboxed frame on another site does. Such a post counts as the site's own only when the browser also says
  `Sec-Fetch-Site: same-origin`, a header a page cannot set; a browser that does not say is refused, as before.
- **Revisit** if Umbraco routes internal surface controllers, or if browsers stop sending `Origin` with posts.
