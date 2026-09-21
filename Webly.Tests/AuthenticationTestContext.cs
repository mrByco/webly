using Webly.Data;
using Webly.Data.Repositories.RefreshTokens;
using Webly.Data.Repositories.SecurityTokens;
using Webly.Data.Repositories.Users;
using Webly.Services.Services.Authentication;
using Webly.Services.Services;
using Webly.Services.Services.Email;
using Webly.Services.UseCases.Authentication;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Webly.Tests;

/// <summary>
/// Wires the authentication use cases by hand around one <see cref="WeblyDbContext"/>, so a test
/// exercises the real classes against a real database without standing up a web host. Sharing the
/// single context is the point — repositories and use cases must see each other's pending changes.
/// </summary>
public sealed class AuthenticationTestContext(WeblyDbContext dbContext) : IDisposable
{
    public static readonly JwtOptions Options = new()
    {
        Issuer = "webly-tests",
        Audience = "webly-tests",
        Key = new string('k', 64),
        RefreshKey = new string('r', 64)
    };

    public static readonly EmailTokenOptions EmailTokens = new()
    {
        Key = new string('e', 64),

        // No cooldown by default: it is a real behaviour with its own test, but leaving it on would
        // make every other test that sends two emails fail for an unrelated reason.
        ResendCooldown = TimeSpan.Zero
    };

    private static readonly Microsoft.Extensions.Options.IOptions<AppOptions> App =
        Microsoft.Extensions.Options.Options.Create(new AppOptions { BaseUrl = "https://localhost:5000" });

    public WeblyDbContext Db { get; } = dbContext;

    public ITokenService TokenService { get; } = new JwtTokenService(Microsoft.Extensions.Options.Options.Create(Options));

    public FakeEmailSender Emails { get; } = new();

    public IUserRepository Users { get; } = new UserRepository(dbContext);
    public ISecurityTokenRepository SecurityTokens { get; } = new SecurityTokenRepository(dbContext);

    public ISecurityTokenService SecurityTokenService { get; } =
        new SecurityTokenService(Microsoft.Extensions.Options.Options.Create(EmailTokens));
    public IRefreshTokenRepository RefreshTokens { get; } = new RefreshTokenRepository(dbContext);
    public IPasswordHasher PasswordHasher { get; } = new BcryptPasswordHasher();

    public IAccessTokenBlacklist AccessTokenBlacklist { get; } =
        new MemoryCacheAccessTokenBlacklist(new MemoryCache(new MemoryCacheOptions()));

    /// <summary>
    /// Addresses that maintain the global catalog. Empty by default, so these tests describe a
    /// plain user unless one of them says otherwise.
    /// </summary>
    public List<string> AdministratorEmails { get; } = [];

    public IAdminPolicy AdminPolicy => new ConfiguredAdminPolicy(
        Users,
        Microsoft.Extensions.Options.Options.Create(new AdministratorOptions { Emails = AdministratorEmails }));

    private IAuthSessionService Sessions =>
        new AuthSessionService(TokenService, RefreshTokens, AdminPolicy, Db);

    private TimeSpan _resendCooldown = TimeSpan.Zero;

    private Microsoft.Extensions.Options.IOptions<EmailTokenOptions> TokenOptions =>
        Microsoft.Extensions.Options.Options.Create(new EmailTokenOptions
        {
            Key = EmailTokens.Key,
            VerificationLifetime = EmailTokens.VerificationLifetime,
            PasswordResetLifetime = EmailTokens.PasswordResetLifetime,
            ResendCooldown = _resendCooldown,
            MaxCodeAttempts = EmailTokens.MaxCodeAttempts
        });

    private Microsoft.Extensions.Options.IOptions<JwtOptions> JwtOptionsAccessor =>
        Microsoft.Extensions.Options.Options.Create(Options);

    public RegisterUser RegisterUser => new(Users, PasswordHasher, Sessions, SendEmailVerification, Db);
    public SignInWithPassword SignInWithPassword => new(Users, PasswordHasher, Sessions);
    public SignInWithExternalLogin SignInWithExternalLogin =>
        new(Users, RefreshTokens, Sessions, Emails, Db);

    public SendEmailVerification SendEmailVerification =>
        new(SecurityTokenService, SecurityTokens, Emails, App, TokenOptions, Db);

    public IEmailVerificationService EmailVerification =>
        new EmailVerificationService(AccessTokenBlacklist, Sessions, JwtOptionsAccessor, Db);

    public VerifyEmail VerifyEmail =>
        new(SecurityTokenService, SecurityTokens, EmailVerification);

    public VerifyEmailWithCode VerifyEmailWithCode =>
        new(SecurityTokenService, SecurityTokens, EmailVerification, TokenOptions, Db);

    public RequestPasswordReset RequestPasswordReset =>
        new(Users, SecurityTokenService, SecurityTokens, Emails, App, TokenOptions, Db);

    public ResetPassword ResetPassword =>
        new(SecurityTokenService, SecurityTokens, RefreshTokens, PasswordHasher, Sessions, Emails, Db);

    public ChangePassword ChangePassword =>
        new(Users, RefreshTokens, PasswordHasher, Sessions, Emails, Db);
    public RotateRefreshToken RotateRefreshToken => new(TokenService, RefreshTokens, Sessions, Db);
    public SignOut SignOut => new(TokenService, RefreshTokens, AccessTokenBlacklist);
    public GetCurrentUser GetCurrentUser => new(Users, Sessions);

    /// <summary>
    /// Turns the resend cooldown on for a test that is specifically about it. Off by default, so
    /// the many tests that legitimately send two emails in a row are not tripped by it.
    /// </summary>
    public void WithResendCooldown(TimeSpan cooldown) => _resendCooldown = cooldown;

    public void Dispose() => Db.Dispose();
}
