using Webly.Api.Extensions;
using Webly.Api.Infrastructure;
using Webly.Api.Options;
using Webly.Data.Models.Authentication;
using Webly.Services.DTO.Authentication;
using Webly.Services.Services.Authentication;
using Webly.Services.UseCases.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Security.Claims;

namespace Webly.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(
    RegisterUser registerUser,
    SignInWithPassword signInWithPassword,
    SignInWithExternalLogin signInWithExternalLogin,
    SignOut signOut,
    GetCurrentUser getCurrentUser,
    DeleteAccount deleteAccount,
    IOptions<JwtOptions> jwtOptions,
    IOptions<GoogleAuthOptions> googleOptions) : ControllerBase
{
    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType<MeResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MeResponse>> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        var result = await registerUser.Execute(request, cancellationToken);

        if (!result.Succeeded)
            return Conflict(new ProblemDetails { Title = "That email address is already registered." });

        return SignInResponse(result);
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType<MeResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MeResponse>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var result = await signInWithPassword.Execute(request, cancellationToken);

        if (!result.Succeeded)
            return Unauthorized(new ProblemDetails { Title = "That email address and password do not match." });

        return SignInResponse(result);
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        // Revoke first, then clear: the cookie is only a copy, and the browser's copy is not the
        // one an attacker would be holding.
        var (tokenId, expiresAt) = this.GetAccessTokenIdentity();

        // Unverified on purpose: being unable to sign out until you have confirmed your address
        // would be absurd, and actively harmful on a shared computer.
        var userId = this.GetUserIdUnverified();

        if (userId is null)
            return Unauthorized();

        await signOut.Execute(
            userId.Value,
            AuthCookies.GetRefreshToken(Request),
            tokenId,
            expiresAt,
            cancellationToken);
        AuthCookies.Clear(Response);

        return NoContent();
    }

    /// <summary>
    /// Closes the account: its sites, their repositories, their hosting, and every row under them.
    ///
    /// <c>GetUserIdUnverified</c>, deliberately. Somebody who registered, never confirmed their address and
    /// thought better of the whole thing is exactly the person most entitled to leave — and the verification gate
    /// exists to stop unverified accounts publishing to the internet, not to hold them captive.
    /// </summary>
    [HttpDelete("account")]
    [Authorize]
    public async Task<IActionResult> DeleteAccount(
        DeleteAccountRequest request,
        CancellationToken cancellationToken)
    {
        var userId = this.GetUserIdUnverified();

        if (userId is null) return Unauthorized();

        var result = await deleteAccount.ExecuteAsync(userId.Value, request.CurrentPassword, cancellationToken);

        if (!result.Succeeded)
            return BadRequest(new ProblemDetails { Title = "That password does not match." });

        // The session is gone with the row; this is the browser's copy.
        AuthCookies.Clear(Response);

        return NoContent();
    }

    [HttpGet("me")]
    [AllowAnonymous]
    [ProducesResponseType<MeResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MeResponse>> Me(CancellationToken cancellationToken) =>
        // Unverified on purpose: "who am I" is exactly the question an unverified user's client
        // needs answered, since the reply is what tells it to show the verification banner.
        await getCurrentUser.Execute(this.GetUserIdUnverified(), cancellationToken);

    [HttpGet("providers")]
    [AllowAnonymous]
    [ProducesResponseType<ExternalProvidersResponse>(StatusCodes.Status200OK)]
    public ActionResult<ExternalProvidersResponse> Providers() =>
        new ExternalProvidersResponse { Google = googleOptions.Value.IsConfigured };

    /// <summary>
    /// Hands the browser off to Google. Hidden from the OpenAPI document because it is a navigation
    /// target, not something the generated client should ever call over XHR — an OAuth redirect
    /// cannot be followed by fetch.
    /// </summary>
    [HttpGet("external/google/start")]
    [AllowAnonymous]
    [ApiExplorerSettings(IgnoreApi = true)]
    public IActionResult StartGoogle(string? returnUrl)
    {
        if (!googleOptions.Value.IsConfigured)
            return NotFound();

        var properties = new AuthenticationProperties
        {
            RedirectUri = Url.Action(nameof(GoogleCallback), new { returnUrl })
        };

        return Challenge(properties, GoogleDefaults.AuthenticationScheme);
    }

    [HttpGet("external/google/callback")]
    [AllowAnonymous]
    [ApiExplorerSettings(IgnoreApi = true)]
    public async Task<IActionResult> GoogleCallback(string? returnUrl, CancellationToken cancellationToken)
    {
        if (!googleOptions.Value.IsConfigured)
            return NotFound();

        var authentication = await HttpContext.AuthenticateAsync(ServiceCollectionExtensions.ExternalScheme);

        if (!authentication.Succeeded)
            return RedirectToLogin("google_failed");

        var info = ReadGoogleClaims(authentication.Principal);

        if (info is null)
            return RedirectToLogin("google_failed");

        var result = await signInWithExternalLogin.Execute(info, cancellationToken);

        // The handshake cookie has done its job; leaving it set would keep a second, stale identity
        // on the browser for no reason.
        await HttpContext.SignOutAsync(ServiceCollectionExtensions.ExternalScheme);

        if (!result.Succeeded)
            return RedirectToLogin(result.Error == AuthError.ExternalEmailNotVerified
                ? "google_email_unverified"
                : "google_failed");

        AuthCookies.Set(
            Response,
            result.AccessToken!,
            result.RefreshToken!,
            jwtOptions.Value.RefreshTokenLifetime);

        // Only a local path. An unvalidated returnUrl here is an open redirect, and this endpoint is
        // reachable by anyone with a link.
        return Redirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : "/");
    }

    private static ExternalLoginInfo? ReadGoogleClaims(ClaimsPrincipal principal)
    {
        var providerKey = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var email = principal.FindFirstValue(ClaimTypes.Email);

        if (string.IsNullOrEmpty(providerKey) || string.IsNullOrEmpty(email))
            return null;

        return new ExternalLoginInfo
        {
            Provider = ExternalLoginProvider.Google,
            ProviderKey = providerKey,
            Email = email,
            EmailVerified = bool.TryParse(principal.FindFirstValue("email_verified"), out var verified) && verified,
            DisplayName = principal.FindFirstValue(ClaimTypes.Name),
            ProfilePictureUrl = principal.FindFirstValue("picture")
        };
    }

    private IActionResult RedirectToLogin(string error) => Redirect($"/login?error={error}");

    private ActionResult<MeResponse> SignInResponse(AuthResult result)
    {
        AuthCookies.Set(
            Response,
            result.AccessToken!,
            result.RefreshToken!,
            jwtOptions.Value.RefreshTokenLifetime);

        return result.Me!;
    }
}
