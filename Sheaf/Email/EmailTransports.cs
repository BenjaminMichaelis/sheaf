using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Mail;

using Microsoft.Extensions.Options;

namespace Sheaf.Email;

/// <summary>The <c>Email</c> configuration section.</summary>
internal sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>The sender, e.g. <c>Sheaf &lt;no-reply@example.org&gt;</c>. Development has a default.</summary>
    public string? From { get; set; }

    public SmtpOptions Smtp { get; set; } = new();
}

/// <summary>The mail server. Without <see cref="Host"/>, Development and Testing log each message instead of sending it.</summary>
internal sealed class SmtpOptions
{
    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    public bool EnableSsl { get; set; } = true;
    public string? UserName { get; set; }
    public string? Password { get; set; }

    [MemberNotNullWhen(true, nameof(Host))]
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host);
}

/// <summary>
/// Sends through an SMTP server. <see cref="SmtpClient"/> is enough for one plain-text message at a time; Microsoft
/// recommends MailKit for new development, which is worth switching to if the app needs pooling or richer MIME.
/// </summary>
internal sealed class SmtpEmailTransport(IOptions<EmailOptions> options) : IEmailTransport
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        EmailOptions email = options.Value;
        SmtpOptions smtp = email.Smtp;
        if (!smtp.IsConfigured || email.From is null)
        {
            throw new InvalidOperationException("SmtpEmailTransport is only registered when a mail server is configured.");
        }
        using SmtpClient client = new(smtp.Host, smtp.Port) { EnableSsl = smtp.EnableSsl };
        if (!string.IsNullOrWhiteSpace(smtp.UserName))
        {
            client.Credentials = new NetworkCredential(smtp.UserName, smtp.Password);
        }
        using MailMessage mail = new(new MailAddress(email.From), new MailAddress(message.To))
        {
            Subject = message.Subject,
            Body = message.Body,
            IsBodyHtml = false
        };
        await client.SendMailAsync(mail, cancellationToken);
    }
}

/// <summary>
/// Development fallback when no mail server is configured, such as running the API without the AppHost: writes each
/// message, code included, to the log. Options validation keeps it out of deployed environments.
/// </summary>
internal sealed partial class LoggingEmailTransport(ILogger<LoggingEmailTransport> logger) : IEmailTransport
{
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        LogMessage(logger, message.To, message.Subject, message.Body);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "No mail server is configured. Email to {To}: {Subject}\n{Body}")]
    private static partial void LogMessage(ILogger logger, string to, string subject, string body);
}

/// <summary>Checks <see cref="EmailOptions"/> at startup. Deployed environments must name a mail server and sender.</summary>
internal sealed class EmailOptionsValidator(bool requireMailServer) : IValidateOptions<EmailOptions>
{
    public ValidateOptionsResult Validate(string? name, EmailOptions options)
    {
        List<string> failures = [];
        if (requireMailServer && !options.Smtp.IsConfigured)
        {
            failures.Add("Email:Smtp:Host must be configured outside Development and Testing.");
        }
        if (!MailAddress.TryCreate(options.From, out _))
        {
            failures.Add("Email:From must be a valid sender address, e.g. \"Sheaf <no-reply@example.org>\".");
        }
        if (options.Smtp.Port is <= 0 or > IPEndPoint.MaxPort)
        {
            failures.Add("Email:Smtp:Port must be a valid TCP port.");
        }
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}

internal static class EmailServiceCollectionExtensions
{
    /// <summary>The Aspire resource name of the local Mailpit server.</summary>
    internal const string MailpitConnectionName = "mailpit";

    private const string DevelopmentSender = "Sheaf <no-reply@sheaf.localhost>";

    /// <summary>Registers the outbox, the account work queue, account emails, options and a transport chosen from the options.</summary>
    internal static void AddSheafEmail(this WebApplicationBuilder builder, bool documentGeneration)
    {
        builder.Services.AddSingleton<ChannelEmailOutbox>();
        builder.Services.AddSingleton<IEmailOutbox>(services => services.GetRequiredService<ChannelEmailOutbox>());
        builder.Services.AddHostedService<EmailDeliveryService>();
        builder.Services.AddSingleton<ChannelAccountWorkScheduler>();
        builder.Services.AddSingleton<IAccountWorkScheduler>(services => services.GetRequiredService<ChannelAccountWorkScheduler>());
        builder.Services.AddHostedService<AccountWorkService>();
        builder.Services.AddSingleton<EmailCooldown>();
        builder.Services.AddScoped<AccountEmails>();

        bool developer = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing");
        string? mailpit = builder.Configuration.GetConnectionString(MailpitConnectionName);
        OptionsBuilder<EmailOptions> options = builder.Services.AddOptions<EmailOptions>()
            .BindConfiguration(EmailOptions.SectionName)
            .PostConfigure(email =>
            {
                if (!email.Smtp.IsConfigured && !string.IsNullOrWhiteSpace(mailpit))
                {
                    UseMailpit(email.Smtp, mailpit);
                }
                if (developer || documentGeneration)
                {
                    email.From ??= DevelopmentSender;
                }
            });
        builder.Services.AddSingleton<IValidateOptions<EmailOptions>>(
            new EmailOptionsValidator(requireMailServer: !developer && !documentGeneration));
        if (!documentGeneration)
        {
            options.ValidateOnStart();
        }

        builder.Services.AddSingleton<SmtpEmailTransport>();
        builder.Services.AddSingleton<LoggingEmailTransport>();
        builder.Services.AddSingleton<IEmailTransport>(services =>
            services.GetRequiredService<IOptions<EmailOptions>>().Value.Smtp.IsConfigured
                ? services.GetRequiredService<SmtpEmailTransport>()
                : services.GetRequiredService<LoggingEmailTransport>());
    }

    /// <summary>Aspire injects the local Mailpit server as "Endpoint=smtp://host:port"; it has no TLS or credentials.</summary>
    private static void UseMailpit(SmtpOptions smtp, string connectionString)
    {
        DbConnectionStringBuilder connection = new() { ConnectionString = connectionString };
        if (!connection.TryGetValue("Endpoint", out object? endpoint)
            || !Uri.TryCreate(endpoint as string, UriKind.Absolute, out Uri? uri))
        {
            throw new InvalidOperationException($"ConnectionStrings:{MailpitConnectionName} must contain an Endpoint URI.");
        }
        smtp.Host = uri.Host;
        smtp.Port = uri.Port;
        smtp.EnableSsl = false;
    }
}
