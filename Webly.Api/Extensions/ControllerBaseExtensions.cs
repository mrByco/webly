using Webly.Api.Infrastructure;
using Webly.Services.Services.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.IdentityModel.JsonWebTokens;
using System.Security.Claims;

namespace Webly.Api.Extensions;

/// <summary>
/// How a controller learns who is calling.
///
/// The default accessors return a user <b>only if their email is verified</b>, and an endpoint that
/// is happy to serve an unverified caller has to say so by reaching for
/// <see cref="GetUserIdUnverified"/>. That direction matters: an authorization attribute has to be
/// remembered on every new endpoint, and the one that gets forgotten is the one that matters,
/// whereas nobody writes a controller without asking who is calling. The safe option is the one on
/// the path of least resistance.
/// </summary>
public static class ControllerBaseExtensions
{
    /// <summary>Any signed-in caller, verified or not, or null when signed out.</summary>
    public static int? GetUserIdUnverified(this ControllerBase controller) =>
        controller.User.GetUserIdUnverified();

    public static int? GetUserIdUnverified(this ClaimsPrincipal? principal)
    {
        var value = principal?.FindFirst(JwtTokenService.UserIdClaim)?.Value;

        return int.TryParse(value, out var id) ? id : null;
    }

    /// <summary>Whether the caller's token says their address is proven.</summary>
    public static bool IsEmailVerified(this ClaimsPrincipal? principal) =>
        bool.TryParse(principal?.FindFirst(JwtTokenService.EmailVerifiedClaim)?.Value, out var verified)
        && verified;

    /// <summary>
    /// The caller's id when signed in and verified; null when signed out.
    /// Throws <see cref="EmailNotVerifiedException"/> for a signed-in but unverified caller — that
    /// is a 403, not an anonymous request, and the two must not be confused.
    /// </summary>
    public static int? GetUserIdIfLoggedIn(this ControllerBase controller)
    {
        var userId = controller.GetUserIdUnverified();

        if (userId is null)
            return null;

        if (!controller.User.IsEmailVerified())
            throw new EmailNotVerifiedException();

        return userId;
    }

    /// <summary>
    /// The caller's id on an endpoint that requires a signed-in, verified user.
    ///
    /// Throws rather than returning null on purpose. A nullable return that a call site forgets to
    /// check silently loses the guarantee these accessors exist to provide; an exception cannot be
    /// ignored into a security hole.
    /// </summary>
    public static int GetUserId(this ControllerBase controller)
    {
        var userId = controller.GetUserIdUnverified()
            ?? throw new InvalidOperationException("No authenticated user on a request that requires one.");

        if (!controller.User.IsEmailVerified())
            throw new EmailNotVerifiedException();

        return userId;
    }

    /// <summary>
    /// The caller's id on a <b>hub</b> method that requires a signed-in, verified user.
    ///
    /// A hub method has a <see cref="ClaimsPrincipal"/> and no <c>ControllerBase</c>, so the gate the controllers get
    /// from <see cref="GetUserId(ControllerBase)"/> is restated here — once, next to it, rather than as a check each
    /// hub method remembers. Throws <see cref="HubException"/> rather than the controller exception: the middleware
    /// that turns <see cref="EmailNotVerifiedException"/> into a 403 is an HTTP pipeline, and a hub invocation never
    /// reaches it, so an unverified caller on the hub would otherwise get an unhandled server error.
    /// </summary>
    public static int GetUserIdVerified(this ClaimsPrincipal? principal)
    {
        var userId = principal.GetUserIdUnverified()
            ?? throw new HubException("You are not signed in.");

        if (!principal.IsEmailVerified())
            throw new HubException("Confirm your email address first.");

        return userId;
    }

    /// <summary>The session this caller's access token was minted under, or null for one that predates the claim.</summary>
    public static string? GetSessionId(this ControllerBase controller) =>
        controller.User.FindFirst(JwtTokenService.SessionIdClaim)?.Value;

    /// <summary>The current access token's id and expiry, for blacklisting it on logout.</summary>
    public static (string? TokenId, DateTimeOffset? ExpiresAt) GetAccessTokenIdentity(this ControllerBase controller)
    {
        var tokenId = controller.User.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
        var expiry = controller.User.FindFirst(JwtRegisteredClaimNames.Exp)?.Value;

        return long.TryParse(expiry, out var unixSeconds)
            ? (tokenId, DateTimeOffset.FromUnixTimeSeconds(unixSeconds))
            : (tokenId, null);
    }
}
