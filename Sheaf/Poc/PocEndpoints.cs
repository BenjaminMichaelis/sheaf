using Microsoft.AspNetCore.Http.HttpResults;

using Sheaf.Data;
using Sheaf.Poc.Ai;
using Sheaf.Poc.Demo;

namespace Sheaf.Poc;

public sealed record DemoAccountResponse(string Email, string Password, string Name, ChurchMembershipRole Role, string Blurb);

public sealed record PocStatusResponse(bool AiLive, bool DemoTools);

public static class PocEndpoints
{
    public static IServiceCollection AddPoc(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSheafAi(configuration);
        services.AddScoped<DemoSeeder>();
        services.AddScoped<Capture.CaptureService>();
        services.AddScoped<People.FollowUpService>();
        services.AddScoped<Staff.StaffOverviewService>();
        return services;
    }

    /// <summary>Seeds the demo church on startup in Development.</summary>
    public static async Task SeedDemoAsync(this WebApplication app)
    {
        await using AsyncServiceScope scope = app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DemoSeeder>().EnsureSeededAsync(CancellationToken.None);
    }

    public static void MapPoc(this WebApplication app)
    {
        Capture.CaptureEndpoints.MapPocCapture(app);
        RouteGroupBuilder poc = app.MapGroup("/api/poc").WithTags("POC");
        bool demoTools = app.Environment.IsDevelopment();

        poc.MapGet("/status", (ISheafAi ai) => new PocStatusResponse(ai.IsLive, demoTools)).AllowAnonymous();

        // Demo logins for the sign-in page's quick picker. Development only; the values are local test data.
        poc.MapGet("/demo/accounts", Results<Ok<DemoAccountResponse[]>, NotFound> () => demoTools
                ? TypedResults.Ok(DemoData.Accounts
                    .Select(a => new DemoAccountResponse(a.Email, DemoData.Password, $"{a.FirstName} {a.LastName}", a.Role, a.Blurb))
                    .ToArray())
                : TypedResults.NotFound())
            .AllowAnonymous();

        poc.MapPost("/demo/reset", async Task<Results<NoContent, NotFound>> (IServiceScopeFactory scopes, CancellationToken ct) =>
            {
                if (!demoTools)
                {
                    return TypedResults.NotFound();
                }

                // A fresh scope: the seeder must not share the caller's church selection.
                await using AsyncServiceScope scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<DemoSeeder>().ResetAsync(ct);
                return TypedResults.NoContent();
            })
            .AllowAnonymous();
    }
}
