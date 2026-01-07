using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Data.SqlClient;

namespace BioFXAPI.Services;

public class AccountMaintenanceHostedService : BackgroundService
{
    private readonly ILogger<AccountMaintenanceHostedService> _logger;
    private readonly IServiceProvider _sp;
    private readonly IHostEnvironment _env;
    private readonly IConfiguration _cfg;

    public AccountMaintenanceHostedService(
        ILogger<AccountMaintenanceHostedService> logger,
        IServiceProvider sp,
        IHostEnvironment env,
        IConfiguration cfg)
    {
        _logger = logger;
        _sp = sp;
        _env = env;
        _cfg = cfg;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Default: dev = diario, prod = diario (overridable por config)
        var interval = TimeSpan.FromHours(24);

        if (int.TryParse(_cfg["AccountMaintenance:IntervalMinutes"], out var mins) && mins > 0)
            interval = TimeSpan.FromMinutes(mins);

        // Ventanas
        int disableAfterDays = int.TryParse(_cfg["AccountMaintenance:DisableUnconfirmedAfterDays"], out var d) ? d : 7;
        int deleteTokensAfterDays = int.TryParse(_cfg["AccountMaintenance:DeleteExpiredTokensAfterDays"], out var t) ? t : 30;

        // Evitar ejecutar inmediatamente al arrancar
        await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
        using var timer = new PeriodicTimer(interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var cs = _cfg.GetConnectionString("DefaultConnection");
                if (string.IsNullOrWhiteSpace(cs))
                {
                    _logger.LogError("AccountMaintenance: DefaultConnection no configurada. Se omite el tick.");
                    await timer.WaitForNextTickAsync(stoppingToken);
                    continue;
                }

                // Lock distribuido: mantener conexión abierta durante el job
                await using var lockCon = new SqlConnection(cs);
                try
                {
                    await lockCon.OpenAsync(stoppingToken);

                    // sp_getapplock
                    await using (var cmd = lockCon.CreateCommand())
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.CommandText = "sp_getapplock";

                        cmd.Parameters.Add(new SqlParameter("@Resource", SqlDbType.NVarChar, 255) { Value = "account:maintenance" });
                        cmd.Parameters.Add(new SqlParameter("@LockMode", SqlDbType.NVarChar, 32) { Value = "Exclusive" });
                        cmd.Parameters.Add(new SqlParameter("@LockOwner", SqlDbType.NVarChar, 32) { Value = "Session" });
                        cmd.Parameters.Add(new SqlParameter("@LockTimeout", SqlDbType.Int) { Value = 0 });

                        var ret = new SqlParameter("@RETURN_VALUE", SqlDbType.Int) { Direction = ParameterDirection.ReturnValue };
                        cmd.Parameters.Add(ret);

                        await cmd.ExecuteNonQueryAsync(stoppingToken);
                        var code = (int)ret.Value;

                        if (code < 0)
                        {
                            _logger.LogInformation("AccountMaintenance: lock no adquirido (code={Code}). Se omite el tick.", code);
                            await timer.WaitForNextTickAsync(stoppingToken);
                            continue;
                        }
                    }

                    // Ejecutar tareas
                    int disabledUsers = await DisableOldUnconfirmedUsersAsync(lockCon, disableAfterDays, stoppingToken);
                    int deletedTokens = await DeactivateOldEmailTokensAsync(lockCon, deleteTokensAfterDays, stoppingToken);

                    _logger.LogInformation(
                        "AccountMaintenance: disabledUsers={DisabledUsers} deletedExpiredTokens={DeletedTokens} (disableAfterDays={DisableDays}, deleteTokensAfterDays={TokenDays})",
                        disabledUsers, deletedTokens, disableAfterDays, deleteTokensAfterDays);
                }
                catch (Exception exLock)
                {
                    _logger.LogError(exLock, "AccountMaintenance: error adquiriendo lock o conectando a SQL. Se reintentará en el próximo tick.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AccountMaintenance failed");
            }

            await timer.WaitForNextTickAsync(stoppingToken);
        }
    }

    private static async Task<int> DisableOldUnconfirmedUsersAsync(SqlConnection con, int days, CancellationToken ct)
    {
        var sql = @"
            UPDATE dbo.Usuario
            SET Activo = 0,
                actualizadoEl = GETUTCDATE()
            WHERE Activo = 1
              AND emailConfirmado = 0
              AND creadoEl < DATEADD(day, -@Days, GETUTCDATE());";

        await using var cmd = new SqlCommand(sql, con);
        cmd.Parameters.AddWithValue("@Days", days);
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<int> DeactivateOldEmailTokensAsync(SqlConnection con, int days, CancellationToken ct)
    {
        var sql = @"
            UPDATE dbo.EmailVerificationTokens
            SET Activo = 0,
                ActualizadoEl = GETUTCDATE()
            WHERE Activo = 1
              AND (
                    ExpiraEl < DATEADD(day, -@Days, GETUTCDATE())
                 OR UsadoEl < DATEADD(day, -@Days, GETUTCDATE())
                 OR RevocadoEl < DATEADD(day, -@Days, GETUTCDATE())
              );";

        await using var cmd = new SqlCommand(sql, con);
        cmd.Parameters.AddWithValue("@Days", days);
        return await cmd.ExecuteNonQueryAsync(ct);
    }

}
