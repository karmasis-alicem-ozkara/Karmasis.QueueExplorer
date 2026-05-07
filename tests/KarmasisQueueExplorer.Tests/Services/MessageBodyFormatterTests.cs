using KarmasisQueueExplorer.Core.Services;

namespace KarmasisQueueExplorer.Tests.Services;

public sealed class MessageBodyFormatterTests
{
    private readonly MessageBodyFormatter _formatter = new();

    [Test]
    public void FormatJson_WhenValidJson_ReturnsIndentedJson()
    {
        var result = _formatter.FormatJson("{\"orderId\":10,\"status\":\"Created\"}");

        Assert.Multiple(() =>
        {
            Assert.That(result, Does.Contain("orderId"));
            Assert.That(result, Does.Contain(Environment.NewLine));
        });
    }

    [Test]
    public void FormatJson_WhenInvalidJson_ReturnsReadableError()
    {
        var result = _formatter.FormatJson("not-json");

        Assert.That(result, Does.StartWith("Body is not valid JSON:"));
    }

    [Test]
    public void FormatXml_WhenValidXml_ReturnsIndentedXml()
    {
        var result = _formatter.FormatXml("<order><id>10</id></order>");

        Assert.Multiple(() =>
        {
            Assert.That(result, Does.Contain("<order>"));
            Assert.That(result, Does.Contain(Environment.NewLine));
        });
    }

    [Test]
    public void FormatHex_WhenBodyTextProvided_ReturnsOffsetAndAscii()
    {
        var result = _formatter.FormatHex("ABC");

        Assert.Multiple(() =>
        {
            Assert.That(result, Does.StartWith("00000000"));
            Assert.That(result, Does.Contain("41 42 43"));
            Assert.That(result, Does.Contain("ABC"));
        });
    }
}
