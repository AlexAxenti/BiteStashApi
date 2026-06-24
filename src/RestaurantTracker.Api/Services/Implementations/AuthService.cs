using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using RestaurantTracker.Api.Data;
using RestaurantTracker.Api.Entities;

namespace RestaurantTracker.Api.Services;

public class AuthService : IAuthService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IConfiguration _configuration;
    private readonly ApplicationDbContext _dbContext;

    public AuthService(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IConfiguration configuration,
        ApplicationDbContext dbContext)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _configuration = configuration;
        _dbContext = dbContext;
    }

    public async Task<RegisterResult> RegisterAsync(string email, string password)
    {
        var existingUser = await _userManager.FindByEmailAsync(email);
        if (existingUser is not null)
        {
            return new RegisterResult(false, null, "A user with that email already exists.", null);
        }

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email
        };

        var result = await _userManager.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            return new RegisterResult(false, null, null, result.Errors.Select(e => e.Description));
        }

        return new RegisterResult(true, user, null, null);
    }

    public async Task<ApplicationUser?> ValidateLoginAsync(string email, string password)
    {
        var user = await _userManager.FindByEmailAsync(email);
        if (user is null) return null;

        var result = await _signInManager.CheckPasswordSignInAsync(user, password, lockoutOnFailure: false);
        return result.Succeeded ? user : null;
    }

    public (string AccessToken, DateTime ExpiresAtUtc) GenerateAccessToken(ApplicationUser user)
    {
        var secret = _configuration["Jwt:Secret"]
            ?? throw new InvalidOperationException("JWT secret is not configured.");

        var issuer = _configuration["Jwt:Issuer"]
            ?? throw new InvalidOperationException("JWT issuer is not configured.");

        var audience = _configuration["Jwt:Audience"]
            ?? throw new InvalidOperationException("JWT audience is not configured.");

        var expiryMinutes = int.Parse(_configuration["Jwt:ExpiryMinutes"] ?? "60");

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(ClaimTypes.Email, user.Email ?? string.Empty)
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var expiresAtUtc = DateTime.UtcNow.AddMinutes(expiryMinutes);

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: expiresAtUtc,
            signingCredentials: credentials);

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAtUtc);
    }

    public async Task<string> CreateRefreshTokenAsync(ApplicationUser user)
    {
        var (refreshToken, refreshTokenValue) = BuildRefreshToken(user);

        _dbContext.RefreshTokens.Add(refreshToken);
        await _dbContext.SaveChangesAsync();

        return refreshTokenValue;
    }

    public async Task<RefreshResult> RefreshTokenAsync(string refreshToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync();

        var refreshTokenHash = HashToken(refreshToken);

        var existingRefreshToken = await _dbContext.RefreshTokens
            .Include(rt => rt.User)
            .FirstOrDefaultAsync(rt => rt.TokenHash == refreshTokenHash);

        if (existingRefreshToken is null)
        {
            return new RefreshResult(false, null, null, null, "Invalid refresh token.");
        }

        if (existingRefreshToken.RevokedAtUtc is not null)
        {
            return new RefreshResult(false, null, null, null, "Refresh token has been revoked.");
        }

        if (existingRefreshToken.ExpiresAtUtc <= DateTime.UtcNow)
        {
            return new RefreshResult(false, null, null, null, "Refresh token has expired.");
        }

        existingRefreshToken.RevokedAtUtc = DateTime.UtcNow;

        var (newRefreshToken, refreshTokenValue) = BuildRefreshToken(existingRefreshToken.User);
        existingRefreshToken.ReplacedByTokenHash = newRefreshToken.TokenHash;
        _dbContext.RefreshTokens.Add(newRefreshToken);

        try
        {
            await _dbContext.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync();
            return new RefreshResult(false, null, null, null, "Refresh token has already been used.");
        }

        var (accessToken, expiresAtUtc) = GenerateAccessToken(existingRefreshToken.User);

        return new RefreshResult(true, accessToken, refreshTokenValue, expiresAtUtc, null);
    }

    public async Task DeleteOldRefreshTokensAsync()
    {
        var now = DateTime.UtcNow;
        var revokedCutoff = now.AddDays(-7);

        await _dbContext.RefreshTokens
            .Where(t =>
                t.ExpiresAtUtc < now ||
                (t.RevokedAtUtc != null && t.RevokedAtUtc < revokedCutoff))
            .ExecuteDeleteAsync();
    }

    private (RefreshToken RefreshToken, string RefreshTokenValue) BuildRefreshToken(ApplicationUser user)
    {
        var refreshTokenDays = int.Parse(_configuration["Jwt:RefreshTokenDays"] ?? "30");
        var refreshTokenValue = GenerateRefreshTokenValue();

        return (
            new RefreshToken
            {
                Id = Guid.NewGuid(),
                TokenHash = HashToken(refreshTokenValue),
                UserId = user.Id,
                CreatedAtUtc = DateTime.UtcNow,
                ExpiresAtUtc = DateTime.UtcNow.AddDays(refreshTokenDays)
            },
            refreshTokenValue);
    }

    private static string GenerateRefreshTokenValue()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
    }

    private static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToBase64String(bytes);
    }
}
