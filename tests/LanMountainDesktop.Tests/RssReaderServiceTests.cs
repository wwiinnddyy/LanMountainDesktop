using System.Text;
using System.Xml;
using System.Xml.Linq;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.Services.RssReader;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class RssReaderServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "LanMountainDesktop.RssTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void ComponentDefinition_UsesInfoCategoryAndEightByFourMinimum()
    {
        var registry = ComponentRegistry.CreateDefault();
        Assert.True(registry.TryGetDefinition(BuiltInComponentIds.DesktopRssReader, out var definition));
        Assert.Equal("Info", definition.Category);
        Assert.Equal(8, definition.MinWidthCells);
        Assert.Equal(4, definition.MinHeightCells);
        Assert.Equal(DesktopComponentResizeMode.Free, definition.ResizeMode);
    }

    [Theory]
    [InlineData("HTTPS://Example.COM:443/feed/#fragment", "https://example.com/feed")]
    [InlineData("http://Example.com:80/rss", "http://example.com/rss")]
    public void NormalizeFeedUrl_CanonicalizesAddress(string input, string expected)
    {
        Assert.Equal(expected, RssReaderService.NormalizeFeedUrl(input));
    }

    [Fact]
    public void ParseProbe_ReadsRssAndAtom()
    {
        var rss = Encoding.UTF8.GetBytes("""<?xml version="1.0"?><rss version="2.0"><channel><title>News</title><link>https://example.com</link><description>Test</description><item><title>One</title><guid>1</guid></item></channel></rss>""");
        var atom = Encoding.UTF8.GetBytes("""<?xml version="1.0"?><feed xmlns="http://www.w3.org/2005/Atom"><title>Atom News</title><id>x</id><updated>2026-01-01T00:00:00Z</updated><entry><title>One</title><id>1</id><updated>2026-01-01T00:00:00Z</updated></entry></feed>""");

        Assert.Equal("News", RssReaderService.ParseProbe(rss, "https://example.com/rss").Title);
        Assert.Equal("Atom", RssReaderService.ParseProbe(atom, "https://example.com/atom").Format);
    }

    [Fact]
    public void ParseProbe_RejectsDtd()
    {
        var xml = Encoding.UTF8.GetBytes("""<?xml version="1.0"?><!DOCTYPE rss [<!ENTITY xxe SYSTEM "file:///etc/passwd">]><rss version="2.0"><channel><title>&xxe;</title><description>x</description><link>https://example.com</link></channel></rss>""");
        Assert.ThrowsAny<XmlException>(() => RssReaderService.ParseProbe(xml, "https://example.com/rss"));
    }

    [Fact]
    public void SanitizeHtml_RemovesExecutableContentAndRemoteImages()
    {
        var sanitized = RssReaderService.SanitizeHtml("<script>alert(1)</script><p onclick=\"x()\">Hello</p><img src=\"https://x/img.png\"><a href=\"javascript:x()\">bad</a>", false);
        Assert.DoesNotContain("script", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onclick", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("javascript:", sanitized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Settings_PersistAcrossServiceInstances()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "rss.db");
        using (var first = new RssReaderService(path)) first.SaveSettings(new RssReaderSettings(60, 45, true));
        using var second = new RssReaderService(path);
        Assert.Equal(new RssReaderSettings(60, 45, true), second.GetSettings());
    }

    [Fact]
    public void ExportOpml_CreatesValidEmptyDocument()
    {
        Directory.CreateDirectory(_root);
        using var service = new RssReaderService(Path.Combine(_root, "rss.db"));
        var path = Path.Combine(_root, "feeds.opml");
        service.ExportOpml(path);
        var document = XDocument.Load(path);
        Assert.Equal("opml", document.Root?.Name.LocalName);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
