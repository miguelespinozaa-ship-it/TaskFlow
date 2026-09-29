using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Auth;
using TaskFlow.Application.Common.Exceptions;
using TaskFlow.Infrastructure.Identity;

namespace TaskFlow.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController(IAuthService auth, ICurrentUser currentUser) : ControllerBase
{
    // El refresh token vive en una cookie httpOnly: JavaScript no puede leerla, así que un XSS no la roba.
    // El access token (15 min) lo guarda el cliente EN MEMORIA, nunca en localStorage.
    public const string RefreshCookie = "tf_refresh";
    private const string CookiePath = "/api/v1/auth"; // el navegador solo la manda a estos endpoints

    [AllowAnonymous]
    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request, CancellationToken ct) =>
        Session(await auth.RegisterAsync(request, ct));

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request, CancellationToken ct) =>
        Session(await auth.LoginAsync(request, ct));

    [AllowAnonymous]
    [HttpPost("refresh")]
    public async Task<ActionResult<AuthResponse>> Refresh(CancellationToken ct)
    {
        try
        {
            return Session(await auth.RefreshAsync(Request.Cookies[RefreshCookie], ct));
        }
        catch (UnauthorizedException ex)
        {
            // No se relanza: UseExceptionHandler limpia los headers de la respuesta y se perdería el
            // borrado de la cookie. Sesión inválida → que el navegador deje de mandar una cookie muerta.
            ClearCookie();
            return Problem(title: ex.Message, statusCode: StatusCodes.Status401Unauthorized);
        }
    }

    [HttpPost("switch-workspace")]
    public async Task<ActionResult<AuthResponse>> SwitchWorkspace(SwitchWorkspaceRequest request, CancellationToken ct) =>
        Session(await auth.SwitchWorkspaceAsync(
            currentUser.RequireUserId(), request.WorkspaceId, Request.Cookies[RefreshCookie], ct));

    [AllowAnonymous] // el access token puede haber vencido; con la cookie alcanza para cerrar la sesión
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        await auth.LogoutAsync(Request.Cookies[RefreshCookie], ct);
        ClearCookie();
        return NoContent();
    }

    [HttpGet("me")]
    public async Task<ActionResult<MeResponse>> Me(CancellationToken ct)
    {
        var workspaceId = Guid.Parse(User.FindFirst(TaskFlowClaims.WorkspaceId)!.Value);
        return Ok(await auth.GetMeAsync(currentUser.RequireUserId(), workspaceId, ct));
    }

    private ActionResult<AuthResponse> Session(AuthResult result)
    {
        Response.Cookies.Append(RefreshCookie, result.RefreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Strict, // no se envía en requests iniciados desde otros sitios (CSRF)
            Path = CookiePath,
            Expires = result.RefreshTokenExpiresAt,
        });
        return Ok(AuthResponse.From(result));
    }

    private void ClearCookie() =>
        Response.Cookies.Delete(RefreshCookie, new CookieOptions { Path = CookiePath, Secure = Request.IsHttps });
}
