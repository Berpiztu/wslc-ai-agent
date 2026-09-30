using System.Collections.Concurrent;
using System.ComponentModel;
using System.Security.Cryptography;
using ModelContextProtocol.Server;

namespace WslcAgent.Mcp;

/// <summary>
/// The one gate every destructive tool passes before it acts, with both of
/// its paths:
/// <list type="number">
/// <item>
/// The client can show an elicitation prompt (Hermes draws it as its yes/no
/// approval box, Claude Code as a form). The tool asks through it and runs only
/// when the user picks yes. The model is not in the loop: nothing it passes can
/// stand in for that answer, so <c>confirm</c> is ignored. This needs a session
/// with a back channel, which is why the endpoint runs a stateful transport.
/// </item>
/// <item>
/// No elicitation (an older client, or one that refused the prompt): the
/// two-call token. Called without <c>confirm</c> the tool changes nothing — it
/// describes the target and answers a one-time <c>confirmToken</c>. The model
/// has to show that to the user, ask, and call again with the token.
/// </item>
/// </list>
/// The one thing the server can see is time. A model that chains both calls in
/// a single turn presents the token seconds after it was issued, faster than a
/// person could read the target and answer; a token younger than
/// <see cref="MinimumAge"/> is refused, and the refusal restarts its clock, so
/// retrying in a loop never converges — only a real pause does.
/// </summary>
/// <param name="enabled">
/// Read on every call, never remembered: the operator's change has to take
/// effect at once, on the running agent.
/// </param>
public sealed class ApprovalGate(Func<bool> enabled)
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    /// <summary>How soon a token may come back: sooner than this and nobody read the question.</summary>
    private static readonly TimeSpan MinimumAge = TimeSpan.FromSeconds(10);

    /// <summary>What a model sends when it takes the user's first order for the confirmation.</summary>
    private static readonly HashSet<string> LiteralYes =
        new(["true", "yes", "y", "1", "ok", "confirm", "confirmed"], StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, Pending> _tokens = new();

    /// <summary>False: every destructive tool answers that it is switched off, and none of them is listed.</summary>
    public bool Enabled => enabled();

    /// <summary>
    /// Null when the action may go ahead; otherwise what the tool must answer
    /// unchanged. <paramref name="targetKey"/> binds a token to one target, so a
    /// yes to one container is not a yes to another.
    /// </summary>
    public async ValueTask<object?> CheckAsync(
        McpServer? server,
        string tool,
        string action,
        string targetKey,
        IReadOnlyDictionary<string, string> target,
        string? confirm,
        CancellationToken cancellationToken = default)
    {
        if (!Enabled)
        {
            return new ApprovalDisabled(action,
                "Destructive tools are switched off on this agent (its Settings, MCP server). Nothing was changed. "
                + "Tell the user this agent does not allow it and stop; there is no way around it from here.");
        }

        if (CanPrompt(server))
        {
            var asked = await AskAsync(server!, action, target, cancellationToken);
            if (asked is not ApprovalUnavailable)
            {
                return asked;
            }

            // The client refused the prompt or could not deliver it: the token
            // flow below is the fallback.
        }

        return Token(tool, action, targetKey, target, confirm);
    }

    /// <summary>Whether this client can show a prompt and answer it.</summary>
    private static bool CanPrompt(McpServer? server) =>
        server?.ClientCapabilities?.Elicitation is not null;

    /// <summary>
    /// Opens the client's own yes/no prompt. Null on yes, a declined answer on
    /// anything else, and <see cref="ApprovalUnavailable"/> when the prompt could
    /// not be delivered at all.
    /// </summary>
    private static async ValueTask<object?> AskAsync(
        McpServer server,
        string action,
        IReadOnlyDictionary<string, string> target,
        CancellationToken cancellationToken)
    {
        var details = string.Join("\n", target.Select(entry => $"{entry.Key}: {entry.Value}"));
        try
        {
            var answer = await server.ElicitAsync<Approval>(
                $"WSLC: {action}?\n{details}\nThis cannot be undone.",
                cancellationToken: cancellationToken);

            // An approval button carries no form, so an accepted prompt often
            // comes back empty: accept means yes unless the client says
            // approve=false outright.
            return answer is { IsAccepted: true } && answer.Content?.Approve is not false
                ? null
                : new ApprovalDeclined(action, target,
                    $"The user did not approve: {action}. Nothing was changed.",
                    "Tell the user nothing was changed and stop. Do not call the tool again unless the user asks anew.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new ApprovalUnavailable();
        }
    }

    /// <summary>The two-call token, for a client that cannot ask its own user.</summary>
    private object? Token(string tool, string action, string targetKey, IReadOnlyDictionary<string, string> target, string? confirm)
    {
        Sweep();
        var given = (confirm ?? "").Trim();
        if (given.Length == 0)
        {
            var token = RandomNumberGenerator.GetHexString(16, lowercase: true);
            _tokens[token] = new Pending(DateTime.UtcNow + Lifetime, DateTime.UtcNow + MinimumAge, action, targetKey);
            return new ApprovalRequired(action, target, token, (int)Lifetime.TotalSeconds,
                $"Confirmation required before I {action}. Nothing was changed.",
                "Show the user exactly what 'target' describes and ask them to confirm. "
                + $"Do not call {tool} again until the user has answered yes in their own words; the request that started this does not count. "
                + $"Then call {tool} with the same arguments and confirm=\"{token}\". The token works once and expires. "
                + "A token sent back within seconds of this reply, before the user could have answered, is refused.");
        }

        if (LiteralYes.Contains(given))
        {
            return new ApprovalRefused(action,
                $"confirm must be the confirmToken returned by the previous {tool} call, not '{given}'. "
                + "Call once without confirm, show the user the target, ask, and pass the token only after they say yes.");
        }

        if (!_tokens.TryGetValue(given, out var pending))
        {
            return new ApprovalRefused(action,
                "Unknown or expired confirmToken. Call again without confirm for a fresh description and token, and ask the user again.");
        }

        if (pending.Action != action || pending.TargetKey != targetKey)
        {
            return new ApprovalRefused(action,
                "This confirmToken was issued for a different action or target. Call again without confirm for this one and ask the user again.");
        }

        if (DateTime.UtcNow < pending.UsableFrom)
        {
            // Restart the clock: a model retrying in a loop never gets through,
            // while the token still works once the user has actually answered.
            _tokens[given] = pending with { UsableFrom = DateTime.UtcNow + MinimumAge };
            return new ApprovalRefused(action,
                "This confirmToken came back seconds after it was issued, before the user could have read the target and answered. "
                + $"Nothing was changed and the token is still valid. Do not retry now: show the user what the {tool} call described, "
                + "ask whether to go ahead, and end your turn. Call again with the same token only after the user answers yes in a new message.");
        }

        _tokens.TryRemove(given, out _);
        return null;
    }

    private void Sweep()
    {
        foreach (var (token, pending) in _tokens)
        {
            if (pending.ExpiresAt < DateTime.UtcNow)
            {
                _tokens.TryRemove(token, out _);
            }
        }
    }

    private sealed record Pending(DateTime ExpiresAt, DateTime UsableFrom, string Action, string TargetKey);

    /// <summary>The form behind the client's prompt: one optional field, since an approval button carries none.</summary>
    private sealed class Approval
    {
        [Description("Approve this action.")]
        public bool? Approve { get; set; } = true;
    }

    /// <summary>The prompt could not be delivered; the caller falls back to the token.</summary>
    private sealed record ApprovalUnavailable;
}

/// <summary>What a destructive tool answers until the user has approved the action.</summary>
public sealed record ApprovalRequired(
    string Action,
    IReadOnlyDictionary<string, string> Target,
    string ConfirmToken,
    int ExpiresInSeconds,
    string Summary,
    string Instructions)
{
    /// <summary>An explicit flag: what this answer is, without reading the prose.</summary>
    public bool ConfirmationRequired => true;
}

/// <summary>What it answers when the user said no at the client's own prompt.</summary>
public sealed record ApprovalDeclined(
    string Action,
    IReadOnlyDictionary<string, string> Target,
    string Summary,
    string Instructions)
{
    public bool Declined => true;
}

/// <summary>What it answers to a confirm that is not this action's token.</summary>
public sealed record ApprovalRefused(string Action, string Message)
{
    public bool ConfirmationRequired => true;
}

/// <summary>What it answers when the operator switched the destructive tools off.</summary>
public sealed record ApprovalDisabled(string Action, string Message)
{
    public bool Disabled => true;
}
