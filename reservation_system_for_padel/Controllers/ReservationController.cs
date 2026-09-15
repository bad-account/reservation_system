using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using reservation_system_for_padel.Models;
using reservation_system_for_padel.Services;

namespace reservation_system_for_padel.Controllers
{
    public class ReservationController : Controller
    {
        private readonly AppDbContext _context;

        public ReservationController(AppDbContext context)
        {
            _context = context;
        }

        // GET: Reservation
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

            // Prototyp: pro testování vybereme prvního uživatele v DB (nebo vytvoříme dummy)
            var currentUser = await _context.Users.FirstOrDefaultAsync();
            if (currentUser == null)
            {
                currentUser = new User { Name = "Jan", Surname = "Novák", Email = "jan@novak.cz", Role = UserRole.User };
                _context.Users.Add(currentUser);
                await _context.SaveChangesAsync();
            }

            ViewBag.SelectedDate = targetDate;
            ViewBag.Courts = courts;
            ViewBag.TimeSlots = slots;
            ViewBag.Reservations = reservations;
            ViewBag.CurrentUserId = currentUser.Id;

            return View();
        }

        // GET: Reservation/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null || _context.Reservations == null)
            {
                return NotFound();
            }

            var reservation = await _context.Reservations
                .Include(r => r.Court)
                .Include(r => r.TimeSlot)
                .Include(r => r.User)
                .FirstOrDefaultAsync(m => m.Id == id);
            if (reservation == null)
            {
                return NotFound();
            }

            return View(reservation);
        }

        // GET: Reservation/Create
        public IActionResult Create()
        {
            ViewData["CourtId"] = new SelectList(_context.Courts, "Id", "Id");
            ViewData["TimeSlotId"] = new SelectList(_context.TimeSlots, "Id", "Id");
            ViewData["UserId"] = new SelectList(_context.Users, "Id", "Id");
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Create(int courtId, int timeSlotId, DateOnly date, int userId)
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            if (date < today)
            {
                ModelState.AddModelError("", "Nelze rezervovat termíny v minulosti.");
                return RedirectToAction(nameof(Index), new { date });
            }

            // Pravidlo: Max 2 aktivní rezervace dopředu
            var activeReservationsCount = await _context.Reservations
                .CountAsync(r => r.UserId == userId
                              && r.Date >= today
                              && (r.State == ReservationState.Draft || r.State == ReservationState.Confirmed));

            if (activeReservationsCount >= 2)
            {
                TempData["Error"] = "Máte již vyčerpaný limit 2 aktivních rezervací.";
                return RedirectToAction(nameof(Index), new { date });
            }

            // Pravidlo: Žádné překryvy
            bool isAlreadyBooked = await _context.Reservations
                .AnyAsync(r => r.CourtId == courtId
                            && r.Date == date
                            && r.TimeSlotId == timeSlotId
                            && r.State != ReservationState.Canceled
                            && r.State != ReservationState.Rejected);

            if (isAlreadyBooked)
            {
                TempData["Error"] = "Tento termín je již obsazen.";
                return RedirectToAction(nameof(Index), new { date });
            }

            var reservation = new Reservation
            {
                CourtId = courtId,
                TimeSlotId = timeSlotId,
                Date = date,
                UserId = userId,
                State = ReservationState.Confirmed // nebo Draft podle potřeby schvalování
            };

            _context.Reservations.Add(reservation);
            await _context.SaveChangesAsync();

            TempData["Success"] = "Rezervace byla úspěšně vytvořena.";
            return RedirectToAction(nameof(Index), new { date });
        }

        // GET: Reservation/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null || _context.Reservations == null)
            {
                return NotFound();
            }

            var reservation = await _context.Reservations.FindAsync(id);
            if (reservation == null)
            {
                return NotFound();
            }
            ViewData["CourtId"] = new SelectList(_context.Courts, "Id", "Id", reservation.CourtId);
            ViewData["TimeSlotId"] = new SelectList(_context.TimeSlots, "Id", "Id", reservation.TimeSlotId);
            ViewData["UserId"] = new SelectList(_context.Users, "Id", "Id", reservation.UserId);
            return View(reservation);
        }

        // POST: Reservation/Edit/5
        // To protect from overposting attacks, enable the specific properties you want to bind to.
        // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("Id,Date,CourtId,TimeSlotId,UserId,State,CreatedAt")] Reservation reservation)
        {
            if (id != reservation.Id)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(reservation);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!ReservationExists(reservation.Id))
                    {
                        return NotFound();
                    }
                    else
                    {
                        throw;
                    }
                }
                return RedirectToAction(nameof(Index));
            }
            ViewData["CourtId"] = new SelectList(_context.Courts, "Id", "Id", reservation.CourtId);
            ViewData["TimeSlotId"] = new SelectList(_context.TimeSlots, "Id", "Id", reservation.TimeSlotId);
            ViewData["UserId"] = new SelectList(_context.Users, "Id", "Id", reservation.UserId);
            return View(reservation);
        }

        // GET: Reservation/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null || _context.Reservations == null)
            {
                return NotFound();
            }

            var reservation = await _context.Reservations
                .Include(r => r.Court)
                .Include(r => r.TimeSlot)
                .Include(r => r.User)
                .FirstOrDefaultAsync(m => m.Id == id);
            if (reservation == null)
            {
                return NotFound();
            }

            return View(reservation);
        }

        // POST: Reservation/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            if (_context.Reservations == null)
            {
                return Problem("Entity set 'AppDbContext.Reservations'  is null.");
            }
            var reservation = await _context.Reservations.FindAsync(id);
            if (reservation != null)
            {
                _context.Reservations.Remove(reservation);
            }
            
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        private bool ReservationExists(int id)
        {
          return (_context.Reservations?.Any(e => e.Id == id)).GetValueOrDefault();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BookSlot(int courtId, int timeSlotId, DateOnly date, int userId)
        {
            var today = DateOnly.FromDateTime(DateTime.Today);

            // Pravidlo 1: Max 2 aktivní rezervace dopředu
            var activeCount = await _context.Reservations
                .CountAsync(r => r.UserId == userId
                              && r.Date >= today
                              && (r.State == ReservationState.Draft || r.State == ReservationState.Confirmed));

            if (activeCount >= 2)
            {
                TempData["ErrorMessage"] = "Máte již vyčerpaný limit 2 aktivních rezervací.";
                return RedirectToAction(nameof(Index), new { date = date.ToString("yyyy-MM-dd") });
            }

            // Pravidlo 2: Prevence překryvu
            bool alreadyBooked = await _context.Reservations
                .AnyAsync(r => r.CourtId == courtId
                            && r.Date == date
                            && r.TimeSlotId == timeSlotId
                            && r.State != ReservationState.Canceled
                            && r.State != ReservationState.Rejected);

            if (alreadyBooked)
            {
                TempData["ErrorMessage"] = "Tento termín byl právě zarezervován někým jiným.";
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

            TempData["SuccessMessage"] = "Rezervace kurtu proběhla úspěšně!";
            return RedirectToAction(nameof(Index), new { date = date.ToString("yyyy-MM-dd") });
        }
    }
}
