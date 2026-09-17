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

    private async Task CleanupExpiredDraftsAsync()
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-5);
        var expiredDrafts = await _context.Reservations
            .Where(r => r.State == ReservationState.Draft && r.CreatedAt < cutoff)
            .ToListAsync();

        if (expiredDrafts.Any())
        {
            _context.Reservations.RemoveRange(expiredDrafts);
            await _context.SaveChangesAsync();
        }
    }

    // GET: /Reservation?date=2026-09-16
    public async Task<IActionResult> Index(DateOnly? date)
    {
        await CleanupExpiredDraftsAsync();

        var targetDate = date ?? DateOnly.FromDateTime(DateTime.Today);
        var now = DateTime.Now;
        var today = DateOnly.FromDateTime(now);
        var currentTime = TimeOnly.FromDateTime(now);

        var courts = await _context.Courts
            .Where(c => c.IsActive)
            .OrderBy(c => c.Number)
            .ToListAsync();

        var querySlots = _context.TimeSlots.AsQueryable();

        if (targetDate == today)
        {
            var currentHourStart = new TimeOnly(currentTime.Hour, 0);
            querySlots = querySlots.Where(s => s.StartTime > currentHourStart);
        }
        else if (targetDate < today)
        {
            querySlots = querySlots.Where(s => false);
        }

        var slots = await querySlots
            .OrderBy(s => s.StartTime)
            .ToListAsync();

        // Načteme všechny rezervace včetně DRAFTů
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

    // POST: /Reservation/CreateDraft
    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateDraft(int courtId, int timeSlotId, DateOnly date)
    {
        await CleanupExpiredDraftsAsync();
        int userId = CurrentUserId!.Value;
        var today = DateOnly.FromDateTime(DateTime.Today);

        if (date < today)
        {
            return Json(new { success = false, message = "Nelze rezervovat termíny v minulosti." });
        }

        // Pokud už uživatel má pro tento konkrétní slot rozpracovaný Draft, vrátíme ho
        var existingDraft = await _context.Reservations
            .FirstOrDefaultAsync(r => r.CourtId == courtId && r.TimeSlotId == timeSlotId && r.Date == date && r.State == ReservationState.Draft);

        if (existingDraft != null)
        {
            if (existingDraft.UserId == userId)
            {
                var remainingSec = (int)(existingDraft.CreatedAt.AddMinutes(5) - DateTime.UtcNow).TotalSeconds;
                return Json(new { success = true, reservationId = existingDraft.Id, remainingSeconds = Math.Max(0, remainingSec) });
            }
            else
            {
                return Json(new { success = false, message = "Tento termín právě rezervuje jiný uživatel." });
            }
        }

        // Pravidlo 2 aktivní rezervace
        var activeCount = await _context.Reservations
            .CountAsync(r => r.UserId == userId
                          && r.Date >= today
                          && (r.State == ReservationState.Draft || r.State == ReservationState.Confirmed));

        if (activeCount >= 2)
        {
            return Json(new { success = false, message = "Máte již vyčerpaný limit 2 aktivních rezervací." });
        }

        // Kontrola zda není obsazeno Confirmed rezervací
        bool alreadyBooked = await _context.Reservations
            .AnyAsync(r => r.CourtId == courtId
                        && r.Date == date
                        && r.TimeSlotId == timeSlotId
                        && r.State == ReservationState.Confirmed);

        if (alreadyBooked)
        {
            return Json(new { success = false, message = "Tento termín kurtu je již obsazen." });
        }

        var draft = new Reservation
        {
            CourtId = courtId,
            TimeSlotId = timeSlotId,
            Date = date,
            UserId = userId,
            State = ReservationState.Draft,
            CreatedAt = DateTime.UtcNow
        };

        _context.Reservations.Add(draft);
        await _context.SaveChangesAsync();

        return Json(new { success = true, reservationId = draft.Id, remainingSeconds = 300 });
    }

    // POST: /Reservation/ConfirmDraft
    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmDraft(int reservationId, DateOnly date)
    {
        await CleanupExpiredDraftsAsync();
        int userId = CurrentUserId!.Value;

        var reservation = await _context.Reservations.FindAsync(reservationId);
        if (reservation == null || reservation.UserId != userId || reservation.State != ReservationState.Draft)
        {
            TempData["ErrorMessage"] = "Draft rezervace vypršel nebo nebyl nalezen.";
            return RedirectToAction(nameof(Index), new { date = date.ToString("yyyy-MM-dd") });
        }

        reservation.State = ReservationState.Confirmed;
        await _context.SaveChangesAsync();

        TempData["SuccessMessage"] = "Rezervace byla úspěšně potvrzena!";
        return RedirectToAction(nameof(Index), new { date = date.ToString("yyyy-MM-dd") });
    }

    // POST: /Reservation/CancelDraft
    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelDraft(int reservationId, DateOnly date)
    {
        var reservation = await _context.Reservations.FindAsync(reservationId);
        if (reservation != null && reservation.State == ReservationState.Draft)
        {
            _context.Reservations.Remove(reservation);
            await _context.SaveChangesAsync();
        }

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

        reservation.State = ReservationState.Canceled;
        await _context.SaveChangesAsync();

        TempData["SuccessMessage"] = "Rezervace byla úspěšně zrušena a termín je opět volný.";
        return RedirectToAction(nameof(Index), new { date = date.ToString("yyyy-MM-dd") });
    }

    [Authorize]
    public async Task<IActionResult> MyReservations()
    {
        await CleanupExpiredDraftsAsync();
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