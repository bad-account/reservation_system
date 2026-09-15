using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using reservation_system_for_padel.Models;
using reservation_system_for_padel.Services;

namespace reservation_system_for_padel.Controllers;

public class AccountController : Controller
{
    private readonly AppDbContext _context;

    public AccountController(AppDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        ViewBag.ReturnUrl = returnUrl;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(string email, string password, string? returnUrl = null)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);

        if (user == null || user.PasswordHash != password)
        {
            ViewBag.Error = "Neplatný e-mail nebo heslo.";
            ViewBag.ReturnUrl = returnUrl;
            return View();
        }

        await SignInUser(user);

        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);

        return RedirectToAction("Index", "Reservation");
    }

    [HttpGet]
    public IActionResult Register() => View();

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(string name, string surname, string email, string password)
    {
        if (await _context.Users.AnyAsync(u => u.Email == email))
        {
            ViewBag.Error = "Uživatel s tímto e-mailem již existuje.";
            return View();
        }

        var user = new User
        {
            Name = name,
            Surname = surname,
            Email = email,
            PasswordHash = password,
            Role = UserRole.User
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        await SignInUser(user);

        return RedirectToAction("Index", "Reservation");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Index", "Reservation");
    }

    public IActionResult AccessDenied()
    {
        return View();
    }

    private async Task SignInUser(User user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, $"{user.Name} {user.Surname}"),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, user.Role.ToString())
        };

        var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var authProperties = new AuthenticationProperties
        {
            IsPersistent = true // Udržuje přihlášení i po zavření prohlížeče
        };

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(claimsIdentity),
            authProperties);
    }
}