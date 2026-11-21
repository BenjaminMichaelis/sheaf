using System.Net;
using System.Threading.RateLimiting;

using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;

using Sheaf.Middleware;

using IPNetwork = System.Net.IPNetwork;

namespace Sheaf.Authentication;

internal static class RequestSecurityExtensions
{
    internal const string AuthRateLimitPolicy = "AuthByIp";

    internal static void AddRequestSecurity(this WebApplicationBuilder builder, bool documentGeneration)
    {
        var origins = new AllowedOriginPolicy(builder.Configuration, builder.Environment, documentGeneration);
        builder.Services.AddSingleton(origins);
        builder.Services.AddCors(options => options.AddPolicy("AllowedOrigins", policy => policy
            .SetIsOriginAllowed(origins.IsAllowed).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

        string[] proxies = builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [];
        string[] networks = builder.Configuration.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>() ?? [];
        IPAddress[] knownProxies = [.. proxies.Select(IPAddress.Parse)];
        IPNetwork[] knownNetworks = [.. networks.Select(IPNetwork.Parse)];
        int forwardLimit = builder.Configuration.GetValue("ForwardedHeaders:ForwardLimit", 1);
        if (forwardLimit <= 0)
        {
            throw new InvalidOperationException("ForwardedHeaders:ForwardLimit must be positive.");
        }
        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();
            foreach (IPAddress proxy in knownProxies)
            {
                options.KnownProxies.Add(proxy);
            }
            foreach (IPNetwork network in knownNetworks)
            {
                options.KnownIPNetworks.Add(network);
            }
            // Empty trust lists otherwise mean trust every proxy to the framework.
            options.ForwardedHeaders = knownProxies.Length + knownNetworks.Length == 0
                ? ForwardedHeaders.None
                : ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = forwardLimit;
        });

        int attempts = builder.Configuration.GetValue("Authentication:Lockout:MaxFailedAccessAttempts", 5);
        int lockoutMinutes = builder.Configuration.GetValue("Authentication:Lockout:Minutes", 5);
        int permitLimit = builder.Configuration.GetValue("Authentication:RateLimit:PermitLimit", 10);
        int windowSeconds = builder.Configuration.GetValue("Authentication:RateLimit:WindowSeconds", 60);
        if (attempts <= 0 || lockoutMinutes <= 0 || permitLimit <= 0 || windowSeconds <= 0)
        {
            throw new InvalidOperationException("Authentication lockout and rate limit settings must be positive.");
        }
        builder.Services.Configure<IdentityOptions>(options =>
        {
            options.Lockout.AllowedForNewUsers = true;
            options.Lockout.MaxFailedAccessAttempts = attempts;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(lockoutMinutes);
        });
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(AuthRateLimitPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.MapToIPv6().ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = permitLimit,
                    Window = TimeSpan.FromSeconds(windowSeconds),
                    QueueLimit = 0,
                    AutoReplenishment = true
                }));
        });
    }
}
