using Microsoft.Extensions.DependencyInjection;
using Webly.Data.Repositories.Chat;
using Webly.Data.Repositories.Deployments;
using Webly.Data.Repositories.Domains;
using Webly.Data.Repositories.RefreshTokens;
using Webly.Data.Repositories.SecurityTokens;
using Webly.Data.Repositories.Forms;
using Webly.Data.Repositories.Sites;
using Webly.Data.Repositories.Users;
using Webly.Services.Agent;
using Webly.Services.Agent.Agents;
using Webly.Services.Services.Authentication;
using Webly.Services.Services.Realtime;
using Webly.Services.Services.Repositories;
using Webly.Services.Services.Workspaces;
using Webly.Services.UseCases.Authentication;
using Webly.Services.UseCases.Chat;
using Webly.Services.UseCases.Deployments;
using Webly.Services.UseCases.Domains;
using Webly.Services.UseCases.Assets;
using Webly.Services.UseCases.Forms;
using Webly.Services.UseCases.Sites;

namespace Webly.Services;

/// <summary>
/// Registers everything in this assembly and the data layer below it, so <c>Program.cs</c> does not need to know
/// the shape of either.
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
        services.AddScoped<IFormSubmissionRepository, FormSubmissionRepository>();

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
        services.AddScoped<DeleteAccount>();

        // A site's source: one bare git repository each, and the starter project every one begins as.
        services.AddSingleton<ISiteRepositoryStore, GitSiteRepositoryStore>();
        services.AddSingleton<ISiteTemplateSource, DirectorySiteTemplateSource>();

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
        services.AddScoped<ReadSiteFiles>();
        services.AddScoped<ExportSite>();
        services.AddScoped<WakeSiteWorkspace>();

        services.AddScoped<AddDomain>();
        services.AddScoped<ListDomains>();
        services.AddScoped<CheckDomain>();
        services.AddScoped<SetPrimaryDomain>();
        services.AddScoped<RemoveDomain>();

        services.AddScoped<PublishSite>();
        services.AddScoped<ListDeployments>();

        services.AddScoped<GetChat>();
        services.AddScoped<ArchiveChat>();

        services.AddScoped<UploadSiteImages>();
        services.AddScoped<ListSiteImages>();

        services.AddScoped<SubmitForm>();
        services.AddScoped<ListFormSubmissions>();

        return services;
    }

    /// <summary>
    /// The run substrate and the workspaces.
    ///
    /// Note the lifetimes. The registries and the launcher are singletons, because a run and a sandbox both
    /// outlive the request that started them — and because a workspace belongs to the instance that started it.
    /// <see cref="RunWriter"/> is scoped, because a run creates its own scope, which is what makes "one writer per
    /// run" true rather than hopeful.
    /// </summary>
    public static IServiceCollection AddWeblyRealtime(this IServiceCollection services)
    {
        services.AddSingleton<RunRegistry>();
        services.AddSingleton<AgentBudget>();
        services.AddSingleton<IChatRunLauncher, ChatRunLauncher>();
        services.AddSingleton<ISiteWorkspaceRegistry, SiteWorkspaceRegistry>();
        services.AddScoped<IRunEventSink, RunEventSink>();
        services.AddScoped<RunWriter>();

        services.AddHostedService<OrphanRunReaper>();
        services.AddHostedService<WorkspaceReaper>();
        services.AddHostedService<Services.Deployments.DeploymentJobRunner>();

        return services;
    }

    /// <summary>
    /// The coding agents. Both are registered whether or not they are configured — the registry asks each one
    /// whether it can actually run, so "which agents does this deployment have" has one answer in one place.
    /// </summary>
    public static IServiceCollection AddWeblyAgents(this IServiceCollection services)
    {
        services.AddSingleton<ICodingAgent, ClaudeCodeAgent>();
        services.AddSingleton<ICodingAgent, OpenCodeAgent>();

        // The keyless one. Registered unconditionally like the others and reporting itself unconfigured
        // unless Agent:Mock:Enabled is set, so "which agents does this deployment have" stays one question
        // with one answer in CodingAgentRegistry.
        services.AddSingleton<ICodingAgent, MockCodingAgent>();
        services.AddSingleton<CodingAgentRegistry>();
        services.AddScoped<IAgentTurnService, AgentTurnService>();

        return services;
    }
}
