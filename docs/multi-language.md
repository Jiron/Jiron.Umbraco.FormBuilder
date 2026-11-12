# Multi-language sites

How forms speak the languages of a site: the texts editors write, the words the package brings, and how a form follows
the visitor's language.

## Contents

- [How a form picks its language](#how-a-form-picks-its-language)
- [Translating a form](#translating-a-form)
- [Dictionary items](#dictionary-items)
- [The words forms bring](#the-words-forms-bring)
- [Visitor texts](#visitor-texts)
- [Right-to-left languages](#right-to-left-languages)
- [The backoffice](#the-backoffice)

## How a form picks its language

A form shows in the language of the page it is on, as Umbraco's Culture and Hostnames set it. A headless site names
the language when it asks for the form. Every text falls back step by step: the page's culture, such as `de-CH`, then
its language, `de`, then the form's default text.

Each entry remembers the language it was sent in. Its emails go out in that language, and exports show it in a
Language column.

## Translating a form

On a site with more than one language, **Language** in the canvas's toolbar switches the texts you see and edit, in
the designer, the Settings view, the actions and the preview. In another language:

- each box shows the default language's text as a grey hint until you write a translation; visitors get the default
  text until then;
- the inspector says which language you are editing;
- an element with text in some language but not this one carries **No German text yet** (or the language shown), so
  missing translations are easy to find.

Options, outcome messages, button labels, email subjects and bodies are translated the same way. The optional AI add-on
can fill in missing translations ([ai.md](ai.md)).

## Dictionary items

A text written as `#key`, for example `#Forms.Contact.Name`, takes its words from Umbraco's dictionary item with that
key, in each visitor's language. This suits sites whose translators work in Umbraco's Translation section.

- Users whose groups can open the Translation section get **Use a dictionary item** at the end of a text box, which
  opens Umbraco's own dictionary picker. It is offered for labels, help texts, placeholders, page and group titles,
  button labels and email subjects.
- Under a `#key` box, a line shows what visitors read, or warns when the item has no text in this language or doesn't
  exist; visitors then see the text as written.
- In formatted texts, such as a text block or an outcome message, use the merge tag `{{ dictionary.key }}` instead
  ([merge-tags.md](merge-tags.md)).
- Option labels and messages take a `#key` typed in.

The form check warns about a dictionary item that is missing in one of the site's languages.

## The words forms bring

The words visitors see that editors didn't write, such as "Send", "Choose a file", "Enter an email address" or
"Choose a country", come in English, German, French, Italian, Dutch, Spanish and Danish. French, Spanish,
Dutch and German address visitors formally; Italian and Danish informally, without gender. A language without words of
its own takes English.

## Visitor texts

Every one of those words can be reworded:

- **For a site:** Settings › General › **Visitor texts**, for all sites or one.
- **For a form:** its Settings view › Submission › More options › **Visitor texts**.

**Reword a text** lists them by group: around the questions, files, signature, notices, after sending, error messages,
and yes and no. Each wording is per language and one line; a text you reword in German stays as it was in French. The
form's wording comes first, then the site's, then the package's. A field's own message for one of its checks comes before
all of them.

The **Send**, **Next** and **Back** buttons have boxes of their own, in Settings › General for the site and in the
form's Settings view.

## Right-to-left languages

For a right-to-left language such as Arabic or Hebrew, the form writes `dir="rtl"` and its layout mirrors; the themes
use only logical CSS properties, so they mirror too, and emails do the same. None of the seven built-in languages is
right-to-left, so such a site supplies the words through visitor texts or dictionary items.

## The backoffice

The Form Builder section speaks English and German: a user whose Umbraco language is German gets it in German.
