namespace reservation_system_for_padel.Models
{
    public class Court
    {
        public int Id { get; set; }
        public int Number { get; set; }
        public bool IsActive { get; set; } = true;

        public List<Reservation> Reservations { get; set; } = new();
    }
}
