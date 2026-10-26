# 0067. The Umbraco sources and the base URL

- Status: accepted; amends [ADR-0014](0014-merge-tag-language.md) (`page.url` is Umbraco's absolute address of the
  page, a member's property shows only when Umbraco lets the member see it, and the dictionary's fallback),
  [ADR-0008](0008-settings-model.md) (the base URL is an origin, and which domains give its default in which order)
  and [ADR-0065](0065-the-merge-tag-service-and-its-sources.md) (the scope carries the page and the member, and every
  built-in source exists)
- Date: 2026-10-26
- Roadmap step: 5.3e (decided and built); 9.1 gives the section the pathname `entry.url` names and adds the health
  check for a site without a base URL, 11.2 the entry's route behind `entry.url`

## Context

ADR-0014 promises five sources that read Umbraco: `page` (the content page the form is on, its properties and its
ancestors'), `member` (the signed-in member), `site` (the root's name and the base URL), `dictionary` (an item in the
culture) and `entry.url` (the entry's link in the backoffice). ADR-0008 makes the base URL a per-site setting with a
default from the site's domains. ADR-0066 left the labels that name a dictionary item (`#key`, ADR-0002) to this step.
Eight questions were open:

1. what the base URL is: an origin, or an address with a path; and which domains give its default, in which order;
2. what `page.url` is: the base URL followed by the page's path, or the address Umbraco gives the page;
3. what a property's value shows as, when Umbraco's value converters give strings, rich text, numbers, dates, pickers,
   links, blocks and more;
4. which of a member's properties a tag may show;
5. what a dictionary item falls back to when the culture has no text;
6. how labels that name a dictionary item get its text without one read of the dictionary per label;
7. where `entry.url` points while the backoffice section and the entry's screen do not exist yet (9.1, 11.2);
8. what happens when a template renders without a request (mail sent in the background), and during one.

## Options

1. **The base URL.**
   - An address with a path, such as `https://example.com/de`: Umbraco's paths start at the root, so a page's path
     after it would hold `/de` twice, and a path in the setting would mean something different for each kind of link.
   - An origin only: the scheme (`https` or `http`), the host and a port other than the scheme's, nothing after them.
     Chosen. The setting refuses anything else with its own message and stores the origin as .NET writes it (lower
     case, without the default port), so `HTTPS://www.Example.com/` is stored as `https://www.example.com`.
   - **The default from the domains:** the root's own domains only would leave a site whose languages each have a
     domain on a node below the root without one. The site's domains, the root's own first, then those deeper down
     by depth, each level in Umbraco's sort order. Chosen. A domain written without a scheme (`example.com/de`, as
     editors usually write them) counts as `https`, as a public site is reached; one written with `http://` does not
     count (ADR-0008 asks for `https`), and a wildcard domain names no host.
   - The request's Host header: never (ADR-0008); a forged header must not put another host into a mail.
2. **`page.url`.**
   - The base URL followed by the page's path: a page under a domain on another host (`lindenhall.de/de`) would get
     the base URL's host, which may not serve it.
   - The absolute address Umbraco's URL provider gives the page in the culture, with the base URL as the current
     address. Chosen. A page under a domain keeps the domain's address; a page without one gets its path after the
     base URL. Without a base URL `page.url` is empty, since a relative link in a mail leads nowhere.
3. **Property values.**
   - The value's `ToString()`: type names for pickers and blocks, markup for rich text.
   - What each kind means to a reader, and nothing for kinds that mean nothing as text. Chosen (below). A site that
     needs more from a property adds its own source (ADR-0065).
4. **A member's properties.**
   - Every property of the member type: data the type marks sensitive, or keeps from the member, would end up in
     mail, outcomes and entries.
   - Only properties the member type lets the member see and does not mark sensitive. Chosen. The name and the email
     address always show: they are the member's own and the reason the source exists.
5. **The dictionary's fallback.**
   - The culture's text only: a site with `de-CH` visitors and a `de` language would show nothing.
   - The culture, then its parent cultures (`de-CH`, then `de`), each language followed by the fallback language
     Umbraco gives it and that one's, a circle of fallbacks ending at the first repeat, and last the default language.
     An empty translation counts as none. Chosen.
6. **Dictionary texts in the answers.**
   - A read per label: a form with thirty fields would read the dictionary thirty times per mail.
   - The keys the form names (labels, option labels, a grid's row statements, and settings texts) collected once
     per scope and read in one call. Chosen. A form that names no key reads nothing.
7. **`entry.url`.**
   - A management API address that redirects to the entry: the management API takes bearer tokens, so a link opened
     from a mail would not be signed in.
   - The backoffice route `/umbraco/section/jfb-forms/workspace/jfb-entry/edit/<key>`, from constants in Core
     (`BackofficeLinks`) that the section (9.1) and the entry's workspace (11.2) must answer, each with a check.
     Chosen.
8. **With and without a request.** Umbraco's URL provider works without an HTTP context (it reads no preview cookie
   then), so mail sent in the background gets the same addresses as a page. During a request the addresses still come
   from the base URL and the domains, never from the request's host. An integration test renders both ways.

## Decision

### The base URL

`general.baseUrl` (ADR-0008) is an origin. A site's value comes first, then the global one as the settings service
merges them, then the origin of the site's first `https` domain (the root's own, then deeper by depth, each level by
sort order; a domain without a scheme counts as `https`), then the origin of
`Umbraco:CMS:WebRouting:UmbracoApplicationUrl`, otherwise none. Without a site (a form outside content), the setting
and the application URL count, and no domain is read. The request's host never counts. The links of mail and the
absolute URLs the server builds use it (§4.10).

### The scope

`MergeTagScope` gains `Page` (the content page the form is on) and `Member` (the signed-in member), both keys. The
pipeline that fills the scope (6.1) takes them from the request it already trusts: the page from the rendered or
named content, the member from Umbraco's member sign-in. Neither ever comes from a posted value.

### The sources

| Path | Gives |
| --- | --- |
| `page.name` | the page's name in the culture |
| `page.url` | Umbraco's absolute address of the page in the culture, with the base URL as the current address; empty without a base URL |
| `page.<alias>` | the page's property in the culture |
| `page.up.<alias>` | the page's own value, otherwise the nearest ancestor's that has one |
| `site.name`, `site.url` | the name of the site's root in the culture, and the base URL |
| `member.name`, `member.email` | the signed-in member's name and email address |
| `member.<alias>` | a property the member type lets the member see and does not mark sensitive |
| `dictionary.<key>` | the item's text in the culture with the fallback above; an item that does not exist renders empty with `unknown_path`, an item without a text in any language tried renders empty |
| `entry.url` | the base URL followed by the entry's backoffice route; empty without a base URL |

Without a page, a member or a site, their paths are empty, as is a property the type lacks. A content's name in a
culture is its own name there, the one name of an invariant item, or nothing. `page` and `site` are the same for every
visitor of a page; `member` is per visitor (ADR-0014's places).

**Values** as Umbraco's converters give them:

- a string as it is, rich text as its text (the markup left out, a space where a block ends, scripts and styles left
  out, character references read, white space run together), never markup;
- true and false as ADR-0066's words for yes and no;
- integers, decimals and doubles as numbers, dates, times and date-times as dates (a date-time at midnight as a date),
  each with the wire form behind the text so the `number`, `currency`, `date` and `json` filters work on it;
- a content item by its name in the culture, a link by its name or else its address;
- a value that holds several as a list of those;
- anything else (blocks, media with crops, JSON) as nothing rather than the name of its type.

### Dictionary texts in the answers

A label, an option label, a grid row's statement or a settings text that is exactly `#key` (ADR-0002) shows the item's
text in `field.*` and `entry.answers`, with the fallback above. The keys come from the form once per scope, are read in
one call and kept with the scope while it lives; a form that names none reads nothing. A key without a text shows as
written, so the editor sees what is missing (the form check warns, ADR-0002).

### The backoffice link

`BackofficeLinks.SectionPathname` (`jfb-forms`) and `BackofficeLinks.EntryEntityType` (`jfb-entry`) are the contract
between the server and the backoffice. 9.1 checks that the section's manifest uses the pathname, 11.2 that the entry's
workspace answers the route and opens the entry without its list.

## Consequences

- Links in mail are absolute, point at the site's own address and are the same whichever server or background job
  sends them.
- A site with no base URL setting, no `https` domain and no application URL gets empty `page.url`, `site.url` and
  `entry.url`; 9.1's health check warns about such a site.
- No member data that Umbraco keeps from the member or marks sensitive reaches a template.
- A dictionary edit changes mails and outcomes from their next rendering; stored consent texts are unaffected
  (ADR-0002).
- Every built-in source name of ADR-0014 now has its source, so ADR-0065's rule for a name without a source applies
  to none.
- **Revisit** if Umbraco's backoffice routes change, or if editors need block content as text.
