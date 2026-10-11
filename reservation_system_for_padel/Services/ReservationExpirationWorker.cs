namespace reservation_system_for_padel.Services
{
    public class ReservationExpirationWorker : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<ReservationExpirationWorker> _logger;

        public ReservationExpirationWorker(IServiceProvider serviceProvider, ILogger<ReservationExpirationWorker> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("ReservationExpirationWorker byl spuštěn.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<IReservationManagementService>();
                    await service.CleanupExpiredReservationsAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Chyba při běhu ReservationExpirationWorkeru.");
                }

                // Kontrola každých 60 sekund
                await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
            }
        }
    }
}
