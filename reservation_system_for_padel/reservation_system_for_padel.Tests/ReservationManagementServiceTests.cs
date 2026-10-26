using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using reservation_system_for_padel.Models;
using reservation_system_for_padel.Services;
using Xunit;

namespace reservation_system_for_padel.Tests;

public class ReservationManagementServiceTests
{
    // Jednoduchý In-Memory Fake pro izolaci unit/component testů bez nutnosti SQLite a SMTP
    private class FakeReservationRepository : IReservationRepository
    {
        public List<Reservation> Reservations { get; } = new();
        public List<Court> Courts { get; } = new()
        {
            new Court { Id = 1, Number = 1, IsActive = true },
            new Court { Id = 4, Number = 4, IsActive = true }
        };
        public List<TimeSlot> TimeSlots { get; } = new()
        {
            new TimeSlot { Id = 1, StartTime = new TimeOnly(10, 0), EndTime = new TimeOnly(11, 0) }
        };
        public bool ThrowConflictOnSave { get; set; }

        public Task<Reservation?> GetByIdAsync(int id) =>
            Task.FromResult(Reservations.FirstOrDefault(r => r.Id == id));

        public Task<Reservation?> GetDraftAsync(int courtId, int timeSlotId, DateOnly date) =>
            Task.FromResult(Reservations.FirstOrDefault(r =>
                r.CourtId == courtId && r.TimeSlotId == timeSlotId && r.Date == date && r.State == ReservationState.Draft));

        public Task<int> GetActiveReservationsCountAsync(int userId, DateOnly fromDate) =>
            Task.FromResult(Reservations.Count(r =>
                r.UserId == userId && r.Date >= fromDate &&
                (r.State == ReservationState.Draft || r.State == ReservationState.Confirmed || r.State == ReservationState.PendingApproval)));

        public Task<bool> IsSlotBookedAsync(int courtId, DateOnly date, int timeSlotId) =>
            Task.FromResult(Reservations.Any(r =>
                r.CourtId == courtId && r.Date == date && r.TimeSlotId == timeSlotId &&
                (r.State == ReservationState.Confirmed || r.State == ReservationState.PendingApproval)));

        public Task<Court?> GetCourtByIdAsync(int id) =>
            Task.FromResult(Courts.FirstOrDefault(c => c.Id == id));

        public Task<TimeSlot?> GetTimeSlotByIdAsync(int id) =>
            Task.FromResult(TimeSlots.FirstOrDefault(t => t.Id == id));

        public Task<List<Reservation>> GetExpiredDraftsAsync(DateTime cutoffUtc) =>
            Task.FromResult(Reservations.Where(r => r.State == ReservationState.Draft && r.CreatedAt < cutoffUtc).ToList());

        public Task<List<Reservation>> GetPendingReservationsAsync() =>
            Task.FromResult(Reservations.Where(r => r.State == ReservationState.PendingApproval).ToList());

        public Task AddAsync(Reservation reservation)
        {
            reservation.Id = Reservations.Count + 1;
            Reservations.Add(reservation);
            return Task.CompletedTask;
        }

        public void RemoveRange(IEnumerable<Reservation> reservations)
        {
            foreach (var r in reservations.ToList()) Reservations.Remove(r);
        }

        public void Remove(Reservation reservation) => Reservations.Remove(reservation);

        public Task SaveChangesAsync()
        {
            if (ThrowConflictOnSave)
                throw new DbUpdateException("Simulated unique constraint violation");
            return Task.CompletedTask;
        }
    }

    private class SpyEmailSender : IEmailSender
    {
        public int SentCount { get; private set; }
        public byte[]? LastQrCodeBytes { get; private set; }

        public Task SendEmailAsync(string toEmail, string subject, string htmlBody, byte[]? qrCodeBytes = null)
        {
            SentCount++;
            LastQrCodeBytes = qrCodeBytes;
            return Task.CompletedTask;
        }
    }

    private class FakeQrCodeService : IQRCodeService
    {
        public int GeneratedCount { get; private set; }
        public byte[] GenerateReservationQrCode(int reservationId, int courtNumber, DateOnly date, TimeOnly startTime, TimeOnly endTime, string userEmail)
        {
            GeneratedCount++;
            return new byte[] { 1, 2, 3, 4 };
        }
    }

    private static (ReservationManagementService Service, FakeReservationRepository Repo, SpyEmailSender Email, FakeQrCodeService Qr) CreateSut()
    {
        var repo = new FakeReservationRepository();
        var email = new SpyEmailSender();
        var qr = new FakeQrCodeService();
        var service = new ReservationManagementService(repo, email, qr, NullLogger<ReservationManagementService>.Instance);
        return (service, repo, email, qr);
    }

    // TEST 1: Algorithm branch & State transition (Court 4 -> PendingApproval without QR)
    [Fact]
    public async Task ConfirmDraftAsync_Court4_TransitionsToPendingApprovalWithoutQrCode()
    {
        var (service, repo, email, qr) = CreateSut();
        var futureDate = DateOnly.FromDateTime(DateTime.Today.AddDays(3));
        var draft = new Reservation
        {
            Id = 10,
            UserId = 1,
            CourtId = 4,
            Court = repo.Courts.First(c => c.Number == 4),
            TimeSlotId = 1,
            TimeSlot = repo.TimeSlots.First(),
            Date = futureDate,
            State = ReservationState.Draft,
            CreatedAt = DateTime.UtcNow,
            User = new User { Id = 1, Name = "Jan", Email = "jan@padel.cz" }
        };
        repo.Reservations.Add(draft);

        var result = await service.ConfirmDraftAsync(10, userId: 1);

        Assert.True(result.Success);
        Assert.Equal(ReservationState.PendingApproval, draft.State);
        Assert.Equal(0, qr.GeneratedCount);
        Assert.Equal(1, email.SentCount);
        Assert.Null(email.LastQrCodeBytes);
    }

    // TEST 2: Algorithm branch & Collaboration (Standard Court 1 -> Confirmed + QR generated + Email with QR)
    [Fact]
    public async Task ConfirmDraftAsync_StandardCourt1_TransitionsToConfirmedAndGeneratesQrCode()
    {
        var (service, repo, email, qr) = CreateSut();
        var futureDate = DateOnly.FromDateTime(DateTime.Today.AddDays(2));
        var draft = new Reservation
        {
            Id = 11,
            UserId = 1,
            CourtId = 1,
            Court = repo.Courts.First(c => c.Number == 1),
            TimeSlotId = 1,
            TimeSlot = repo.TimeSlots.First(),
            Date = futureDate,
            State = ReservationState.Draft,
            CreatedAt = DateTime.UtcNow,
            User = new User { Id = 1, Name = "Jan", Email = "jan@padel.cz" }
        };
        repo.Reservations.Add(draft);

        var result = await service.ConfirmDraftAsync(11, userId: 1);

        Assert.True(result.Success);
        Assert.Equal(ReservationState.Confirmed, draft.State);
        Assert.Equal(1, qr.GeneratedCount);
        Assert.Equal(1, email.SentCount);
        Assert.NotNull(email.LastQrCodeBytes);
    }

    // TEST 3: Interface contract & BR-04 Quota Invariant (Reject 3rd active reservation)
    [Fact]
    public async Task CreateDraftAsync_WhenUserAlreadyHasTwoActiveReservations_ReturnsQuotaFailure()
    {
        var (service, repo, _, _) = CreateSut();
        var futureDate = DateOnly.FromDateTime(DateTime.Today.AddDays(3));
        var slot = repo.TimeSlots.First();

        repo.Reservations.Add(new Reservation { Id = 1, UserId = 5, Date = futureDate, State = ReservationState.Confirmed, TimeSlot = slot });
        repo.Reservations.Add(new Reservation { Id = 2, UserId = 5, Date = futureDate.AddDays(1), State = ReservationState.PendingApproval, TimeSlot = slot });

        var result = await service.CreateDraftAsync(courtId: 1, timeSlotId: 1, date: futureDate.AddDays(2), userId: 5);

        Assert.False(result.Success);
        Assert.Null(result.ReservationId);
        Assert.Contains("limit 2 aktivních rezervací", result.Message);
    }

    // TEST 4: Temporal rule DR-01 (Court 4 requested less than 24h in advance is rejected)
    [Fact]
    public async Task CreateDraftAsync_Court4LessThan24HoursBeforeStart_ReturnsAdvanceBookingFailure()
    {
        var (service, _, _, _) = CreateSut();
        var today = DateOnly.FromDateTime(DateTime.Today);

        var result = await service.CreateDraftAsync(courtId: 4, timeSlotId: 1, date: today, userId: 2);

        Assert.False(result.Success);
        Assert.Contains("24 hodin předem", result.Message);
    }

    // TEST 5: Failure behavior & Concurrency contract (DbUpdateException translated to clean failure tuple)
    [Fact]
    public async Task ConfirmDraftAsync_WhenConcurrentDbUpdateExceptionOccurs_ReturnsGracefulFailure()
    {
        var (service, repo, email, _) = CreateSut();
        var draft = new Reservation
        {
            Id = 20,
            UserId = 1,
            CourtId = 1,
            Court = repo.Courts.First(c => c.Number == 1),
            TimeSlotId = 1,
            TimeSlot = repo.TimeSlots.First(),
            Date = DateOnly.FromDateTime(DateTime.Today.AddDays(2)),
            State = ReservationState.Draft,
            CreatedAt = DateTime.UtcNow,
            User = new User { Id = 1, Name = "Jan", Email = "jan@padel.cz" }
        };
        repo.Reservations.Add(draft);
        repo.ThrowConflictOnSave = true;

        var result = await service.ConfirmDraftAsync(20, userId: 1);

        Assert.False(result.Success);
        Assert.Equal(0, email.SentCount);
    }

    // TEST 6: State transition OP-05 (ApprovePendingReservationAsync transitions PendingApproval -> Confirmed + QR)
    [Fact]
    public async Task ApprovePendingReservationAsync_ValidPendingRequest_TransitionsToConfirmedAndSendsQr()
    {
        var (service, repo, email, qr) = CreateSut();
        var pending = new Reservation
        {
            Id = 30,
            UserId = 1,
            CourtId = 4,
            Court = repo.Courts.First(c => c.Number == 4),
            TimeSlotId = 1,
            TimeSlot = repo.TimeSlots.First(),
            Date = DateOnly.FromDateTime(DateTime.Today.AddDays(4)),
            State = ReservationState.PendingApproval,
            CreatedAt = DateTime.UtcNow,
            User = new User { Id = 1, Name = "Jan", Email = "jan@padel.cz" }
        };
        repo.Reservations.Add(pending);

        var result = await service.ApprovePendingReservationAsync(30);

        Assert.True(result.Success);
        Assert.Equal(ReservationState.Confirmed, pending.State);
        Assert.Equal(1, qr.GeneratedCount);
        Assert.Equal(1, email.SentCount);
        Assert.NotNull(email.LastQrCodeBytes);
    }

    // Temporary verification test for finding F-02
    [Fact]
    public async Task CancelReservationAsync_DraftReservation_IsRejected()
    {
        // Arrange
        var (service, repo, email, _) = CreateSut();

        var draft = new Reservation
        {
            Id = 123,
            UserId = 1,
            CourtId = 1,
            TimeSlotId = 1,
            Date = DateOnly.FromDateTime(DateTime.Today.AddDays(1)),
            State = ReservationState.Draft,
            User = new User
            {
                Id = 1,
                Name = "Test User",
                Email = "test@example.com"
            },
            Court = repo.Courts.First(c => c.Id == 1),
            TimeSlot = repo.TimeSlots.First(s => s.Id == 1)
        };

        repo.Reservations.Add(draft);

        // Act
        var result = await service.CancelReservationAsync(
            reservationId: 123,
            userId: 1,
            isAdmin: false);

        // Assert: DRAFT se nesmí zrušit přes OP-04
        Assert.False(result.Success);
        Assert.Equal(ReservationState.Draft, draft.State);
        Assert.Equal(0, email.SentCount);
    }
}