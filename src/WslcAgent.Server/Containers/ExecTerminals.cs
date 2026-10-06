using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Containers;

/// <summary>Interactive shells shared between successive WebSocket connections.</summary>
public sealed class ExecTerminals(
    IWslcRunner wslc,
    ICliActivity activity,
    IOptionsMonitor<ExecTerminalOptions> options,
    ILogger<ExecTerminals> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PolicyTick = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan CloseWarning = TimeSpan.FromSeconds(60);
    private readonly ConcurrentDictionary<string, Session> _sessions = new();
    private readonly SemaphoreSlim _startGate = new(1, 1);

    public Task RunAsync(WebSocket socket, string container, CancellationToken cancellationToken) =>
        RunSessionAsync(socket, container, (start, ct) => StartExecAsync(container, start, ct), cancellationToken);

    public Task RunHostAsync(WebSocket socket, CancellationToken cancellationToken) =>
        RunSessionAsync(socket, "host",
            (start, _) => Task.FromResult(new Started(HostShell.Start(Number(start, "cols", 120), Number(start, "rows", 30)), HostShell.Label, Traced: false)),
            cancellationToken);

    private async Task RunSessionAsync(WebSocket socket, string target, Func<JsonElement, CancellationToken, Task<Started>> starter, CancellationToken cancellationToken)
    {
        try
        {
            var limits = options.CurrentValue;
            var first = await ReceiveAsync(socket, limits, cancellationToken);
            if (first is null)
            {
                return;
            }

            Session? session;
            if (Type(first.Value) == "resume")
            {
                var id = Text(first.Value, "sessionId");
                if (!_sessions.TryGetValue(id, out session) || session.Target != target)
                {
                    await SendAsync(socket, new { type = "error", message = "Terminal session not found or expired." }, cancellationToken);
                    return;
                }
            }
            else if (Type(first.Value) == "start")
            {
                await _startGate.WaitAsync(cancellationToken);
                try
                {
                    if (limits.MaxSessions > 0 && _sessions.Count >= limits.MaxSessions)
                    {
                        await SendAsync(socket, new { type = "error", message = $"Too many terminal sessions are open ({limits.MaxSessions}). Close one and try again." }, cancellationToken);
                        return;
                    }

                    var started = await starter(first.Value, cancellationToken);
                    session = new Session(Guid.NewGuid().ToString("N"), target, started, limits, activity, logger,
                        id => _sessions.TryRemove(id, out _));
                    if (!_sessions.TryAdd(session.Id, session))
                    {
                        started.Process.Dispose();
                        throw new InvalidOperationException("Could not register terminal session.");
                    }

                    session.Start();
                }
                finally
                {
                    _startGate.Release();
                }
            }
            else
            {
                await SendAsync(socket, new { type = "error", message = "The session must begin with a start or resume message." }, cancellationToken);
                return;
            }

            await session.AttachAsync(socket, cancellationToken);
        }
        catch (Exception ex) when (ex is WslcException or WslcNotFoundException or ArgumentException or IOException)
        {
            if (socket.State == WebSocketState.Open)
            {
                await SendAsync(socket, new { type = "error", message = ex.Message }, CancellationToken.None);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException)
        {
            logger.LogInformation("terminal for {Target} disconnected: {Message}", target, ex.Message);
        }
        finally
        {
            await CloseAsync(socket);
        }
    }

    private async Task<Started> StartExecAsync(string container, JsonElement start, CancellationToken cancellationToken)
    {
        var id = WslcArgs.Require(container, "container");
        var shell = await ContainerShell.ResolveAsync(wslc, id, Text(start, "command"), cancellationToken);
        List<string> args = ["exec", "--interactive"];
        if (wslc.SupportsTerminal)
        {
            args.Add("--tty");
        }

        args.Add(id);
        args.AddRange(shell);
        return new Started(wslc.StartInteractive(args, Number(start, "cols", 120), Number(start, "rows", 30)), "", Traced: true);
    }

    private sealed class Session(
        string id, string target, Started started, ExecTerminalOptions limits,
        ICliActivity activity, ILogger logger, Action<string> remove)
    {
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly CancellationTokenSource _stop = new();
        private readonly Queue<(string Data, int Bytes)> _pending = new();
        private readonly SessionClock _clock = new(DateTimeOffset.UtcNow);
        private readonly Stopwatch _elapsed = Stopwatch.StartNew();
        private WebSocket? _socket;
        private DateTimeOffset? _detachedAt = DateTimeOffset.UtcNow;
        private int _pendingBytes;
        private int? _exitCode;
        private bool _finished;
        private int _stopping;

        public string Id => id;
        public string Target => target;

        public void Start() => _ = RunAsync();

        private async Task RunAsync()
        {
            var process = started.Process;
            var trace = started.Traced ? activity.Start(process.Args) : null;
            try
            {
                var output = Task.WhenAll(PumpAsync(process.Output), PumpAsync(process.Error));
                var policy = PolicyAsync();
                try
                {
                    await process.WaitForExitAsync(_stop.Token);
                    await Task.WhenAny(output, Task.Delay(TimeSpan.FromSeconds(1), _stop.Token));
                    await _gate.WaitAsync();
                    try
                    {
                        _exitCode = process.ExitCode;
                        if (_socket is not null)
                        {
                            await SendAsync(_socket, new { type = "exit", code = _exitCode }, CancellationToken.None);
                            await EndAsync(_socket);
                            Stop();
                        }
                    }
                    finally
                    {
                        _gate.Release();
                    }

                    // A detached shell may finish while its client is away; keep its output and exit code.
                    await Task.Delay(Timeout.InfiniteTimeSpan, _stop.Token);
                }
                catch (OperationCanceledException)
                {
                    // Closed explicitly, or a policy limit was reached.
                }

                Stop();
                await Task.WhenAny(output, Task.Delay(TimeSpan.FromSeconds(2)));
                await policy;
            }
            finally
            {
                process.Kill();
                process.Dispose();
                _elapsed.Stop();
                if (trace is not null)
                {
                    activity.Finish(trace, _elapsed.Elapsed, process.ExitCode, process.ExitCode == 0 ? "success" : "error", "", "");
                }

                await _gate.WaitAsync();
                try
                {
                    _finished = true;
                }
                finally
                {
                    _gate.Release();
                }

                remove(id);
            }
        }

        public async Task AttachAsync(WebSocket socket, CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken);
            try
            {
                if (_finished || _stopping != 0 || _socket is not null ||
                    (_detachedAt is { } detached && DateTimeOffset.UtcNow - detached >= TimeSpan.FromSeconds(limits.ResumeTimeoutSeconds)))
                {
                    await SendAsync(socket, new { type = "error", message = "Terminal session is no longer available." }, cancellationToken);
                    return;
                }

                _socket = socket;
                _detachedAt = null;
                await SendAsync(socket, new { type = "ready", backend = started.Process.Backend, pty = started.Process.HasTerminal, label = started.Label, sessionId = id }, cancellationToken);
                while (_pending.TryDequeue(out var item))
                {
                    _pendingBytes -= item.Bytes;
                    await SendAsync(socket, new { type = "stdout", data = item.Data }, cancellationToken);
                }

                if (_exitCode is { } code)
                {
                    await SendAsync(socket, new { type = "exit", code }, cancellationToken);
                    await EndAsync(socket);
                    Stop();
                    return;
                }
            }
            finally
            {
                _gate.Release();
            }

            try
            {
                using var receive = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);
                while (!_stop.IsCancellationRequested && socket.State == WebSocketState.Open)
                {
                    var message = await ReceiveAsync(socket, limits, receive.Token);
                    if (message is null)
                    {
                        break;
                    }

                    _clock.Touch();
                    switch (Type(message.Value))
                    {
                        case "stdin":
                            await started.Process.Input.WriteAsync(Text(message.Value, "data"));
                            await started.Process.Input.FlushAsync(CancellationToken.None);
                            break;
                        case "resize":
                            started.Process.Resize(Number(message.Value, "cols", 120), Number(message.Value, "rows", 30));
                            break;
                        case "ping":
                            await SendToClientAsync(new { type = "pong" });
                            break;
                        case "close":
                            Stop();
                            return;
                    }
                }
            }
            finally
            {
                await _gate.WaitAsync();
                try
                {
                    if (_socket == socket)
                    {
                        _socket = null;
                        _detachedAt = DateTimeOffset.UtcNow;
                    }
                }
                finally
                {
                    _gate.Release();
                }
            }
        }

        private async Task PumpAsync(TextReader? reader)
        {
            if (reader is null)
            {
                return;
            }

            var buffer = new char[4096];
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    var count = await reader.ReadAsync(buffer, _stop.Token);
                    if (count == 0)
                    {
                        return;
                    }

                    _clock.Touch();
                    var text = new string(buffer, 0, count);
                    await _gate.WaitAsync(_stop.Token);
                    try
                    {
                        if (_socket is { State: WebSocketState.Open } socket)
                        {
                            await SendAsync(socket, new { type = "stdout", data = text }, _stop.Token);
                        }
                        else
                        {
                            var bytes = Encoding.UTF8.GetByteCount(text);
                            _pending.Enqueue((text, bytes));
                            _pendingBytes += bytes;
                            while (_pendingBytes > limits.MaxBufferedBytes && _pending.TryDequeue(out var discarded))
                            {
                                _pendingBytes -= discarded.Bytes;
                            }
                        }
                    }
                    finally
                    {
                        _gate.Release();
                    }
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException)
            {
                logger.LogDebug("Terminal output ended: {Message}", ex.Message);
            }
        }

        private async Task SendToClientAsync(object message)
        {
            await _gate.WaitAsync();
            try
            {
                if (_socket is { State: WebSocketState.Open } socket)
                {
                    await SendAsync(socket, message, CancellationToken.None);
                }
            }
            finally
            {
                _gate.Release();
            }
        }

        private async Task PolicyAsync()
        {
            var nextBeat = DateTimeOffset.UtcNow + Heartbeat;
            var warned = false;
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    await Task.Delay(PolicyTick, _stop.Token);
                    var now = DateTimeOffset.UtcNow;
                    if (Passed(limits.IdleTimeoutSeconds, _clock.LastActivity, now)
                        || Passed(limits.MaxLifetimeSeconds, _clock.StartedAt, now)
                        || (_detachedAt is { } detached && now - detached >= TimeSpan.FromSeconds(limits.ResumeTimeoutSeconds)))
                    {
                        await SendToClientAsync(new { type = "error", message = "The session was closed after being idle, disconnected or open for too long." });
                        Stop();
                        return;
                    }

                    var closing = Passed(limits.IdleTimeoutSeconds, _clock.LastActivity + CloseWarning, now)
                        || Passed(limits.MaxLifetimeSeconds, _clock.StartedAt + CloseWarning, now);
                    if (closing && !warned)
                    {
                        await SendToClientAsync(new { type = "warning", message = "This session is about to close; press a key to keep it." });
                    }

                    warned = closing;
                    if (now >= nextBeat)
                    {
                        nextBeat = now + Heartbeat;
                        await SendToClientAsync(new { type = "ping" });
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Another limit or the client closed the shell.
            }
        }

        private void Stop()
        {
            if (Interlocked.Exchange(ref _stopping, 1) == 0)
            {
                _stop.Cancel();
                started.Process.Kill();
            }
        }
    }

    private static bool Passed(int seconds, DateTimeOffset since, DateTimeOffset now) =>
        seconds > 0 && now - since >= TimeSpan.FromSeconds(seconds);

    private static string Type(JsonElement message) => Text(message, "type");

    private static int Number(JsonElement message, string property, int fallback) =>
        message.ValueKind == JsonValueKind.Object && message.TryGetProperty(property, out var value) && value.TryGetInt32(out var number) && number > 0
            ? number : fallback;

    private static string Text(JsonElement message, string property) =>
        message.ValueKind == JsonValueKind.Object && message.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? "" : "";

    private static async Task<JsonElement?> ReceiveAsync(WebSocket socket, ExecTerminalOptions limits, CancellationToken cancellationToken)
    {
        var message = new System.Buffers.ArrayBufferWriter<byte>(4096);
        var chunk = new byte[4096];
        while (true)
        {
            var received = await socket.ReceiveAsync(chunk, cancellationToken);
            if (received.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            message.Write(chunk.AsSpan(0, received.Count));
            if (limits.MaxMessageBytes > 0 && message.WrittenCount > limits.MaxMessageBytes)
            {
                throw new IOException($"A terminal message went over {limits.MaxMessageBytes} bytes.");
            }

            if (received.EndOfMessage)
            {
                break;
            }
        }

        try
        {
            return JsonDocument.Parse(message.WrittenMemory).RootElement.Clone();
        }
        catch (JsonException)
        {
            throw new IOException("A terminal message was not JSON.");
        }
    }

    private static async Task SendAsync(WebSocket socket, object message, CancellationToken cancellationToken)
    {
        if (socket.State != WebSocketState.Open)
        {
            return;
        }

        try
        {
            await socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(message, Json), WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException)
        {
            // A disconnected client can resume this session.
        }
    }

    private static async Task EndAsync(WebSocket socket)
    {
        if (socket.State == WebSocketState.Open)
        {
            try
            {
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "session ended", CancellationToken.None);
            }
            catch (Exception ex) when (ex is WebSocketException or ObjectDisposedException or InvalidOperationException)
            {
                // Already closing.
            }
        }
    }

    private static async Task CloseAsync(WebSocket socket)
    {
        if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            try
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "session ended", CancellationToken.None);
            }
            catch (Exception ex) when (ex is WebSocketException or ObjectDisposedException)
            {
                // Already gone.
            }
        }
    }

    private sealed record Started(IWslcSession Process, string Label, bool Traced);

    private sealed class SessionClock(DateTimeOffset startedAt)
    {
        private long _lastActivity = startedAt.UtcTicks;
        public DateTimeOffset StartedAt { get; } = startedAt;
        public DateTimeOffset LastActivity => new(Interlocked.Read(ref _lastActivity), TimeSpan.Zero);
        public void Touch() => Interlocked.Exchange(ref _lastActivity, DateTimeOffset.UtcNow.UtcTicks);
    }
}
