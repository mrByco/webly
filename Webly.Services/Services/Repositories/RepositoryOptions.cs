using System.ComponentModel.DataAnnotations;

namespace Webly.Services.Services.Repositories;

/// <summary>Where the bare repositories live, and how large a site may get.</summary>
public class RepositoryOptions
{
    public const string SectionName = "Repositories";

    /// <summary>
    /// The directory holding one bare repository per site. A volume in production (see
    /// deploy/portainer-stack.yml) and <c>.run/repositories</c> in development. Required, because a site's
    /// source has nowhere else to be.
    /// </summary>
    [Required]
    public string Root { get; set; } = string.Empty;

    /// <summary>
    /// The most a working tree may weigh, in bytes. Checked when a sandbox hands one back, because that is
    /// the moment an agent that decided to commit <c>node_modules</c> or a 40 MB video would otherwise put
    /// it in somebody's git history for ever.
    /// </summary>
    public long MaxTreeBytes { get; set; } = 20 * 1024 * 1024;

    /// <summary>The most files a tree may hold, for the same reason.</summary>
    public int MaxTreeFiles { get; set; } = 2_000;

    /// <summary>How long a git invocation may take before it is killed.</summary>
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(30);
}
