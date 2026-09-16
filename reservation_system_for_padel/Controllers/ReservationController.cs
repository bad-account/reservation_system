using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using reservation_system_for_padel.Models;
using reservation_system_for_padel.Services;

namespace reservation_system_for_padel.Controllers;

public class ReservationController : Controller
{
    private readonly AppDbContext _context;

    public ReservationController(AppDbContext context)
    {
        _context = context;
    }

    private int? CurrentUserId
    {
        get
        {
            var idClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return int.TryParse(idClaim, out var id) ? id : null;
        }
    }

    // GET: /Reservation?date=2026-09-16
    public async Task<IActionResult> Index(DateOnly? date)
    {
        var targetDate = date ?? DateOnly.FromDateTime(DateTime.Today);
        var now = DateTime.Now;
        var today = DateOnly.FromDateTime(now);
        var currentTime = TimeOnly.FromDateTime(now);

        var courts = await _context.Courts
            .Where(c => c.IsActive)
            .OrderBy(c => c.Number)
            .ToListAsync();

        // Načtení časových slotů
        var querySlots = _context.TimeSlots.AsQueryable();

        // Pokud je vybrán dnešek, vyfiltrujeme sloty, jejichž StartTime už nastal nebo proběhl
        if (targetDate == today)
        {
            // Počáteční hodina z aktuálního času (např. 12:05 -> 12:00)
            var currentHourStart = new TimeOnly(currentTime.Hour, 0);
            querySlots = querySlots.Where(s => s.StartTime > currentHourStart);
        }
        else if (targetDate < today)
        {
            // Pro minulost nezobrazíme žádné sloty
            querySlots = querySlots.Where(s => false);
        }

        var slots = await querySlots
            .OrderBy(s => s.StartTime)
            .ToListAsync();

        var reservations = await _context.Reservations
            .Include(r => r.User)
            .Where(r => r.Date == targetDate && r.State != ReservationState.Canceled && r.State != ReservationState.Rejected)
            .ToListAsync();

        ViewBag.SelectedDate = targetDate;
        ViewBag.Courts = courts;
        ViewBag.TimeSlots = slots;
        ViewBag.Reservations = reservations;
        ViewBag.CurrentUserId = CurrentUserId;

        return View();
    }

    // POST: /Reservation/BookSlot
    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BookSlot(int courtId, int timeSlotId, DateOnly date)
    {
        int userId = CurrentUserId!.Value;
        var today = DateOnly.FromDateTime(DateTime.Today);

        if (date < today)
        {
            TempData["ErrorMessage"] = "Nelze rezervovat termíny v minulosti.";
            return RedirectToAction(nameof(Index), new { date = date.ToString("yyyy-MM-dd") });
        }

        // Pravidlo 1: Maximálně 2 aktivní rezervace do budoucna
        var activeCount = await _context.Reservations
            .CountAsync(r => r.UserId == userId
                          && r.Date >= today
                          && (r.State == ReservationState.Draft || r.State == ReservationState.Confirmed));

        if (activeCount >= 2)
        {
            TempData["ErrorMessage"] = "Máte již vyčerpaný limit 2 aktivních rezervací.";
            return RedirectToAction(nameof(Index), new { date = date.ToString("yyyy-MM-dd") });
        }

        // Pravidlo 2: Prevence překryvu / obsazení stejného kurtu
        bool alreadyBooked = await _context.Reservations
            .AnyAsync(r => r.CourtId == courtId
                        && r.Date == date
                        && r.TimeSlotId == timeSlotId
                        && r.State != ReservationState.Canceled
                        && r.State != ReservationState.Rejected);

        if (alreadyBooked)
        {
            TempData["ErrorMessage"] = "Tento termín kurtu byl právě obsazen.";
            return RedirectToAction(nameof(Index), new { date = date.ToString("yyyy-MM-dd") });
        }

        var res = new Reservation
        {
            CourtId = courtId,
            TimeSlotId = timeSlotId,
            Date = date,
            UserId = userId,
            State = ReservationState.Confirmed,
            CreatedAt = DateTime.UtcNow
        };


        _context.Reservations.Add(res);
        await _context.SaveChangesAsync();

        TempData["SuccessMessage"] = "Kurt byl úspěšně zarezervován!";
        return RedirectToAction(nameof(Index), new { date = date.ToString("yyyy-MM-dd") });
    }

    // POST: /Reservation/Cancel
    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int reservationId, DateOnly date)
    {
        int userId = CurrentUserId!.Value;
        bool isAdmin = User.IsInRole("Admin");

        var reservation = await _context.Reservations.FindAsync(reservationId);
        if (reservation == null)
        {
            TempData["ErrorMessage"] = "Rezervace nebyla nalezena.";
            return RedirectToAction(nameof(Index), new { date = date.ToString("yyyy-MM-dd") });
        }

        if (reservation.UserId != userId && !isAdmin)
        {
            TempData["ErrorMessage"] = "Nemáte oprávnění zrušit cizí rezervaci.";
            return RedirectToAction(nameof(Index), new { date = date.ToString("yyyy-MM-dd") });
        }

        // Místo změny stavu záznam rovnou odstraníme z DB
        _context.Reservations.Remove(reservation);
        await _context.SaveChangesAsync();

        TempData["SuccessMessage"] = "Rezervace byla úspěšně zrušena a termín je opět volný.";
        return RedirectToAction(nameof(Index), new { date = date.ToString("yyyy-MM-dd") });
    }

    [Authorize]
    public async Task<IActionResult> MyReservations()
    {
        int userId = CurrentUserId!.Value;
        var now = DateTime.Now;
        var today = DateOnly.FromDateTime(now);
        var currentTime = TimeOnly.FromDateTime(now);

        var myReservations = await _context.Reservations
            .Include(r => r.Court)
            .Include(r => r.TimeSlot)
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.Date)
            .ThenByDescending(r => r.TimeSlot.StartTime)
            .ToListAsync();

        bool updated = false;
        foreach (var res in myReservations)
        {
            if (res.State == ReservationState.Confirmed)
            {
                if (res.Date < today || (res.Date == today && res.TimeSlot.EndTime <= currentTime))
                {
                    res.State = ReservationState.Expired;
                    updated = true;
                }
            }
        }

        if (updated)
        {
            await _context.SaveChangesAsync();
        }

        return View(myReservations);
    }
}