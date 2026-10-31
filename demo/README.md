# The demo site

`Jiron.Umbraco.FormBuilder.Demo` is the website of the fictional Lindenhall Library: Umbraco 17.7.1 on SQLite with
Jiron Form Builder, in English and German. Every roadmap step shows its work here, and the feature steps add their
demo forms to it. It is never packed or shipped. Why it is built this way: [ADR-0028](../docs/decisions/0028-demo-site.md).

## Running it

Start the demo only with the script. It generates the accounts and the imaging key and passes them to the site
through its environment; started any other way, the site refuses to start.

```
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\demo.ps1 start
```

| Command | What it does |
| --- | --- |
| `start` | Builds the client assets when they are missing or stale, starts the demo in the background and waits until it is seeded and answers in both languages. The first start installs Umbraco unattended. |
| `stop` | Stops the demo this script started, with its process tree, and nothing else. |
| `reset` | Stops it and deletes its database, logs, media and mail, so the next start is a fresh install. The accounts are kept. |
| `login` | Prints the backoffice address and where the accounts are. It never prints a password. |

| Address | |
| --- | --- |
| http://127.0.0.1:5500/en/ | the site in English |
| http://127.0.0.1:5500/de/ | the site in German |
| http://127.0.0.1:5500/umbraco | the backoffice |

The port comes from `Umbraco:CMS:WebRouting:UmbracoApplicationUrl` in `appsettings.json`, and the script refuses
ports outside 5500 to 5599. The site's output goes to `.local/demo/demo.log`.

## The Testing project and the Delivery API

The demo references the Testing project, so the browser tests run this build as their host on port 5520
([tests/e2e/README.md](../tests/e2e/README.md)). Everything in it stays off unless the `JironFormBuilderTesting`
section turns it on, which `scripts/demo.ps1` never does ([ADR-0029](../docs/decisions/0029-testing-project.md)).
The Content Delivery API is registered, as the template's "use Delivery API" option does, and stays off until
`Umbraco:CMS:DeliveryApi:Enabled` turns it on ([ADR-0031](../docs/decisions/0031-browser-harness.md)).

## Accounts and mail

The first `start` generates these into `.local/credentials.json` under `demo` (git-ignored, never printed):

- `admin`: the backoffice administrator;
- `members`: two library members, who can sign in to the members area;
- `imagingHmacSecretKey`: the key Umbraco signs image URLs with.

Mail is written to `.local/mail-pickup/demo` as `.eml` files and never sent.

## What the seeder creates

When the site has started, `Seeding/DemoSeeder.cs` creates whatever is missing and leaves everything else as it is:

- the German language, next to English as the default;
- the dictionary items of the views (`Demo.*`), in English and German;
- the document types `demoHome`, `demoPage`, `demoLegalPage`, `demoMembers`, `demoLogin`, `demoSpecimen` and
  `demoThanks`, with their templates (the legal pages, the specimen and the thanks page use `demoPage`'s), whose views
  are committed in `Views/`, with a view for each block (`Views/Partials/blocklist/Components/`, the form block's
  `jfbFormBlock.cshtml` among them);
- the form block of "Create a form block for me" (ROADMAP 6.7b), whose picker's page after a send is the thanks page,
  as a site builder sets it next ([ADR-0136](../docs/decisions/0136-the-form-picker-on-the-demo.md));
- a block list ("Demo blocks") with a text block, a highlight and the form block, labelled with the picked form's name;
- the pages, from `Seeding/DemoContent.json`: home, events, careers, feedback, contact, members, sign-in, privacy,
  imprint, the specimen, volunteer and the thanks page, with English under `/en` and German under `/de`; the volunteer
  page's form block picks the volunteer sign-up, so that page is made once the forms exist;
- the newsletter slot in the footer, with its texts and the newsletter form on every page, so a page with a form of
  its own holds two;
- the member group "Library members", the two members, and public access on the members page;
- the demo forms, from `Seeding/Forms/<alias>.json` in the package's definition format, created and published
  through the form service when their alias is missing ([ADR-0094](../docs/decisions/0094-demo-forms.md)): the
  contact form, which the contact page shows through its `form` property and gives its `formTopic` property as the
  context value `topic`: a signed-in member finds their name in its field and no email field, their own address
  standing in for it, every visitor finds "What is it about?" chosen from the page's topic, and "Where did you hear
  about us?" takes the address's `?source=` ([ADR-0124](../docs/decisions/0124-prefill.md)); the workshop booking on the events
  page, which is for members: a visitor sees a notice that asks them to sign in, a member the form, where the
  workshop is chosen from cards, already chosen when the visitor follows a workshop's link on the events page
  (`?workshop=`, a default with merge tags, [ADR-0118](../docs/decisions/0118-defaults-with-merge-tags-on-the-page.md)),
  a help text recalls its name
  ([ADR-0095](../docs/decisions/0095-visitor-texts-members-only-forms-and-the-pending-note.md)), the question whether
  anyone comes along names the visitor, the guests who come along are the rows of a repeatable group the answer shows
  ([ADR-0098](../docs/decisions/0098-the-repeaters-rows.md)), and a closing text lists them, each text following the
  answers as the visitor types ([ADR-0115](../docs/decisions/0115-answer-recall-on-the-page.md)); the feedback
  form on the feedback page, with radio buttons side by side, checkboxes with "Other", a dropdown, a rating, a grid,
  a ranking, yes or no and a scale ([ADR-0096](../docs/decisions/0096-the-choice-controls.md)), and rules: the
  evenings that suit a visitor show once they would come in the evening, and the comments are required when they
  would hardly recommend the library ([ADR-0111](../docs/decisions/0111-rules-on-the-page.md)); and the room hire
  form on its page, with a day from tomorrow to three months ahead, a start time, a room by its hourly rate (or another
  the visitor describes), the hours on a slider and the cost worked out from them as they answer, none for a room the
  library still has to quote ([ADR-0114](../docs/decisions/0114-calculations-on-the-page.md)), the key's pick-up, a
  name, an address, a file and a signature
  ([ADR-0097](../docs/decisions/0097-the-part-date-and-output-controls.md)); the newsletter form in the footer, with an
  email address, a switch and a consent; and the job application on the careers page, three pages long, with a name,
  two references as the rows of a repeatable group, a letter, a switch and a consent, which works without JavaScript
  too ([ADR-0100](../docs/decisions/0100-the-post-without-javascript.md)).

## Looking at the themes

`?theme=<alias>` on any page shows its forms in that theme, for example
`http://127.0.0.1:5500/en/feedback/?theme=bootstrap5`: the demo keeps the choice in the cookie `jfb-demo-theme`, so the
next pages and a post without JavaScript keep it, and `?theme=` alone goes back to each form's own theme. Only a theme
the site registers counts (`Jiron.Umbraco.FormBuilder.Demo/DemoThemes.cs`; ROADMAP 6.3a). A real site names a form's
theme in the form, in the General settings or where it places the form
([ADR-0105](../docs/decisions/0105-the-theme-registry-and-the-base-rules.md)).

`?scheme=dark` gives the site its dark design and the forms the dark scheme, and `?scheme=auto` does when the
system asks for dark; the demo keeps it in the cookie `jfb-demo-scheme`, and `?scheme=` alone goes back to light. The
layout puts the class `jfb-scheme-dark` or `jfb-scheme-auto` on the page's root, as a site would
([ADR-0106](../docs/decisions/0106-the-default-theme-ships.md)).

A page whose forms show in `bootstrap5` loads Bootstrap, as a Bootstrap site loads its own: the demo's copy of
Bootstrap 5.3.8 in `wwwroot/demo/vendor/bootstrap-5.3.8/` (MIT, kept byte for byte as published, with its licence and
its right-to-left build), before the site's styles. That page is then a Bootstrap site: its ground and text are
Bootstrap's body colours, and `?scheme=dark` puts Bootstrap in its dark mode (`data-bs-theme="dark"`); `?scheme=auto`
leaves Bootstrap light, since its own "auto" needs a script
([ADR-0108](../docs/decisions/0108-the-bootstrap5-theme.md)). Nothing of Bootstrap ships with the package.

The specimen (`/en/specimen/`, `/de/muster/`), which the menu leaves out, holds a form with a field of every built-in
type, on two pages, to look at a theme with: `http://127.0.0.1:5500/en/specimen/?theme=default&scheme=dark`. Its
image field and picture choices show the demo's one picture (`Seeding/Media/picture.svg`, drawn for the demo), which
the seeder adds to the media library.

The volunteer sign-up is the demo's form for headless sites: two pages, a rule that shows a field, and its settings
allow headless use. The volunteer page shows it through its form block (6.7h), and a send goes to the thanks page the
block's picker names; a site that does not run Umbraco shows it through `<jfb-form>`, as
the browser tests' static page on 5521 does (`tests/e2e/support/static-site.ts`, ROADMAP 6.6f,
[ADR-0128](../docs/decisions/0128-the-element-writes-the-servers-view-model.md)).

To add a page, add it to `DemoContent.json` with a new fixed key. Texts the views show belong in its `dictionary`. A
block with `form` (a form's alias) is a form block.
To add a form, add its definition to `Seeding/Forms/` and its alias to `DemoFormSeeder`, and name it in a page's
`form`, unless only headless sites show it. The seeder never changes a page, a type or a form that exists, so after changing texts or types, run `reset`.

Umbraco writes each new template's view back with a byte-order mark; the seeder removes it at once, so the
committed views stay as they are.

## Checking it

With the demo running:

```
node scripts/demo-check.mjs
```

It checks both languages, the members area (a wrong password, signing in and out), axe, that no page requests
anything from another host, and the backoffice sign-in with the generated admin. Screenshots go to
`.local/demo/shots/`. Its helpers are tested with the other script tests (`node --test "scripts/tests/*.test.mjs"`).
