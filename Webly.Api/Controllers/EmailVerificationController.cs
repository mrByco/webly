using Webly.Api.Extensions;
using Webly.Api.Infrastructure;
using Webly.Data.Repositories.Users;
using Webly.Services.DTO.Authentication;
using Webly.Services.Services.Authentication;
using Webly.Services.UseCases.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Webly.Api.Controllers;

[ApiController]
[Route("api/auth/email")]
public class EmailVerificationController(
    VerifyEmail verifyEmail,
    VerifyEmailWithCode verifyEmailWithCode,
    SendEmailVerification sendEmailVerification,
    IUserRepository userRepository,
    IOptions<JwtOptions> jwtOptions) : ControllerBase
{
    /// <summary>
    /// Consumes a verification token. Anonymous, because the link may well be opened in a browser
    /// that has never signed in — a different device from the one that registered.
    /// </summary>
    [HttpPost("verify")]
    [AllowAnonymous]
    [ProducesResponseType<MeResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MeResponse>> Verify(VerifyEmailRequest request, CancellationToken cancellationToken)
    {
        var result = await verifyEmail.Execute(request.Token, cancellationToken);

        if (!result.Succeeded)
            return BadRequest(new ProblemDetails { Title = "That confirmation link is not valid any more." });

        return SignedIn(result);
    }

    /// <summary>
    /// Verifies from the code typed off the email. Signed-in callers only, deliberately: the code is
    /// looked up by owner, which is what stops six digits being guessed against every account at
    /// once. <c>GetUserIdUnverified</c> for the obvious reason — nobody reaching here is verified yet.
    /// </summary>
    [HttpPost("verify-code")]
    [Authorize]
    [ProducesResponseType<MeResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MeResponse>> VerifyCode(
        VerifyEmailCodeRequest request,
        CancellationToken cancellationToken)
    {
        var userId = this.GetUserIdUnverified();

        if (userId is null)
            return Unauthorized();

        var result = await verifyEmailWithCode.Execute(userId.Value, request.Code, cancellationToken);

        // One answer for a wrong code, an expired one and one that has run out of attempts. Which of
        // the three it is would tell a guesser how close they are.
        if (!result.Succeeded)
            return BadRequest(new ProblemDetails { Title = "That code is wrong or has expired. Ask for a new one." });

        return SignedIn(result);
    }

    /// <summary>
    /// Sends a fresh verification link. Deliberately uses <c>GetUserIdUnverified</c> — requiring a
    /// verified address to ask for a verification email would be a closed loop.
    /// </summary>
    [HttpPost("resend")]
    [Authorize]
    [EnableRateLimiting(RateLimitPolicies.Mail)]
    public async Task<IActionResult> Resend(CancellationToken cancellationToken)
    {
        var userId = this.GetUserIdUnverified();
        var user = userId is null ? null : await userRepository.FindByIdAsync(userId.Value, cancellationToken);

        if (user is not null)
            await sendEmailVerification.Execute(user, cancellationToken);

        // Always no-content, whether we sent, skipped for the cooldown, or the address was already
        // verified. Nothing here is worth telling an attacker apart.
        return NoContent();
    }

    /// <summary>
    /// Signs the browser that proved the address in, and refreshes the cookies of one that was
    /// already signed in so its token stops claiming the address is unverified.
    /// </summary>
    private MeResponse SignedIn(AuthResult result)
    {
        AuthCookies.Set(
            Response,
            result.AccessToken!,
            result.RefreshToken!,
            jwtOptions.Value.RefreshTokenLifetime);

        return result.Me!;
    }
}
