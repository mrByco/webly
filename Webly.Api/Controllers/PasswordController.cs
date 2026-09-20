using Webly.Api.Extensions;
using Webly.Api.Infrastructure;
using Webly.Services.DTO.Authentication;
using Webly.Services.Services.Authentication;
using Webly.Services.UseCases.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Webly.Api.Controllers;

[ApiController]
[Route("api/auth/password")]
public class PasswordController(
    RequestPasswordReset requestPasswordReset,
    ResetPassword resetPassword,
    ChangePassword changePassword,
    IOptions<JwtOptions> jwtOptions) : ControllerBase
{
    /// <summary>
    /// Starts a reset. Always 204, whether or not the address is registered — anything else turns
    /// this into a way to find out who has an account here.
    /// </summary>
    [HttpPost("forgot")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Mail)]
    public async Task<IActionResult> Forgot(ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        await requestPasswordReset.Execute(request.Email, cancellationToken);

        return NoContent();
    }

    [HttpPost("reset")]
    [AllowAnonymous]
    [ProducesResponseType<MeResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MeResponse>> Reset(ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        var result = await resetPassword.Execute(request.Token, request.NewPassword, cancellationToken);

        if (!result.Succeeded)
            return BadRequest(new ProblemDetails { Title = "That link is not valid any more. Ask for a new one." });

        return SignedIn(result);
    }

    /// <summary>
    /// Changes the password, or sets the first one on a Google-created account.
    ///
    /// Uses <c>GetUserIdUnverified</c>: a Google account is verified anyway, and a password-holding
    /// user who has not verified should still be able to change their password — locking that behind
    /// verification would strand exactly the person trying to secure their account.
    /// </summary>
    [HttpPost("change")]
    [Authorize]
    [ProducesResponseType<MeResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MeResponse>> Change(ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        var userId = this.GetUserIdUnverified();

        if (userId is null)
            return Unauthorized();

        var result = await changePassword.Execute(
            userId.Value, request.CurrentPassword, request.NewPassword, cancellationToken);

        if (!result.Succeeded)
        {
            return result.Error == AuthError.EmailNotVerified
                ? BadRequest(new ProblemDetails { Title = "Confirm your email address first." })
                : BadRequest(new ProblemDetails { Title = "That is not your current password." });
        }

        return SignedIn(result);
    }

    private ActionResult<MeResponse> SignedIn(AuthResult result)
    {
        // Every other session was just revoked, so the caller needs the new cookies or they would
        // have logged themselves out by changing their own password.
        AuthCookies.Set(
            Response,
            result.AccessToken!,
            result.RefreshToken!,
            jwtOptions.Value.RefreshTokenLifetime);

        return result.Me!;
    }
}
