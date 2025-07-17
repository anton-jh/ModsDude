using Asp.Versioning;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web;
using ModsDude.Server.Api.Endpoints;
using ModsDude.Server.Api.Middleware.UserLoading;
using ModsDude.Server.Application;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Application.Repositories;
using ModsDude.Server.Application.Services;
using ModsDude.Server.Domain.Common;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Repositories;
using ModsDude.Server.Storage.Extensions;
using NSwag;
using NSwag.AspNetCore;
using NSwag.Generation.Processors.Security;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddMediatR(config =>
    {
        config.RegisterServicesFromAssemblyContaining<ApplicationAssemblyMarker>();
    });

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

builder.Services
    .AddSingleton<ITimeService, TimeService>();

builder.Services
    .AddScoped<IRepoMembershipRepository, RepoMembershipRepository>()
    .AddScoped<IUserRepository, UserRepository>()
    .AddScoped<IRepoRepository, RepoRepository>()
    .AddScoped<IProfileRepository, ProfileRepository>()
    .AddScoped<IModRepository, ModRepository>();

builder.Services
    .AddDbContext<ApplicationDbContext>(options =>
        options.UseNpgsql(builder.Configuration.GetConnectionString("Database")));
builder.Services
    .AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<ApplicationDbContext>());

builder.Services.AddStorage(builder.Configuration.GetValue<string>("Storage:StorageAccountName")!);


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

app.UseAuthentication();
app.UseAuthorization();

app.UseMiddleware<UserLoadingMiddleware>();

app.MapGroup("api/v{v:apiVersion}")
    .WithApiVersionSet(apiVersionSet)
    .RequireAuthorization()
    .MapAllEndpointsFromAssembly(typeof(Program).Assembly);


using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
        .Database.Migrate();
}


app.Run();
