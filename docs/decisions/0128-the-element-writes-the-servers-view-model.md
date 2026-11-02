# 0128. The headless element writes the server's view model

- Status: accepted; amends [ADR-0015](0015-themes-and-the-markup-contract.md)'s "renders the same markup from the
  projection": the element renders the same markup from the server's view model of the projection
- Date: 2026-10-31; amended 2026-10-31 (6.8a: a name the form does not list never reaches the rendering)
- Roadmap step: 6.6d (decided; builds the endpoint), 6.6e (the element's markup), 6.6f (the element on a headless
  page), 6.6g (its pages and sending)

## Context

`<jfb-form>` renders a published form on a site that does not use Umbraco's Razor views, in the light DOM, so the
themes, the site's CSS and the website runtime behave as on a Razor page. Its markup must be the server's: the same
elements, ids, classes, states and hooks (ADR-0015), held to a fixture of whole forms that the integration tests
write and the element's tests read.

The Razor views do not read the projection directly. The server first builds a view model of the form's first render
(`FormModels.BuildAsync`, about 2,500 lines with its parts): every id below the instance, the label element and the
marks of each field, the required note, the current page, the progress and the navigation with the site's button
labels, the default values and the state the rules give them, each repeatable group's rows, a date's window for the
site's today, each calculation's value, the options of a list, a source or a parent, the countries in the culture's
names and order, the images' addresses and sizes, the honeypot's name for the revision, the notice of a form that is
closed, full, not yet open or for members, the theme's classes and the words of the culture. A survey found 17 of
these inputs absent from the definition endpoint's answer, and no TypeScript for the defaults, the rows, the ids and
classes, the marks or the navigation.

## Options

1. **Port the view model to TypeScript** and feed it the definition answer plus a second answer with what only the
   server knows (words, theme classes, honeypot, countries, media, source options, type traits, notices). Two
   implementations of the same composition, kept equal only by the fixture; every later change to the views' models
   is made twice, and the browser's clock decides the date windows and defaults while the server's decides the
   page's.
2. **Insert the render endpoint's HTML.** Equal by construction, but it is the markup of a page that posts without
   JavaScript (its action, its route token), not of a headless form, and the step asks for a renderer from JSON that
   site developers can follow.
3. **The server answers the first render's view model as JSON; the element writes the views from it.** The
   composition stays in one place, the same code that feeds the Razor views; the element ports only the views
   (`Views/JironFormBuilder/default`, about 1,000 lines), held to the fixture. Chosen.

## Decision

- **The element endpoint** `GET forms/{key}/element?instance=&culture=&contentKey=&theme=&context-<name>=` (§4.6)
  answers the view model of the published form's first render as the render endpoint renders it (the same
  `FormRendering`, for the same members, migrations, themes and availability) with the context values the form lists,
  a `context-<name>` the form does not list dropped before the rendering, so a name a request makes up is neither
  kept nor logged (6.8a), as JSON with a `contract` number, `no-store`; a form that does not allow headless use is not found. The definition
  endpoint's answer travels inside it as the exact text the runtime reads (`modelData`), so the element writes the
  same `<script data-jfb-model>`. What only a post without JavaScript uses (the action, the encoding, the route token
  and post values, a button's `formaction`) is left out; the element's form posts through the runtime.
- **One structure:** the JSON is the view models themselves (camel case, a control's model named by its `kind`), so
  each template of the element follows its Razor view property for property. Its JSON schema is generated from the
  models' own serializer metadata and the element's types from the schema, both drift-tested, as for the projection
  (ADR-0125). The answer is a contract between the endpoint and the element of the same install (the element is
  served from `/jfb/assets/element/`); an element that meets another `contract` shows nothing and logs why.
- **The element** (`packages/element`) takes `api` (the public API's base, absolute when cross-origin), `form` (key),
  `culture`, `content` and `theme`, and `context-<name>` values like `<jfb-render>`; it takes the next free
  `jfb-<n>` instance id of the document, fetches the endpoint, writes the views with DOM methods (a rich text the
  server sanitised is parsed through a `<template>`), adds the theme's stylesheets and the runtime once per document,
  and dispatches `jfb:rendered`. It writes the built-in views only: a site's own views (a theme's folder, a form's
  `_forms/<alias>` overrides) apply to Razor pages, and a field of a site's own type gets its control from a renderer
  the site registers with the element (15.1), the field's frame without one.
- **The element's life** (decided in 6.6e): it takes its instance id on its first render and keeps it, so a render
  again keeps its ids; it renders when it first joins a document and again when `api`, `form`, `culture`,
  `content` or `theme` changes (after a change of a `context-` attribute the page calls `render()`); moved to
  another place it keeps its form and what the visitor typed; an answer, or a failure, that comes after a newer render
  started is dropped. It adds each stylesheet once per document and shows the form once they loaded or failed, or
  after three seconds, so the form never shows unstyled and then jumps; the runtime's module loads beside them and
  enhances the form once it is written. `jfb:error` carries the problem's `code`, else `network_error` (the API
  not reachable, or not allowing the page's origin), `server_error`, `contract_mismatch` or `invalid_request`.
- **No post as a page would make it:** the element's form has no address, which only a post without JavaScript uses,
  so the browser would post the visitor's answers to the page's own address. The element stops every native post of
  its form; the runtime sends it once it can (6.4i).
- **Parity:** the fixture holds, for each form, the endpoint's answer and the server's markup for the same request;
  the element's tests write the answer and compare the two as DOM trees (elements, attributes as sets, text with its
  white space collapsed), the server's markup without its post-only parts.

## Consequences

- The element costs a server render per form shown (the render budget of section 4.12), and a request that the
  page's cache cannot keep, as the render endpoint does.
- A change to a view model changes the element's types and the schema in the same commit; a change to a Razor view
  fails the fixture until the element's template follows.
- The runtime enhances the element's form as it enhances a Razor page's, so the rules, checks and texts work there
  as they do on Razor pages; it moves through its pages, each checked on the server before the next, and sends with
  its files by the same code (6.5d, 6.4i, 6.4m), and a summary page is the runtime's to write, as no post can.
- **Published again:** the element's `fresh()` writes the form as it is now, not yet on the page, and the runtime puts
  it in the old one's place on the page the visitor was on, with the answers kept and the notice, as it does on a Razor
  page with the render endpoint's markup. A form with rows or a ranking, whose answers it cannot carry over, shows the
  notice of a failed send instead, as a form with no address cannot post; so does a form that cannot be had or takes no
  entry now.
