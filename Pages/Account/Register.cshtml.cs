using System.ComponentModel.DataAnnotations;
using LakesideMedical.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LakesideMedical.Pages.Account;

[AllowAnonymous]
public class RegisterModel(
    UserManager<IdentityUser> userManager,
    SignInManager<IdentityUser> signInManager,
    RoleManager<IdentityRole> roleManager) : PageModel
{
    [BindProperty]
    public RegisterInput Input { get; set; } = new();

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

        var email = Input.Email.Trim().ToLowerInvariant();
        var user = new IdentityUser { UserName = email, Email = email };
        var result = await userManager.CreateAsync(user, Input.Password);
        if (result.Succeeded)
        {
            if (await roleManager.RoleExistsAsync(ReceptionSeeder.PatientRole))
            {
                var roleResult = await userManager.AddToRoleAsync(user, ReceptionSeeder.PatientRole);
                if (!roleResult.Succeeded)
                {
                    await userManager.DeleteAsync(user);
                    foreach (var error in roleResult.Errors)
                    {
                        ModelState.AddModelError("", error.Description);
                    }
                    return Page();
                }
            }
            await signInManager.SignInAsync(user, isPersistent: false);
            return LocalRedirect(SafeReturnUrl());
        }

        foreach (var error in result.Errors)
        {
            ModelState.AddModelError("", error.Description);
        }
        return Page();
    }

    private string SafeReturnUrl() => Url.IsLocalUrl(ReturnUrl) ? ReturnUrl! : Url.Page("/MyAppointments")!;

    public class RegisterInput
    {
        [Required, EmailAddress, StringLength(254), Display(Name = "Email address")]
        public string Email { get; set; } = "";

        [Required, StringLength(100, MinimumLength = 8), DataType(DataType.Password)]
        public string Password { get; set; } = "";

        [Required, Compare(nameof(Password), ErrorMessage = "The passwords do not match.")]
        [DataType(DataType.Password), Display(Name = "Confirm password")]
        public string ConfirmPassword { get; set; } = "";
    }
}
