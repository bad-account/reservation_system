using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using reservation_system_for_padel.Models;
using reservation_system_for_padel.Services;
using QRCoder;

namespace reservation_system_for_padel.Controllers;

public class ReservationController : Controller
{
    private readonly AppDbContext _context;
    private readonly IEmailSender _emailSender;

    public ReservationController(AppDbContext context, IEmailSender emailSender)
    {
        _context = context;
        _emailSender = emailSender;
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

    // GET: /Reservation?date=2026-10-05
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

        // Načítame rezervácie blokujúce kalendár (okrem zrušených a zamietnutých)
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
        await CleanupExpiredDraftsAsync();
        int userId = CurrentUserId!.Value;
        var today = DateOnly.FromDateTime(DateTime.Today);

        if (date < today)
        {
            return Json(new { success = false, message = "Nelze rezervovat termíny v minulosti." });
        }

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

        // Pravidlo BR-04: Max 2 aktívne rezervácie (Draft, Confirmed aj PendingApproval)
        var activeCount = await _context.Reservations
            .CountAsync(r => r.UserId == userId
                          && r.Date >= today
                          && (r.State == ReservationState.Draft
                              || r.State == ReservationState.Confirmed
                              || r.State == ReservationState.PendingApproval));

        if (activeCount >= 2)
        {
            return Json(new { success = false, message = "Máte již vyčerpaný limit 2 aktivních rezervací." });
        }

        // Kontrola BR-02: Termín nesmie byť obsadený Confirmed ani PendingApproval rezerváciou
        bool alreadyBooked = await _context.Reservations
            .AnyAsync(r => r.CourtId == courtId
                        && r.Date == date
                        && r.TimeSlotId == timeSlotId
                        && (r.State == ReservationState.Confirmed || r.State == ReservationState.PendingApproval));

        if (alreadyBooked)
        {
            return Json(new { success = false, message = "Tento termín kurtu je již obsazen nebo čeká na schválení." });
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

        var reservation = await _context.Reservations
            .Include(r => r.Court)
            .Include(r => r.TimeSlot)
            .Include(r => r.User)
            .FirstOrDefaultAsync(r => r.Id == reservationId);

        if (reservation == null || reservation.UserId != userId || reservation.State != ReservationState.Draft)
        {
            TempData["ErrorMessage"] = "Draft rezervace vypršel nebo nebyl nalezen.";
            return RedirectToAction(nameof(Index), new { date = date.ToString("yyyy-MM-dd") });
        }

        // ZMENA C02: Kurt č. 4 vyžaduje schválenie správcom
        if (reservation.Court.Number == 4)
        {
            reservation.State = ReservationState.PendingApproval;
            await _context.SaveChangesAsync();

            if (!string.IsNullOrWhiteSpace(reservation.User.Email))
            {
                var subject = $"Žádost o rezervaci kurtu č. {reservation.Court.Number} byla odeslána";
                var body = $@"
                    <div style='font-family: Arial, sans-serif; line-height: 1.5;'>
                        <h2 style='color: #fd7e14;'>Žádost čeká na schválení správcem</h2>
                        <p>Ahoj <strong>{reservation.User.Name}</strong>,</p>
                        <p>vaše rezervace turnajového Kurtu č. 4 byla zaevidována a čeká na posouzení správcem klubu.</p>
                        <ul>
                            <li><strong>Kurt:</strong> č. {reservation.Court.Number} (Turnajový)</li>
                            <li><strong>Datum:</strong> {reservation.Date:dd. MM. yyyy}</li>
                            <li><strong>Čas:</strong> {reservation.TimeSlot.StartTime:HH:mm} – {reservation.TimeSlot.EndTime:HH:mm}</li>
                        </ul>
                        <p>O schválení vás budeme informovat e-mailem spolu se vstupním QR kódem.</p>
                    </div>";

                await _emailSender.SendEmailAsync(reservation.User.Email, subject, body);
            }

            TempData["SuccessMessage"] = "Žádost o rezervaci Kurtu č. 4 byla odeslána správci ke schválení.";
            return RedirectToAction(nameof(Index), new { date = date.ToString("yyyy-MM-dd") });
        }

        // Štandardné kurty 1, 2, 3: Priame potvrdenie
        reservation.State = ReservationState.Confirmed;
        await _context.SaveChangesAsync();

        var qrPayload = $"PADEL-RESERVATION|ID:{reservation.Id}|COURT:{reservation.Court.Number}|DATE:{reservation.Date:yyyy-MM-dd}|TIME:{reservation.TimeSlot.StartTime:HH:mm}-{reservation.TimeSlot.EndTime:HH:mm}|USER:{reservation.User.Email}";
        using var qrGenerator = new QRCodeGenerator();
        using var qrCodeData = qrGenerator.CreateQrCode(qrPayload, QRCodeGenerator.ECCLevel.Q);
        using var qrCode = new PngByteQRCode(qrCodeData);
        byte[] qrCodeBytes = qrCode.GetGraphic(20);

        if (!string.IsNullOrWhiteSpace(reservation.User.Email))
        {
            var subject = $"Potvrzení rezervace – Kurt č. {reservation.Court.Number} - Padel Ostrava";
            var body = $@"
                <div style='font-family: Arial, sans-serif; line-height: 1.5;'>
                    <h2 style='color: #198754;'>Rezervace kurtu je potvrzena!</h2>
                    <p>Ahoj <strong>{reservation.User.Name}</strong>,</p>
                    <p>těšíme se na tebe na hřišti. Zde jsou podrobnosti:</p>
                    <ul>
                        <li><strong>Kurt:</strong> č. {reservation.Court.Number}</li>
                        <li><strong>Datum:</strong> {reservation.Date:dd. MM. yyyy}</li>
                        <li><strong>Čas:</strong> {reservation.TimeSlot.StartTime:HH:mm} – {reservation.TimeSlot.EndTime:HH:mm}</li>
                    </ul>
                </div>";

            await _emailSender.SendEmailAsync(reservation.User.Email, subject, body, qrCodeBytes);
        }

        TempData["SuccessMessage"] = "Rezervace byla úspěšně potvrzena a QR kód vám byl odeslán na e-mail!";
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

        var reservation = await _context.Reservations
            .Include(r => r.Court)
            .Include(r => r.TimeSlot)
            .Include(r => r.User)
            .FirstOrDefaultAsync(r => r.Id == reservationId);

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

        // Umožňuje zrušiť Confirmed aj PendingApproval
        reservation.State = ReservationState.Canceled;
        await _context.SaveChangesAsync();

        if (!string.IsNullOrWhiteSpace(reservation.User?.Email))
        {
            var subject = $"Zrušení rezervace – Kurt č. {reservation.Court.Number} - Padel Ostrava";
            var body = $@"
                <div style='font-family: Arial, sans-serif; line-height: 1.5;'>
                    <h2 style='color: #dc3545;'>Vaše rezervace byla zrušena</h2>
                    <p>Ahoj <strong>{reservation.User.Name}</strong>,</p>
                    <p>potvrzujeme zrušení rezervace kurtu č. {reservation.Court.Number} na termín {reservation.Date:dd. MM. yyyy} ({reservation.TimeSlot.StartTime:HH:mm} – {reservation.TimeSlot.EndTime:HH:mm}).</p>
                    <p>Termín byl uvolněn pro ostatní hráče.</p>
                </div>";

            await _emailSender.SendEmailAsync(reservation.User.Email, subject, body);
        }

        TempData["SuccessMessage"] = "Rezervace byla úspěšně zrušena.";
        return RedirectToAction(nameof(Index), new { date = date.ToString("yyyy-MM-dd") });
    }

    // ========================================================
    // OPERÁCIA OP-05: SCHVAĽOVACÍ PROCES (ADMINISTRÁCIA)
    // ========================================================

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
        var reservation = await _context.Reservations
            .Include(r => r.User)
            .Include(r => r.Court)
            .Include(r => r.TimeSlot)
            .FirstOrDefaultAsync(r => r.Id == reservationId);

        if (reservation == null || reservation.State != ReservationState.PendingApproval)
        {
            TempData["ErrorMessage"] = "Žádost ke schválení nebyla nalezena.";
            return RedirectToAction(nameof(PendingApprovals));
        }

        reservation.State = ReservationState.Confirmed;
        await _context.SaveChangesAsync();

        var qrPayload = $"PADEL-RESERVATION|ID:{reservation.Id}|COURT:{reservation.Court.Number}|DATE:{reservation.Date:yyyy-MM-dd}|TIME:{reservation.TimeSlot.StartTime:HH:mm}-{reservation.TimeSlot.EndTime:HH:mm}|USER:{reservation.User.Email}";
        using var qrGenerator = new QRCodeGenerator();
        using var qrCodeData = qrGenerator.CreateQrCode(qrPayload, QRCodeGenerator.ECCLevel.Q);
        using var qrCode = new PngByteQRCode(qrCodeData);
        byte[] qrCodeBytes = qrCode.GetGraphic(20);

        if (!string.IsNullOrWhiteSpace(reservation.User.Email))
        {
            var subject = $"Žádost SCHVÁLENA: Kurt č. {reservation.Court.Number} - Padel Ostrava";
            var body = $@"
                <div style='font-family: Arial, sans-serif; line-height: 1.5;'>
                    <h2 style='color: #198754;'>Vaše rezervace byla schválena!</h2>
                    <p>Ahoj <strong>{reservation.User.Name}</strong>,</p>
                    <p>správce areálu schválil vaši rezervaci prémiového Kurtu č. 4.</p>
                    <ul>
                        <li><strong>Kurt:</strong> č. {reservation.Court.Number}</li>
                        <li><strong>Datum:</strong> {reservation.Date:dd. MM. yyyy}</li>
                        <li><strong>Čas:</strong> {reservation.TimeSlot.StartTime:HH:mm} – {reservation.TimeSlot.EndTime:HH:mm}</li>
                    </ul>
                    <p>Vstupní QR kód najdete v příloze a v sekci Moje rezervace.</p>
                </div>";

            await _emailSender.SendEmailAsync(reservation.User.Email, subject, body, qrCodeBytes);
        }

        TempData["SuccessMessage"] = $"Rezervace č. {reservation.Id} byla úspěšně schválena.";
        return RedirectToAction(nameof(PendingApprovals));
    }

    // POST: /Reservation/Reject
    [HttpPost]
    [Authorize(Roles = "Admin")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(int reservationId)
    {
        var reservation = await _context.Reservations
            .Include(r => r.User)
            .Include(r => r.Court)
            .Include(r => r.TimeSlot)
            .FirstOrDefaultAsync(r => r.Id == reservationId);

        if (reservation == null || reservation.State != ReservationState.PendingApproval)
        {
            TempData["ErrorMessage"] = "Žádost k zamítnutí nebyla nalezena.";
            return RedirectToAction(nameof(PendingApprovals));
        }

        reservation.State = ReservationState.Rejected;
        await _context.SaveChangesAsync();

        if (!string.IsNullOrWhiteSpace(reservation.User.Email))
        {
            var subject = $"Žádost ZAMÍTNUTA: Kurt č. {reservation.Court.Number} - Padel Ostrava";
            var body = $@"
                <div style='font-family: Arial, sans-serif; line-height: 1.5;'>
                    <h2 style='color: #dc3545;'>Rezervace nemohla být schválena</h2>
                    <p>Ahoj <strong>{reservation.User.Name}</strong>,</p>
                    <p>správce areálu zamítl žádost o rezervaci Kurtu č. 4 na termín {reservation.Date:dd. MM. yyyy} ({reservation.TimeSlot.StartTime:HH:mm} – {reservation.TimeSlot.EndTime:HH:mm}).</p>
                    <p>Termín byl uvolněn a kapacita z vašeho limitu byla vrácena.</p>
                </div>";

            await _emailSender.SendEmailAsync(reservation.User.Email, subject, body);
        }

        TempData["SuccessMessage"] = $"Rezervace č. {reservation.Id} byla zamítnuta.";
        return RedirectToAction(nameof(PendingApprovals));
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
            if (res.State == ReservationState.Confirmed || res.State == ReservationState.PendingApproval)
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

        var qrPayload = $"PADEL-RESERVATION|ID:{reservation.Id}|COURT:{reservation.Court.Number}|DATE:{reservation.Date:yyyy-MM-dd}|TIME:{reservation.TimeSlot.StartTime:HH:mm}-{reservation.TimeSlot.EndTime:HH:mm}|USER:{reservation.User.Email}";

        using var qrGenerator = new QRCodeGenerator();
        using var qrCodeData = qrGenerator.CreateQrCode(qrPayload, QRCodeGenerator.ECCLevel.Q);
        using var qrCode = new PngByteQRCode(qrCodeData);
        byte[] qrCodeBytes = qrCode.GetGraphic(20);

        return File(qrCodeBytes, "image/png");
    }
}