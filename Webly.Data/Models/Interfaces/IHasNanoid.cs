namespace Webly.Data.Models.Interfaces;

/// <summary>
/// An entity that is addressed by a nanoid outside <c>Webly.Data</c>.
///
/// Integer <c>Id</c>s stay internal to the data layer; every API surface, URL and generated client
/// type refers to entities by <see cref="Nanoid"/>. <see cref="WeblyDbContext"/> generates one on
/// insert when it is missing, so no write site has to remember to — a row that reaches the database
/// without a public id is unaddressable, and that is not something a caller should be able to cause.
/// </summary>
public interface IHasNanoid
{
    string Nanoid { get; set; }
}
