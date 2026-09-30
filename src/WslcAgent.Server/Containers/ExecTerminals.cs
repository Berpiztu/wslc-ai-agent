using System.Buffers;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text.Json;
using Microsoft.Extensions.Options;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Containers;

/// <summary>
/// The interactive terminals: one <c>wslc exec --interactive</c> process, or the
/// Terminal page's host shell (<see cref="HostShell"/>),
/// bridged to one WebSocket.
/// <code>
/// client → {"type":"start","command":"/bin/sh -i"} {"type":"stdin","data":"…"}
///          {"type":"resize","cols":120,"rows":30} {"type":"pong"} {"type":"close"}
/// agent  → {"type":"ready","backend":"conpty","pty":true,"label":"…"} {"type":"stdout","data":"…"}
///          {"type":"ping"} {"type":"warning","message":"…"} {"type":"exit","code":N}
///          {"type":"error","message":"…"}
/// </code>
/// On Windows the process runs behind a pseudo console, so <c>pty</c> is true:
/// the shell echoes, draws its own prompt and answers a resize, and the client
/// only renders. Without one the backend is <c>pipe</c>, <c>pty</c> is false
/// and the client echoes and sends whole lines.
/// </summary>
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

    private int _open;

    /// <summary>Runs one exec session until the shell exits, the client leaves or a limit closes it.</summary>
    public Task RunAsync(WebSocket socket, string container, CancellationToken cancellationToken) =>
        RunSessionAsync(socket, container, (start, ct) => StartExecAsync(container, start, ct), cancellationToken);

    /// <summary>
    /// The Terminal page's host shell over the same protocol and limits. It is not a
    /// <c>wslc</c> command, so CLI Activity does not list it.
    /// </summary>
    public Task RunHostAsync(WebSocket socket, CancellationToken cancellationToken) =>
        RunSessionAsync(
            socket,
            "host",
            (start, _) => Task.FromResult(new Started(HostShell.Start(Number(start, "cols", 120), Number(start, "rows", 30)), HostShell.Label, Traced: false)),
            cancellationToken);

    private async Task RunSessionAsync(WebSocket socket, string target, Func<JsonElement, CancellationToken, Task<Started>> start, CancellationToken cancellationToken)
    {
        var limits = options.CurrentValue;
        if (limits.MaxSessions > 0 && Interlocked.Increment(ref _open) > limits.MaxSessions)
        {
            Interlocked.Decrement(ref _open);
            await SendAsync(socket, new { type = "error", message = $"Too many terminal sessions are open ({limits.MaxSessions}). Close one and try again." }, cancellationToken);
            await CloseAsync(socket);
            return;
        }

        try
        {
            await BridgeAsync(socket, start, limits, cancellationToken);
        }
        catch (Exception ex) when (ex is WslcException or WslcNotFoundException or ArgumentException or IOException)
        {
            await SendAsync(socket, new { type = "error", message = ex.Message }, CancellationToken.None);
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException)
        {
            logger.LogInformation("terminal for {Target} ended: {Message}", target, ex.Message);
        }
        finally
        {
            Interlocked.Decrement(ref _open);
            await CloseAsync(socket);
        }
    }

    /// <summary><c>wslc exec --interactive [--tty] ID SHELL</c>.</summary>
    private async Task<Started> StartExecAsync(string container, JsonElement start, CancellationToken cancellationToken)
    {
        var id = WslcArgs.Require(container, "container");
        var shell = await ContainerShell.ResolveAsync(wslc, id, Text(start, "command"), cancellationToken);
        // --tty only where a pseudo console gives the shell a real terminal;
        // asking for one over pipes is what made wslc print "can't access tty".
        List<string> args = ["exec", "--interactive"];
        if (wslc.SupportsTerminal)
        {
            args.Add("--tty");
        }

        args.Add(id);
        args.AddRange(shell);
        return new Started(wslc.StartInteractive(args, Number(start, "cols", 120), Number(start, "rows", 30)), "", Traced: true);
    }

    private async Task BridgeAsync(WebSocket socket, Func<JsonElement, CancellationToken, Task<Started>> starter, ExecTerminalOptions limits, CancellationToken cancellationToken)
    {
        if (await ReceiveAsync(socket, limits, cancellationToken) is not { } start || Type(start) != "start")
        {
            await SendAsync(socket, new { type = "error", message = "The session must begin with a start message." }, cancellationToken);
            return;
        }

        var started = await starter(start, cancellationToken);
        using var process = started.Process;
        var startedAt = DateTimeOffset.UtcNow;
        var elapsed = Stopwatch.StartNew();
        // Running for as long as the terminal is open, as any other command is while it runs.
        var trace = started.Traced ? activity.Start(process.Args) : null;
        using var session = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var clock = new SessionClock(startedAt);

        await SendAsync(socket, new { type = "ready", backend = process.Backend, pty = process.HasTerminal, label = started.Label }, cancellationToken);

        var output = Task.WhenAll(
            PumpAsync(process.Output, socket, session, clock),
            PumpAsync(process.Error, socket, session, clock));
        var background = Task.WhenAll(
            output,
            ExitAsync(process, output, socket, session),
            PolicyAsync(socket, limits, session, clock));

        try
        {
            await ReadClientAsync(socket, process, limits, session, clock);
        }
        finally
        {
            await session.CancelAsync();
            process.Kill();
            await Task.WhenAny(background, Task.Delay(TimeSpan.FromSeconds(2), CancellationToken.None));
            elapsed.Stop();
            if (trace is not null)
            {
                activity.Finish(trace, elapsed.Elapsed, process.ExitCode, process.ExitCode == 0 ? "success" : "error", "", "");
            }
        }
    }

    /// <summary>Client messages until it closes the socket or the session ends.</summary>
    private static async Task ReadClientAsync(WebSocket socket, IWslcSession process, ExecTerminalOptions limits, CancellationTokenSource session, SessionClock clock)
    {
        while (!session.IsCancellationRequested && socket.State == WebSocketState.Open)
        {
            if (await ReceiveAsync(socket, limits, session.Token) is not { } message)
            {
                return;
            }

            clock.Touch();
            switch (Type(message))
            {
                case "stdin":
                    await process.Input.WriteAsync(Text(message, "data"));
                    await process.Input.FlushAsync(CancellationToken.None);
                    break;
                case "resize":
                    process.Resize(Number(message, "cols", 120), Number(message, "rows", 30));
                    break;
                case "ping":
                    await SendAsync(socket, new { type = "pong" }, session.Token);
                    break;
                case "close":
                    return;
                default:
                    // pong: nothing to do, it only says the client is alive.
                    break;
            }
        }
    }

    /// <summary>One output stream to the client, in slices as they arrive.</summary>
    private static async Task PumpAsync(TextReader? reader, WebSocket socket, CancellationTokenSource session, SessionClock clock)
    {
        if (reader is null)
        {
            // A terminal has one screen: there is no second stream to pump.
            return;
        }

        var buffer = new char[4096];
        try
        {
            while (!session.IsCancellationRequested)
            {
                var read = await reader.ReadAsync(buffer, session.Token);
                if (read <= 0)
                {
                    return;
                }

                clock.Touch();
                await SendAsync(socket, new { type = "stdout", data = new string(buffer, 0, read) }, session.Token);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException)
        {
            // The process died or the socket went away: the session is over.
        }
    }

    /// <summary>
    /// The shell's own end. Its last output goes out first (the pipes reach EOF
    /// when it exits), then the exit code, then the socket is closed from this
    /// side: cancelling the session instead would abort the connection and the
    /// client could lose the exit message it was just sent.
    /// </summary>
    private static async Task ExitAsync(IWslcSession process, Task output, WebSocket socket, CancellationTokenSource session)
    {
        try
        {
            await process.WaitForExitAsync(session.Token);
            await Task.WhenAny(output, Task.Delay(TimeSpan.FromSeconds(1), session.Token));
            await SendAsync(socket, new { type = "exit", code = process.ExitCode }, CancellationToken.None);
            await EndAsync(socket);
        }
        catch (OperationCanceledException)
        {
            // The client left first.
        }
        finally
        {
            await session.CancelAsync();
        }
    }

    /// <summary>
    /// The limits, checked on their own tick: the heartbeat, the warning a
    /// minute before, and the close itself. Cancelling the session aborts the
    /// pending receive, which is what ends the bridge.
    /// </summary>
    private static async Task PolicyAsync(WebSocket socket, ExecTerminalOptions limits, CancellationTokenSource session, SessionClock clock)
    {
        var nextBeat = DateTimeOffset.UtcNow + Heartbeat;
        var warned = false;
        try
        {
            while (!session.IsCancellationRequested)
            {
                await Task.Delay(PolicyTick, session.Token);
                var now = DateTimeOffset.UtcNow;

                if (Passed(limits.IdleTimeoutSeconds, clock.LastActivity, now) || Passed(limits.MaxLifetimeSeconds, clock.StartedAt, now))
                {
                    await SendAsync(socket, new { type = "error", message = "The session was closed after being idle or open for too long." }, CancellationToken.None);
                    await EndAsync(socket);
                    await session.CancelAsync();
                    return;
                }

                var closing = Passed(limits.IdleTimeoutSeconds, clock.LastActivity + CloseWarning, now)
                    || Passed(limits.MaxLifetimeSeconds, clock.StartedAt + CloseWarning, now);
                if (closing && !warned)
                {
                    await SendAsync(socket, new { type = "warning", message = "This session is about to close; press a key to keep it." }, session.Token);
                }

                warned = closing;

                if (now >= nextBeat)
                {
                    nextBeat = now + Heartbeat;
                    await SendAsync(socket, new { type = "ping" }, session.Token);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The session ended for another reason.
        }
    }

    private static bool Passed(int seconds, DateTimeOffset since, DateTimeOffset now) =>
        seconds > 0 && now - since >= TimeSpan.FromSeconds(seconds);

    private static string Type(JsonElement message) => Text(message, "type");

    /// <summary>A number the client sent (the window size), or the default when it sent none.</summary>
    private static int Number(JsonElement message, string property, int fallback) =>
        message.ValueKind == JsonValueKind.Object && message.TryGetProperty(property, out var value) && value.TryGetInt32(out var number) && number > 0
            ? number
            : fallback;

    private static string Text(JsonElement message, string property) =>
        message.ValueKind == JsonValueKind.Object && message.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    /// <summary>One client message, or null when the client closed the socket.</summary>
    private static async Task<JsonElement?> ReceiveAsync(WebSocket socket, ExecTerminalOptions limits, CancellationToken cancellationToken)
    {
        var message = new ArrayBufferWriter<byte>(4096);
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
            // The client left: the session ends on its own.
        }
    }

    /// <summary>
    /// Says goodbye without waiting for the answer: a plain close would need to
    /// read the reply, and the session's own receive loop owns that.
    /// </summary>
    private static async Task EndAsync(WebSocket socket)
    {
        if (socket.State != WebSocketState.Open)
        {
            return;
        }

        try
        {
            await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "session ended", CancellationToken.None);
        }
        catch (Exception ex) when (ex is WebSocketException or ObjectDisposedException or InvalidOperationException)
        {
            // Already closing or gone.
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

    /// <summary>A shell that has started: the process, what the client is told it is, and whether CLI Activity lists it.</summary>
    private sealed record Started(IWslcSession Process, string Label, bool Traced);

    /// <summary>When the session started and when it last saw a keystroke or a byte of output.</summary>
    private sealed class SessionClock(DateTimeOffset startedAt)
    {
        private long _lastActivity = startedAt.UtcTicks;

        public DateTimeOffset StartedAt { get; } = startedAt;

        public DateTimeOffset LastActivity => new(Interlocked.Read(ref _lastActivity), TimeSpan.Zero);

        public void Touch() => Interlocked.Exchange(ref _lastActivity, DateTimeOffset.UtcNow.UtcTicks);
    }
}
