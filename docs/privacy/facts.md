# Privacy facts

What Jiron Form Builder stores, where and for how long, what it keeps on a visitor's device, and whom it talks to. It
describes the package as it ships, its defaults first; your site is the controller of the data, and these facts are for
you and your data protection adviser to work from. The [checklist](checklist.md) lists what to decide, and the
privacy-policy templates ([English](policy-template.en.md), [German](policy-template.de.md)) give wording to adapt.

A test reads the two tables of "On the visitor's device" and compares them with what a form page really does, so they
stay true.

## What a submission stores

Everything below lives in your Umbraco database, in the tables whose names start with `jfb`, except uploaded files,
which lie in the file store (by default `umbraco/Data/JironFormBuilder/Uploads`, outside the web root; a site may
register its own store).

| Data | What is kept | Default | Who can change it |
| --- | --- | --- | --- |
| Answers | the values of the form's fields | kept with the entry (see "How long") | a form may store nothing (its actions run, then the entry goes) or keep entries for a number of days |
| Files | the uploaded files, under a name of their own | kept with the entry; an upload no entry took goes after 24 hours | the form's file fields |
| Answers kept only until the actions have run | a field set so | stored only while an action may still use it, then removed with every copy the actions made: the entry's mail and its runs' error messages; one no action could use is never stored | the field's "Keep the answer" |
| Sensitive answers | a field marked sensitive | stored, masked in lists, panels and previews, left out of mail, exports, search, titles, the timeline and logs unless chosen and allowed; each reveal is audited | the field's "Sensitive" and the "View sensitive data" permission |
| Consent | that it was given, the exact wording shown (by its hash) and the language | kept with the entry; the wordings themselves name no one and are kept | the consent field |
| IP address | nothing | not stored; rate limits and blocked ranges read it in memory only | the site or a form: the address with its end removed (IPv4 /24, IPv6 /48), or the full address where the host sets `JironFormBuilder:Privacy:AllowFullIp` |
| Page | the path of the page the form was on, never its query or the referrer | stored | no |
| Member | the key of the member signed in, if any | stored | a form: Anonymous in its Privacy group keeps no member, whoever is signed in (a form for members and one entry per member need it, so they cannot be anonymous); a member deleted in Umbraco has their entries anonymised, or deleted where Privacy › Data collection › Members says so, drafts always deleted and entries on legal hold kept |
| Context values | only the names the form lists, and the name of the server key a server-to-server post used | none listed | the form's developer settings |
| Time to complete | seconds from the first touch to the send | stored | no |
| Spam check | a score and its reasons | stored | the site's and the form's spam protection |
| Challenge | the id of the challenge the send answered, a random value that names no one | stored | no |
| Submission digest | a SHA-256 of the answers, which tells a send retried after a lost reply from another; anonymising blanks it | stored | no |
| Confirmation | when the address was confirmed, on a form with email confirmation | kept with the entry | no |
| Browser, screen, location | never | never | no |
| Timeline | state changes, edits (a sensitive answer's or a kept one's only by field, without texts), forwards with their recipient, legal holds | kept with the entry | no |
| Notes | what your users write about an entry | kept with the entry | your users |
| Mail | each mail's addresses, subject and body; the confirmation and resume links lose their code once their mail has left | 30 days after it was sent or gave up | no |
| Action runs | each action's state, its error code and a message of at most 1,000 characters | kept with the entry | no |
| Saved drafts ("Save and continue later") | the answers without files or consents, and a hash of the link's token; the link is mailed to the address given | until the link expires (30 days by default) | the form's setting and its days |
| Entries waiting for email confirmation | the entry | until 7 days after the confirmation link expires, so a late visitor can ask for a new link: 14 days by default | the form's setting and its days |
| Test entries from the preview | the entry, never an IP address or member | 7 days | no |
| Exports in the background | the export's file, and a record of what was asked that never names a person | 24 hours | no |
| Audit log | who did what to which item, with counts, never a value or an address | 365 days | no |
| Daily statistics | counts per form and day, without identifiers | 3 years | the site's and each form's insights switch |

### How long entries are kept

Counted from an entry's last change of state, by default: submitted 365 days, approved 365 days, rejected 90 days, Spam
30 days. The site sets these under Privacy › Retention and a form may set its own, each at most 3,650 days (ten years);
keeping a state's entries longer, for good, needs the host's `JironFormBuilder:Privacy:AllowRetentionOff`, and the
Privacy health check warns while any state is kept for good. The retention job deletes entries hourly with their
values, files, events, notes, action runs and mail. A legal hold keeps an entry past its time, with a reason and an end
at most a year ahead, and no delete takes it until then.

### What never goes to logs

Answers, email addresses, tokens, file names and IP addresses. Wherever Jiron Form Builder logs an exception, in its
background jobs, its public API and the backoffice's indexing, it logs the exception's type, the codes a database gives
and where it was thrown, never its message, which can quote a value the database refused. A test captures every log line of a submission whose
actions fail and finds none of these.

### Exports

An export of a form's entries that chooses no state leaves out the entries still waiting for their email confirmation,
and its dialog says how many: a file taken for a mailing holds only addresses someone confirmed. Choosing their state,
or ticking them, exports them.

## On the visitor's device

**No cookies.** Jiron Form Builder never sets a cookie. A captcha provider's script, once the visitor agreed to load it,
sets cookies and browser storage of its own: list them in your consent tool and your privacy policy.

**Browser storage.** Jiron Form Builder keeps one key, only for a visitor who opened the link to a form they saved for
later, in the tab's `sessionStorage` only, and removes it once the entry is sent:

| Storage | Key | When | Holds |
| --- | --- | --- | --- |
| `sessionStorage` | `jfb-resume:{form}` | only on a form with "Save and continue later", after its link was opened | the link's token, until the entry is sent |

**Requests a form page makes.** All go to your own site:

| Request | What for |
| --- | --- |
| `GET /jfb/assets/*` | the runtime, its parts and the theme's stylesheet, loaded only on pages with a form |
| `POST /jfb/api/v1/forms/{form}/challenge` | the challenge that tells a person from a script, asked when the form is first touched, or as the page loads on a form with a captcha, so its notice stands before the form is touched |
| `POST /jfb/api/v1/forms/{form}/validate` | the check of a page's answers before the next page |
| `POST /jfb/api/v1/forms/{form}/entries` | the send |
| `POST /jfb/api/v1/forms/{form}/receipt` | the answer to a send whose reply was lost |
| `POST /jfb/api/v1/forms/{form}/uploads` | an uploaded file, before the send |
| `GET /jfb/api/v1/forms/{form}/options/{field}` | the options of a field that depend on another answer |
| `GET /jfb/api/v1/forms/{form}/render` | the form again in another language or after a change |
| `POST /jfb/api/v1/forms/{form}/events` | the insights counters (a view, a start, a page reached, the kind of error a field showed), without any identifier, while the site's and the form's insights are on |
| `GET /jfb/api/v1/forms/{form}/element` | the form for the `<jfb-form>` element on a page that embeds it |
| `POST /jfb/api/v1/forms/{form}/drafts` | saving a draft, with "Save and continue later" |
| `POST /jfb/api/v1/drafts/resume` | opening a saved draft from its link, with "Save and continue later" |

**Third parties.** None: no web fonts, no analytics, no scripts from elsewhere. The one exception is a captcha that an
editor chooses for a form:

| Captcha | The visitor's browser loads | Your server asks | What your server sends |
| --- | --- | --- | --- |
| reCAPTCHA v2 or v3 | `www.google.com/recaptcha/api.js`, or `www.recaptcha.net` where the host sets `UseRecaptchaNet` | `/recaptcha/api/siteverify` on the same host | your secret key and the visitor's token, never their IP address |
| hCaptcha | `js.hcaptcha.com/1/api.js` | `api.hcaptcha.com/siteverify` | your secret key and the visitor's token, never their IP address |

The script loads only on a form that uses the captcha, and only after the visitor agrees: on the form's notice, through
a consent field, through Cookiebot, or through your consent manager calling `JironFormBuilder.consent.grant('captcha')`.
Loading it without asking needs the host's `JironFormBuilder:Privacy:AllowImplicitCaptchaConsent`, and the Privacy
health check warns while that is in use. Proof-of-work protection runs in the visitor's browser and asks only your site for its puzzle.

## What your site decides

- **Behind a proxy or load balancer,** set up ASP.NET Core's forwarded headers, or an IP address an entry keeps is the
  proxy's. Jiron Form Builder never reads `X-Forwarded-For` itself, as a visitor can write it.
- **Mail** goes through Umbraco's mail settings (`Umbraco:CMS:Global:Smtp`); your mail provider is a processor of the
  addresses and contents of the mail.
- **Copies outside Jiron Form Builder** are outside these facts, and neither retention nor an erase reaches them: a mail
  already delivered, a file someone downloaded, an export saved elsewhere, the Umbraco content a content action created
  (with its versions), what a webhook's receiver keeps, and what your own code does with Jiron Form Builder's
  notifications.
- **The confirmation link's code travels in the page's address** (`/jfb/api/v1/verify?t=…`), so web server access logs
  that keep query strings hold it until the link expires; keep such logs short, or leave the query out of them.
- **HTTPS** carries every answer; serve every page with a form over it.

## Answering a person

Privacy › People finds a person's entries in every form by an address in an email field or as a member, exports
everything about them as a ZIP (answers with each consent's wording, the entry's facts with its context values, spam
check and time to complete, the timeline, notes, files, and the mail sent to their address), puts entries on legal
hold, and erases them, deleting or anonymising with a preview first. An address typed into a text field is not found
there: search the entries lists for it too. The audit log records each step without naming the person.
