using Microsoft.Extensions.DependencyInjection;
using Webly.Data.Repositories.Chat;
using Webly.Data.Repositories.Deployments;
using Webly.Data.Repositories.Domains;
using Webly.Data.Repositories.RefreshTokens;
using Webly.Data.Repositories.SecurityTokens;
using Webly.Data.Repositories.Sites;
using Webly.Data.Repositories.Users;
using Webly.Services.Agent;
using Webly.Services.Agent.Tools;
using Webly.Services.Services.Authentication;
using Webly.Services.Services.Deployments;
using Webly.Services.Services.Realtime;
using Webly.Services.Services.Rendering;
using Webly.Services.Services.Rendering.Sections;
using Webly.Services.UseCases.Authentication;
using Webly.Services.UseCases.Chat;
using Webly.Services.UseCases.Deployments;
using Webly.Services.UseCases.Domains;
using Webly.Services.UseCases.Sites;

namespace Webly.Services;

/// <summary>
/// Registers everything in this assembly and the data layer below it, so <c>Program.cs</c> does not need to know the
/// shape of either.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddWeblyServices(this IServiceCollection services)
    {
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<ISecurityTokenRepository, SecurityTokenRepository>();
        services.AddScoped<ISiteRepository, SiteRepository>();
        services.AddScoped<ISiteVersionRepository, SiteVersionRepository>();
        services.AddScoped<IDomainRepository, DomainRepository>();
        services.AddScoped<IDeploymentRepository, DeploymentRepository>();
        services.AddScoped<IConversationRepository, ConversationRepository>();

        services.AddMemoryCache();

        services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();
        services.AddSingleton<IAccessTokenBlacklist, MemoryCacheAccessTokenBlacklist>();
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddSingleton<ISecurityTokenService, SecurityTokenService>();
        services.AddScoped<IAdminPolicy, ConfiguredAdminPolicy>();
        services.AddScoped<IAuthSessionService, AuthSessionService>();
        services.AddScoped<IEmailVerificationService, EmailVerificationService>();

        services.AddScoped<RegisterUser>();
        services.AddScoped<SignInWithPassword>();
        services.AddScoped<SignInWithExternalLogin>();
        services.AddScoped<RotateRefreshToken>();
        services.AddScoped<SignOut>();
        services.AddScoped<GetCurrentUser>();
        services.AddScoped<SendEmailVerification>();
        services.AddScoped<VerifyEmail>();
        services.AddScoped<VerifyEmailWithCode>();
        services.AddScoped<RequestPasswordReset>();
        services.AddScoped<ResetPassword>();
        services.AddScoped<ChangePassword>();

        services.AddScoped<SiteMapper>();
        services.AddScoped<CommitSiteVersion>();
        services.AddScoped<CreateSite>();
        services.AddScoped<ListSites>();
        services.AddScoped<GetSite>();
        services.AddScoped<RenameSite>();
        services.AddScoped<SwitchCurrentSite>();
        services.AddScoped<DeleteSite>();
        services.AddScoped<ListSiteVersions>();
        services.AddScoped<GetSiteVersion>();
        services.AddScoped<RestoreSiteVersion>();
        services.AddScoped<EditSection>();
        services.AddScoped<PreviewSite>();

        services.AddScoped<AddDomain>();
        services.AddScoped<ListDomains>();
        services.AddScoped<CheckDomain>();
        services.AddScoped<SetPrimaryDomain>();
        services.AddScoped<RemoveDomain>();

        services.AddScoped<PublishSite>();
        services.AddScoped<ListDeployments>();

        services.AddScoped<GetChat>();
        services.AddScoped<ArchiveChat>();

        // One renderer per section type, discovered as a set. A missing one is caught by
        // SectionCatalogueTests.Every_section_type_has_a_schema_and_a_renderer rather than by a blank band on a
        // published page.
        services.AddSingleton<ISectionRenderer, HeroSectionRenderer>();
        services.AddSingleton<ISectionRenderer, RichTextSectionRenderer>();
        services.AddSingleton<ISectionRenderer, FeatureGridSectionRenderer>();
        services.AddSingleton<ISectionRenderer, TestimonialsSectionRenderer>();
        services.AddSingleton<ISectionRenderer, FaqSectionRenderer>();
        services.AddSingleton<ISectionRenderer, CtaSectionRenderer>();
        services.AddSingleton<ISiteRenderer, HtmlSiteRenderer>();

        return services;
    }

    /// <summary>
    /// The run substrate. Separate from <see cref="AddWeblyServices"/> because a test that only exercises use cases has
    /// no reason to start a background reaper, and because the publisher it depends on lives in the API layer.
    ///
    /// Note the lifetimes: the registry and the launcher are singletons (a run outlives the request that started it),
    /// while the writer and the per-run context are scoped — a run creates its own scope, which is what makes
    /// "one writer per run" true rather than hopeful.
    /// </summary>
    public static IServiceCollection AddWeblyRealtime(this IServiceCollection services)
    {
        services.AddSingleton<RunRegistry>();
        services.AddSingleton<AgentBudget>();
        services.AddSingleton<IChatRunLauncher, ChatRunLauncher>();
        services.AddScoped<IRunEventSink, RunEventSink>();
        services.AddScoped<RunWriter>();
        services.AddHostedService<OrphanRunReaper>();
        services.AddHostedService<DeploymentJobRunner>();

        return services;
    }

    /// <summary>
    /// The editor agent and the scoped objects its tools read. Registered only when a provider key exists, so that a
    /// deployment with no AI credentials still runs — see <see cref="AgentOptions"/>.
    /// </summary>
    public static IServiceCollection AddWeblyAgent(this IServiceCollection services)
    {
        services.AddScoped<AgentRunContext>();
        services.AddScoped<SiteEditSession>();
        services.AddScoped<SiteToolkit>();
        services.AddScoped<QuestionToolkit>();
        services.AddScoped<IAgentTurnService, AgentTurnService>();

        return services;
    }
}
