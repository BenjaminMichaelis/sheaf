using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Net;

using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;

using Microsoft.Extensions.Options;

using MimeKit;
using MimeKit.Text;

using MailAddress = System.Net.Mail.MailAddress;

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
/// Sends through an SMTP server with MailKit, which Microsoft recommends over <c>System.Net.Mail.SmtpClient</c> for new
/// development. Works with any SMTP relay, including Mailpit locally and Mailjet or Azure Communication Services.
/// </summary>
internal sealed class SmtpEmailTransport(IOptions<EmailOptions> options) : IEmailTransport
{
    /// <summary>The submission port that uses TLS from the first byte (RFC 8314) rather than upgrading with STARTTLS.</summary>
    private const int ImplicitTlsPort = 465;

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        EmailOptions email = options.Value;
        SmtpOptions smtp = email.Smtp;
        if (!smtp.IsConfigured || email.From is null)
        {
            throw new InvalidOperationException("SmtpEmailTransport is only registered when a mail server is configured.");
        }

        using MimeMessage mail = new();
        mail.From.Add(MailboxAddress.Parse(email.From));
        mail.To.Add(MailboxAddress.Parse(message.To));
        mail.Subject = message.Subject;
        mail.Body = new TextPart(TextFormat.Plain) { Text = message.Body };

        using SmtpClient client = new();
        await client.ConnectAsync(smtp.Host, smtp.Port, SecureSocket(smtp), cancellationToken);
        if (!string.IsNullOrWhiteSpace(smtp.UserName))
        {
            await client.AuthenticateAsync(smtp.UserName, smtp.Password ?? "", cancellationToken);
        }
        await client.SendAsync(mail, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);
    }

    /// <summary>
    /// With TLS on, port 465 connects over TLS and any other port (587 by default) must upgrade with STARTTLS: a server
    /// that doesn't offer it fails the send rather than getting the code in the clear. Mailpit runs without TLS.
    /// </summary>
    private static SecureSocketOptions SecureSocket(SmtpOptions smtp) => smtp switch
    {
        { EnableSsl: false } => SecureSocketOptions.None,
        { Port: ImplicitTlsPort } => SecureSocketOptions.SslOnConnect,
        _ => SecureSocketOptions.StartTls
    };
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

    /// <summary>Registers the outbox, account emails, options and a transport chosen from the options.</summary>
    internal static void AddSheafEmail(this WebApplicationBuilder builder, bool documentGeneration)
    {
        builder.Services.AddSingleton<ChannelEmailOutbox>();
        builder.Services.AddSingleton<IEmailOutbox>(services => services.GetRequiredService<ChannelEmailOutbox>());
        builder.Services.AddHostedService<EmailDeliveryService>();
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
