using Umbraco.Cms.Core;
using Umbraco.Cms.Core.IO;
using Umbraco.Cms.Core.PropertyEditors;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;

namespace Jiron.Umbraco.FormBuilder.Demo.Seeding;

/// <summary>
/// Creates the demo's one picture when it is missing (ROADMAP 6.3b): a stand-in for an editor's photo, which the
/// specimen form shows in its image field and on its picture choices. It is kept by its key, which the form names, and
/// it is a vector graphic, so the demo needs no photo of its own. A picture that exists is never changed.
/// </summary>
internal sealed class DemoMediaSeeder(
    IMediaService media,
    MediaFileManager files,
    MediaUrlGeneratorCollection urlGenerators,
    IShortStringHelper shortStrings,
    IContentTypeBaseServiceProvider contentTypes)
{
    /// <summary>The picture's key, which <c>Seeding/Forms/specimen.json</c> names.</summary>
    public static readonly Guid PictureKey = new("d0000000-0000-4000-8000-0000000000f1");

    private const string Resource = "Jiron.Umbraco.FormBuilder.Demo.Media.picture.svg";

    /// <summary>Creates the picture when it is missing; true when it was created.</summary>
    public bool Seed()
    {
        if (media.GetById(PictureKey) is not null)
        {
            return false;
        }

        var picture = media.CreateMedia("Sample picture", Constants.System.Root, Constants.Conventions.MediaTypes.VectorGraphicsAlias);
        picture.Key = PictureKey;
        using (var stream = typeof(DemoMediaSeeder).Assembly.GetManifestResourceStream(Resource)
            ?? throw new InvalidOperationException($"Demo seeding: the resource {Resource} is missing."))
        {
            picture.SetValue(files, urlGenerators, shortStrings, contentTypes, Constants.Conventions.Media.File, "sample-picture.svg", stream);
        }

        var saved = media.Save(picture);
        return saved.Success ? true : throw new InvalidOperationException($"Demo seeding: the sample picture was not saved ({saved.Result?.Result}).");
    }
}
