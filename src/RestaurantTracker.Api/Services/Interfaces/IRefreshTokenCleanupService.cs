namespace RestaurantTracker.Api.Services;

public interface IRefreshTokenCleanupService
{
    Task RunIfDueAsync();
}
