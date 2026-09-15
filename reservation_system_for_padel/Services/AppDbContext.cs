using Microsoft.EntityFrameworkCore;
using reservation_system_for_padel.Models;

namespace reservation_system_for_padel.Services
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<User> Users { get; set; } = null!;
        public DbSet<Court> Courts { get; set; } = null!;
        public DbSet<TimeSlot> TimeSlots { get; set; } = null!;
        public DbSet<Reservation> Reservations { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Jeden kurt může mít v daný den a slot pouze jednu platnou rezervaci
            modelBuilder.Entity<Reservation>()
                .HasIndex(r => new { r.CourtId, r.Date, r.TimeSlotId })
                .IsUnique();

            // Seed kurtů
            modelBuilder.Entity<Court>().HasData(
                new Court { Id = 1, Number = 1, IsActive = true },
                new Court { Id = 2, Number = 2, IsActive = true },
                new Court { Id = 3, Number = 3, IsActive = true }
            );

            // Seed 60min slotů (8:00 - 22:00)
            var slots = new List<TimeSlot>();
            int slotId = 1;
            for (int hour = 8; hour < 22; hour++)
            {
                slots.Add(new TimeSlot
                {
                    Id = slotId++,
                    StartTime = new TimeOnly(hour, 0),
                    EndTime = new TimeOnly(hour + 1, 0)
                });
            }
            modelBuilder.Entity<TimeSlot>().HasData(slots);
        }
    }
}
