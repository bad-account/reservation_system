using reservation_system_for_padel.Models;

namespace reservation_system_for_padel.Services
{
    public interface IReservationRepository
    {
        Task<Reservation?> GetByIdAsync(int id);
        Task<Reservation?> GetDraftAsync(int courtId, int timeSlotId, DateOnly date);
        Task<int> GetActiveReservationsCountAsync(int userId, DateOnly fromDate);
        Task<bool> IsSlotBookedAsync(int courtId, DateOnly date, int timeSlotId);
        Task<List<Reservation>> GetExpiredDraftsAsync(DateTime cutoffUtc);
        Task<List<Reservation>> GetPendingReservationsAsync();
        Task AddAsync(Reservation reservation);
        void RemoveRange(IEnumerable<Reservation> reservations);
        void Remove(Reservation reservation);
        Task SaveChangesAsync();
        Task<Court?> GetCourtByIdAsync(int id);
        Task<TimeSlot?> GetTimeSlotByIdAsync(int id);
    }
}
