using System.Reflection;
using System.Text.Json.Serialization;

using Azure.Core;
using Azure.Identity;

using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.OpenApi;

using Scalar.AspNetCore;

using Sheaf.Authentication;
using Sheaf.Authorization;
using Sheaf.Core;
using Sheaf.Data;
using Sheaf.Email;
using Sheaf.Endpoints;
using Sheaf.Middleware;

var builder = WebApplication.CreateBuilder(args);
var isOpenApiGeneration = Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";

builder.AddRequestSecurity(isOpenApiGeneration);
builder.AddServiceDefaults();

if (isOpenApiGeneration && builder.Configuration.GetConnectionString("Database") is null)
{
    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["ConnectionStrings:Database"] = "Server=localhost;Database=OpenApiGeneration;TrustServerCertificate=True"
    });
}

builder.AddDatabase();
IDataProtectionBuilder dataProtectionBuilder = builder.Services.AddDataProtection()
    .SetApplicationName("Sheaf")
    .PersistKeysToDbContext<ApplicationDbContext>();

if (!isOpenApiGeneration
    && !builder.Environment.IsDevelopment()
    && !builder.Environment.IsEnvironment("Testing"))
{
    string? keyIdentifier = builder.Configuration["DataProtection:KeyVaultKeyIdentifier"];
    if (!Uri.TryCreate(keyIdentifier, UriKind.Absolute, out Uri? keyVaultKeyIdentifier)
        || keyVaultKeyIdentifier is null
        || keyVaultKeyIdentifier.Scheme != Uri.UriSchemeHttps)
    {
        throw new InvalidOperationException(
            "DataProtection:KeyVaultKeyIdentifier must be configured as an absolute HTTPS URI outside Development and Testing.");
    }

    builder.Services.AddSingleton<TokenCredential>(_ => new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned));
    dataProtectionBuilder.ProtectKeysWithAzureKeyVault(
        keyVaultKeyIdentifier, services => services.GetRequiredService<TokenCredential>());
}

builder.Services.AddScoped<ChurchClaimsPrincipalFactory>();
builder.Services.AddScoped<IUserClaimsPrincipalFactory<ApplicationUser>>(services =>
    services.GetRequiredService<ChurchClaimsPrincipalFactory>());
builder.Services.AddScoped<ChurchCookieEvents>();
builder.Services.TryAddSingleton(TimeProvider.System);
builder.Services.AddOptions<SecurityStampValidatorOptions>()
    .Configure<TimeProvider>((options, timeProvider) =>
    {
        options.ValidationInterval = TimeSpan.FromMinutes(5);
        options.TimeProvider = timeProvider;
    });
builder.Services.AddOptions<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme)
    .Configure<TimeProvider>((options, timeProvider) =>
    {
        options.EventsType = typeof(ChurchCookieEvents);
        options.TimeProvider = timeProvider;
    });
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter<ChurchMembershipRole>()));

// Add services to the container.
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["identityCookie"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Cookie,
            Name = "__Host-sheaf",
            Description = "ASP.NET Core Identity application cookie."
        };

        return Task.CompletedTask;
    });

    options.AddOperationTransformer((operation, context, _) =>
    {
        if (context.Description.ActionDescriptor.EndpointMetadata.OfType<IAuthorizeData>().Any())
        {
            operation.Security ??= [];
            operation.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("identityCookie", context.Document)] = []
            });
        }

        return Task.CompletedTask;
    });
});

builder.Services.AddSheafAuthorization();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentChurchActor, HttpCurrentChurchActor>();
builder.Services.AddScoped<Sheaf.Services.TrackerScope>();
builder.Services.AddScoped<Sheaf.Services.IPeopleService, Sheaf.Services.PeopleService>();
builder.Services.AddScoped<Sheaf.Services.IGroupService, Sheaf.Services.GroupService>();
builder.Services.AddScoped<Sheaf.Services.IAssignmentService, Sheaf.Services.AssignmentService>();
builder.Services.AddScoped<Sheaf.Services.IHouseholdService, Sheaf.Services.HouseholdService>();
builder.Services.AddScoped<Sheaf.Services.IAttendanceService, Sheaf.Services.AttendanceService>();

var authBuilder = builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = IdentityConstants.ApplicationScheme;
    options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
});

authBuilder.AddIdentityCookies(options =>
{
    options.ApplicationCookie?.Configure(cookieOptions =>
    {
        cookieOptions.Cookie.Name = "__Host-sheaf";
        cookieOptions.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        cookieOptions.Cookie.HttpOnly = true;
        cookieOptions.Cookie.Path = "/";
        cookieOptions.Cookie.Domain = null;
        cookieOptions.ExpireTimeSpan = TimeSpan.FromDays(14);
        cookieOptions.SlidingExpiration = true;

        if (builder.Environment.IsDevelopment())
        {
            // In development, clients may be cross-origin so we need SameSite=None
            cookieOptions.Cookie.SameSite = SameSiteMode.None;
        }
        else
        {
            // In production, clients are expected to be same-site (same eTLD+1),
            // so Lax cookies are sent on cross-origin fetch requests.
            // SameSite=None would be blocked by iOS Safari's ITP.
            cookieOptions.Cookie.SameSite = SameSiteMode.Lax;
        }
    });
});

builder.AddSheafEmail(isOpenApiGeneration);
// Minimal API validation of DataAnnotations on endpoint parameters; invalid requests get a 400 ValidationProblem.
builder.Services.AddValidation();

var app = builder.Build();

app.UseForwardedHeaders();
app.MapDefaultEndpoints();

// Configure the HTTP request pipeline.
app.UseExceptionHandler();
app.UseRouting();

// Enable CORS
app.UseCors("AllowedOrigins");

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
    app.MapScalarApiReference().AllowAnonymous();
    app.UseMigrationsEndPoint();
}
else
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

// Add exception handling middleware
app.UseMiddleware<ExceptionHandlingMiddleware>();

app.UseMiddleware<CsrfOriginCheck>();
app.UseAuthentication();
// After authentication so per-account limits (tracker lookup) can read the user; login limits remain per IP.
app.UseRateLimiter();
app.UseAuthorization();

app.MapGet("/", () => "API is running.").AllowAnonymous();

app.MapAuthEndpoints();
app.MapTracker();
app.MapChurchSettings();

app.Run();

public partial class Program { }
