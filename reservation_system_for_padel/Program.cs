using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using reservation_system_for_padel.Models;
using reservation_system_for_padel.Services;

namespace reservation_system_for_padel
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            builder.Services.AddDbContext<AppDbContext>(options =>
                options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")
                                  ?? "Data Source=padel.db"));

            builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                .AddCookie(options =>
                {
                    options.LoginPath = "/Account/Login";
                    options.AccessDeniedPath = "/Account/AccessDenied";
                    options.ExpireTimeSpan = TimeSpan.FromDays(7);
                    options.SlidingExpiration = true;
                });

            builder.Services.AddControllersWithViews();
            builder.Services.AddScoped<IEmailSender, EmailSender>();

            var app = builder.Build();

            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.Database.Migrate();

                // Odstranìní starého unikátního indexu, pokud v SQLite ještì existuje
                db.Database.ExecuteSqlRaw("DROP INDEX IF EXISTS IX_Reservations_CourtId_Date_TimeSlotId;");

                // Pojistka pro již existující databázi: zajištìní existence Kurtu è. 4
                if (!db.Courts.Any(c => c.Number == 4))
                {
                    db.Courts.Add(new Court { Number = 4, IsActive = true });
                    db.SaveChanges();
                }

                // Pojistka pro již existující databázi: zajištìní existence úètu Admin
                var adminEmail = "admin@padel.cz";
                var adminUser = db.Users.FirstOrDefault(u => u.Email == adminEmail);

                if (adminUser == null)
                {
                    db.Users.Add(new User
                    {
                        Name = "Správce",
                        Surname = "Areálu",
                        Email = adminEmail,
                        PasswordHash = "admin123",
                        Role = UserRole.Admin
                    });
                    db.SaveChanges();
                }
                else if (adminUser.Role != UserRole.Admin)
                {
                    adminUser.Role = UserRole.Admin;
                    db.SaveChanges();
                }
            }

            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Home/Error");
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles();

            app.UseRouting();

            app.UseAuthentication();
            app.UseAuthorization();

            app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Reservation}/{action=Index}/{id?}");

            app.Run();
        }
    }
}