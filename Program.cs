using LakesideMedical.Data;
using LakesideMedical.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using System.Threading.RateLimiting;
using dotenv.net;

var builder = WebApplication.CreateBuilder(args);
var envPath = Path.Combine(builder.Environment.ContentRootPath, ".env");
if (File.Exists(envPath))
{
    DotEnv.Load(new DotEnvOptions(ignoreExceptions: false, envFilePaths: [envPath], overwriteExistingVars: false));
}

// Add services to the container.
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("The DefaultConnection connection string is missing.")));
builder.Services.AddIdentity<IdentityUser, IdentityRole>(options =>
{
    options.SignIn.RequireConfirmedEmail = false;
    options.SignIn.RequireConfirmedAccount = false;
    options.User.RequireUniqueEmail = true;
    options.Password.RequiredLength = 8;
})
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.Cookie.HttpOnly = true;
});
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<AppointmentService>();
builder.Services.AddSingleton<IStripeCheckoutGateway, StripeCheckoutGateway>();
builder.Services.AddScoped<DepositService>();
builder.Services.Configure<AssistantOptions>(options =>
{
    options.ApiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
    options.Model = Environment.GetEnvironmentVariable("ANTHROPIC_MODEL") ?? "claude-haiku-5-5";
});
builder.Services.AddHttpClient<AssistantService>();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("assistant-chat", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});
builder.Services.AddRazorPages();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseRouting();
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapPost("/api/chat", async (
    ChatRequest? request,
    AssistantService assistant,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    if (request?.Messages is null || !AssistantService.IsValidConversation(request.Messages))
    {
        return Results.BadRequest(new { error = "Send a conversation with up to 12 alternating user and assistant messages, ending with a user message." });
    }

    try
    {
        var reply = await assistant.GetReplyAsync(request.Messages, cancellationToken);
        return Results.Ok(new ChatResponse(reply));
    }
    catch (AssistantNotConfiguredException exception)
    {
        logger.LogError(exception, "The clinic assistant is not configured.");
        return Results.Problem(
            "The chat assistant is not configured yet. Please call the clinic.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (HttpRequestException exception)
    {
        logger.LogError(exception, "The clinic assistant provider request failed.");
        return Results.Problem(
            "The chat assistant is temporarily unavailable. Please try again or call the clinic.",
            statusCode: StatusCodes.Status502BadGateway);
    }
    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
    {
        logger.LogWarning("The clinic assistant provider request timed out.");
        return Results.Problem(
            "The chat assistant took too long to respond. Please try again.",
            statusCode: StatusCodes.Status504GatewayTimeout);
    }
})
    .WithMetadata(new RequestSizeLimitAttribute(16_384))
    .RequireRateLimiting("assistant-chat");

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

await app.StartAsync();

if (builder.Configuration.GetValue("Reception:SeedOnStartup", true))
{
    using var scope = app.Services.CreateScope();
    var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("ReceptionSeeder");
    try
    {
        await ReceptionSeeder.SeedAsync(
            scope.ServiceProvider,
            Environment.GetEnvironmentVariable("RECEPTION_PASSWORD"),
            logger);
    }
    catch (SqlException exception)
    {
        logger.LogError(exception,
            "Reception setup could not access the database. Apply database migrations and verify SQL Server before using reception or appointment features.");
    }
}

await app.WaitForShutdownAsync();

public partial class Program;
