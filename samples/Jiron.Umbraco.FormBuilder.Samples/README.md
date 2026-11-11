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
(none by default); the browser tests turn on `colour` and `embedView`.

| Name | Sample | Files |
| --- | --- | --- |
| `colour` | A colour choice: a field type whose colours are one JSON setting, drawn in the backoffice by the sample's own setting editor and shown in the designer by its own preview. | `Colour/`, `wwwroot/App_Plugins/JironFormBuilderSamples/colour-swatches.js`, `colour-preview.js` |
| `embedView` | A view on the form workspace that shows a developer how to place the open form, in a Razor view and on another site. | `wwwroot/App_Plugins/JironFormBuilderSamples/embed-view.js` |

The backoffice scripts are plain JavaScript modules that the backoffice loads as they are; their JSDoc types come from
`/App_Plugins/JironFormBuilder/types/jiron-form-builder.d.ts`, and the client's `npm run typecheck` checks them. Their
words, in English and German, are in `lang/`.

## Tests

The samples' unit tests are in `tests/Jiron.Umbraco.FormBuilder.Tests.Unit/Samples/`, and the browser test of the
editor, the preview and the view in `tests/e2e/specs/backoffice/extending.spec.ts`.
