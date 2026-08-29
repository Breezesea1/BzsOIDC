using BzsOIDC.Idp.Infra.Http;
using Microsoft.AspNetCore.Http;

namespace BzsOIDC.Idp.UnitTests.Infra.Http;

public sealed class StaticAssetHardeningExtensionsTests
{
    [Theory]
    [InlineData("/_framework/blazor.webassembly.js")]
    [InlineData("/_content/Bzs.Blazor/bzs.blazor.css")]
    [InlineData("/css/app.0123456789abcdef.css")]
    public void IsImmutableAssetPath_WhenGeneratedAsset_ReturnsTrue(string path)
    {
        Assert.True(StaticAssetHardeningExtensions.IsImmutableAssetPath(new PathString(path)));
    }

    [Theory]
    [InlineData("/index.html")]
    [InlineData("/app.css")]
    [InlineData("/api/session")]
    public void IsImmutableAssetPath_WhenDynamicOrShell_ReturnsFalse(string path)
    {
        Assert.False(StaticAssetHardeningExtensions.IsImmutableAssetPath(new PathString(path)));
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/index.html")]
    public void IsDocumentPath_WhenHtmlShell_ReturnsTrue(string path)
    {
        Assert.True(StaticAssetHardeningExtensions.IsDocumentPath(new PathString(path)));
    }

    [Fact]
    public void IsDocumentPath_WhenApiRoute_ReturnsFalse()
    {
        Assert.False(StaticAssetHardeningExtensions.IsDocumentPath(new PathString("/api/session")));
    }
}
