using Asp.Versioning;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;
using ModsDude.Server.Api.Endpoints;
using ModsDude.Server.Api.ErrorHandling;
using ModsDude.Server.Api.Maintenance;
using ModsDude.Server.Api.ModHub;
using ModsDude.Server.Api.Middleware.ErrorHandling;
using ModsDude.Server.Api.Middleware.UserLoading;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Application.Services;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Retention;
using ModsDude.Server.ModHub;
using ModsDude.Server.ModHub.Extensions;
using ModsDude.Server.Storage.Extensions;
using NSwag;
using NSwag.AspNetCore;
using NSwag.Generation.Processors.Security;
using System.Reflection;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// The OpenAPI document is written at build time by running this entry point against a server that never
// listens (see scripts/openapi.ps1). Everything that reaches outside the process - the database, storage,
// Hangfire - is skipped then: the document needs none of it, and describing the API is no reason to touch
// real data.
var isDescribingOnly = Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";

builder.Services
    .ConfigureHttpJsonOptions(options =>
    {
        options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });
builder.Services
    .Configure<Microsoft.AspNetCore.Mvc.JsonOptions>(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

builder.Services
    .AddEndpointsApiExplorer()
    .AddOpenApiDocument(config =>
    {
        var tokenEndpoint = builder.Configuration["EntraExternalId:TokenEndpoint"];
        var authorizationEndpoint = builder.Configuration["EntraExternalId:AuthorizationEndpoint"];

        config.Title = "ModsDude Server";
        config.AddSecurity("EntraExternalId", new OpenApiSecurityScheme
        {
            Type = OpenApiSecuritySchemeType.OAuth2,
            Flows = new OpenApiOAuthFlows
            {
                AuthorizationCode = new OpenApiOAuthFlow
                {
                    AuthorizationUrl = authorizationEndpoint,
                    TokenUrl = tokenEndpoint,
                    RefreshUrl = tokenEndpoint,
                    Scopes =
                    {
                        { "offline_access", "Offline access" },
                        { "openid", "OpenID" },
                        { "api://modsdude-server/act_as_user", "ModsDude Server default user scope" }
                    }
                }
            }
        });
        config.OperationProcessors.Add(new AspNetCoreOperationSecurityScopeProcessor("EntraExternalId"));

        // Relative, so the document says "wherever this is served" rather than naming a machine. Written
        // at build time there is no request to take a host from, and without any server at all NSwag's
        // client template assigns BaseUrl from a constructor parameter it never declares.
        config.PostProcess = document => document.Servers.Add(new OpenApiServer { Url = "/" });
    });

builder.Services
    .AddApiVersioning(options =>
    {
        options.DefaultApiVersion = new ApiVersion(1);
        options.ReportApiVersions = true;
        options.AssumeDefaultVersionWhenUnspecified = true;
        options.ApiVersionReader = new UrlSegmentApiVersionReader();
    })
    .AddMvc()
    .AddApiExplorer(options =>
    {
        options.GroupNameFormat = "'v'V";
        options.SubstituteApiVersionInUrl = true;
    });

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(
    options =>
    {
        builder.Configuration.Bind("EntraExternalId", options);
        options.TokenValidationParameters.NameClaimType = "name";
        options.MapInboundClaims = false;
    },
    options =>
    {
        builder.Configuration.Bind("EntraExternalId", options);
    });
builder.Services.AddAuthorization();

builder.Services.AddHttpContextAccessor();

builder.Services.AddScoped<UserLoadingMiddleware>();
builder.Services.AddScoped<NotAuthenticatedMiddleware>();

builder.Services
    .Configure<BlobReclamationOptions>(builder.Configuration.GetSection(BlobReclamationOptions.SectionName));
builder.Services.AddScoped<BlobReclamationJob>();

builder.Services.AddModHub(builder.Configuration);
builder.Services.AddScoped<ModHubCrawlJob>();

builder.Services
    .Configure<RetentionOptions>(builder.Configuration.GetSection(RetentionOptions.SectionName))
    .Configure<HangfireDashboardOptions>(builder.Configuration.GetSection(HangfireDashboardOptions.SectionName));
builder.Services.AddScoped<RetentionSweeper>();
builder.Services.AddScoped<RetentionUpkeep>();
builder.Services.AddScoped<RetentionJobs>();

// In the application's own database, under a schema of its own. Hangfire manages that schema itself,
// outside the EF migrations, which is why the two never meet. The invisibility timeout slides because
// a ModHub backfill runs for hours: a fixed one hands a job still running to a second worker after
// thirty minutes.
if (!isDescribingOnly)
{
    builder.Services.AddHangfire(config => config
        .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
        .UseSimpleAssemblyNameTypeSerializer()
        .UseRecommendedSerializerSettings()
        .UsePostgreSqlStorage(
            options => options.UseNpgsqlConnection(builder.Configuration.GetConnectionString("Database")),
            new PostgreSqlStorageOptions { UseSlidingInvisibilityTimeout = true }));
    builder.Services.AddHangfireServer();
}

builder.Services
    .AddSingleton<ITimeService, TimeService>();

builder.Services
    .AddDbContext<ApplicationDbContext>(options =>
        options.UseNpgsql(builder.Configuration.GetConnectionString("Database")));
builder.Services
    .AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<ApplicationDbContext>());

builder.Services.AddStorage(
    builder.Configuration.GetValue<string>("Storage:StorageAccountName")!,
    builder.Environment.IsDevelopment());


var app = builder.Build();

var apiVersionSet = app.NewApiVersionSet()
    .HasApiVersion(new ApiVersion(1))
    .Build();


app.UseHttpsRedirection();

var dashboard = app.Services.GetRequiredService<IOptions<HangfireDashboardOptions>>().Value;
if (isDescribingOnly)
{
    // No Hangfire to show.
}
else if (dashboard.IsConfigured)
{
    app.UseHangfireDashboard(dashboard.Path, new DashboardOptions
    {
        Authorization = [new BasicAuthDashboardFilter(dashboard.Username, dashboard.Password)],
        DisplayStorageConnectionString = false
    });
}
else
{
    app.Logger.LogInformation("The Hangfire dashboard has no username or password configured and is not mapped.");
}

if (app.Environment.IsDevelopment())
{
    app.UseOpenApi();
    app.UseSwaggerUi(config =>
    {
        config.OAuth2Client = new OAuth2ClientSettings
        {
            ClientId = builder.Configuration["SwaggerAuthentication:ClientId"],
            ClientSecret = "",
            UsePkceWithAuthorizationCodeGrant = true
        };
    });
}

app.UseMiddleware<NotAuthenticatedMiddleware>();

app.UseAuthentication();
app.UseAuthorization();

app.UseMiddleware<UserLoadingMiddleware>();

// The 401 is declared once here rather than in every endpoint's Results<...> union, because the
// endpoints that can produce it include the ones that return a bare Ok<T> and have no union.
app.MapGroup("api/v{v:apiVersion}")
    .WithApiVersionSet(apiVersionSet)
    .RequireAuthorization()
    .WithMetadata(new ProducesResponseTypeMetadata(
        StatusCodes.Status401Unauthorized,
        typeof(CustomProblemDetails),
        ["application/json"]))
    .MapAllEndpointsFromAssembly(typeof(Program).Assembly);


if (!isDescribingOnly)
{
    using (var scope = app.Services.CreateScope())
    {
        scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
            .Database.Migrate();

        // Every container this server writes to, created if it is not there. Not fatal, unlike the
        // migration above: the API serves every metadata route perfectly well without them, and a
        // storage account that is momentarily unreachable is not a reason to refuse to start. Logged as
        // an error because uploads fail until it succeeds, and a fresh storage account otherwise
        // presents as a feature that silently never works.
        var containers = new (string Name, Func<CancellationToken, Task> Ensure)[]
        {
            ("mod image", scope.ServiceProvider.GetRequiredService<IModImageStorageService>().EnsureContainerExists),
            ("mod", scope.ServiceProvider.GetRequiredService<IModStorageService>().EnsureContainerExists),
            ("savegame", scope.ServiceProvider.GetRequiredService<ISavegameStorageService>().EnsureContainerExists)
        };

        foreach (var (name, ensure) in containers)
        {
            try
            {
                await ensure(CancellationToken.None);
            }
            catch (Exception exception)
            {
                // One failure must not stop the others being tried: they are independent, and a
                // permission problem on one says nothing about the rest.
                app.Logger.LogError(exception, "Could not ensure the {Container} container exists. Uploads of that kind will fail until it does.", name);
            }
        }
    }


    // After the migration, like everything else that reads the database at startup. Registering is
    // idempotent, so a changed time in configuration simply replaces the old one.
    var recurringJobs = app.Services.GetRequiredService<IRecurringJobManager>();

    RetentionJobs.Register(recurringJobs, app.Services.GetRequiredService<IOptions<RetentionOptions>>().Value);
    BlobReclamationJob.Register(recurringJobs, app.Services.GetRequiredService<IOptions<BlobReclamationOptions>>().Value);
    ModHubCrawlJob.Register(recurringJobs, app.Services.GetRequiredService<IOptions<ModHubOptions>>().Value, app.Logger);
}

app.Run();
