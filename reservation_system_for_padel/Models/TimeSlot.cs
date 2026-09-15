namespace reservation_system_for_padel.Models
{
    public class TimeSlot
    {
        public int Id { get; set; }
        public TimeOnly StartTime { get; set; }
        public TimeOnly EndTime { get; set; }
    }
}
