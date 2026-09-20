using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using MimeKit.Utils;

namespace reservation_system_for_padel.Services;

public class EmailSender : IEmailSender
{
    private readonly IConfiguration _config;
    private readonly ILogger<EmailSender> _logger;

    public EmailSender(IConfiguration config, ILogger<EmailSender> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task SendEmailAsync(string toEmail, string subject, string htmlBody, byte[]? qrCodeBytes = null)
    {
        try
        {
            var server = _config["SmtpSettings:Server"];
            var port = int.Parse(_config["SmtpSettings:Port"] ?? "465");
            var senderEmail = _config["SmtpSettings:SenderEmail"];
            var username = _config["SmtpSettings:Username"];
            var password = _config["SmtpSettings:Password"];

            var message = new MimeMessage();
            message.From.Add(new MailboxAddress("Padel Ostrava", senderEmail));
            message.To.Add(MailboxAddress.Parse(toEmail));
            message.Subject = subject;

            var bodyBuilder = new BodyBuilder();

            // Pokud máme QR kód, vložíme ho jako inline attachment
            if (qrCodeBytes != null && qrCodeBytes.Length > 0)
            {
                var qrAttachment = bodyBuilder.LinkedResources.Add("qrcode.png", qrCodeBytes, new ContentType("image", "png"));
                qrAttachment.ContentId = MimeUtils.GenerateMessageId();

                // Vložíme obrázek přímo do HTML šablony přes cid
                htmlBody += $@"
                    <div style='margin-top: 20px;'>
                        <h4>Vstupní QR kód</h4>
                        <img src='cid:{qrAttachment.ContentId}' alt='QR kód pro vstup na kurt' style='border: 1px solid #ccc; padding: 10px; border-radius: 8px; width: 220px; height: 220px;' />
                        <p style='color: #666; font-size: 13px; margin-top: 5px;'>Ukažte tento kód u čtečky u vstupu na kurt.</p>
                    </div>";
            }

            bodyBuilder.HtmlBody = htmlBody;
            message.Body = bodyBuilder.ToMessageBody();

            using var client = new SmtpClient();
            var socketOption = port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;

            await client.ConnectAsync(server, port, socketOption);
            await client.AuthenticateAsync(username, password);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);

            _logger.LogInformation("E-mail s QR kódem úspěšně odeslán na {Email}", toEmail);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Chyba při odesílání e-mailu na {Email}", toEmail);
        }
    }
}