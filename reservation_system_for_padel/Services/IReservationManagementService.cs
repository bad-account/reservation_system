namespace reservation_system_for_padel.Services
{
    public interface IReservationManagementService
    {
        Task<(bool Success, string Message, int? ReservationId, int RemainingSeconds)> CreateDraftAsync(int courtId, int timeSlotId, DateOnly date, int userId);
        Task<(bool Success, string Message)> ConfirmDraftAsync(int reservationId, int userId);
        Task CancelDraftAsync(int reservationId);
        Task<(bool Success, string Message)> CancelReservationAsync(int reservationId, int userId, bool isAdmin);
        Task<(bool Success, string Message)> ApprovePendingReservationAsync(int reservationId);
        Task<(bool Success, string Message)> RejectPendingReservationAsync(int reservationId);
        Task CleanupExpiredReservationsAsync();
    }
}
