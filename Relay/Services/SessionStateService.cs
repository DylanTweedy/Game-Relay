using System.IO;
using System.Text.Json;
using Relay.Data.Models;

namespace Relay.Services;

public sealed class SessionStateService(LoggingService logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };
    private static readonly SemaphoreSlim SaveGate = new(1, 1);
    private readonly object _debounceSync = new();
    private CancellationTokenSource? _debounceCts;
    private SessionState? _pendingState;
    private string _pendingReason = string.Empty;

    private static string SessionPath => Path.Combine(AppContext.BaseDirectory, "SessionState.json");
    private static string SessionTempPath => SessionPath + ".tmp";

    public async Task<SessionState> LoadAsync()
    {
        if (!File.Exists(SessionPath))
        {
            logger.Info($"Session state file not found: {SessionPath}");
            return new SessionState();
        }

        await using var stream = new FileStream(
            SessionPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            bufferSize: 8192,
            useAsync: true);
        var model = await JsonSerializer.DeserializeAsync<SessionState>(stream, JsonOptions);
        logger.Info($"Loaded session state from {SessionPath}");
        return model ?? new SessionState();
    }

    public void RequestSave(SessionState session, string reason, int debounceMs = 350)
    {
        CancellationTokenSource? previousCts = null;
        CancellationTokenSource newCts;
        lock (_debounceSync)
        {
            previousCts = _debounceCts;
            _pendingState = session;
            _pendingReason = reason;
            _debounceCts = new CancellationTokenSource();
            newCts = _debounceCts;
        }

        previousCts?.Cancel();
        previousCts?.Dispose();

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(debounceMs, newCts.Token).ConfigureAwait(false);

                SessionState? stateToSave;
                string reasonToSave;
                lock (_debounceSync)
                {
                    stateToSave = _pendingState;
                    reasonToSave = _pendingReason;
                }

                if (stateToSave is null)
                {
                    return;
                }

                await SaveNowAsync(stateToSave, reasonToSave).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                logger.Error(ex.ToString());
            }
        });
    }

    public async Task SaveNowAsync(SessionState session, string reason, CancellationToken ct = default)
    {
        await SaveGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await WriteAtomicAsync(session, ct).ConfigureAwait(false);
            logger.Info($"Saved session state to {SessionPath} (reason={reason}, shortcuts={session.ShortcutResults.Count}, folders={session.FolderCandidates.Count})");
        }
        finally
        {
            SaveGate.Release();
        }
    }

    public async Task DeleteAsync(CancellationToken ct = default)
    {
        CancelPending();
        await SaveGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (File.Exists(SessionTempPath))
            {
                File.Delete(SessionTempPath);
            }

            if (File.Exists(SessionPath))
            {
                File.Delete(SessionPath);
                logger.Info($"Deleted session state file: {SessionPath}");
            }
            else
            {
                logger.Info($"Session state file already missing: {SessionPath}");
            }
        }
        finally
        {
            SaveGate.Release();
        }
    }

    public void CancelPending()
    {
        CancellationTokenSource? cts = null;
        lock (_debounceSync)
        {
            cts = _debounceCts;
            _debounceCts = null;
            _pendingState = null;
            _pendingReason = string.Empty;
        }

        cts?.Cancel();
        cts?.Dispose();
    }

    private static async Task WriteAtomicAsync(SessionState session, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SessionPath) ?? AppContext.BaseDirectory);
        await using (var stream = new FileStream(
                         SessionTempPath,
                         FileMode.Create,
                         FileAccess.Write,
                         FileShare.Read,
                         bufferSize: 8192,
                         useAsync: true))
        {
            await JsonSerializer.SerializeAsync(stream, session, JsonOptions, ct).ConfigureAwait(false);
            await stream.FlushAsync(ct).ConfigureAwait(false);
        }

        if (File.Exists(SessionPath))
        {
            File.Replace(SessionTempPath, SessionPath, destinationBackupFileName: null);
        }
        else
        {
            File.Move(SessionTempPath, SessionPath);
        }
    }

    public string GetSessionPath() => SessionPath;
}
