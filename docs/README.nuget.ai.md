# Jiron Form Builder AI

An optional add-on for [Jiron Form Builder](https://www.nuget.org/packages/Jiron.Umbraco.FormBuilder) on Umbraco 18
that helps editors in the designer through [Umbraco.AI](https://docs.umbraco.com/umbraco-ai).

- **Draft a form with AI** from a description, or from the pasted text of a questionnaire.
- **Translate with AI:** a form's texts into another of the site's languages, side by side before they are applied.
- **Suggest help text:** a help text and clearer wording for a field, each with its reason.

Everything it writes is a draft for an editor to review: nothing is published, or even saved, until an editor saves it.
Nothing a visitor enters ever reaches an AI, and the add-on never sees or stores a key.

## Getting started

1. Install `Jiron.Umbraco.FormBuilder.AI` beside `Jiron.Umbraco.FormBuilder`. It brings Umbraco.AI with it.
2. Install the Umbraco.AI provider package for the AI service you use, and create a connection and a chat profile in
   Umbraco.AI's own screens.
3. In the Form Builder section, open **Settings › AI** and choose the chat profile the add-on uses.

The health check **AI add-on** says whether the chosen profile exists and answers.
[The AI add-on](https://github.com/Jiron/Jiron.Umbraco.FormBuilder/blob/main/docs/ai.md) explains each feature, what
the AI sees and what to do when it cannot help.

## Licence

Like Jiron Form Builder, the add-on is **free to use and change, including commercially; no selling, no published
versions of your own (free or paid, open source or not), no competing products; source-available.** The package asks
you to accept the
[PolyForm Perimeter License 1.0.1](https://github.com/Jiron/Jiron.Umbraco.FormBuilder/blob/main/LICENSE.md), and the
[licence FAQ](https://github.com/Jiron/Jiron.Umbraco.FormBuilder/blob/main/docs/license-faq.md) explains it in plain
words.

Made by [Manassés Zähnler](https://jiron.dev) · [Ligata](https://ligata.ch). Issues and security reports go to the
[repository](https://github.com/Jiron/Jiron.Umbraco.FormBuilder).

Umbraco is a trademark of Umbraco A/S. Jiron Form Builder is an independent community project and is not affiliated
with or endorsed by Umbraco A/S.
