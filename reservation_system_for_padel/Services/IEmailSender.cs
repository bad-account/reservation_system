namespace reservation_system_for_padel.Services
{
    public interface IEmailSender
    {
        Task SendEmailAsync(string toEmail, string subject, string htmlBody, byte[]? qrCodeBytes = null);
    }
}
