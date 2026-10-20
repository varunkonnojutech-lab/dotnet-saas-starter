using System.Net;
using System.Net.Http.Json;
using System.Net.Mail;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SaasStarter.Infrastructure.Email;

public class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>Console (default, zero config) | Smtp | SendGrid</summary>
    public string Provider { get; set; } = "Console";

    public string FromAddress { get; set; } = "no-reply@saasstarter.local";
    public string FromName { get; set; } = "SaaS Starter";

    public SmtpOptions Smtp { get; set; } = new();
    public SendGridOptions SendGrid { get; set; } = new();

    public class SmtpOptions
    {
        public string Host { get; set; } = "localhost";
        public int Port { get; set; } = 25;
        public string? Username { get; set; }
        public string? Password { get; set; }
        public bool EnableSsl { get; set; }
    }

    public class SendGridOptions
    {
        public string ApiKey { get; set; } = string.Empty;
    }
}

/// <summary>
/// The one email abstraction the whole app uses. Business code never talks
/// to an SMTP server or SendGrid directly — swap providers in config.
/// </summary>
public interface IEmailSender
{
    Task SendAsync(string to, string subject, string htmlBody, CancellationToken cancellationToken = default);
}

/// <summary>Dev default: writes the email to the log so flows are testable with zero setup.</summary>
public class ConsoleEmailSender : IEmailSender
{
    private readonly ILogger<ConsoleEmailSender> _logger;

    public ConsoleEmailSender(ILogger<ConsoleEmailSender> logger) => _logger = logger;

    public Task SendAsync(string to, string subject, string htmlBody, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "[EMAIL:console] To: {To} | Subject: {Subject} | Body: {Body}", to, subject, htmlBody);
        return Task.CompletedTask;
    }
}

public class SmtpEmailSender : IEmailSender
{
    private readonly EmailOptions _options;

    public SmtpEmailSender(IOptions<EmailOptions> options) => _options = options.Value;

    public async Task SendAsync(string to, string subject, string htmlBody, CancellationToken cancellationToken = default)
    {
        using var message = new MailMessage
        {
            From = new MailAddress(_options.FromAddress, _options.FromName),
            Subject = subject,
            Body = htmlBody,
            IsBodyHtml = true
        };
        message.To.Add(new MailAddress(to));

        using var client = new SmtpClient(_options.Smtp.Host, _options.Smtp.Port)
        {
            EnableSsl = _options.Smtp.EnableSsl
        };
        if (!string.IsNullOrEmpty(_options.Smtp.Username))
        {
            client.Credentials = new NetworkCredential(_options.Smtp.Username, _options.Smtp.Password);
        }

        await client.SendMailAsync(message, cancellationToken);
    }
}

/// <summary>
/// SendGrid via its v3 HTTP API — no SDK dependency, one POST.
/// Set Email:Provider=SendGrid and Email:SendGrid:ApiKey (use a test/sandbox
/// key while developing; no real key is ever committed to this repo).
/// </summary>
public class SendGridEmailSender : IEmailSender
{
    private readonly HttpClient _httpClient;
    private readonly EmailOptions _options;

    public SendGridEmailSender(HttpClient httpClient, IOptions<EmailOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task SendAsync(string to, string subject, string htmlBody, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.SendGrid.ApiKey))
        {
            throw new InvalidOperationException("Email:SendGrid:ApiKey is not configured.");
        }

        var payload = new
        {
            personalizations = new[] { new { to = new[] { new { email = to } } } },
            from = new { email = _options.FromAddress, name = _options.FromName },
            subject,
            content = new[] { new { type = "text/html", value = htmlBody } }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.sendgrid.com/v3/mail/send")
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _options.SendGrid.ApiKey);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"SendGrid send failed ({(int)response.StatusCode}): {body}");
        }
    }
}
