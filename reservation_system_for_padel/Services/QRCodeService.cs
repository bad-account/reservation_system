using QRCoder;

namespace reservation_system_for_padel.Services
{
    public class QRCodeService : IQRCodeService
    {
        public byte[] GenerateReservationQrCode(int reservationId, int courtNumber, DateOnly date, TimeOnly startTime, TimeOnly endTime, string userEmail)
        {
            var qrPayload = $"PADEL-RESERVATION|ID:{reservationId}|COURT:{courtNumber}|DATE:{date:yyyy-MM-dd}|TIME:{startTime:HH:mm}-{endTime:HH:mm}|USER:{userEmail}";

            using var qrGenerator = new QRCodeGenerator();
            using var qrCodeData = qrGenerator.CreateQrCode(qrPayload, QRCodeGenerator.ECCLevel.Q);
            using var qrCode = new PngByteQRCode(qrCodeData);
            return qrCode.GetGraphic(20);
        }
    }
}
