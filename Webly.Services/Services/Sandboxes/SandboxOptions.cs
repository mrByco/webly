namespace Webly.Services.Services.Sandboxes;

/// <summary>How sandboxes are started, and how long they are allowed to cost money.</summary>
public class SandboxOptions
{
    public const string SectionName = "Sandbox";

    /// <summary>
    /// Which provider to use: <c>e2b</c> in production, <c>local</c> or <c>docker</c> in development.
    ///
    /// The default is <c>local</c>, which needs nothing but node — not because it is the best provider but
    /// because it is the one a fresh clone definitely has. <c>docker</c> is the same thing with real
    /// isolation, for anyone who has a daemon and wants it; <c>e2b</c> is what production uses, and selecting
    /// <c>local</c> outside Development is refused at startup.
    /// </summary>
    public string Provider { get; set; } = "local";

    /// <summary>
    /// The image (or E2B template) holding node, git, tar, the agent CLIs and the sandbox agent — with the
    /// starter template's dependencies already installed, which is what makes a cold workspace's dev server
    /// start in seconds instead of after an npm install.
    /// </summary>
    public string Image { get; set; } = "byc0/webly-sandbox:latest";

    /// <summary>
    /// How long a workspace stays warm with nobody talking to it. Sandboxes bill by the second, so this is
    /// the single number that decides what an idle editor costs; the reaper enforces it.
    /// </summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>The hard ceiling on one sandbox, whatever it is doing. A runaway build is not free.</summary>
    public TimeSpan MaxLifetime { get; set; } = TimeSpan.FromHours(2);

    /// <summary>How long to wait for a freshly started sandbox's agent to answer.</summary>
    public TimeSpan StartupTimeout { get; set; } = TimeSpan.FromSeconds(90);

    public E2bOptions E2b { get; set; } = new();

    public class E2bOptions
    {
        public string ApiKey { get; set; } = string.Empty;

        /// <summary>The template built from <c>deploy/sandbox/Dockerfile</c>.</summary>
        public string TemplateId { get; set; } = "webly-sandbox";

        public string BaseUrl { get; set; } = "https://api.e2b.dev";

        /// <summary>
        /// The hostname pattern for a sandbox's exposed port. A setting rather than a constant because it is
        /// the provider's routing convention, and a convention is exactly the kind of thing that changes
        /// without our code changing. <c>{port}</c> and <c>{id}</c> are substituted.
        /// </summary>
        public string PortHostPattern { get; set; } = "https://{port}-{id}.e2b.app";

        public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
    }
}
