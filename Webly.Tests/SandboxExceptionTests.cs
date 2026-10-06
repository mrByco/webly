using Webly.Services.Services.Sandboxes;

namespace Webly.Tests;

/// <summary>
/// A sandbox failure has two audiences, and each half of the exception is for one of them: the message for the person
/// in the chat, the detail for whoever reads the log. Every place that logs one logs the exception, so the detail has
/// to travel inside it.
/// </summary>
public class SandboxExceptionTests
{
    [Test]
    public void A_logged_failure_carries_its_cause_and_the_screen_does_not()
    {
        var exception = new SandboxException(
            "The editing agent could not finish.",
            "opencode exited 1: You have no credits remaining.");

        Assert.That(exception.ToString(), Does.Contain("You have no credits remaining."));
        Assert.That(exception.Message, Is.EqualTo("The editing agent could not finish."));
    }

    [Test]
    public void Only_the_end_of_a_long_detail_is_kept_because_that_is_where_a_command_says_why()
    {
        var text = new SandboxException("It failed.", new string('x', 50_000) + " because of this").ToString();

        Assert.That(text, Does.EndWith(" because of this"));
        Assert.That(text.Length, Is.LessThan(5_000));
    }

    [Test]
    public void Without_a_detail_it_is_an_ordinary_exception()
    {
        var exception = new SandboxException("It failed.");

        Assert.That(exception.ToString(), Does.Not.Contain("Detail:"));
    }
}
