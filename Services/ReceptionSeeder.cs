using LakesideMedical.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace LakesideMedical.Services;

public static class ReceptionSeeder
{
    public const string ReceptionEmail = "reception@lakeside.local";
    public const string PatientRole = "patient";
    public const string ReceptionRole = "reception";

    public static async Task SeedAsync(
        IServiceProvider services,
        string? receptionPassword,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = services.GetRequiredService<UserManager<IdentityUser>>();
        var context = services.GetRequiredService<AppDbContext>();

        foreach (var role in new[] { PatientRole, ReceptionRole })
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                var result = await roleManager.CreateAsync(new IdentityRole(role));
                EnsureSucceeded(result, $"creating the {role} role");
            }
        }

        var unassignedUsers = await context.Users
            .Where(user => !context.UserRoles.Any(userRole => userRole.UserId == user.Id))
            .ToListAsync(cancellationToken);
        foreach (var user in unassignedUsers)
        {
            var result = await userManager.AddToRoleAsync(user, PatientRole);
            EnsureSucceeded(result, $"assigning the patient role to {user.Email}");
        }

        var receptionUser = await userManager.FindByEmailAsync(ReceptionEmail);
        if (receptionUser is null)
        {
            if (string.IsNullOrWhiteSpace(receptionPassword))
            {
                logger.LogWarning(
                    "RECEPTION_PASSWORD is not configured; no reception account was created. Set it in the environment and restart the app.");
                return;
            }

            receptionUser = new IdentityUser
            {
                UserName = ReceptionEmail,
                Email = ReceptionEmail,
                EmailConfirmed = true
            };
            var createResult = await userManager.CreateAsync(receptionUser, receptionPassword);
            EnsureSucceeded(createResult, "creating the reception account");
        }

        if (!await userManager.IsInRoleAsync(receptionUser, ReceptionRole))
        {
            var roleResult = await userManager.AddToRoleAsync(receptionUser, ReceptionRole);
            EnsureSucceeded(roleResult, "assigning the reception role");
        }
    }

    private static void EnsureSucceeded(IdentityResult result, string operation)
    {
        if (!result.Succeeded)
        {
            var errors = string.Join("; ", result.Errors.Select(error => error.Description));
            throw new InvalidOperationException($"Failed {operation}: {errors}");
        }
    }
}
