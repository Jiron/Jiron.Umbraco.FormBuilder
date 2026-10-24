using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.Membership;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Core.Services;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Support;

/// <summary>
/// Signs backoffice users in through Umbraco itself, without a browser: an API user of the given groups with a client
/// secret, and Umbraco's own client-credentials flow at the Management API's token endpoint, so the token is the one
/// the backoffice's scheme validates (ADR-0049). The secret is generated per user, never printed, and lives only in
/// the test's database.
/// </summary>
internal static class BackOfficeApiUsers
{
    /// <summary>The Management API's token endpoint.</summary>
    public const string TokenEndpoint = "/umbraco/management/api/v1/security/back-office/token";

    /// <summary>Creates an API user in the groups and returns its access token.</summary>
    /// <param name="services">The site's services.</param>
    /// <param name="client">A client of the site.</param>
    /// <param name="name">A name of letters and digits, unique per database.</param>
    /// <param name="groupAliases">The user groups, for example <c>admin</c>.</param>
    public static async Task<string> SignInAsync(IServiceProvider services, HttpClient client, string name, params string[] groupAliases)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(client);
        var groups = services.GetRequiredService<IUserGroupService>();
        var keys = new HashSet<Guid>();
        foreach (var alias in groupAliases)
        {
            keys.Add((await groups.GetAsync(alias))?.Key ?? throw new InvalidOperationException($"There is no user group {alias}."));
        }

        var email = $"api-{name}@jfb.test";
        var created = await services.GetRequiredService<IUserService>().CreateAsync(
            Constants.Security.SuperUserKey,
            new UserCreateModel { Email = email, UserName = email, Name = name, Kind = UserKind.Api, UserGroupKeys = keys },
            approveUser: true);
        if (!created.Success || created.Result.CreatedUser is not { } user)
        {
            throw new InvalidOperationException($"The API user {name} was not created: {created.Status}.");
        }

        var clientId = "umbraco-back-office-" + name;
        var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        using (var scope = services.GetRequiredService<IServiceScopeFactory>().CreateScope())
        {
            var saved = await scope.ServiceProvider.GetRequiredService<IBackOfficeUserClientCredentialsManager>().SaveAsync(user.Key, clientId, secret);
            if (!saved.Success)
            {
                throw new InvalidOperationException($"The client credentials of {name} were not saved: {saved.Result}.");
            }
        }

        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = clientId,
            ["client_secret"] = secret,
        });
        using var response = await client.PostAsync(new Uri(TokenEndpoint, UriKind.Relative), form);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"The token endpoint answered {(int)response.StatusCode} for {name}.");
        }

        using var token = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return token.RootElement.GetProperty("access_token").GetString() ?? throw new InvalidOperationException("The token endpoint gave no token.");
    }
}
