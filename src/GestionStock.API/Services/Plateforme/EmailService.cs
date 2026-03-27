using System.Net;
using System.Net.Mail;

namespace GestionStock.API.Services;

public interface IEmailService
{
    Task<bool> SendAsync(string to, string subject, string htmlBody);
}

public class EmailService : IEmailService
{
    private readonly IConfiguration _config;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IConfiguration config, ILogger<EmailService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task<bool> SendAsync(string to, string subject, string htmlBody)
    {
        try
        {
            var smtp = _config.GetSection("SmtpSettings");
            var host = smtp["Host"]!;
            var port = int.Parse(smtp["Port"]!);
            var useSsl = bool.Parse(smtp["UseSsl"]!);
            var username = smtp["Username"]!;
            var password = smtp["Password"]!;
            var fromName = smtp["FromName"] ?? "GestionStock";
            var fromEmail = smtp["FromEmail"] ?? username;

            using var client = new SmtpClient(host, port)
            {
                Credentials = new NetworkCredential(username, password),
                EnableSsl = useSsl
            };

            var message = new MailMessage
            {
                From = new MailAddress(fromEmail, fromName),
                Subject = subject,
                Body = htmlBody,
                IsBodyHtml = true
            };
            message.To.Add(to);

            await client.SendMailAsync(message);
            _logger.LogInformation("Email envoyé à {To}: {Subject}", to, subject);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Échec envoi email à {To} - mail non bloquant", to);
            // Non-bloquant : on log mais on ne propage pas
            return false;
        }
    }
}
