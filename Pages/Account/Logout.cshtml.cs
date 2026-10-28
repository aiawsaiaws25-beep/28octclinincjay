using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LakesideMedical.Pages.Account;

[Authorize]
public class LogoutModel(SignInManager<IdentityUser> signInManager) : PageModel
{
    public IActionResult OnGet() => RedirectToPage("/Index");

    public async Task<IActionResult> OnPostAsync()
    {
        await signInManager.SignOutAsync();
        return RedirectToPage("/Index");
    }
}
