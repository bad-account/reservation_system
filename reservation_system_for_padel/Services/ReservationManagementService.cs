using Microsoft.EntityFrameworkCore;
using reservation_system_for_padel.Models;

namespace reservation_system_for_padel.Services;

public class ReservationManagementService : IReservationManagementService
{
    private readonly IReservationRepository _repo;
    private readonly IEmailSender _emailSender;
    private readonly IQRCodeService _qrCodeService;
    private readonly ILogger<ReservationManagementService> _logger;

    public ReservationManagementService(
        IReservationRepository repo,
        IEmailSender emailSender,
        IQRCodeService qrCodeService,
        ILogger<ReservationManagementService> logger)
    {
        _repo = repo;
        _emailSender = emailSender;
        _qrCodeService = qrCodeService;
        _logger = logger;
    }

    public async Task<(bool Success, string Message, int? ReservationId, int RemainingSeconds)> CreateDraftAsync(int courtId, int timeSlotId, DateOnly date, int userId)
    {
        await CleanupExpiredReservationsAsync();
        var today = DateOnly.FromDateTime(DateTime.Today);

        if (date < today)
        {
            return (false, "Nelze rezervovat termíny v minulosti.", null, 0);
        }

        var existingDraft = await _repo.GetDraftAsync(courtId, timeSlotId, date);

        if (existingDraft != null)
        {
            if (existingDraft.UserId == userId)
            {
                var remainingSec = (int)(existingDraft.CreatedAt.AddMinutes(5) - DateTime.UtcNow).TotalSeconds;
                return (true, string.Empty, existingDraft.Id, Math.Max(0, remainingSec));
            }
            return (false, "Tento termín právě rezervuje jiný uživatel.", null, 0);
        }

        // Pravidlo BR-04: Maximálně 2 aktivní rezervace
        var activeCount = await _repo.GetActiveReservationsCountAsync(userId, today);
        if (activeCount >= 2)
        {
            return (false, "Máte již vyčerpaný limit 2 aktivních rezervací.", null, 0);
        }

        // Invariant BR-02: Termín nesmí být obsazen
        bool alreadyBooked = await _repo.IsSlotBookedAsync(courtId, date, timeSlotId);
        if (alreadyBooked)
        {
            return (false, "Tento termín kurtu je již obsazen nebo čeká na schválení.", null, 0);
        }

        var court = await _repo.GetCourtByIdAsync(courtId);
        var timeSlot = await _repo.GetTimeSlotByIdAsync(timeSlotId);

        if (court != null && timeSlot != null && court.Number == 4)
        {
            var slotDateTime = date.ToDateTime(timeSlot.StartTime);
            if (slotDateTime < DateTime.Now.AddHours(24))
            {
                return (false, "Rezervaci turnajového Kurtu č. 4 je nutné podat nejpozději 24 hodin předem.", null, 0);
            }
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

        try
        {
            await _repo.AddAsync(draft);
            await _repo.SaveChangesAsync();
            return (true, string.Empty, draft.Id, 300);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Souběžný pokus o rezervaci stejného slotu.");
            return (false, "Tento termín byl právě obsazen jiným uživatelem.", null, 0);
        }
    }

    public async Task<(bool Success, string Message)> ConfirmDraftAsync(int reservationId, int userId)
    {
        await CleanupExpiredReservationsAsync();

        var reservation = await _repo.GetByIdAsync(reservationId);

        if (reservation == null || reservation.UserId != userId || reservation.State != ReservationState.Draft)
        {
            return (false, "Draft rezervace vypršel nebo nebyl nalezen.");
        }

        // Změna C02: Kurt č. 4 vyžaduje schválení správcem
        if (reservation.Court.Number == 4)
        {
            reservation.State = ReservationState.PendingApproval;
            try
            {
                await _repo.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                return (false, "Tento termín byl v mezičase obsazen.");
            }

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
                    </div>";

                await _emailSender.SendEmailAsync(reservation.User.Email, subject, body);
            }

            return (true, "Žádost o rezervaci Kurtu č. 4 byla odeslána správci ke schválení.");
        }

        // Běžné kurty 1, 2, 3
        reservation.State = ReservationState.Confirmed;
        try
        {
            await _repo.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return (false, "Tento termín byl v mezičase obsazen.");
        }

        byte[] qrCodeBytes = _qrCodeService.GenerateReservationQrCode(
            reservation.Id, reservation.Court.Number, reservation.Date,
            reservation.TimeSlot.StartTime, reservation.TimeSlot.EndTime, reservation.User.Email);

        if (!string.IsNullOrWhiteSpace(reservation.User.Email))
        {
            var subject = $"Potvrzení rezervace – Kurt č. {reservation.Court.Number} - Padel Ostrava";
            var body = $@"
                <div style='font-family: Arial, sans-serif; line-height: 1.5;'>
                    <h2 style='color: #198754;'>Rezervace kurtu je potvrzena!</h2>
                    <p>Ahoj <strong>{reservation.User.Name}</strong>,</p>
                    <ul>
                        <li><strong>Kurt:</strong> č. {reservation.Court.Number}</li>
                        <li><strong>Datum:</strong> {reservation.Date:dd. MM. yyyy}</li>
                        <li><strong>Čas:</strong> {reservation.TimeSlot.StartTime:HH:mm} – {reservation.TimeSlot.EndTime:HH:mm}</li>
                    </ul>
                </div>";

            await _emailSender.SendEmailAsync(reservation.User.Email, subject, body, qrCodeBytes);
        }

        return (true, "Rezervace byla úspěšně potvrzena a QR kód vám byl odeslán na e-mail!");
    }

    public async Task CancelDraftAsync(int reservationId)
    {
        var reservation = await _repo.GetByIdAsync(reservationId);
        if (reservation != null && reservation.State == ReservationState.Draft)
        {
            _repo.Remove(reservation);
            await _repo.SaveChangesAsync();
        }
    }

    public async Task<(bool Success, string Message)> CancelReservationAsync(int reservationId, int userId, bool isAdmin)
    {
        var reservation = await _repo.GetByIdAsync(reservationId);

        if (reservation == null)
        {
            return (false, "Rezervace nebyla nalezena.");
        }

        if (reservation.UserId != userId && !isAdmin)
        {
            return (false, "Nemáte oprávnění zrušit cizí rezervaci.");
        }

        reservation.State = ReservationState.Canceled;
        await _repo.SaveChangesAsync();

        if (!string.IsNullOrWhiteSpace(reservation.User?.Email))
        {
            var subject = $"Zrušení rezervace – Kurt č. {reservation.Court.Number} - Padel Ostrava";
            var body = $@"
                <div style='font-family: Arial, sans-serif; line-height: 1.5;'>
                    <h2 style='color: #dc3545;'>Vaše rezervace byla zrušena</h2>
                    <p>Ahoj <strong>{reservation.User.Name}</strong>,</p>
                    <p>potvrzujeme zrušení rezervace kurtu č. {reservation.Court.Number} na termín {reservation.Date:dd. MM. yyyy}.</p>
                </div>";

            await _emailSender.SendEmailAsync(reservation.User.Email, subject, body);
        }

        return (true, "Rezervace byla úspěšně zrušena.");
    }

    public async Task<(bool Success, string Message)> ApprovePendingReservationAsync(int reservationId)
    {
        var reservation = await _repo.GetByIdAsync(reservationId);

        if (reservation == null || reservation.State != ReservationState.PendingApproval)
        {
            return (false, "Žádost ke schválení nebyla nalezena.");
        }

        reservation.State = ReservationState.Confirmed;
        await _repo.SaveChangesAsync();

        byte[] qrCodeBytes = _qrCodeService.GenerateReservationQrCode(
            reservation.Id, reservation.Court.Number, reservation.Date,
            reservation.TimeSlot.StartTime, reservation.TimeSlot.EndTime, reservation.User.Email);

        if (!string.IsNullOrWhiteSpace(reservation.User.Email))
        {
            var subject = $"Žádost SCHVÁLENA: Kurt č. {reservation.Court.Number} - Padel Ostrava";
            var body = $@"
                <div style='font-family: Arial, sans-serif; line-height: 1.5;'>
                    <h2 style='color: #198754;'>Vaše rezervace byla schválena!</h2>
                    <p>Ahoj <strong>{reservation.User.Name}</strong>,</p>
                    <p>správce areálu schválil vaši rezervaci prémiového Kurtu č. 4.</p>
                </div>";

            await _emailSender.SendEmailAsync(reservation.User.Email, subject, body, qrCodeBytes);
        }

        return (true, $"Rezervace č. {reservation.Id} byla úspěšně schválena.");
    }

    public async Task<(bool Success, string Message)> RejectPendingReservationAsync(int reservationId)
    {
        var reservation = await _repo.GetByIdAsync(reservationId);

        if (reservation == null || reservation.State != ReservationState.PendingApproval)
        {
            return (false, "Žádost k zamítnutí nebyla nalezena.");
        }

        reservation.State = ReservationState.Rejected;
        await _repo.SaveChangesAsync();

        if (!string.IsNullOrWhiteSpace(reservation.User.Email))
        {
            var subject = $"Žádost ZAMÍTNUTA: Kurt č. {reservation.Court.Number} - Padel Ostrava";
            var body = $@"
                <div style='font-family: Arial, sans-serif; line-height: 1.5;'>
                    <h2 style='color: #dc3545;'>Rezervace nemohla být schválena</h2>
                    <p>Ahoj <strong>{reservation.User.Name}</strong>,</p>
                    <p>správce areálu zamítl žádost o rezervaci Kurtu č. 4.</p>
                </div>";

            await _emailSender.SendEmailAsync(reservation.User.Email, subject, body);
        }

        return (true, $"Rezervace č. {reservation.Id} byla zamítnuta.");
    }

    public async Task CleanupExpiredReservationsAsync()
    {
        // 1. Expirace 5minutových draftů
        var draftCutoff = DateTime.UtcNow.AddMinutes(-5);
        var expiredDrafts = await _repo.GetExpiredDraftsAsync(draftCutoff);

        if (expiredDrafts.Any())
        {
            _repo.RemoveRange(expiredDrafts);
            await _repo.SaveChangesAsync();
        }

        // 2. Expirace nevyřízených PendingApproval rezervací
        var now = DateTime.Now;
        var thresholdDate = DateOnly.FromDateTime(now.AddHours(24));
        var thresholdTime = TimeOnly.FromDateTime(now.AddHours(24));

        var pendingToEvaluate = await _repo.GetPendingReservationsAsync();

        var expiredPending = pendingToEvaluate
            .Where(r => r.TimeSlot != null && r.Date.ToDateTime(r.TimeSlot.StartTime) <= now.AddHours(24))
            .ToList();

        if (expiredPending.Any())
        {
            foreach (var item in expiredPending)
            {
                item.State = ReservationState.Expired;
            }
            await _repo.SaveChangesAsync();
        }
    }
}