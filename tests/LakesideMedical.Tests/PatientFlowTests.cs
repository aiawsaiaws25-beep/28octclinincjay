using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.RegularExpressions;
using LakesideMedical.Data;
using LakesideMedical.Models;
using LakesideMedical.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace LakesideMedical.Tests;

public class ClinicFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly string databaseName = $"LakesideMedicalTests_{Guid.NewGuid():N}";
    private bool databaseCreated;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Reception:SeedOnStartup"] = "false"
            }));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
            services.AddDbContext<AppDbContext>(options => options.UseSqlServer(
                $"Server=(localdb)\\MSSQLLocalDB;Database={databaseName};Trusted_Connection=True;TrustServerCertificate=True"));
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(new FixedClock());
            services.RemoveAll<IStripeCheckoutGateway>();
            services.AddSingleton<IStripeCheckoutGateway, FakeStripeCheckoutGateway>();
        });
    }

    public async Task InitializeAsync()
    {
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
        await ReceptionSeeder.SeedAsync(
            scope.ServiceProvider,
            "Test-Reception-123!",
            NullLogger.Instance);
        databaseCreated = true;
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        if (databaseCreated)
        {
            using var scope = Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureDeletedAsync();
        }
        await DisposeAsync();
    }
}

public class PatientFlowTests(ClinicFactory factory) : IClassFixture<ClinicFactory>
{
    private HttpClient CreateClient() => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        BaseAddress = new Uri("https://localhost")
    });

    [Fact]
    public async Task FullPatientJourneyPersistsScopesAndCancelsAppointments()
    {
        using var alice = CreateClient();
        using var bob = CreateClient();
        var aliceEmail = $"alice-{Guid.NewGuid():N}@example.test";
        var bobEmail = $"bob-{Guid.NewGuid():N}@example.test";
        await RegisterAsync(alice, aliceEmail);
        await RegisterAsync(bob, bobEmail);

        var html = await alice.GetStringAsync("/Booking");
        var input = ValidInput();
        input["Input.PatientEmail"] = bobEmail;
        input["Step"] = "1";
        html = await PostStepAsync(alice, "Next", html, input, 2);
        input["Step"] = "2";
        html = await PostStepAsync(alice, "Next", html, input, 3);
        Assert.Equal(24, Regex.Matches(html, "class=\"slot-choice").Count);
        input["Step"] = "3";
        html = await PostStepAsync(alice, "Next", html, input, 4);
        Assert.Contains(aliceEmail, html);
        input["Step"] = "4";
        html = await PostStepAsync(alice, "Next", html, input, 5);
        Assert.Contains("Review your appointment", html);

        using (var scope = factory.Services.CreateScope())
        {
            Assert.Empty(await scope.ServiceProvider.GetRequiredService<AppointmentService>().GetBookingsByEmailAsync(aliceEmail));
        }

        var confirmed = await PostFormAsync(alice, "/Booking?handler=Confirm", html, input);
        Assert.Equal(HttpStatusCode.Redirect, confirmed.StatusCode);
        Assert.StartsWith("https://checkout.stripe.com/", confirmed.Headers.Location!.OriginalString);
        Appointment pending;
        using (var scope = factory.Services.CreateScope())
        {
            pending = Assert.Single(await scope.ServiceProvider.GetRequiredService<AppointmentService>().GetBookingsByEmailAsync(aliceEmail));
            Assert.Equal("Pending", pending.Status);
            Assert.NotNull(pending.StripeSessionId);
        }
        var successUrl = $"/booking-success?session_id={pending.StripeSessionId}";
        var unpaid = await alice.GetStringAsync(successUrl);
        Assert.Contains("Payment not completed", unpaid);
        Assert.Contains("Pending", unpaid);
        var stripe = Assert.IsType<FakeStripeCheckoutGateway>(factory.Services.GetRequiredService<IStripeCheckoutGateway>());
        stripe.Pay(pending.StripeSessionId!);
        var confirmation = await alice.GetStringAsync(successUrl);
        Assert.Contains("Booking confirmed", confirmation);
        Assert.Contains(aliceEmail, confirmation);
        Assert.DoesNotContain(bobEmail, confirmation);
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.GetAsync("/Booking/Confirmation?token=tampered")).StatusCode);

        Appointment appointment;
        using (var scope = factory.Services.CreateScope())
        {
            appointment = Assert.Single(await scope.ServiceProvider.GetRequiredService<AppointmentService>().GetBookingsByEmailAsync(aliceEmail));
            Assert.Equal("Booked", appointment.Status);
            Assert.Equal("Dr. Sara Ahmed", appointment.Doctor);
        }
        var aliceBookings = await alice.GetStringAsync("/MyAppointments");
        Assert.Contains($"LM-{appointment.Id}", aliceBookings);
        var bobBookings = await bob.GetStringAsync("/MyAppointments");
        Assert.DoesNotContain($"LM-{appointment.Id}", bobBookings);
        var unauthorizedCancel = await PostFormAsync(bob, "/MyAppointments?handler=Cancel", bobBookings,
            new() { ["id"] = appointment.Id.ToString() });
        Assert.Equal(HttpStatusCode.NotFound, unauthorizedCancel.StatusCode);
        var missingAntiforgery = await alice.PostAsync("/MyAppointments?handler=Cancel",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["id"] = appointment.Id.ToString() }));
        Assert.Equal(HttpStatusCode.BadRequest, missingAntiforgery.StatusCode);
        var cancelled = await PostFormAsync(alice, "/MyAppointments?handler=Cancel", aliceBookings,
            new() { ["id"] = appointment.Id.ToString() });
        Assert.Equal(HttpStatusCode.Redirect, cancelled.StatusCode);
        Assert.Contains("Cancelled", await alice.GetStringAsync("/MyAppointments"));
        var replay = await alice.GetStringAsync(successUrl);
        Assert.Contains("This appointment is cancelled", replay);
        Assert.DoesNotContain("<h1 class=\"page-title\">Booking confirmed", replay);

        var logoutPage = await alice.GetStringAsync("/MyAppointments");
        var logout = await PostFormAsync(alice, "/Account/Logout", logoutPage, new());
        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
        var requiresLogin = await alice.GetAsync("/MyAppointments");
        Assert.Equal(HttpStatusCode.Redirect, requiresLogin.StatusCode);
        Assert.Contains("/Account/Login", requiresLogin.Headers.Location!.OriginalString);
        var loginPage = await alice.GetStringAsync("/Account/Login");
        var login = await PostFormAsync(alice, "/Account/Login", loginPage, new()
        {
            ["Input.Email"] = aliceEmail,
            ["Input.Password"] = "Test-Patient-123!"
        });
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Contains("Cancelled", await alice.GetStringAsync("/MyAppointments"));
    }

    [Fact]
    public async Task ServicePreventsDoubleBookingAndReleasesCancelledSlot()
    {
        var email = $"guest-{Guid.NewGuid():N}@example.test";
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AppointmentService>();
        var input = new BookingInput
        {
            Specialty = "Dentist", Doctor = "Dr. Lina Haddad", Date = new DateTime(2030, 1, 8),
            Time = "20:30", PatientName = "Test Guest", PatientPhone = "+971501234567", PatientEmail = email
        };
        var first = await service.CreateBookingAsync(input);
        Assert.NotNull(first);
        Assert.Equal("Pending", first.Status);
        Assert.Null(await service.CreateBookingAsync(input));
        Assert.False(await service.UpdateStatusAsync(first.Id, "someone-else@example.test", "Cancelled"));
        var checkout = await scope.ServiceProvider.GetRequiredService<DepositService>().StartCheckoutAsync(first, CancellationToken.None);
        Assert.StartsWith("https://checkout.stripe.com/", checkout);
        var linked = await service.GetBookingAsync(first.Id);
        var stripe = Assert.IsType<FakeStripeCheckoutGateway>(factory.Services.GetRequiredService<IStripeCheckoutGateway>());
        stripe.Pay(linked!.StripeSessionId!);
        await scope.ServiceProvider.GetRequiredService<DepositService>().VerifyCheckoutAsync(linked.StripeSessionId!, CancellationToken.None);
        Assert.True(await service.UpdateStatusAsync(first.Id, email.ToUpperInvariant(), "Cancelled"));
        Assert.NotNull(await service.CreateBookingAsync(input));
        var bookings = await service.GetBookingsByDateAsync(input.Date.Value);
        Assert.Contains(bookings, a => a.Id == first.Id && a.Status == "Cancelled");
        Assert.Single(bookings, a => a.Doctor == input.Doctor && a.Status == "Pending");
        var ownBookings = await service.GetBookingsByEmailAsync(email);
        Assert.Equal(ownBookings.OrderByDescending(a => a.AppointmentDateTime).ThenByDescending(a => a.CreatedAt)
            .ThenByDescending(a => a.Id).Select(a => a.Id), ownBookings.Select(a => a.Id));

        input.Doctor = "Dr. Sara Ahmed";
        await Assert.ThrowsAsync<ValidationException>(() => service.CreateBookingAsync(input));
        input.Doctor = "Dr. Lina Haddad";
        input.Time = "21:00";
        await Assert.ThrowsAsync<ValidationException>(() => service.CreateBookingAsync(input));
        input.Time = "09:00";
        input.Date = new DateTime(2030, 1, 13);
        await Assert.ThrowsAsync<ValidationException>(() => service.CreateBookingAsync(input));
        input.Date = new DateTime(2030, 1, 8);
        input.PatientEmail = "invalid";
        await Assert.ThrowsAsync<ValidationException>(() => service.CreateBookingAsync(input));
    }

    [Fact]
    public async Task ServerRejectsTamperedWizardDataAndInvalidPatientDetails()
    {
        using var client = CreateClient();
        var html = await client.GetStringAsync("/Booking");
        var input = ValidInput();
        input["Step"] = "5";
        input["Input.PatientName"] = " ";
        input["Input.PatientEmail"] = "invalid";
        input["Input.PatientPhone"] = "not-a-phone";
        input["Input.Doctor"] = "Dr. Wrong";
        input["Input.Time"] = "21:00";
        var result = await PostFormAsync(client, "/Booking?handler=Confirm", html, input);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        var errors = await result.Content.ReadAsStringAsync();
        Assert.Contains("validation-summary-errors", errors);
        Assert.Contains("Choose the doctor", errors);
        Assert.Contains("future 30-minute slot", errors);
        Assert.Contains("not a valid e-mail", errors);
        Assert.DoesNotContain("Booking confirmed", errors);
    }

    [Fact]
    public async Task GuestBookingConflictReturnsToSlotSelectionWithoutAnotherInsert()
    {
        using var guest = CreateClient();
        var html = await guest.GetStringAsync("/Booking");
        var input = ValidInput();
        input["Input.Specialty"] = "ENT";
        input["Input.Doctor"] = "Dr. Omar Khan";
        input["Input.Time"] = "20:30";
        input["Input.PatientEmail"] = $"guest-{Guid.NewGuid():N}@example.test";
        input["Step"] = "5";
        var first = await PostFormAsync(guest, "/Booking?handler=Confirm", html, input);
        Assert.Equal(HttpStatusCode.Redirect, first.StatusCode);
        Assert.StartsWith("https://checkout.stripe.com/", first.Headers.Location!.OriginalString);
        var conflict = await PostFormAsync(guest, "/Booking?handler=Confirm", html, input);
        Assert.Equal(HttpStatusCode.OK, conflict.StatusCode);
        var conflictHtml = await conflict.Content.ReadAsStringAsync();
        Assert.Contains("That time was just booked", conflictHtml);
        Assert.Matches("name=\"Step\"[^>]*value=\"3\"", conflictHtml);
        using var scope = factory.Services.CreateScope();
        Assert.Single(await scope.ServiceProvider.GetRequiredService<AppointmentService>()
            .GetBookingsByEmailAsync(input["Input.PatientEmail"]));
    }

    [Fact]
    public async Task ReceptionDashboardRequiresReceptionRoleAndUpdatesTodaysAppointments()
    {
        using var patient = CreateClient();
        await RegisterAsync(patient, $"patient-{Guid.NewGuid():N}@example.test");
        var denied = await patient.GetAsync("/reception");
        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        Assert.Contains("/Account/AccessDenied", denied.Headers.Location!.OriginalString);

        var appointmentInput = new BookingInput
        {
            Specialty = "General Physician",
            Doctor = "Dr. Sara Ahmed",
            Date = new DateTime(2030, 1, 7),
            Time = "12:00",
            PatientName = "Reception Test Patient",
            PatientPhone = "+971501234567",
            PatientEmail = "reception-patient@example.test"
        };
        using (var scope = factory.Services.CreateScope())
        {
            var created = await scope.ServiceProvider.GetRequiredService<AppointmentService>()
                .CreateBookingAsync(appointmentInput);
            Assert.NotNull(created);
        }

        using var reception = CreateClient();
        var loginPage = await reception.GetStringAsync("/Account/Login?returnUrl=%2Freception");
        var login = await PostFormAsync(reception, "/Account/Login", loginPage, new()
        {
            ["ReturnUrl"] = "/reception",
            ["Input.Email"] = ReceptionSeeder.ReceptionEmail,
            ["Input.Password"] = "Test-Reception-123!"
        });
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Equal("/reception", login.Headers.Location!.OriginalString);

        var dashboard = await reception.GetStringAsync("/reception");
        using var appointmentScope = factory.Services.CreateScope();
        var todaysAppointments = await appointmentScope.ServiceProvider
            .GetRequiredService<AppointmentService>().GetBookingsByDateAsync(new DateTime(2030, 1, 7));
        Assert.Contains($"{todaysAppointments.Count} appointment", dashboard);
        Assert.Contains("Reception Test Patient", dashboard);
        Assert.Contains("Dr. Sara Ahmed", dashboard);
        Assert.Contains("General Physician", dashboard);
        Assert.Contains("12:00 PM", dashboard);
        Assert.Contains("Send reminder", dashboard);

        var appointment = Assert.Single(await appointmentScope.ServiceProvider
            .GetRequiredService<AppointmentService>()
            .GetBookingsByEmailAsync("reception-patient@example.test"));
        var update = await PostFormAsync(reception, "/reception?handler=UpdateStatus", dashboard, new()
        {
            ["id"] = appointment.Id.ToString(),
            ["status"] = "Arrived"
        });
        Assert.Equal(HttpStatusCode.Redirect, update.StatusCode);

        var refreshedDashboard = await reception.GetStringAsync("/reception");
        Assert.Contains("Arrived", refreshedDashboard);
        var updatedAppointment = await appointmentScope.ServiceProvider
            .GetRequiredService<AppointmentService>().GetBookingAsync(appointment.Id);
        Assert.Equal("Arrived", updatedAppointment!.Status);
    }

    private static Dictionary<string, string> ValidInput() => new()
    {
        ["Input.Specialty"] = "General Physician",
        ["Input.Doctor"] = "Dr. Sara Ahmed",
        ["Input.Date"] = "2030-01-07",
        ["Input.Time"] = "09:00",
        ["Input.PatientName"] = "Test Patient",
        ["Input.PatientPhone"] = "+971501234567",
        ["Input.PatientEmail"] = "guest@example.test"
    };

    private static async Task RegisterAsync(HttpClient client, string email)
    {
        var html = await client.GetStringAsync("/Account/Register");
        var response = await PostFormAsync(client, "/Account/Register", html, new()
        {
            ["Input.Email"] = email,
            ["Input.Password"] = "Test-Patient-123!",
            ["Input.ConfirmPassword"] = "Test-Patient-123!"
        });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    private static async Task<string> PostStepAsync(HttpClient client, string handler, string html,
        Dictionary<string, string> input, int expectedStep)
    {
        var response = await PostFormAsync(client, $"/Booking?handler={handler}", html, input);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadAsStringAsync();
        Assert.Matches($"name=\"Step\"[^>]*value=\"{expectedStep}\"", result);
        return result;
    }

    private static Task<HttpResponseMessage> PostFormAsync(HttpClient client, string url, string html, Dictionary<string, string> fields)
    {
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(token.Success, "Expected an antiforgery token in the form.");
        var values = new Dictionary<string, string>(fields)
        {
            ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token.Groups[1].Value)
        };
        return client.PostAsync(url, new FormUrlEncodedContent(values));
    }
}
