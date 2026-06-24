namespace RestaurantTracker.Api.Entities;

public class RefreshToken
{
    public Guid Id { get; set; }

    public string TokenHash { get; set; } = null!;

    public DateTime ExpiresAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? RevokedAtUtc { get; set; }

    public string? ReplacedByTokenHash { get; set; }


    public string UserId { get; set; } = null!;
    public ApplicationUser User { get; set; } = null!;

    public uint RowVersion { get; set; }

    public bool IsExpired => DateTime.UtcNow >= ExpiresAtUtc;
    public bool IsRevoked => RevokedAtUtc.HasValue;
    public bool IsActive => !IsExpired && !IsRevoked;
}
