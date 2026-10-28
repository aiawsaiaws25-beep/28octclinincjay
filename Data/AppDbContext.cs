using LakesideMedical.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace LakesideMedical.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<IdentityUser>(options)
{
    public DbSet<Appointment> Appointments => Set<Appointment>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Appointment>(appointment =>
        {
            appointment.Property(a => a.Status).HasDefaultValue("Booked");
            appointment.Property(a => a.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
            appointment.HasIndex(a => a.PatientEmail);
            appointment.Property(a => a.StripeSessionId).HasColumnName("stripeSessionId");
            appointment.HasIndex(a => a.StripeSessionId).IsUnique()
                .HasFilter("[stripeSessionId] IS NOT NULL");
            appointment.HasIndex(a => a.AppointmentDateTime);
            appointment.HasIndex(a => new { a.Doctor, a.AppointmentDateTime })
                .IsUnique()
                .HasFilter("[Status] IN (N'Booked', N'Pending')");
        });
    }
}
