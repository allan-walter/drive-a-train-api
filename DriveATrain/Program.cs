using DriveATrain;
using DriveATrain.Auth;
using DriveATrain.Data;
using DriveATrain.Hubs;
using DriveATrain.OpenCv;
using DriveATrain.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(["http://localhost:4200", "http://192.168.20.201:4200"]) // your frontend origin
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

builder.Services.AddSingleton<DccService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<DccService>());
builder.Services.AddSingleton<DetectorService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<DetectorService>());
builder.Services.AddSingleton<LimiterService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<UnitService>());
builder.Services.AddSingleton<UnitService>();
builder.Services.AddSingleton<CaptureService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<CaptureService>());
builder.Services.AddSingleton<BroadcastService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<BroadcastService>());

builder.Services.AddSignalR();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite("Data Source=database.db"));

builder.Services.Configure<Config>(builder.Configuration);
builder.Services.AddSingleton(resolver =>
    resolver.GetRequiredService<IOptions<Config>>().Value);

builder.Services
    .AddIdentity<IdentityUser, IdentityRole>(options =>
    {
        options.Password.RequireDigit = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequiredLength = 1;
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

// Add services to the container.
// builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
//     .AddCookie(options =>
//     {
//         options.ExpireTimeSpan = TimeSpan.FromMinutes(20);
//         options.SlidingExpiration = true;
//         options.AccessDeniedPath = "/Forbidden/";
//     });
// builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
//     .AddCookie();
builder.Services.AddAuthorization();


builder.Services.ConfigureApplicationCookie(options =>
{
    // 1. Set to Lax. Different ports on localhost are considered "Same-Site".
    options.Cookie.SameSite = SameSiteMode.Lax;

    // 2. FORCE Secure to false. This allows your HTTP Angular app to save it.
    options.Cookie.SecurePolicy = CookieSecurePolicy.None;
    options.Cookie.HttpOnly = false;
});

var app = builder.Build();
app.Urls.Add("http://0.0.0.0:5127");

// Configure the HTTP request pipeline.

// app.UseHttpsRedirection();

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", () =>
{
    var forecast = Enumerable.Range(1, 5).Select(index =>
            new WeatherForecast
            (
                DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
                Random.Shared.Next(-20, 55),
                summaries[Random.Shared.Next(summaries.Length)]
            ))
        .ToArray();
    return forecast;
});

app.UseCors();
app.UseWebSockets();

app.UseAuthentication();
app.UseAuthorization();

// app.MapRazorPages();
// app.MapDefaultControllerRoute();

app.MapAuthEndpoints();
app.MapHub<InfoHub>("/hubs/info");
app.MapHub<CropHub>("/hubs/crop");
app.MapHub<ThrottleHub>("/hubs/throttle");
app.MapHub<UnitHub>("/hubs/units");

app.Map("/ws/video/birdsEye", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = 400;
        return;
    }

    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    var streamer = app.Services.GetRequiredService<BroadcastService>();
    await streamer.RegisterClientAsync(socket, context.RequestAborted);
});

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;

    // Run EF migrations
    var db = services.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();

    // Seed identity users
    var userManager = services.GetRequiredService<UserManager<IdentityUser>>();
    var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();

    await SeedAsync(userManager, roleManager);
}

app.Run();

static async Task SeedAsync(UserManager<IdentityUser> userManager, RoleManager<IdentityRole> roleManager)
{
    // Seed roles
    if (!await roleManager.RoleExistsAsync("Admin"))
        await roleManager.CreateAsync(new IdentityRole("Admin"));

    // Seed users
    const string adminEmail = "allanjwalter@gmail.com";
    if (await userManager.FindByEmailAsync(adminEmail) == null)
    {
        var user = new IdentityUser
        {
            UserName = adminEmail,
            Email = adminEmail,
            EmailConfirmed = true,
        };

        var result = await userManager.CreateAsync(user, "Password123");

        if (result.Succeeded)
        {
            await userManager.AddToRoleAsync(user, "Admin");
        }
    }
}

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}