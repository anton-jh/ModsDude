using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using ModsDude.Server.Api.Accounts.Email;
using ModsDude.Server.Api.Accounts.Passwords;
using ModsDude.Server.Api.Accounts.SignUp;
using ModsDude.Server.Api.Invites;
using ModsDude.Server.Persistence.Accounts;
using ModsDude.Server.Persistence.DbContexts;
using OpenIddict.Validation.AspNetCore;
using System.Security.Cryptography.X509Certificates;
using System.Threading.RateLimiting;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace ModsDude.Server.Api.Accounts;

/// <summary>
/// Accounts and sign-in: ASP.NET Core Identity stores the accounts, OpenIddict issues and validates the
/// tokens, and the account pages sign the browser in with Identity's cookie.
/// </summary>
public static class AccountsSetup
{
    public static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(30);

    public const int MinimumPasswordLength = 12;

    /// <summary>The width of the email column.</summary>
    public const int MaximumEmailLength = 256;

    /// <summary>For the account pages that need the browser signed in.</summary>
    public const string SignedInPolicy = "AccountSignedIn";


    public static WebApplicationBuilder AddAccounts(this WebApplicationBuilder builder, bool isDescribingOnly)
    {
        var services = builder.Services;
        var certificates = LoadCertificates(builder, isDescribingOnly);

        // Behind Caddy, on the compose network. The issuer in every token and the rate limiter's
        // partitions depend on the scheme and address it forwards, so only private networks are trusted
        // to forward them.
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("10.0.0.0/8"));
            options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("172.16.0.0/12"));
            options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("192.168.0.0/16"));
        });

        // In the database, so the cookies and antiforgery tokens of the account pages survive a deploy,
        // and encrypted with the token certificate, so a database backup alone cannot forge them.
        var dataProtection = services.AddDataProtection()
            .SetApplicationName("ModsDude")
            .PersistKeysToDbContext<ApplicationDbContext>();

        if (certificates is not null)
        {
            dataProtection.ProtectKeysWithCertificate(certificates.Encryption);
        }

        services
            .AddIdentityCore<UserAccount>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.SignIn.RequireConfirmedEmail = true;

                options.Password.RequiredLength = MinimumPasswordLength;
                options.Password.RequiredUniqueChars = 1;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;

                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);

                options.ClaimsIdentity.UserIdClaimType = Claims.Subject;
                options.ClaimsIdentity.EmailClaimType = Claims.Email;
            })
            .AddSignInManager()
            .AddDefaultTokenProviders()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddPasswordValidator<PasswordRulesValidator>()
            .AddPasswordValidator<PwnedPasswordValidator>();

        services
            .AddAuthentication(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)
            .AddIdentityCookies();

        services.AddAuthorizationBuilder()
            .AddPolicy(SignedInPolicy, policy => policy
                .AddAuthenticationSchemes(IdentityConstants.ApplicationScheme)
                .RequireAuthenticatedUser());

        // Checked on every request rather than every 30 minutes, so a changed password signs out every
        // browser at once and none of them can be used to get new tokens.
        services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.Zero);

        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = "ModsDude.Account";
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.LoginPath = AccountPagePaths.SignIn(null);
            options.AccessDeniedPath = AccountPagePaths.SignIn(null);
            options.ExpireTimeSpan = TimeSpan.FromDays(14);
            options.SlidingExpiration = true;
        });

        services.AddOpenIddict()
            .AddCore(options => options.UseEntityFrameworkCore().UseDbContext<ApplicationDbContext>())
            .AddServer(options =>
            {
                options
                    .SetAuthorizationEndpointUris(ConnectEndpoints.AuthorizePath.TrimStart('/'))
                    .SetTokenEndpointUris(ConnectEndpoints.TokenPath.TrimStart('/'));

                options
                    .AllowAuthorizationCodeFlow()
                    .AllowRefreshTokenFlow()
                    .RequireProofKeyForCodeExchange()
                    .Configure(x => x.CodeChallengeMethods.Remove(CodeChallengeMethods.Plain));

                options.RegisterScopes(Scopes.OpenId, Scopes.Email, Scopes.Profile, Scopes.OfflineAccess, AccountTokens.ApiScope);
                options.RegisterPromptValues(AccountTokens.CreatePrompt);

                options
                    .SetAccessTokenLifetime(AccessTokenLifetime)
                    .SetRefreshTokenLifetime(RefreshTokenLifetime);

                // Signed but readable, like any JWT the API is handed. Refresh tokens and codes stay encrypted.
                options.DisableAccessTokenEncryption();

                AddCredentials(options, certificates, isDescribingOnly);

                options.UseAspNetCore()
                    .EnableAuthorizationEndpointPassthrough()
                    .EnableTokenEndpointPassthrough();
            })
            .AddValidation(options =>
            {
                options.UseLocalServer();
                options.AddAudiences(AccountTokens.ApiResource);
                options.UseAspNetCore();
            });

        services.AddRateLimiter(options =>
        {
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(AccountRateLimits.Partition);
            options.OnRejected = AccountRateLimits.OnRejectedAsync;
        });

        services
            .AddOptions<EmailOptions>()
            .Bind(builder.Configuration.GetSection(EmailOptions.SectionName))
            .Validate(x => x.Delivery is not EmailDelivery.Resend || x.ResendApiKey.Length > 0, $"{EmailOptions.SectionName}:ResendApiKey is required to send email through Resend.")
            .ValidateOnStart();

        if (builder.Configuration.GetSection(EmailOptions.SectionName).Get<EmailOptions>()?.Delivery is EmailDelivery.PickupFolder)
        {
            services.AddSingleton<IEmailTransport, PickupFolderEmailTransport>();
        }
        else
        {
            services.AddHttpClient<IEmailTransport, ResendEmailTransport>(client =>
            {
                client.BaseAddress = new Uri(ResendEmailTransport.BaseAddress);
                client.Timeout = TimeSpan.FromSeconds(15);
            });
        }

        services.AddHttpClient(PwnedPasswordValidator.HttpClientName, client =>
        {
            client.BaseAddress = new Uri(PwnedPasswordValidator.BaseAddress);
            client.Timeout = TimeSpan.FromSeconds(5);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("ModsDude");
        });

        services.AddScoped<IAccountEmails, AccountEmails>();
        services.AddScoped<IAccountSignUp, AccountSignUp>();
        services.AddScoped<IAccountSessions, AccountSessions>();
        services.AddScoped<IInviteCodeLookup, InviteCodeLookup>();
        services.AddScoped<ITokenPruner, TokenPruner>();

        return builder;
    }


    private static void AddCredentials(OpenIddictServerBuilder options, TokenCertificates? certificates, bool isDescribingOnly)
    {
        if (isDescribingOnly)
        {
            options.AddEphemeralEncryptionKey().AddEphemeralSigningKey();
        }
        else if (certificates is not null)
        {
            options.AddEncryptionCertificate(certificates.Encryption).AddSigningCertificate(certificates.Signing);
        }
        else
        {
            options.AddDevelopmentEncryptionCertificate().AddDevelopmentSigningCertificate();
        }
    }

    /// <summary>Null where none are needed: in Development, and while describing the API.</summary>
    private static TokenCertificates? LoadCertificates(WebApplicationBuilder builder, bool isDescribingOnly)
    {
        if (isDescribingOnly || builder.Environment.IsDevelopment())
        {
            return null;
        }

        var options = builder.Configuration.GetSection(TokenCertificatesOptions.SectionName).Get<TokenCertificatesOptions>();

        if (options is not { IsConfigured: true })
        {
            throw new InvalidOperationException(
                $"'{TokenCertificatesOptions.SectionName}' is not configured. Generate the certificates with scripts/new-token-certificates.ps1.");
        }

        return new TokenCertificates(options.LoadSigningCertificate(), options.LoadEncryptionCertificate());
    }


    private sealed record TokenCertificates(X509Certificate2 Signing, X509Certificate2 Encryption);
}
