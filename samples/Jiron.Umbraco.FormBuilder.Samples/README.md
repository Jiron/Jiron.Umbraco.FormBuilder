# Jiron Form Builder samples

What a site writes to extend Jiron Form Builder, one sample of each kind, compiled and tested with the rest of the
repository and never packed. [docs/extending.md](../../docs/extending.md) explains each extension point; this folder
shows it working. Why the samples are built this way: [ADR-0234](../../docs/decisions/0234-the-samples.md).

## Turning a sample on

No sample registers itself. Turn one on by its name in your own composer:

```csharp
using Jiron.Umbraco.FormBuilder.Samples;
using Umbraco.Cms.Core.Composing;

public sealed class SiteSamplesComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder) => builder.AddFormBuilderSample(FormBuilderSamples.Colour);
}
```

Or copy what you need: the C# into your site, the view below `Views/`, the scripts into your own `App_Plugins` folder,
with their manifests in your `umbraco-package.json`. The demo turns on the samples its setting `Demo:Samples` names
(none by default); the browser tests turn on `colour` and `embedView`, and `arithmeticCaptcha` on the host of the
protection tests.

| Name | Sample | Files |
| --- | --- | --- |
| `colour` | A colour choice: a field type whose colours are one JSON setting, drawn in the backoffice by the sample's own setting editor, shown in the designer by its own preview, on a Razor page by its own view and on a headless site by its own control. | `Colour/`, `Views/JironFormBuilder/default/fields/sample.colour.cshtml`, `wwwroot/App_Plugins/JironFormBuilderSamples/colour-swatches.js`, `colour-preview.js`, `wwwroot/jfb-samples/colour-control.js` |
| `embedView` | A view on the form workspace that shows a developer how to place the open form, in a Razor view and on another site. | `wwwroot/App_Plugins/JironFormBuilderSamples/embed-view.js` |
| `crm` | An action that adds the visitor to a list of the site's CRM, once per delivery, tried again while the CRM is away. The CRM is a stub. | `Crm/` |
| `products` | An option source of a fake shop's products in stock, under their categories. | `Products/` |
| `iCalendar` | An export format that writes a form of bookings as an `.ics` file. | `Calendar/` |
| `arithmeticCaptcha` | A captcha of the site's own: a sum sealed with the site's Data Protection keys. | `Captcha/`, `wwwroot/jfb-samples/arithmetic-captcha.js` |
| `linkDensity` | A spam check that counts the links in the answers against their words. | `Spam/` |
| `siteSettings` | A merge-tag source of the site's settings node, per site. | `SiteSettings/` |
| `eicarScanner` | A file scanner stub that quarantines every file whose name holds "eicar". | `Scanning/` |
| `blobStore` | The uploaded files in a private container of Azure Blob Storage, through Umbraco's official provider. | `Blob/` |
| `blockedDomains` | A notification handler that refuses email addresses at the domains a setting lists, at their field. | `Notifications/` |

The backoffice scripts are plain JavaScript modules that the backoffice loads as they are; their JSDoc types come from
`/App_Plugins/JironFormBuilder/types/jiron-form-builder.d.ts`, and the client's `npm run typecheck` checks them. Their
words, in English and German, are in `lang/`.

## Tests

The samples' unit tests are in `tests/Jiron.Umbraco.FormBuilder.Tests.Unit/Samples/`. The blob store passes the file
store's contract tests (`tests/Jiron.Umbraco.FormBuilder.Tests.Integration/Files/`) against the local storage emulator,
which the tests start themselves. The browser tests: the editor, the preview and the view in
`tests/e2e/specs/backoffice/extending.spec.ts`, the colour field on a Razor page and a headless one in
`tests/e2e/specs/website/samples-on-the-page.spec.ts`, and the arithmetic captcha in
`tests/e2e/specs/website/protection-on-the-page.spec.ts`.
