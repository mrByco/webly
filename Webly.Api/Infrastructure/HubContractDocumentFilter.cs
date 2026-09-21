using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;
using Webly.Api.Hubs;
using Webly.Services.DTO.Realtime;
using Webly.Services.Services.Realtime;

namespace Webly.Api.Infrastructure;

/// <summary>
/// Puts the hub's types into the OpenAPI document, so the client generates them instead of declaring them.
///
/// Swagger describes HTTP, and the realtime contract is not HTTP — so <c>RunEventEnvelope</c>, <c>RunEvent</c>
/// and the two enums were written out by hand in <c>realtime.service.ts</c>, with a comment saying why.
/// That is a copy of a contract, and the copy is the dangerous kind: <see cref="RunEventType"/> is a union the
/// client switches on, and adding a value to it on this side changes nothing on that side until somebody
/// remembers. A generated union makes the same change a compile error instead.
///
/// Nothing references these types from a controller, which is the whole problem and also the whole fix: a
/// document filter can add a schema the document does not otherwise need. <c>ignoreUnusedModels</c> is false
/// in <c>client/open-api-gen.json</c>, so the generator keeps them.
///
/// The enums cross as names rather than numbers — <c>JsonStringEnumConverter</c> on both the controllers and
/// the hub — and the schema generator is configured from the same options, so what it writes here is what the
/// hub sends.
/// </summary>
public class HubContractDocumentFilter : IDocumentFilter
{
    /// <summary>
    /// Everything the hub sends or returns. <see cref="RunStarted"/> and <see cref="RunSubscription"/> are the
    /// two return values; the rest arrive as events.
    /// </summary>
    private static readonly Type[] Contract =
    [
        typeof(RunEventEnvelope),
        typeof(RunEvent),
        typeof(RunEventType),
        typeof(RunKind),
        typeof(RunStarted),
        typeof(RunSubscription)
    ];

    public void Apply(OpenApiDocument document, DocumentFilterContext context)
    {
        foreach (var type in Contract)
            context.SchemaGenerator.GenerateSchema(type, context.SchemaRepository);
    }
}
