using Webly.Api.Infrastructure;
using Npgsql;

namespace Webly.Tests;

public class ConnectionPoolTests
{
    private const string Plain = "Host=db;Port=5432;Database=webly;Username=webly;Password=s3cret";

    [Test]
    public void A_connection_string_that_says_nothing_gets_a_pool_the_server_can_hold()
    {
        var capped = new NpgsqlConnectionStringBuilder(ConnectionPool.Capped(Plain));

        Assert.That(capped.MaxPoolSize, Is.EqualTo(ConnectionPool.DefaultMaxSize));
        Assert.That(capped.Host, Is.EqualTo("db"));
        Assert.That(capped.Password, Is.EqualTo("s3cret"), "everything else is left as it was");
    }

    [TestCase("Maximum Pool Size=100")]
    [TestCase("maxpoolsize=100")]
    public void A_size_somebody_chose_is_left_alone(string choice)
    {
        // A hundred on purpose: the default, so this fails if the check reads Npgsql's answer rather than the string.
        var given = $"{Plain};{choice}";

        Assert.That(new NpgsqlConnectionStringBuilder(ConnectionPool.Capped(given)).MaxPoolSize, Is.EqualTo(100));
    }
}
