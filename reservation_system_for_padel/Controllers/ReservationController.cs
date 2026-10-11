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
    private readonly IReservationManagementService _reservationService;
    private readonly IQRCodeService _qrCodeService;

    public ReservationController(
        AppDbContext context,
        IReservationManagementService reservationService,
        IQRCodeService qrCodeService)
    {
        _context = context;
        _reservationService = reservationService;
        _qrCodeService = qrCodeService;
    }

    private int? CurrentUserId
    {
        get
        {
            var idClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return int.TryParse(idClaim, out var id) ? id : null;
        }
    }

    // GET: /Reservation?date=2026-10-05
    public async Task<IActionResult> Index(DateOnly? date)
    {
        await _reservationService.CleanupExpiredReservationsAsync();

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

        var reservations = await _context.Reservations
            .Include(r => r.User)
            .Where(r => r.Date == targetDate
                     && r.State != ReservationState.Canceled
                     && r.State != ReservationState.Rejected)
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
        int userId = CurrentUserId!.Value;
        var result = await _reservationService.CreateDraftAsync(courtId, timeSlotId, date, userId);

        if (!result.Success)
        {
            return Json(new { success = false, message = result.Message });
        }

        return Json(new { success = true, reservationId = result.ReservationId, remainingSeconds = result.RemainingSeconds });
    }

    // POST: /Reservation/ConfirmDraft
    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmDraft(int reservationId, DateOnly date)
    {
        int userId = CurrentUserId!.Value;
        var result = await _reservationService.ConfirmDraftAsync(reservationId, userId);

        if (!result.Success)
        {
            TempData["ErrorMessage"] = result.Message;
        }
        else
        {
            TempData["SuccessMessage"] = result.Message;
        }

        return RedirectToAction(nameof(Index), new { date = date.ToString("yyyy-MM-dd") });
    }

    // POST: /Reservation/CancelDraft
    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelDraft(int reservationId, DateOnly date)
    {
        await _reservationService.CancelDraftAsync(reservationId);
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

        var result = await _reservationService.CancelReservationAsync(reservationId, userId, isAdmin);

        if (!result.Success)
        {
            TempData["ErrorMessage"] = result.Message;
        }
        else
        {
            TempData["SuccessMessage"] = result.Message;
        }

        return RedirectToAction(nameof(Index), new { date = date.ToString("yyyy-MM-dd") });
    }

    // GET: /Reservation/PendingApprovals
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> PendingApprovals()
    {
        var pending = await _context.Reservations
            .Include(r => r.User)
            .Include(r => r.Court)
            .Include(r => r.TimeSlot)
            .Where(r => r.State == ReservationState.PendingApproval)
            .OrderBy(r => r.Date)
            .ThenBy(r => r.TimeSlot.StartTime)
            .ToListAsync();

        return View(pending);
    }

    // POST: /Reservation/Approve
    [HttpPost]
    [Authorize(Roles = "Admin")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(int reservationId)
    {
        var result = await _reservationService.ApprovePendingReservationAsync(reservationId);

        if (!result.Success)
        {
            TempData["ErrorMessage"] = result.Message;
        }
        else
        {
            TempData["SuccessMessage"] = result.Message;
        }

        return RedirectToAction(nameof(PendingApprovals));
    }

    // POST: /Reservation/Reject
    [HttpPost]
    [Authorize(Roles = "Admin")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(int reservationId)
    {
        var result = await _reservationService.RejectPendingReservationAsync(reservationId);

        if (!result.Success)
        {
            TempData["ErrorMessage"] = result.Message;
        }
        else
        {
            TempData["SuccessMessage"] = result.Message;
        }

        return RedirectToAction(nameof(PendingApprovals));
    }

    // GET: /Reservation/MyReservations
    [Authorize]
    public async Task<IActionResult> MyReservations()
    {
        await _reservationService.CleanupExpiredReservationsAsync();
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

    // GET: /Reservation/GetQrCode?reservationId=5
    [HttpGet]
    [Authorize]
    public async Task<IActionResult> GetQrCode(int reservationId)
    {
        int userId = CurrentUserId!.Value;
        bool isAdmin = User.IsInRole("Admin");

        var reservation = await _context.Reservations
            .Include(r => r.Court)
            .Include(r => r.TimeSlot)
            .Include(r => r.User)
            .FirstOrDefaultAsync(r => r.Id == reservationId);

        if (reservation == null || reservation.State != ReservationState.Confirmed)
        {
            return NotFound();
        }

        if (reservation.UserId != userId && !isAdmin)
        {
            return Forbid();
        }

        byte[] qrCodeBytes = _qrCodeService.GenerateReservationQrCode(
            reservation.Id, reservation.Court.Number, reservation.Date,
            reservation.TimeSlot.StartTime, reservation.TimeSlot.EndTime, reservation.User.Email);

        return File(qrCodeBytes, "image/png");
    }
}