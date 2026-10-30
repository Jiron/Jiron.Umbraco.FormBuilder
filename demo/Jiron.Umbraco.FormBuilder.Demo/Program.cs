// The Jiron Form Builder demo site (ROADMAP 3.3). Start it with scripts/demo.ps1, which passes the generated admin,
// members and imaging key as environment variables.
using Jiron.Umbraco.FormBuilder.Demo;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

#if DEBUG
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
#endif

// Without an imaging key Umbraco generates one and writes it into the tracked appsettings.json, so a start that did
// not come through the script stops here (the imaging-key lesson, ROADMAP section 3.3).
if (string.IsNullOrEmpty(builder.Configuration["Umbraco:CMS:Imaging:HMACSecretKey"]))
{
    throw new InvalidOperationException(
        "Start the demo with scripts/demo.ps1: it passes the generated imaging key and admin through the environment.");
}

// The Content Delivery API is registered as the template's "use Delivery API" option does, and stays off until
// Umbraco:CMS:DeliveryApi:Enabled turns it on (the browser tests run the host both ways; headless forms, 6.6).
builder.CreateUmbracoBuilder()
    .AddBackOffice()
    .AddWebsite()
    .AddDeliveryApi()
    .AddComposers()
    .Build();

WebApplication app = builder.Build();

await app.BootUmbracoAsync();

// ?theme= shows the demo's forms in another theme, kept in a cookie (ROADMAP 6.3a).
app.UseDemoThemes();

app.UseUmbraco()
    .WithMiddleware(u =>
    {
        u.UseBackOffice();
        u.UseWebsite();
    })
    .WithEndpoints(u =>
    {
        u.UseBackOfficeEndpoints();
        u.UseWebsiteEndpoints();
    });

await app.RunAsync();
