namespace Webly.Services.Services.Deployments;

/// <summary>
/// How Webly reaches its deployment provider. Validated at startup like every other options class, so a
/// deployment that cannot possibly work is a crash on boot rather than a failure the first time a customer
/// presses publish.
/// </summary>
public class DeploymentOptions
{
    public const string SectionName = "Deployment";

    public VercelOptions Vercel { get; set; } = new();

    public class VercelOptions
    {
        /// <summary>
        /// One platform-owned token, not the customer's. "Without touching a single line of code" cannot
        /// begin with "create a Vercel account and generate an API token" — so Webly owns the account and
        /// the customer never hears the provider's name. See CLAUDE.md "Deploying" for the
        /// bring-your-own-account upgrade, which is one more nullable column and this interface unchanged.
        /// </summary>
        /// <summary>
        /// Deliberately not <c>[Required]</c>: <b>no token is a valid configuration</b>, and a fresh clone has to run
        /// without one. Publishing is then unavailable — <c>PublishSite</c> says so in a sentence and the client
        /// disables the button — while editing, versioning and everything else works. The alternative, failing at
        /// startup, means nobody can look at the product without a Vercel account, which is the same mistake the
        /// product itself is built to avoid.
        /// </summary>
        public string Token { get; set; } = string.Empty;

        public bool IsConfigured => !string.IsNullOrWhiteSpace(Token);

        /// <summary>Set when the token belongs to a team rather than a personal account; every call then
        /// carries it as a query parameter. Empty is a valid, complete configuration.</summary>
        public string TeamId { get; set; } = string.Empty;

        /// <summary>
        /// Prefix for provider-side project names, so that one Vercel account can host more than one Webly
        /// environment without staging deploys landing on production projects.
        /// </summary>
        public string ProjectPrefix { get; set; } = "webly";
    }
}
