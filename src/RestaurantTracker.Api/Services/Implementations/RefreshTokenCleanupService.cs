namespace RestaurantTracker.Api.Services;

public class RefreshTokenCleanupService : IRefreshTokenCleanupService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);
    private DateTime? _lastRunAt;

    public RefreshTokenCleanupService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task RunIfDueAsync()
    {
        var now = DateTime.UtcNow;

        if (_lastRunAt.HasValue && (now - _lastRunAt.Value) < Interval)
            return;

        if (!await _lock.WaitAsync(0))
            return;

        try
        {
            now = DateTime.UtcNow;
            if (_lastRunAt.HasValue && (now - _lastRunAt.Value) < Interval)
                return;

            using var scope = _scopeFactory.CreateScope();
            var authService = scope.ServiceProvider.GetRequiredService<IAuthService>();
            await authService.DeleteOldRefreshTokensAsync();

            _lastRunAt = DateTime.UtcNow;
        }
        finally
        {
            _lock.Release();
        }
    }
}
