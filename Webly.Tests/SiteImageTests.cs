using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Webly.Api.Infrastructure;
using Webly.Services.UseCases.Assets;

namespace Webly.Tests;

/// <summary>
/// Uploading a photograph, which in this product means committing it: an image is a version, in the site's own
/// repository under <c>public/images</c>, and not a row in an asset table beside it.
///
/// So what these assert is mostly about the repository rather than about a file store — that the bytes come
/// back out of the commit, that the site's own URL for them is right, that two photographs called the same
/// thing do not overwrite each other, and that what decides the extension is the file's first few bytes and
/// never what the uploader called it.
/// </summary>
public class SiteImageTests : AuthEndpointTestBase
{
    private const string Password = "hunyadi-matyas-1458";

    /// <summary>The smallest valid GIF there is: 35 bytes, one transparent pixel. Real bytes, so the sniffing
    /// it goes through is the real thing rather than a header this test made up.</summary>
    private static byte[] Gif() => Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7");

    /// <summary>A one-pixel PNG, for the case where two files disagree about what they are called.</summary>
    private static byte[] Png() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private async Task<string> AccountAsync(string email)
    {
        await Client.PostAsync("/api/auth/register",
            Json(new { email, password = Password, displayName = email.Split('@')[0] }));

        var verified = await Client.PostAsync("/api/auth/email/verify", Json(new { token = LatestTokenFor(email) }));

        return CookieValue(verified, AuthCookies.AccessTokenName)!;
    }

    private async Task<string> SiteAsync(string access, string name)
    {
        var request = Request(HttpMethod.Post, "/api/sites", (AuthCookies.AccessTokenName, access));
        request.Content = Json(new { name });

        var created = await Client.SendAsync(request);

        Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.OK), await created.Content.ReadAsStringAsync());

        return JsonDocument.Parse(await created.Content.ReadAsStringAsync())
            .RootElement.GetProperty("nanoid").GetString()!;
    }

    private async Task<HttpResponseMessage> UploadAsync(
        string access,
        string siteNanoid,
        params (string Name, byte[] Content)[] files)
    {
        var body = new MultipartFormDataContent();

        foreach (var (name, content) in files)
        {
            var part = new ByteArrayContent(content);
            part.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            body.Add(part, "files", name);
        }

        var request = Request(HttpMethod.Post, $"/api/sites/{siteNanoid}/images",
            (AuthCookies.AccessTokenName, access));

        request.Content = body;

        return await Client.SendAsync(request);
    }

    private async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), body);

        return JsonDocument.Parse(body).RootElement.Clone();
    }

    [Test]
    public async Task An_uploaded_photograph_becomes_a_version_and_a_file_the_site_can_serve()
    {
        var owner = await AccountAsync("owner@example.com");
        var site = await SiteAsync(owner, "Koopman Cycles");

        var uploaded = await ReadAsync(await UploadAsync(owner, site, ("Shop Front.gif", Gif())));

        Assert.Multiple(() =>
        {
            // The name is kept as far as a URL can keep it, because it is the only handle the person has on
            // which photograph is which when they write the next message.
            Assert.That(uploaded.GetProperty("images")[0].GetProperty("url").GetString(),
                Is.EqualTo("/images/shop-front.gif"));

            Assert.That(uploaded.GetProperty("versionNanoid").GetString(), Is.Not.Null,
                "an upload is a version; there is no second way for a site's files to change");
        });

        // In the repository, at the path a static export serves from the root — which is what makes the
        // published site carry its own photographs rather than pointing at ours.
        var request = Request(HttpMethod.Get, $"/api/sites/{site}/file?path=public/images/shop-front.gif",
            (AuthCookies.AccessTokenName, owner));

        var file = await Client.SendAsync(request);

        Assert.That(file.StatusCode, Is.EqualTo(HttpStatusCode.OK), await file.Content.ReadAsStringAsync());

        var listed = await ReadAsync(await Client.SendAsync(
            Request(HttpMethod.Get, $"/api/sites/{site}/images", (AuthCookies.AccessTokenName, owner))));

        Assert.Multiple(() =>
        {
            Assert.That(listed.GetArrayLength(), Is.EqualTo(1));
            Assert.That(listed[0].GetProperty("fileName").GetString(), Is.EqualTo("shop-front.gif"));
            Assert.That(listed[0].GetProperty("bytes").GetInt64(), Is.EqualTo(Gif().Length));
        });

        // And the history says who did it and in what words, rather than describing the mechanism.
        var versions = await ReadAsync(await Client.SendAsync(
            Request(HttpMethod.Get, $"/api/sites/{site}/versions", (AuthCookies.AccessTokenName, owner))));

        Assert.Multiple(() =>
        {
            Assert.That(versions[0].GetProperty("origin").GetString(), Is.EqualTo("Upload"));
            Assert.That(versions[0].GetProperty("summary").GetString(), Is.EqualTo("Added shop-front.gif"));
        });
    }

    [Test]
    public async Task Two_photographs_with_the_same_name_do_not_overwrite_each_other()
    {
        var owner = await AccountAsync("owner@example.com");
        var site = await SiteAsync(owner, "Koopman Cycles");

        await UploadAsync(owner, site, ("image.gif", Gif()));

        // A different picture under the same name — two phones both calling it image.jpg is a normal afternoon,
        // and silently replacing the first would remove it from whatever page is already using it.
        var second = await ReadAsync(await UploadAsync(owner, site, ("image.png", Png())));

        Assert.That(second.GetProperty("images")[0].GetProperty("url").GetString(), Is.EqualTo("/images/image.png"));

        var third = await ReadAsync(await UploadAsync(owner, site, ("image.gif", Png())));

        Assert.That(third.GetProperty("images")[0].GetProperty("url").GetString(),
            Is.EqualTo("/images/image-2.png"),
            "the extension follows the bytes, so this is a png, and the name is taken");
    }

    [Test]
    public async Task What_a_file_is_called_does_not_decide_what_it_is()
    {
        var owner = await AccountAsync("owner@example.com");
        var site = await SiteAsync(owner, "Koopman Cycles");

        // The extension is the only thing a static host uses to decide how to serve a file, and the name comes
        // from whoever is uploading. A PNG called .jpg is stored as a .png; a web page called .jpg is refused.
        var renamed = await ReadAsync(await UploadAsync(owner, site, ("portrait.jpg", Png())));

        Assert.That(renamed.GetProperty("images")[0].GetProperty("url").GetString(), Is.EqualTo("/images/portrait.png"));

        var notAnImage = await UploadAsync(owner, site,
            ("payload.jpg", Encoding.UTF8.GetBytes("<html><script>alert(1)</script></html>")));

        Assert.That(notAnImage.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await notAnImage.Content.ReadAsStringAsync(), Does.Contain("payload.jpg"));
    }

    [Test]
    public async Task An_image_larger_than_a_website_should_carry_is_refused()
    {
        var owner = await AccountAsync("owner@example.com");
        var site = await SiteAsync(owner, "Koopman Cycles");

        // Valid GIF bytes followed by megabytes of padding: refused for its size, not for its type, which is
        // the difference the message has to get right.
        var padded = new byte[UploadSiteImages.MaxImageBytes + 1];
        Gif().CopyTo(padded, 0);

        var response = await UploadAsync(owner, site, ("enormous.gif", padded));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("enormous.gif"));
    }

    [Test]
    public async Task A_stranger_cannot_add_a_photograph_to_somebody_else_is_site()
    {
        var owner = await AccountAsync("owner@example.com");
        var stranger = await AccountAsync("stranger@example.com");
        var site = await SiteAsync(owner, "Koopman Cycles");

        var response = await UploadAsync(stranger, site, ("image.gif", Gif()));

        // 404, the rule every per-site route follows: a 403 would confirm the nanoid names a real site.
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
}
