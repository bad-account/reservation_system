using Microsoft.EntityFrameworkCore;
using reservation_system_for_padel.Models;

namespace reservation_system_for_padel.Services
{
    public class ReservationRepository : IReservationRepository
    {
        private readonly AppDbContext _context;

        public ReservationRepository(AppDbContext context)
        {
            _context = context;
        }

        public async Task<Reservation?> GetByIdAsync(int id)
        {
            return await _context.Reservations
                .Include(r => r.Court)
                .Include(r => r.TimeSlot)
                .Include(r => r.User)
                .FirstOrDefaultAsync(r => r.Id == id);
        }

        public async Task<Reservation?> GetDraftAsync(int courtId, int timeSlotId, DateOnly date)
        {
            return await _context.Reservations
                .FirstOrDefaultAsync(r => r.CourtId == courtId && r.TimeSlotId == timeSlotId && r.Date == date && r.State == ReservationState.Draft);
        }

        public async Task<int> GetActiveReservationsCountAsync(int userId, DateOnly fromDate)
        {
            return await _context.Reservations
                .CountAsync(r => r.UserId == userId
                              && r.Date >= fromDate
                              && (r.State == ReservationState.Draft
                                  || r.State == ReservationState.Confirmed
                                  || r.State == ReservationState.PendingApproval));
        }

        public async Task<bool> IsSlotBookedAsync(int courtId, DateOnly date, int timeSlotId)
        {
            return await _context.Reservations
                .AnyAsync(r => r.CourtId == courtId
                            && r.Date == date
                            && r.TimeSlotId == timeSlotId
                            && (r.State == ReservationState.Confirmed || r.State == ReservationState.PendingApproval));
        }

        public async Task<List<Reservation>> GetExpiredDraftsAsync(DateTime cutoffUtc)
        {
            return await _context.Reservations
                .Where(r => r.State == ReservationState.Draft && r.CreatedAt < cutoffUtc)
                .ToListAsync();
        }

        public async Task<List<Reservation>> GetPendingReservationsAsync()
        {
            return await _context.Reservations
                .Include(r => r.TimeSlot)
                .Where(r => r.State == ReservationState.PendingApproval)
                .ToListAsync();
        }

        public async Task AddAsync(Reservation reservation)
        {
            await _context.Reservations.AddAsync(reservation);
        }

        public void RemoveRange(IEnumerable<Reservation> reservations)
        {
            _context.Reservations.RemoveRange(reservations);
        }

        public void Remove(Reservation reservation)
        {
            _context.Reservations.Remove(reservation);
        }

        public async Task SaveChangesAsync()
        {
            await _context.SaveChangesAsync();
        }

        public async Task<Court?> GetCourtByIdAsync(int id)
        {
            return await _context.Courts.FindAsync(id);
        }

        public async Task<TimeSlot?> GetTimeSlotByIdAsync(int id)
        {
            return await _context.TimeSlots.FindAsync(id);
        }
    }
}
