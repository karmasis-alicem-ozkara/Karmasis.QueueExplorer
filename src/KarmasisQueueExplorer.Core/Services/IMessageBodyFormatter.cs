namespace KarmasisQueueExplorer.Core.Services;

/// <summary>
/// Formats message body text for readable detail views.
/// </summary>
public interface IMessageBodyFormatter
{
    string FormatJson(string bodyText);

    string FormatXml(string bodyText);

    string FormatHex(string bodyText);
}
