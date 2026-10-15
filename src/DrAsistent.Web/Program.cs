using DrAsistent.Data;
using DrAsistent.Export;
using DrAsistent.Web.Components;
using DrAsistent.Web.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("Lipsește ConnectionStrings__Default.");

builder.Services.AddDbContextFactory<AppDbContext>(o =>
    o.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure()));

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddMudServices();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<AuthenticationStateProvider, RevalidatingAuthStateProvider>();
builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddIdentityCookies();
builder.Services.ConfigureApplicationCookie(o =>
{
    o.LoginPath = "/account/login";
    o.ExpireTimeSpan = TimeSpan.FromHours(12);
    o.SlidingExpiration = true;
});
builder.Services.AddAuthorization();
builder.Services.AddIdentityCore<ApplicationUser>(o =>
    {
        o.Password.RequiredLength = 8;
        o.Password.RequireNonAlphanumeric = false;
        o.Lockout.MaxFailedAccessAttempts = 5;
        o.User.RequireUniqueEmail = false;
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

// Cheile pentru cookie-uri stau in baza de date, ca sesiunile sa supravietuiasca repornirii containerului
builder.Services.AddDataProtection()
    .PersistKeysToDbContext<AppDbContext>()
    .SetApplicationName("DrAsistent");

builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

builder.Services.AddScoped<ScheduleService>();

var app = builder.Build();

await DatabaseInitializer.InitializeAsync(app.Services, app.Configuration, app.Logger);

app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapPost("/account/logout", async (SignInManager<ApplicationUser> signIn) =>
{
    await signIn.SignOutAsync();
    return Results.LocalRedirect("/account/login");
}).RequireAuthorization();

var export = app.MapGroup("/export").RequireAuthorization();
export.MapGet("/{year:int}/{month:int}.xlsx", async (int year, int month, ScheduleService svc) =>
{
    var doc = await svc.DocumentAsync(year, month);
    return doc is null
        ? Results.NotFound("Graficul nu este salvat.")
        : Results.File(ExcelExporter.Export(doc), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"grafic-{year}-{month:00}.xlsx");
});
export.MapGet("/{year:int}/{month:int}.pdf", async (int year, int month, ScheduleService svc) =>
{
    var doc = await svc.DocumentAsync(year, month);
    return doc is null
        ? Results.NotFound("Graficul nu este salvat.")
        : Results.File(PdfExporter.Export(doc), "application/pdf", $"grafic-{year}-{month:00}.pdf");
});

app.MapGet("/health", () => Results.Ok("ok")).AllowAnonymous();

app.Run();
