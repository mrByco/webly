using Webly.Services.Services.Email.Templates;

namespace Webly.Tests;

/// <summary>
/// The same cases as the client's <c>models/field-label.spec.ts</c>, because it is the same rule in two places: the
/// Messages screen tidies a field's name there, and the notification email tidies it here.
/// </summary>
public class FieldLabelTests
{
    [TestCase("name", "Name")]
    [TestCase("email", "Email")]
    [TestCase("message", "Message")]
    public void The_plain_names_the_template_posts_are_capitalised(string name, string label) =>
        Assert.That(FieldLabel.Of(name), Is.EqualTo(label));

    [TestCase("preferred-date")]
    [TestCase("preferred_date")]
    [TestCase("preferredDate")]
    public void Both_ways_a_field_name_joins_words_are_broken(string name) =>
        Assert.That(FieldLabel.Of(name), Is.EqualTo("Preferred date"));

    [TestCase("How can we help?")]
    [TestCase("Your name")]
    public void A_name_that_is_already_a_sentence_is_left_alone(string name) =>
        Assert.That(FieldLabel.Of(name), Is.EqualTo(name));

    [TestCase("qty", "Qty")]
    [TestCase("POSTCODE", "Postcode")]
    public void It_does_not_invent_meaning_it_cannot_have(string name, string label) =>
        Assert.That(FieldLabel.Of(name), Is.EqualTo(label));

    [TestCase("", "")]
    [TestCase("  ", "")]
    [TestCase("__", "__")]
    public void A_name_that_is_nothing_much_survives(string name, string label) =>
        Assert.That(FieldLabel.Of(name), Is.EqualTo(label));
}
