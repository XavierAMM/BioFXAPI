using BioFXAPI.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Data.SqlClient;


namespace BioFXAPI.Services;

public class PlacetoPayAutoRefreshHostedService : BackgroundService
{
    private readonly ILogger<PlacetoPayAutoRefreshHostedService> _logger;
    private readonly string _instanceId = Guid.NewGuid().ToString("N");
    private readonly IServiceProvider _sp;
    private readonly IHostEnvironment _env;
    private readonly IConfiguration _cfg;

    public PlacetoPayAutoRefreshHostedService(
        ILogger<PlacetoPayAutoRefreshHostedService> logger,
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
        // Intervalo: dev diario, prod cada hora (overridable por config)
        TimeSpan interval = _env.IsDevelopment()
            ? TimeSpan.FromHours(24)
            : TimeSpan.FromHours(1);

        if (int.TryParse(_cfg["PlacetoPay:AutoRefreshMinutes"], out var mins) && mins > 0)
            interval = TimeSpan.FromMinutes(mins);

        int maxBatch = int.TryParse(_cfg["PlacetoPay:AutoRefreshMaxBatch"], out var b) ? b : 300;
        int lookbackHours = int.TryParse(_cfg["PlacetoPay:AutoRefreshLookbackHours"], out var h) ? h : 48;
        int concurrency = int.TryParse(_cfg["PlacetoPay:AutoRefreshConcurrency"], out var c) ? c : 4;

        // Evitar ejecutar inmediatamente al arrancar
        await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
        using var timer = new PeriodicTimer(interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _sp.CreateScope();
                var refresh = scope.ServiceProvider.GetRequiredService<PlacetoPayRefreshService>();

                var cs = _cfg.GetConnectionString("DefaultConnection");
                if (string.IsNullOrWhiteSpace(cs))
                {
                    _logger.LogError("PlacetoPay AutoRefresh: DefaultConnection no configurada. Se omite el tick.");
                    await timer.WaitForNextTickAsync(stoppingToken);
                    continue;
                }

                // === Lock distribuido (mantener conexión abierta durante el batch) ===
                await using var lockCon = new SqlConnection(cs);
                try
                {
                    await lockCon.OpenAsync(stoppingToken);

                    await using var cmd = lockCon.CreateCommand();
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.CommandText = "sp_getapplock";

                    cmd.Parameters.Add(new SqlParameter("@Resource", SqlDbType.NVarChar, 255) { Value = "placetopay:auto-refresh" });
                    cmd.Parameters.Add(new SqlParameter("@LockMode", SqlDbType.NVarChar, 32) { Value = "Exclusive" });
                    cmd.Parameters.Add(new SqlParameter("@LockOwner", SqlDbType.NVarChar, 32) { Value = "Session" });
                    cmd.Parameters.Add(new SqlParameter("@LockTimeout", SqlDbType.Int) { Value = 0 });

                    var ret = new SqlParameter("@RETURN_VALUE", SqlDbType.Int) { Direction = ParameterDirection.ReturnValue };
                    cmd.Parameters.Add(ret);

                    await cmd.ExecuteNonQueryAsync(stoppingToken);
                    var code = (int)ret.Value;

                    if (code < 0)
                    {
                        _logger.LogInformation("PlacetoPay AutoRefresh: lock no adquirido (code={Code}). Se omite el tick.", code);
                    }
                    else
                    {
                        var summary = await refresh.RefreshPendingBatchAsync(maxBatch, lookbackHours, concurrency, stoppingToken);

                        _logger.LogInformation(
                            "PlacetoPay AutoRefresh: candidates={Candidates} ok={Ok} paid={Paid} errors={Errors}",
                            summary.CandidateCount, summary.OkCount, summary.PaidCount, summary.ErrorCount);
                    }
                }
                catch (Exception exLock)
                {
                    // Esto cubre: SQL caído / red / credenciales / timeout / etc.
                    // Reintento: se hará automáticamente en el próximo tick.
                    _logger.LogError(exLock, "PlacetoPay AutoRefresh: error adquiriendo lock o conectando a SQL. Se reintentará en el próximo tick.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PlacetoPay AutoRefresh failed");
            }

            await timer.WaitForNextTickAsync(stoppingToken);            
        }
    }

}
