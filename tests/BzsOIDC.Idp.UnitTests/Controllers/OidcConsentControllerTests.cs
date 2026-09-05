using BzsOIDC.Contracts;
using BzsOIDC.Idp.Controllers;
using BzsOIDC.Idp.Services.Oidc;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace BzsOIDC.Idp.UnitTests.Controllers;

public sealed class OidcConsentControllerTests
{
    [Fact]
    public async Task GetPreview_WhenRequestIsInvalid_ReturnsInvalidRequest()
    {
        var service = Substitute.For<IOidcConsentPreviewService>();
        service.GetPreviewAsync(string.Empty, Arg.Any<CancellationToken>())
            .Returns((OidcConsentPreviewResponse?)null);
        var sut = new OidcConsentController(service);

        var result = await sut.GetPreview(null, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal("invalid_request", badRequest.Value?.GetType().GetProperty("error")?.GetValue(badRequest.Value));
    }

    [Fact]
    public async Task GetPreview_WhenServiceReturnsPreview_ReturnsOk()
    {
        var preview = new OidcConsentPreviewResponse(
            "client",
            "Client",
            [],
            "/connect/authorize?client_id=client",
            "token");
        var service = Substitute.For<IOidcConsentPreviewService>();
        service.GetPreviewAsync("token", Arg.Any<CancellationToken>()).Returns(preview);
        var sut = new OidcConsentController(service);

        var result = await sut.GetPreview("token", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(preview, ok.Value);
    }
}
