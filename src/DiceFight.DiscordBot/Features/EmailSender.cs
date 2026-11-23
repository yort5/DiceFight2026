using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;

namespace DiceFight.DiscordBot.Features;

// Sends the verification emails. Plain SMTP, so any mailbox that allows it
// works - e.g. a Gmail account with an app password:
//
//   "Smtp": { "Host": "smtp.gmail.com", "Port": 587, "UserName": "...@gmail.com", "From": "...@gmail.com", "FromName": "Barnabas" }
//
// with the password set on the server as ..._Smtp__Password, never committed.
public interface IEmailSender
{
    Task SendAsync(SmtpSettings smtp, string to, string subject, string body, CancellationToken cancel = default);
}

public sealed class SmtpSettings
{
    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    public bool UseSsl { get; set; } = true;
    public string? UserName { get; set; }
    public string? Password { get; set; }
    // Defaults to UserName.
    public string? From { get; set; }
    public string? FromName { get; set; }

    public bool Configured => !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(From ?? UserName);

    public static SmtpSettings Read(IConfiguration section) => section.Get<SmtpSettings>() ?? new SmtpSettings();
}

public sealed class SmtpEmailSender : IEmailSender
{
    public async Task SendAsync(SmtpSettings smtp, string to, string subject, string body, CancellationToken cancel = default)
    {
        using var client = new SmtpClient(smtp.Host, smtp.Port) { EnableSsl = smtp.UseSsl, Timeout = 20_000 };
        if (!string.IsNullOrEmpty(smtp.UserName))
            client.Credentials = new NetworkCredential(smtp.UserName, smtp.Password);
        using var message = new MailMessage(new MailAddress((smtp.From ?? smtp.UserName)!, smtp.FromName), new MailAddress(to))
        {
            Subject = subject,
            Body = body,
        };
        await client.SendMailAsync(message, cancel);
    }
}
