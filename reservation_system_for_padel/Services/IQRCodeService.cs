namespace reservation_system_for_padel.Services
{
    public interface IQRCodeService
    {
        byte[] GenerateReservationQrCode(int reservationId, int courtNumber, DateOnly date, TimeOnly startTime, TimeOnly endTime, string userEmail);
    }
}
