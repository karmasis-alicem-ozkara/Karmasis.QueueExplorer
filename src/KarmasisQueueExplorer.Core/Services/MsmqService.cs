using KarmasisQueueExplorer.Core.Models;
using Microsoft.Extensions.Logging;

namespace KarmasisQueueExplorer.Core.Services;

/// <summary>
/// Local MSMQ service implementation. Phase 1 focuses on safe queue discovery only.
/// </summary>
public sealed class MsmqService : IMsmqService
{
    private const int MqPeekAccess = 32;
    private const int MqDenyNone = 0;

    private readonly ILogger<MsmqService> _logger;
    private readonly Func<string, string> _lqsPathResolver;

    public MsmqService(ILogger<MsmqService> logger)
        : this(logger, ResolveDefaultLqsPath)
    {
    }

    public MsmqService(ILogger<MsmqService> logger, string lqsPath)
        : this(logger, _ => lqsPath)
    {
    }

    public MsmqService(ILogger<MsmqService> logger, Func<string, string> lqsPathResolver)
    {
        _logger = logger;
        _lqsPathResolver = lqsPathResolver;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<QueueInfo>> GetQueuesAsync(string machineName, CancellationToken cancellationToken = default)
    {
        return Task.Run<IReadOnlyList<QueueInfo>>(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            return DiscoverQueues(NormalizeMachineName(machineName), cancellationToken);
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<MessageInfo>> GetMessagesAsync(string queuePath, int maxCount = 100, CancellationToken cancellationToken = default)
    {
        return Task.Run<IReadOnlyList<MessageInfo>>(() => PeekMessages(queuePath, maxCount, cancellationToken), cancellationToken);
    }

    /// <inheritdoc />
    public Task SendMessageAsync(string queuePath, string label, string bodyText, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => SendTextMessage(queuePath, label, bodyText, cancellationToken), cancellationToken);
    }

    /// <inheritdoc />
    public Task DeleteMessageAsync(string queuePath, string messageId, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => DeleteMessageById(queuePath, messageId, cancellationToken), cancellationToken);
    }

    /// <inheritdoc />
    public Task CopyMessageAsync(string targetQueuePath, MessageInfo message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        return SendMessageAsync(targetQueuePath, message.Label, message.BodyText, cancellationToken);
    }

    private void DeleteMessageById(string queuePath, string messageId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(messageId))
        {
            throw new ArgumentException("Message id cannot be empty.", nameof(messageId));
        }

        if (!OperatingSystem.IsWindows())
        {
            throw new MsmqUnavailableException("MSMQ delete is only supported on Windows.");
        }

        object? queue = null;

        try
        {
            var queueInfoType = Type.GetTypeFromProgID("MSMQ.MSMQQueueInfo");
            if (queueInfoType is null)
            {
                throw new MsmqUnavailableException("MSMQ COM components are unavailable. Enable the 'Microsoft Message Queue (MSMQ) Server' Windows Feature and refresh.");
            }

            dynamic queueInfo = Activator.CreateInstance(queueInfoType)!;
            queueInfo.PathName = queuePath;
            queue = queueInfo.Open(1, MqDenyNone);
            dynamic dynamicQueue = queue;

            // ReceiveById removes exactly the selected message. This method is intentionally
            // exposed only through ViewModel confirmation flow.
            dynamicQueue.ReceiveById(messageId);
        }
        catch (MsmqUnavailableException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete message {MessageId} from {QueuePath}.", messageId, queuePath);
            throw new InvalidOperationException($"Failed to delete message from {queuePath}: {ex.Message}", ex);
        }
        finally
        {
            TryCloseComQueue(queue);
        }
    }

    private void SendTextMessage(string queuePath, string label, string bodyText, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsWindows())
        {
            throw new MsmqUnavailableException("MSMQ sending is only supported on Windows.");
        }

        object? queue = null;

        try
        {
            var queueInfoType = Type.GetTypeFromProgID("MSMQ.MSMQQueueInfo");
            var messageType = Type.GetTypeFromProgID("MSMQ.MSMQMessage");
            if (queueInfoType is null || messageType is null)
            {
                throw new MsmqUnavailableException("MSMQ COM components are unavailable. Enable the 'Microsoft Message Queue (MSMQ) Server' Windows Feature and refresh.");
            }

            dynamic queueInfo = Activator.CreateInstance(queueInfoType)!;
            queueInfo.PathName = queuePath;
            queue = queueInfo.Open(2, MqDenyNone);

            dynamic message = Activator.CreateInstance(messageType)!;
            message.Label = string.IsNullOrWhiteSpace(label) ? "KarmasisQueueExplorer Test Message" : label.Trim();
            message.Body = bodyText ?? string.Empty;
            message.Send(queue);
        }
        catch (MsmqUnavailableException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send message to {QueuePath}.", queuePath);
            throw new InvalidOperationException($"Failed to send message to {queuePath}: {ex.Message}", ex);
        }
        finally
        {
            TryCloseComQueue(queue);
        }
    }

    private IReadOnlyList<MessageInfo> PeekMessages(string queuePath, int maxCount, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }

        object? queue = null;
        object? cursor = null;

        try
        {
            var queueInfoType = Type.GetTypeFromProgID("MSMQ.MSMQQueueInfo");
            if (queueInfoType is null)
            {
                throw new MsmqUnavailableException("MSMQ COM components are unavailable. Enable the 'Microsoft Message Queue (MSMQ) Server' Windows Feature and refresh.");
            }

            dynamic queueInfo = Activator.CreateInstance(queueInfoType)!;
            queueInfo.PathName = queuePath;
            queue = queueInfo.Open(MqPeekAccess, MqDenyNone);
            dynamic dynamicQueue = queue;
            cursor = dynamicQueue.CreateCursor();

            var messages = new List<MessageInfo>();
            var first = true;

            while (messages.Count < maxCount)
            {
                cancellationToken.ThrowIfCancellationRequested();

                object? message = TryPeekMessage(dynamicQueue, cursor, first);
                if (message is null)
                {
                    break;
                }

                messages.Add(ConvertToMessageInfo(message));
                first = false;
            }

            return messages;
        }
        catch (MsmqUnavailableException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to peek messages from {QueuePath}.", queuePath);
            return [];
        }
        finally
        {
            TryCloseComQueue(cursor);
            TryCloseComQueue(queue);
        }
    }

    private object? TryPeekMessage(dynamic queue, object? cursor, bool first)
    {
        try
        {
            return first
                ? queue.PeekCurrent(0, false, true, cursor)
                : queue.PeekNext(0, false, true, cursor);
        }
        catch
        {
            return null;
        }
    }

    private static MessageInfo ConvertToMessageInfo(object message)
    {
        dynamic dynamicMessage = message;

        var label = SafeString(() => dynamicMessage.Label);
        var bodyText = ConvertBodyToText(SafeObject(() => dynamicMessage.Body));
        var bodyPreview = bodyText.Length > 180 ? $"{bodyText[..180]}..." : bodyText;

        return new MessageInfo(
            SafeString(() => dynamicMessage.Id),
            string.IsNullOrWhiteSpace(label) ? "(no label)" : label,
            SafeDateTime(() => dynamicMessage.SentTime),
            SafeLong(() => dynamicMessage.BodyLength),
            SafeString(() => dynamicMessage.Priority),
            SafeString(() => dynamicMessage.MsgClass),
            bodyText,
            bodyPreview);
    }

    private static string ConvertBodyToText(object? body)
    {
        return body switch
        {
            null => string.Empty,
            string text => text,
            byte[] bytes => TryDecodeBytes(bytes),
            Array bytes when bytes.GetType().GetElementType() == typeof(byte) => TryDecodeBytes(bytes.Cast<byte>().ToArray()),
            _ => body.ToString() ?? string.Empty
        };
    }

    private static string TryDecodeBytes(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return string.Empty;
        }

        try
        {
            return System.Text.Encoding.UTF8.GetString(bytes).TrimEnd('\0');
        }
        catch
        {
            return Convert.ToHexString(bytes);
        }
    }

    private static string SafeString(Func<object?> valueFactory)
    {
        try
        {
            return valueFactory()?.ToString() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static object? SafeObject(Func<object?> valueFactory)
    {
        try
        {
            return valueFactory();
        }
        catch
        {
            return null;
        }
    }

    private static DateTime? SafeDateTime(Func<object?> valueFactory)
    {
        try
        {
            var value = valueFactory();
            return value is null ? null : Convert.ToDateTime(value);
        }
        catch
        {
            return null;
        }
    }

    private static long? SafeLong(Func<object?> valueFactory)
    {
        try
        {
            var value = valueFactory();
            return value is null ? null : Convert.ToInt64(value);
        }
        catch
        {
            return null;
        }
    }

    private IReadOnlyList<QueueInfo> DiscoverQueues(string machineName, CancellationToken cancellationToken)
    {
        // .NET 8 does not include the legacy System.Messaging assembly. The first iteration
        // keeps MSMQ access behind IMsmqService and discovers local private queues from the
        // MSMQ LQS metadata directory when available. A richer MSMQ adapter will be added next.
        var queues = new List<QueueInfo>();
        var lqsPath = _lqsPathResolver(machineName);

        if (!Directory.Exists(lqsPath))
        {
            _logger.LogWarning("MSMQ LQS directory was not found at {LqsPath} for machine {MachineName}. MSMQ may be disabled or remote admin access may be unavailable.", lqsPath, machineName);
            throw new MsmqUnavailableException(IsLocalMachine(machineName)
                ? "MSMQ appears to be disabled. Enable the 'Microsoft Message Queue (MSMQ) Server' Windows Feature and refresh."
                : $"Could not access MSMQ metadata on '{machineName}'. Ensure MSMQ is enabled, remote admin share access is allowed, and you have permission to \\\\{machineName}\\admin$. ");
        }

        foreach (var filePath in Directory.EnumerateFiles(lqsPath))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var metadata = TryReadQueueMetadata(filePath, machineName);
            if (metadata is null)
            {
                continue;
            }

            queues.Add(new QueueInfo(
                metadata.Name,
                metadata.Path,
                IsLocalMachine(machineName) ? Environment.MachineName : machineName,
                metadata.Type,
                TryGetLocalQueueCount(metadata.Path)));
        }

        return queues
            .OrderBy(queue => queue.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private LqsQueueMetadata? TryReadQueueMetadata(string filePath, string machineName)
    {
        try
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var line in File.ReadLines(filePath))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith(';') || trimmed.StartsWith('#'))
                {
                    continue;
                }

                var separatorIndex = trimmed.IndexOf('=');
                if (separatorIndex <= 0)
                {
                    continue;
                }

                var key = trimmed[..separatorIndex].Trim();
                var value = trimmed[(separatorIndex + 1)..].Trim().Trim('"');
                values[key] = value;
            }

            var rawName = FirstNonEmpty(values, "QueueName", "QueueLabel", "PathName");
            if (string.IsNullOrWhiteSpace(rawName))
            {
                _logger.LogDebug("Skipping LQS file {FilePath} because no QueueName, QueueLabel, or PathName was found.", filePath);
                return null;
            }

            var name = NormalizeQueueName(rawName);
            var path = FirstNonEmpty(values, "PathName") ?? BuildPrivateQueuePath(machineName, name);
            path = NormalizeQueuePath(path, machineName, name);

            return new LqsQueueMetadata(name, path, InferQueueType(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Failed to read MSMQ queue metadata from {FilePath}.", filePath);
        }

        return null;
    }

    private int? TryGetLocalQueueCount(string queuePath)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        object? queue = null;

        try
        {
            var queueInfoType = Type.GetTypeFromProgID("MSMQ.MSMQQueueInfo");
            if (queueInfoType is null)
            {
                _logger.LogDebug("MSMQ COM component is unavailable; message count will not be displayed.");
                return null;
            }

            dynamic queueInfo = Activator.CreateInstance(queueInfoType)!;
            queueInfo.PathName = queuePath;
            queue = queueInfo.Open(MqPeekAccess, MqDenyNone);

            return Convert.ToInt32(((dynamic)queue).MessageCount);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not read MSMQ message count for {QueuePath}.", queuePath);
            return null;
        }
        finally
        {
            TryCloseComQueue(queue);
        }
    }

    private static void TryCloseComQueue(object? queue)
    {
        if (queue is null)
        {
            return;
        }

        try
        {
            ((dynamic)queue).Close();
        }
        catch
        {
            // Best-effort COM cleanup only.
        }
    }

    private static string? FirstNonEmpty(IReadOnlyDictionary<string, string> values, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static string NormalizeQueueName(string rawName)
    {
        var name = rawName.Trim();
        name = name.Replace('/', '\\');

        var privateMarker = "private$\\";
        var markerIndex = name.IndexOf(privateMarker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex >= 0)
        {
            name = name[(markerIndex + privateMarker.Length)..];
        }

        var lastSlash = name.LastIndexOf('\\');
        if (lastSlash >= 0)
        {
            name = name[(lastSlash + 1)..];
        }

        return name.Trim();
    }

    private static string NormalizeQueuePath(string rawPath, string machineName, string queueName)
    {
        var path = rawPath.Trim().Replace('/', '\\');
        if (path.Contains("private$", StringComparison.OrdinalIgnoreCase))
        {
            return IsLocalMachine(machineName)
                ? path
                : ReplaceQueuePathMachine(path, machineName);
        }

        return BuildPrivateQueuePath(machineName, queueName);
    }

    private static string BuildPrivateQueuePath(string machineName, string queueName)
    {
        return IsLocalMachine(machineName)
            ? $@".\private$\{queueName}"
            : $@"{machineName}\private$\{queueName}";
    }

    private static string ReplaceQueuePathMachine(string path, string machineName)
    {
        var marker = "private$\\";
        var markerIndex = path.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            return path;
        }

        return $@"{machineName}\{path[markerIndex..]}";
    }

    private static string NormalizeMachineName(string machineName)
    {
        return string.IsNullOrWhiteSpace(machineName) ? "." : machineName.Trim().Trim('\\');
    }

    private static bool IsLocalMachine(string machineName)
    {
        return machineName == "." || machineName.Equals("localhost", StringComparison.OrdinalIgnoreCase) || machineName.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveDefaultLqsPath(string machineName)
    {
        return IsLocalMachine(machineName)
            ? Path.Combine(Environment.SystemDirectory, "msmq", "storage", "lqs")
            : $@"\\{machineName}\admin$\System32\msmq\storage\lqs";
    }

    private static QueueType InferQueueType(string path)
    {
        if (path.Contains("dead", StringComparison.OrdinalIgnoreCase))
        {
            return QueueType.DeadLetter;
        }

        return path.Contains("private$", StringComparison.OrdinalIgnoreCase)
            ? QueueType.Private
            : QueueType.Unknown;
    }

    private sealed record LqsQueueMetadata(string Name, string Path, QueueType Type);
}
