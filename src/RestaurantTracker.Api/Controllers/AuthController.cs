using Microsoft.AspNetCore.Mvc;
using RestaurantTracker.Api.Dtos;
using RestaurantTracker.Api.Services;

namespace RestaurantTracker.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly IRefreshTokenCleanupService _cleanupService;

    public AuthController(IAuthService authService, IRefreshTokenCleanupService cleanupService)
    {
        _authService = authService;
        _cleanupService = cleanupService;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest request)
    {
        var result = await _authService.RegisterAsync(request.Email, request.Password);

        if (!result.Succeeded)
        {
            if (result.ErrorMessage is not null)
                return BadRequest(new { message = result.ErrorMessage });

            return BadRequest(new { errors = result.ValidationErrors });
        }

        var (accessToken, expiresAtUtc) = _authService.GenerateAccessToken(result.User!);
        var refreshToken = await _authService.CreateRefreshTokenAsync(result.User!);

        await _cleanupService.RunIfDueAsync();

        return Ok(new AuthResponse(accessToken, refreshToken, expiresAtUtc));
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        var user = await _authService.ValidateLoginAsync(request.Email, request.Password);

        if (user is null)
        {
            return Unauthorized(new { message = "Invalid email or password." });
        }

        var (accessToken, expiresAtUtc) = _authService.GenerateAccessToken(user);
        var refreshToken = await _authService.CreateRefreshTokenAsync(user);

        await _cleanupService.RunIfDueAsync();

        return Ok(new AuthResponse(accessToken, refreshToken, expiresAtUtc));
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(RefreshTokenRequest request)
    {
        var result = await _authService.RefreshTokenAsync(request.RefreshToken);

        if (!result.Succeeded)
        {
            return Unauthorized(new { message = result.ErrorMessage });
        }

        await _cleanupService.RunIfDueAsync();

        return Ok(new AuthResponse(result.AccessToken!, result.RefreshToken!, result.ExpiresAtUtc!.Value));
    }
}