using System.Text;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using SmartSpendAI.Models;

namespace SmartSpendAI.Services.Email
{
    public class SmtpEmailSender : IEmailSender
    {
        private readonly ILogger<SmtpEmailSender> _logger;
        private readonly IOptionsMonitor<SmtpSettings> _smtpOptions;

        public SmtpEmailSender(
            IOptionsMonitor<SmtpSettings> smtpOptions,
            ILogger<SmtpEmailSender> logger)
        {
            _logger = logger;
            _smtpOptions = smtpOptions;
        }

        public async Task SendAsync(
            string toEmail,
            string subject,
            string htmlBody,
            string textBody,
            CancellationToken cancellationToken)
        {
            var smtpSettings = _smtpOptions.CurrentValue;
            var host = (smtpSettings.Host ?? string.Empty).Trim();
            var fromEmail = (smtpSettings.FromEmail ?? string.Empty).Trim();
            var fromName = (smtpSettings.FromName ?? string.Empty).Trim();
            var username = (smtpSettings.Username ?? string.Empty).Trim();
            var password = RemoveWhitespace(smtpSettings.Password ?? string.Empty);

            ValidateConfiguration(smtpSettings, host, fromEmail, username, password, toEmail, subject);

            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(fromName, fromEmail));
            message.To.Add(MailboxAddress.Parse(toEmail));
            message.Subject = subject;
            message.Body = new BodyBuilder
            {
                HtmlBody = htmlBody,
                TextBody = textBody
            }.ToMessageBody();

            using var smtpClient = new SmtpClient();
            cancellationToken.ThrowIfCancellationRequested();

            var secureSocketOptions = smtpSettings.EnableSsl
                ? SecureSocketOptions.StartTls
                : SecureSocketOptions.Auto;

            await smtpClient.ConnectAsync(host, smtpSettings.Port, secureSocketOptions, cancellationToken);
            await smtpClient.AuthenticateAsync(username, password, cancellationToken);
            await smtpClient.SendAsync(message, cancellationToken);
            await smtpClient.DisconnectAsync(true, cancellationToken);
        }

        private void ValidateConfiguration(
            SmtpSettings smtpSettings,
            string host,
            string fromEmail,
            string username,
            string password,
            string toEmail,
            string subject)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                var error = "SMTP chưa cấu hình: Smtp:Host đang rỗng.";
                _logger.LogError("{Error} To={ToEmail} Subject={Subject}", error, toEmail, subject);
                throw new InvalidOperationException(error);
            }

            if (string.IsNullOrWhiteSpace(fromEmail))
            {
                var error = "SMTP chưa cấu hình: Smtp:FromEmail đang rỗng.";
                _logger.LogError("{Error} To={ToEmail} Subject={Subject}", error, toEmail, subject);
                throw new InvalidOperationException(error);
            }

            if (LooksLikePlaceholder(host) || LooksLikePlaceholder(fromEmail))
            {
                var error = "SMTP chưa cấu hình: bạn đang dùng giá trị mẫu trong appsettings.Local.json.";
                _logger.LogError("{Error} To={ToEmail} Subject={Subject}", error, toEmail, subject);
                throw new InvalidOperationException(error);
            }

            if (smtpSettings.UseOAuth2)
            {
                var error = "Scope hien tai chi dung Gmail App Password. Vui long dat Smtp:UseOAuth2 = false.";
                _logger.LogError("{Error} To={ToEmail} Subject={Subject}", error, toEmail, subject);
                throw new InvalidOperationException(error);
            }

            if (string.IsNullOrWhiteSpace(username) || LooksLikePlaceholder(username))
            {
                var error = "SMTP chưa cấu hình: Smtp:Username đang rỗng hoặc placeholder.";
                _logger.LogError("{Error} To={ToEmail} Subject={Subject}", error, toEmail, subject);
                throw new InvalidOperationException(error);
            }

            if (string.IsNullOrWhiteSpace(password) || LooksLikePlaceholder(password))
            {
                var error = "SMTP chưa cấu hình: Smtp:Password đang rỗng. Hãy dùng Gmail App Password 16 ký tự.";
                _logger.LogError("{Error} To={ToEmail} Subject={Subject}", error, toEmail, subject);
                throw new InvalidOperationException(error);
            }
        }

        private static bool LooksLikePlaceholder(string value)
        {
            var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(normalized))
            {
                return false;
            }

            return normalized.Contains("replace-with") ||
                   normalized.Contains("your-email") ||
                   normalized.Contains("your-gmail-app-password");
        }

        private static string RemoveWhitespace(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(input.Length);
            foreach (var ch in input)
            {
                if (!char.IsWhiteSpace(ch))
                {
                    builder.Append(ch);
                }
            }

            return builder.ToString();
        }
    }
}
