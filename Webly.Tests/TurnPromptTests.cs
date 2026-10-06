using Webly.Services.Agent;

namespace Webly.Tests;

/// <summary>
/// The line every turn ends with, asked for and split off in one place for both agents.
/// </summary>
public class TurnPromptTests
{
    [Test]
    public void The_summary_line_comes_off_the_reply()
    {
        var (reply, summary) = TurnPrompt.Split(
            "I added your opening hours.\n\nSUMMARY: Added opening hours to the home page\n",
            fallbackSummary: "Put our hours on the site");

        Assert.Multiple(() =>
        {
            Assert.That(reply, Is.EqualTo("I added your opening hours."));
            Assert.That(summary, Is.EqualTo("Added opening hours to the home page"));
        });
    }

    [Test]
    public void Without_a_usable_summary_the_request_is_the_subject()
    {
        Assert.Multiple(() =>
        {
            Assert.That(TurnPrompt.Split("Which days are you open?", "Put our hours on the site").Summary,
                Is.EqualTo("Put our hours on the site"));
            Assert.That(TurnPrompt.Split("Nothing to change.\nSUMMARY: none", "Check the footer").Summary,
                Is.EqualTo("Check the footer"));
        });
    }

    [Test]
    public void A_long_summary_is_cut_where_a_history_list_wraps()
    {
        var (_, summary) = TurnPrompt.Split($"Done.\nSUMMARY: {new string('x', 120)}", "fallback");

        Assert.That(summary, Has.Length.EqualTo(70).And.EndsWith("..."));
    }

    [Test]
    public void The_prompt_carries_the_history_the_message_and_an_example_of_a_good_summary()
    {
        var prompt = TurnPrompt.Build(new CodingAgentRequest(
            "Add our opening hours", ["They asked: Say who we are"], SessionId: null));

        Assert.Multiple(() =>
        {
            Assert.That(prompt, Does.Contain("- They asked: Say who we are"));
            Assert.That(prompt, Does.Contain("Add our opening hours"));
            Assert.That(prompt, Does.Contain("SUMMARY:"));
            Assert.That(prompt, Does.Contain("not \"Updated the home page\""), "a vague subject is named as the thing to avoid");
        });
    }
}
