using System.Reflection;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.OpenApi;

using Scalar.AspNetCore;

using Sheaf.Core;
using Sheaf.Data;
using Sheaf.Endpoints;
using Sheaf.Middleware;

var builder = WebApplication.CreateBuilder(args);
var isOpenApiGeneration = Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";

builder.AddServiceDefaults();

if (isOpenApiGeneration && builder.Configuration.GetConnectionString("Database") is null)
{
    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["ConnectionStrings:Database"] = "Server=localhost;Database=OpenApiGeneration;TrustServerCertificate=True"
    });
}

builder.AddDatabase();

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
            Name = ".AspNetCore.Identity.Application",
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

// Add CORS for cross-origin clients
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowedOrigins", policy =>
    {
        if (builder.Environment.IsDevelopment())
        {
            // In development, allow any localhost origin
            policy.SetIsOriginAllowed(origin => new Uri(origin).Host == "localhost")
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials();
        }
        else
        {
            // In production, restrict to specific origins from configuration
            var allowedOrigins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>()
                ?? ["https://yourdomain.com"];
            policy.WithOrigins(allowedOrigins)
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials();
        }
    });
});

builder.Services.AddAuthorization();

var authBuilder = builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = IdentityConstants.ApplicationScheme;
    options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
});

authBuilder.AddIdentityCookies(options =>
{
    options.ApplicationCookie?.Configure(cookieOptions =>
    {
        cookieOptions.Cookie.Name = ".AspNetCore.Identity.Application";
        cookieOptions.Cookie.SecurePolicy = CookieSecurePolicy.Always;

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

// No-op email sender for now (can be replaced with real implementation)
builder.Services.AddScoped<IEmailSender<ApplicationUser>>(sp =>
    new NoOpEmailSender<ApplicationUser>());

var app = builder.Build();

app.MapDefaultEndpoints();

// Configure the HTTP request pipeline.
app.UseExceptionHandler();

// Enable CORS
app.UseCors("AllowedOrigins");

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
    app.UseMigrationsEndPoint();
}
else
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

// Add exception handling middleware
app.UseMiddleware<ExceptionHandlingMiddleware>();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => "API is running.");

app.MapAuthEndpoints();

app.Run();

// Simple no-op email sender
internal sealed class NoOpEmailSender<TUser> : IEmailSender<TUser> where TUser : class
{
    public Task SendConfirmationLinkAsync(TUser user, string email, string confirmationLink) => Task.CompletedTask;
    public Task SendPasswordResetLinkAsync(TUser user, string email, string resetLink) => Task.CompletedTask;
    public Task SendPasswordResetCodeAsync(TUser user, string email, string resetCode) => Task.CompletedTask;
}

public partial class Program { }
