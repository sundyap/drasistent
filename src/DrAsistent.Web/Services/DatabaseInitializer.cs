using DrAsistent.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DrAsistent.Web.Services;

public static class DatabaseInitializer
{
    /// <summary>Aplica migrarile si creeaza primul administrator (Admin__User / Admin__Password) daca nu exista utilizatori.</summary>
    public static async Task InitializeAsync(IServiceProvider services, IConfiguration config, ILogger logger)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await db.Database.MigrateAsync();
                break;
            }
            catch (Exception ex) when (attempt < 10)
            {
                logger.LogWarning(ex, "Baza de date nu este disponibilă (încercarea {Attempt}), reîncerc în 5s", attempt);
                await Task.Delay(TimeSpan.FromSeconds(5));
            }
        }

        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        if (await users.Users.AnyAsync()) return;

        var userName = config["Admin:User"];
        var password = config["Admin:Password"];
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning("Nu există utilizatori. Setați Admin__User și Admin__Password pentru a crea primul cont.");
            return;
        }

        var result = await users.CreateAsync(new ApplicationUser { UserName = userName }, password);
        if (result.Succeeded)
            logger.LogInformation("Contul administrator {User} a fost creat", userName);
        else
            logger.LogError("Nu am putut crea contul administrator: {Errors}", string.Join("; ", result.Errors.Select(e => e.Description)));
    }
}
