# Several sites in one Umbraco

One Umbraco installation often holds several websites, each a root page in the content tree with its own domains.
Jiron Form Builder serves them all from one Form Builder section, and lets each site have its own look, words, sender and
spam protection.

## Contents

- [Which site a form belongs to](#which-site-a-form-belongs-to)
- [What each site can set](#what-each-site-can-set)
- [Links in emails](#links-in-emails)
- [The form picker](#the-form-picker)
- [The public API](#the-public-api)

## Which site a form belongs to

Forms themselves belong to no site: any form can be placed on any site's pages, and the forms tree is shared. What
changes per site is how a form behaves where it is shown. A request belongs to:

1. the site of the page the form is on, or was sent from;
2. without a page, as on a headless site, the site whose domain in Umbraco's Culture and Hostnames matches the address
   the request came in on;
3. otherwise, no site: the values for all sites apply.

**An entry keeps its site,** so its later emails, forwards and notices follow the site it came from, even after its page
is gone.

## What each site can set

Settings › **General**, **Email** and **Spam protection** have a **Site** switch at the top: **All sites** first, then
each site. A value set for a site says "This site has its own value", with **Use the value for all sites** to go back.

| Settings | Each site can have its own |
| --- | --- |
| General | default theme, which fields get a mark, the Send, Next and Back labels, visitor texts, base URL and time zone |
| Email | sender name and address, default layout, who gets system notices, and where entries may be forwarded |
| Spam protection | everything: the level, content checks, blocked ranges, the captcha and how it asks for consent |

**Shared by all sites:** Files, Privacy, Integrations, Access, the settings for new forms and for history, and the whole
library of option lists, themes, email layouts and templates.

**The time zone** of a site is used for dates in its emails and exports, and for the times its forms open and close.

**Spam protection** on a request without a page, such as a form placed by a view outside the content tree, uses the
strictest settings of all sites together, never what the request's address claims ([spam-protection.md](spam-protection.md#several-sites)).

A root page deleted for good takes its settings with it; one in the Recycle Bin keeps them.

## Links in emails

Links in a site's emails start with its base URL, in this order:

1. the site's own **Base URL** in Settings › General;
2. the value for all sites;
3. the site's first `https` domain in Umbraco;
4. `Umbraco:CMS:WebRouting:UmbracoApplicationUrl`.

The address a request came in on is never used. The **Base URLs** health check names each site without one
([email.md](email.md#links-in-emails)).

## The form picker

Each form picker data type can be set up for where it is used, so one site's editors see only that site's forms:

- **Allowed folders:** the forms in these folders, and in the folders below them, can be picked.
- **Allowed forms:** these forms can be picked too. With no allowed folders or forms, every form can be picked.
- **Theme:** every form placed with this picker renders in this theme, whatever the form says.
- **Page after a send:** visitors go to this page once they have sent a form placed here.
- **Allow several forms:** off by default.

Give each site's document types a picker data type of their own, limited to that site's folder.

## The public API

The public API has one address for the whole installation, `/jfb/api/v1` unless configured otherwise. Each site's own
pages may always call it. A headless front end on another origin needs that origin in
`JironFormBuilder:PublicApi:AllowedOrigins` ([headless.md](headless.md)).
