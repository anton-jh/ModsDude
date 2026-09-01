using Asp.Versioning;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web;
using ModsDude.Server.Api.Endpoints;
using ModsDude.Server.Api.ErrorHandling;
using ModsDude.Server.Api.Maintenance;
using ModsDude.Server.Api.Middleware.ErrorHandling;
using ModsDude.Server.Api.Middleware.UserLoading;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Application.Services;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Storage.Extensions;
using NSwag;
using NSwag.AspNetCore;
using NSwag.Generation.Processors.Security;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

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
builder.Services.AddHostedService<BlobReclamationService>();

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


using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
        .Database.Migrate();

    // Not fatal, unlike the migration above: the API serves every metadata route perfectly well
    // without it, and a storage account that is momentarily unreachable is not a reason to refuse
    // to start. Logged as an error because uploads will fail until it succeeds, and the client
    // absorbs those failures by design.
    try
    {
        await scope.ServiceProvider.GetRequiredService<IModImageStorageService>()
            .EnsureContainerExists(CancellationToken.None);
    }
    catch (Exception exception)
    {
        app.Logger.LogError(exception, "Could not ensure the mod image container exists. Image uploads will fail until it does.");
    }
}


app.Run();
