using Webly.Services.Services.Email;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Net.Http.Headers;

namespace Webly.Tests;

/// <summary>
/// Runs the real API in memory against the fixture's Postgres, with cookies handled by hand.
///
/// Manual cookie handling is the point: these tests exist to assert what the server sets and how it
/// behaves when a cookie is missing or stale, and an automatic cookie container would hide exactly
/// that.
/// </summary>
public abstract class AuthEndpointTestBase : PostgresTestBase
{
    private WebApplicationFactory<Program> _factory = null!;

    protected HttpClient Client { get; private set; } = null!;

    /// <summary>
    /// Captures what the running app tried to send. Swapped in for the real sender so a test can
    /// read the token out of the link, which is as close as a test gets to being the user who
    /// opened the email.
    /// </summary>
    protected FakeEmailSender Emails { get; } = new();

    /// <summary>
    /// The one address these tests treat as an administrator of the platform. Registering with it
    /// is how a test becomes an administrator, because admin-ness is configuration rather than a
    /// stored column, and configuration is fixed before the host exists.
    /// </summary>
    protected const string AdministratorEmail = "admin@example.com";

    [OneTimeSetUp]
    public void StartApi()
    {
        // Environment variables for these two, because Program.cs resolves the database before the
        // host is built: anything registered on the builder afterwards is already too late to be
        // seen.
        Environment.SetEnvironmentVariable("ConnectionStrings__WeblyDb", ConnectionString);
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder
                // The administrator list goes in last, and deliberately not in an environment
                // variable. Development loads user secrets, that provider is registered after the
                // environment one, and a developer with Administrators:Emails:0 of their own in
                // secrets silently replaced this and made the whole run administrator-less. This
                // source is appended after everything Program.cs sets up, so nothing on the
                // machine running the tests can outrank it.
                .ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["Administrators:Emails:0"] = AdministratorEmail,

                        // The three paths the app checks at boot. They default to relative, resolved against the
                        // working directory, which for the real app is the repository root and for a test host is
                        // the test's own bin directory — so without these the host refuses to start with a
                        // perfectly accurate message about a template that is not there. Absolute here rather
                        // than by chdir: a test run must not depend on where it was launched from.
                        ["Templates:SitePath"] = Path.Combine(RepositoryRoot, "templates", "next-site"),
                        ["Sandbox:Local:AgentPath"] =
                            Path.Combine(RepositoryRoot, "tools", "sandbox-agent", "index.js"),

                        // Somewhere disposable. These tests never create a site, but the directory is created at
                        // boot, and creating it inside the build output is how a stale one ends up committed.
                        ["Repositories:Root"] = Path.Combine(Path.GetTempPath(), $"webly-tests-{Guid.NewGuid():N}")
                    }))
                .ConfigureTestServices(services => services.AddSingleton<IEmailSender>(Emails)));

        Client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            // HTTPS because the pipeline redirects http, and the auth cookies are Secure.
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false,
            AllowAutoRedirect = false
        });
    }

    [OneTimeTearDown]
    public void StopApi()
    {
        Client.Dispose();
        _factory.Dispose();
        Environment.SetEnvironmentVariable("ConnectionStrings__WeblyDb", null);
    }

    /// <summary>
    /// The repository root, found by walking up from the test binaries until the solution file appears. The
    /// alternative is a relative path with a specific number of <c>..</c> in it, which is correct until somebody
    /// changes the target framework or the configuration and then silently points at nothing.
    /// </summary>
    private static string RepositoryRoot
    {
        get
        {
            var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);

            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Webly.slnx")))
                directory = directory.Parent;

            return directory?.FullName
                ?? throw new InvalidOperationException(
                    "Could not find Webly.slnx above the test directory, so the template and sandbox agent "
                    + "cannot be located. Run the tests from inside the repository.");
        }
    }

    protected static IReadOnlyList<string> SetCookies(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values) ? [.. values] : [];

    /// <summary>The value a response assigns to a cookie, or null if it assigns none.</summary>
    protected static string? CookieValue(HttpResponseMessage response, string name)
    {
        var header = SetCookies(response).FirstOrDefault(x => x.StartsWith($"{name}=", StringComparison.Ordinal));
        if (header is null)
            return null;

        var value = header[(name.Length + 1)..].Split(';')[0];

        return string.IsNullOrEmpty(value) ? null : value;
    }

    protected static string? CookieHeader(HttpResponseMessage response, string name) =>
        SetCookies(response).FirstOrDefault(x => x.StartsWith($"{name}=", StringComparison.Ordinal));

    protected static HttpRequestMessage Request(HttpMethod method, string url, params (string Name, string Value)[] cookies)
    {
        var request = new HttpRequestMessage(method, url);

        if (cookies.Length > 0)
            request.Headers.Add("Cookie", string.Join("; ", cookies.Select(c => $"{c.Name}={c.Value}")));

        return request;
    }

    /// <summary>
    /// The most recent token mailed to an address, taken from the link the app actually sent.
    /// </summary>
    protected string LatestTokenFor(string email)
    {
        var message = Emails.Sent.LastOrDefault(x => x.To == email)
            ?? throw new InvalidOperationException($"No email was sent to {email}.");

        return FakeEmailSender.TokenFromLink(message);
    }

    /// <summary>
    /// Resets the per-test state that lives in the running app rather than in the database.
    ///
    /// The token blacklist is keyed by user id, and truncating with RESTART IDENTITY hands out id 1
    /// again in every test — so without this, a revocation recorded for one test's user silently
    /// applies to the next test's unrelated one. Production never reuses ids; this is the price of
    /// resetting the database underneath a long-lived host.
    /// </summary>
    [SetUp]
    public void ClearHostState()
    {
        Emails.Sent.Clear();

        if (_factory.Services.GetRequiredService<IMemoryCache>() is MemoryCache cache)
            cache.Clear();
    }

    protected HttpContent Json(object body) =>
        new StringContent(
            System.Text.Json.JsonSerializer.Serialize(body),
            new MediaTypeHeaderValue("application/json"));
}
