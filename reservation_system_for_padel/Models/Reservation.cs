namespace reservation_system_for_padel.Models
{
    public enum ReservationState
    {
        Draft,
        Confirmed,
        Rejected,
        Canceled,
        Expired
    }

    public class Reservation
    {
        public int Id { get; set; }
        public DateOnly Date { get; set; }

        public int CourtId { get; set; }
        public Court Court { get; set; } = null!;

        public int TimeSlotId { get; set; }
        public TimeSlot TimeSlot { get; set; } = null!;

        public int UserId { get; set; }
        public User User { get; set; } = null!;

        public ReservationState State { get; set; } = ReservationState.Draft;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
