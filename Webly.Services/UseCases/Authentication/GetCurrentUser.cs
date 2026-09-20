using Webly.Data.Repositories.Users;
using Webly.Services.DTO.Authentication;
using Webly.Services.Services.Authentication;

namespace Webly.Services.UseCases.Authentication;

public class GetCurrentUser(
    IUserRepository userRepository,
    IAuthSessionService authSessionService)
{
    /// <summary>
    /// Describes the caller. A null id (nobody signed in) and an id whose row has since been
    /// deleted both answer <see cref="MeResponse.Anonymous"/> rather than failing: "you are not
    /// logged in" is a normal answer to this question.
    /// </summary>
    public async Task<MeResponse> Execute(int? userId, CancellationToken cancellationToken = default)
    {
        if (userId is null)
            return MeResponse.Anonymous;

        var user = await userRepository.FindByIdAsync(userId.Value, cancellationToken);

        return user is null ? MeResponse.Anonymous : authSessionService.Describe(user);
    }
}
