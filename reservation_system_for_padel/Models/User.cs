namespace reservation_system_for_padel.Models
{
    public enum UserRole
    {
        User,
        Admin
    }

    public class User
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Surname { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public UserRole Role { get; set; } = UserRole.User;

        public List<Reservation> Reservations { get; set; } = new();
    }
}
