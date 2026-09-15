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

        var courts = await _context.Courts
            .Where(c => c.IsActive)
            .OrderBy(c => c.Number)
            .ToListAsync();

        var slots = await _context.TimeSlots
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

    // GET: /Reservation/MyReservations
    [Authorize]
    public async Task<IActionResult> MyReservations()
    {
        int userId = CurrentUserId!.Value;

        var myReservations = await _context.Reservations
            .Include(r => r.Court)
            .Include(r => r.TimeSlot)
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.Date)
            .ThenByDescending(r => r.TimeSlot.StartTime)
            .ToListAsync();

        return View(myReservations);
    }
}