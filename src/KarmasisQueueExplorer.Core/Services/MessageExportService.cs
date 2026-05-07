using System.Text;
using KarmasisQueueExplorer.Core.Models;

namespace KarmasisQueueExplorer.Core.Services;

/// <summary>
/// Writes selected message details to an application-local Exports directory.
/// </summary>
public sealed class MessageExportService : IMessageExportService
{
    private readonly string _exportDirectory;

    public MessageExportService()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "KarmasisQueueExplorer", "Exports"))
    {
    }

    public MessageExportService(string exportDirectory)
    {
        _exportDirectory = exportDirectory;
    }

    public async Task<string> ExportMessageAsync(MessageInfo message, string? queuePath, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_exportDirectory);

        var safeLabel = ToSafeFileName(string.IsNullOrWhiteSpace(message.Label) ? "message" : message.Label);
        var fileName = $"{DateTime.Now:yyyyMMdd-HHmmssfff}-{safeLabel}.txt";
        var filePath = Path.Combine(_exportDirectory, fileName);

        var builder = new StringBuilder();
        builder.AppendLine("KarmasisQueueExplorer Message Export");
        builder.AppendLine(new string('=', 40));
        builder.AppendLine($"Queue      : {queuePath ?? string.Empty}");
        builder.AppendLine($"Id         : {message.Id}");
        builder.AppendLine($"Label      : {message.Label}");
        builder.AppendLine($"Sent Time  : {message.SentTime:O}");
        builder.AppendLine($"Body Size  : {message.BodySize}");
        builder.AppendLine($"Priority   : {message.Priority}");
        builder.AppendLine($"Class      : {message.MessageClass}");
        builder.AppendLine();
        builder.AppendLine("Body");
        builder.AppendLine(new string('-', 40));
        builder.AppendLine(message.BodyText);

        await File.WriteAllTextAsync(filePath, builder.ToString(), Encoding.UTF8, cancellationToken);
        return filePath;
    }

    private static string ToSafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(safe) ? "message" : safe;
    }
}
