using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LakesideMedical.Pages.Account;

[AllowAnonymous]
public class LoginModel(SignInManager<IdentityUser> signInManager) : PageModel
{
    [BindProperty]
    public LoginInput Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public IActionResult OnGet() => User.Identity?.IsAuthenticated == true
        ? LocalRedirect(SafeReturnUrl())
        : Page();

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = await signInManager.PasswordSignInAsync(
            Input.Email.Trim().ToLowerInvariant(), Input.Password, Input.RememberMe, lockoutOnFailure: true);
        if (result.Succeeded)
        {
            return LocalRedirect(SafeReturnUrl());
        }

        ModelState.AddModelError("", result.IsLockedOut
            ? "Too many unsuccessful attempts. Please try again in five minutes."
            : "The email address or password is incorrect.");
        return Page();
    }

    private string SafeReturnUrl() => Url.IsLocalUrl(ReturnUrl) ? ReturnUrl! : Url.Page("/MyAppointments")!;

    public class LoginInput
    {
        [Required, EmailAddress, StringLength(254), Display(Name = "Email address")]
        public string Email { get; set; } = "";

        [Required, DataType(DataType.Password)]
        public string Password { get; set; } = "";

        [Display(Name = "Remember me")]
        public bool RememberMe { get; set; }
    }
}
