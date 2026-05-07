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
    private readonly string _lqsPath;

    public MsmqService(ILogger<MsmqService> logger)
        : this(logger, Path.Combine(Environment.SystemDirectory, "msmq", "storage", "lqs"))
    {
    }

    public MsmqService(ILogger<MsmqService> logger, string lqsPath)
    {
        _logger = logger;
        _lqsPath = lqsPath;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<QueueInfo>> GetQueuesAsync(string machineName, CancellationToken cancellationToken = default)
    {
        return Task.Run<IReadOnlyList<QueueInfo>>(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (machineName != "." && !machineName.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("Remote MSMQ discovery was requested for {MachineName}, but remote support starts in Phase 6.", machineName);
                return [];
            }

            return DiscoverLocalQueues(cancellationToken);
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<MessageInfo>> GetMessagesAsync(string queuePath, int maxCount = 100, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Message peeking for {QueuePath} is planned for Phase 2.", queuePath);
        return Task.FromResult<IReadOnlyList<MessageInfo>>([]);
    }

    private IReadOnlyList<QueueInfo> DiscoverLocalQueues(CancellationToken cancellationToken)
    {
        // .NET 8 does not include the legacy System.Messaging assembly. The first iteration
        // keeps MSMQ access behind IMsmqService and discovers local private queues from the
        // MSMQ LQS metadata directory when available. A richer MSMQ adapter will be added next.
        var queues = new List<QueueInfo>();

        if (!Directory.Exists(_lqsPath))
        {
            _logger.LogWarning("MSMQ LQS directory was not found at {LqsPath}. MSMQ Windows Feature may be disabled.", _lqsPath);
            throw new MsmqUnavailableException("MSMQ appears to be disabled. Enable the 'Microsoft Message Queue (MSMQ) Server' Windows Feature and refresh.");
        }

        foreach (var filePath in Directory.EnumerateFiles(_lqsPath))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var metadata = TryReadQueueMetadata(filePath);
            if (metadata is null)
            {
                continue;
            }

            queues.Add(new QueueInfo(
                metadata.Name,
                metadata.Path,
                Environment.MachineName,
                metadata.Type,
                TryGetLocalQueueCount(metadata.Path)));
        }

        return queues
            .OrderBy(queue => queue.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private LqsQueueMetadata? TryReadQueueMetadata(string filePath)
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
            var path = FirstNonEmpty(values, "PathName") ?? $@".\private$\{name}";
            path = NormalizeQueuePath(path, name);

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

    private static string NormalizeQueuePath(string rawPath, string queueName)
    {
        var path = rawPath.Trim().Replace('/', '\\');
        if (path.Contains("private$", StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        return $@".\private$\{queueName}";
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
