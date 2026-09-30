namespace WslcAgent.ApiClient;

/// <summary>The agent answered an error; <see cref="Message"/> is the problem detail meant for the user.</summary>
public sealed class AgentApiException(int statusCode, string message, IReadOnlyDictionary<string, string>? fields = null) : Exception(message)
{
    private static readonly IReadOnlyDictionary<string, string> NoFields = new Dictionary<string, string>();

    public int StatusCode { get; } = statusCode;

    /// <summary>A failed launch: the form's fields the failure is about (<c>Contracts.LaunchFields</c>), each with the reason; empty otherwise.</summary>
    public IReadOnlyDictionary<string, string> Fields { get; } = fields ?? NoFields;
}
