using System.Net;
using System.Net.Mail;
using Auth.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Auth.Application;

public interface IAuthEmailSender
{
    Task SendAsync(string recipient, string subject, string body, CancellationToken cancellationToken);
}

public sealed class SmtpAuthEmailSender(IOptions<SmtpOptions> options) : IAuthEmailSender
{
    public async Task SendAsync(string recipient, string subject, string body, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        using var client = new SmtpClient(settings.Host, settings.Port)
        {
            EnableSsl = settings.EnableSsl,
            UseDefaultCredentials = false,
            Credentials = string.IsNullOrWhiteSpace(settings.Username) ? null
                : new NetworkCredential(settings.Username, settings.Password),
            Timeout = 15000
        };
        using var message = new MailMessage(new MailAddress(settings.FromAddress, settings.FromName), new MailAddress(recipient))
        {
            Subject = subject,
            Body = body,
            IsBodyHtml = false
        };
        await client.SendMailAsync(message, cancellationToken);
    }
}

public sealed class EmailConfirmationTokenProvider(IDataProtectionProvider protection,
    ILogger<DataProtectorTokenProvider<AuthAccount>> logger)
    : DataProtectorTokenProvider<AuthAccount>(protection,
        Microsoft.Extensions.Options.Options.Create(new DataProtectionTokenProviderOptions
        { Name = "Auth.EmailConfirmation.v1", TokenLifespan = TimeSpan.FromHours(24) }), logger);

public sealed class PasswordResetTokenProvider(IDataProtectionProvider protection,
    ILogger<DataProtectorTokenProvider<AuthAccount>> logger)
    : DataProtectorTokenProvider<AuthAccount>(protection,
        Microsoft.Extensions.Options.Options.Create(new DataProtectionTokenProviderOptions
        { Name = "Auth.PasswordReset.v1", TokenLifespan = TimeSpan.FromMinutes(30) }), logger);