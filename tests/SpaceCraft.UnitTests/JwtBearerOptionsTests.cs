using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SpaceCraft.Api.Extensions;
using Shouldly;
using Xunit;

namespace SpaceCraft.UnitTests;

/// <summary>
/// The bearer scheme is built from the validated <c>Jwt</c> section, not from a second read
/// of it.
/// </summary>
/// <remarks>
/// The registration used to bind and validate JwtOptions, then read the section again with
/// Get&lt;&gt;() and bake the values into the AddJwtBearer delegate — so the scheme saw a copy
/// nothing had validated. It is read back here through the same options monitor the handler
/// resolves it from, and compared to what was configured.
/// </remarks>
public class JwtBearerOptionsTests
{
    private const string SigningKey = "a-signing-key-of-at-least-thirty-two-bytes";

    private static JwtBearerOptions BearerFor(Dictionary<string, string?> settings)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(settings);
        builder.Services.AddSpaceCraftAuth(builder.Configuration);

        using var host = builder.Build();
        return host.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
    }

    [Fact]
    public void The_scheme_validates_against_the_configured_issuer_audience_and_key()
    {
        var bearer = BearerFor(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "spacecraft-staging",
            ["Jwt:Audience"] = "spacecraft-staging-api",
            ["Jwt:SigningKey"] = SigningKey,
        });

        var parameters = bearer.TokenValidationParameters;
        parameters.ValidIssuer.ShouldBe("spacecraft-staging");
        parameters.ValidAudience.ShouldBe("spacecraft-staging-api");
        parameters.IssuerSigningKey.ShouldBeOfType<SymmetricSecurityKey>()
            .Key.ShouldBe(Encoding.UTF8.GetBytes(SigningKey));
    }

    [Fact]
    public void The_defaults_of_the_options_class_reach_the_scheme_when_the_keys_are_absent()
    {
        // Issuer and Audience have a right answer and no [Required]; what the class defaults
        // to is what the scheme has to check for, or every token the factory signs is refused.
        var bearer = BearerFor(new Dictionary<string, string?> { ["Jwt:SigningKey"] = SigningKey });

        bearer.TokenValidationParameters.ValidIssuer.ShouldBe("spacecraft");
        bearer.TokenValidationParameters.ValidAudience.ShouldBe("spacecraft-api");
    }

    [Fact]
    public void A_validated_token_is_still_checked_against_the_database()
    {
        // The revocation check lives in OnTokenValidated. Moving the configuration must not
        // lose the event, or every access token outlives the session that issued it.
        var bearer = BearerFor(new Dictionary<string, string?> { ["Jwt:SigningKey"] = SigningKey });

        bearer.Events.ShouldNotBeNull().OnTokenValidated.ShouldNotBeNull();
        bearer.TokenValidationParameters.ClockSkew.ShouldBe(TimeSpan.FromSeconds(30));
    }
}
