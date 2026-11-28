using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;

using Anthropic;
using Anthropic.Models.Beta.Messages;

namespace Sheaf.Poc.Ai;

/// <summary>How much thinking a call gets. Drafts are quick; extraction needs more care.</summary>
public enum AiEffort
{
    Low,
    Medium,
}

/// <summary>
/// POC AI surface. Every call passes a canned result so the app still demos without an API key.
/// Callers must redact sensitive text before calling.
/// </summary>
public interface ISheafAi
{
    /// <summary>True when calls go to Claude; false when they return the canned results.</summary>
    bool IsLive { get; }

    Task<T> CompleteJsonAsync<T>(string system, string user, Func<T> canned, AiEffort effort, CancellationToken ct);

    Task<string> CompleteTextAsync(string system, string user, Func<string> canned, AiEffort effort, CancellationToken ct);
}

public sealed class CannedSheafAi : ISheafAi
{
    public bool IsLive => false;

    public Task<T> CompleteJsonAsync<T>(string system, string user, Func<T> canned, AiEffort effort, CancellationToken ct)
        => Task.FromResult(canned());

    public Task<string> CompleteTextAsync(string system, string user, Func<string> canned, AiEffort effort, CancellationToken ct)
        => Task.FromResult(canned());
}

public sealed partial class ClaudeSheafAi(AnthropicClient client, ILogger<ClaudeSheafAi> logger) : ISheafAi
{
    private const string Model = "claude-opus-5-5";

    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public bool IsLive => true;

    public async Task<T> CompleteJsonAsync<T>(string system, string user, Func<T> canned, AiEffort effort, CancellationToken ct)
    {
        var parameters = new MessageCreateParams
        {
            Model = Model,
            MaxTokens = 16000,
            System = system,
            Messages = [new BetaUserMessageParam { Content = user }],
            OutputConfig = new BetaOutputConfig
            {
                Effort = ToEffort(effort),
                Format = new BetaJsonOutputFormat { Schema = SchemaFor<T>() },
            },
            Betas = ["server-side-fallback-2026-07-01"],
            Fallbacks = new Default(),
        };

        string? text = await SendAsync(parameters, ct);
        if (text is null)
        {
            return canned();
        }

        return JsonSerializer.Deserialize<T>(text, JsonOptions)
            ?? throw new InvalidOperationException("Claude returned an empty JSON result.");
    }

    public async Task<string> CompleteTextAsync(string system, string user, Func<string> canned, AiEffort effort, CancellationToken ct)
    {
        var parameters = new MessageCreateParams
        {
            Model = Model,
            MaxTokens = 16000,
            System = system,
            Messages = [new BetaUserMessageParam { Content = user }],
            OutputConfig = new BetaOutputConfig { Effort = ToEffort(effort) },
            Betas = ["server-side-fallback-2026-07-01"],
            Fallbacks = new Default(),
        };

        return await SendAsync(parameters, ct) ?? canned();
    }

    /// <summary>Returns the response text, or null when Claude declined (the caller then uses its canned result).</summary>
    private async Task<string?> SendAsync(MessageCreateParams parameters, CancellationToken ct)
    {
        BetaMessage response = await client.Beta.Messages.Create(parameters, ct);
        if (response.StopReason == BetaStopReason.Refusal)
        {
            LogDeclined(logger);
            return null;
        }

        return string.Concat(response.Content.Select(b => b.Value).OfType<BetaTextBlock>().Select(t => t.Text));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Claude declined a POC request; using the canned result instead.")]
    private static partial void LogDeclined(ILogger logger);

    private static Effort ToEffort(AiEffort effort) => effort switch
    {
        AiEffort.Low => Effort.Low,
        _ => Effort.Medium,
    };

    /// <summary>
    /// JSON schema for <typeparamref name="T"/> in the strict shape structured outputs needs:
    /// every object closed (<c>additionalProperties: false</c>) and every property required.
    /// </summary>
    internal static Dictionary<string, JsonElement> SchemaFor<T>()
    {
        JsonNode node = JsonOptions.GetJsonSchemaAsNode(typeof(T), new JsonSchemaExporterOptions
        {
            TreatNullObliviousAsNonNullable = true,
        });
        Close(node);
        return node.AsObject().ToDictionary(p => p.Key, p => JsonSerializer.SerializeToElement(p.Value));
    }

    private static void Close(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                if (obj["properties"] is JsonObject props)
                {
                    obj["additionalProperties"] = false;
                    obj["required"] = new JsonArray(props.Select(p => (JsonNode?)JsonValue.Create(p.Key)).ToArray());
                }

                foreach (var child in obj.ToList())
                {
                    Close(child.Value);
                }

                break;
            case JsonArray array:
                foreach (var item in array)
                {
                    Close(item);
                }

                break;
        }
    }
}

public static class SheafAiServiceCollectionExtensions
{
    public static IServiceCollection AddSheafAi(this IServiceCollection services, IConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration["ANTHROPIC_API_KEY"]))
        {
            services.AddSingleton<ISheafAi, CannedSheafAi>();
        }
        else
        {
            services.AddSingleton(_ => new AnthropicClient { ApiKey = configuration["ANTHROPIC_API_KEY"] });
            services.AddSingleton<ISheafAi, ClaudeSheafAi>();
        }

        return services;
    }
}
