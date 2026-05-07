using System.Text;
using System.Text.Json;
using System.Xml;

namespace KarmasisQueueExplorer.Core.Services;

/// <summary>
/// Safe formatter for JSON, XML, and hex body views. Invalid input returns a readable explanation instead of throwing.
/// </summary>
public sealed class MessageBodyFormatter : IMessageBodyFormatter
{
    public string FormatJson(string bodyText)
    {
        if (string.IsNullOrWhiteSpace(bodyText))
        {
            return string.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(bodyText);
            return JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (JsonException ex)
        {
            return $"Body is not valid JSON: {ex.Message}";
        }
    }

    public string FormatXml(string bodyText)
    {
        if (string.IsNullOrWhiteSpace(bodyText))
        {
            return string.Empty;
        }

        try
        {
            var document = new XmlDocument { PreserveWhitespace = false };
            document.LoadXml(bodyText);

            using var stringWriter = new StringWriter();
            using var xmlWriter = XmlWriter.Create(stringWriter, new XmlWriterSettings
            {
                Indent = true,
                OmitXmlDeclaration = false
            });

            document.Save(xmlWriter);
            return stringWriter.ToString();
        }
        catch (XmlException ex)
        {
            return $"Body is not valid XML: {ex.Message}";
        }
    }

    public string FormatHex(string bodyText)
    {
        if (string.IsNullOrEmpty(bodyText))
        {
            return string.Empty;
        }

        var bytes = Encoding.UTF8.GetBytes(bodyText);
        var builder = new StringBuilder();

        for (var offset = 0; offset < bytes.Length; offset += 16)
        {
            var lineBytes = bytes.Skip(offset).Take(16).ToArray();
            var hex = string.Join(" ", lineBytes.Select(value => value.ToString("X2")));
            var ascii = new string(lineBytes.Select(value => value is >= 32 and <= 126 ? (char)value : '.').ToArray());

            builder.Append(offset.ToString("X8"));
            builder.Append("  ");
            builder.Append(hex.PadRight(47));
            builder.Append("  ");
            builder.AppendLine(ascii);
        }

        return builder.ToString().TrimEnd();
    }
}
