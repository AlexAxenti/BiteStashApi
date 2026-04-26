using RestaurantTracker.Api.Entities;

namespace RestaurantTracker.Api.Services;

public record RegisterResult(bool Succeeded, ApplicationUser? User, string? ErrorMessage, IEnumerable<string>? ValidationErrors);
public record RefreshResult(bool Succeeded, string? AccessToken, string? RefreshToken, DateTime? ExpiresAtUtc, string? ErrorMessage);

public interface IAuthService
{
    Task<RegisterResult> RegisterAsync(string email, string password);
    Task<ApplicationUser?> ValidateLoginAsync(string email, string password);
    (string AccessToken, DateTime ExpiresAtUtc) GenerateAccessToken(ApplicationUser user);
    Task<string> CreateRefreshTokenAsync(ApplicationUser user);
    Task<RefreshResult> RefreshTokenAsync(string refreshToken);
    Task DeleteOldRefreshTokensAsync();
}
