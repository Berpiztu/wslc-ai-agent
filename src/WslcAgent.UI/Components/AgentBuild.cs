using Microsoft.AspNetCore.Components;
using MudBlazor;
using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.UI.Components;

/// <summary>
/// Which build of the agent this application was served by. The agent stamps
/// its health answer with the id of its own assembly, which is new with every
/// compilation — the version is not, and a morning of rebuilds leaves it
/// saying the same thing. When the id changes, the code running in this
/// browser is no longer the code the agent is serving, and the two disagree in
/// ways that read as nonsense: a call answered with a shape the client cannot
/// parse, a screen that asks for an endpoint that has been renamed.
/// <para>
/// A development agent is rebuilt under the user all day, so the browser takes
/// the new one at once. Otherwise the user is asked, because reloading
/// somebody in the middle of a form is worse than the disagreement.
/// </para>
/// <para>
/// Only in the browser. A native client's screens are compiled into the
/// client, not served by the agent, so reloading it would fetch nothing new;
/// what it does about a newer agent is its own update check
/// (<see cref="Updates.ClientUpdateChecker"/>).
/// </para>
/// </summary>
public sealed class AgentBuild(NavigationManager navigation, ISnackbar snackbar)
{
    private string _build = "";
    private bool _asked;

    /// <summary>
    /// The agent last said it is a development build: stopped and started
    /// under the user all day, so its silence is no news and the screen says
    /// nothing of it (MainLayout's reconnecting line and veil).
    /// </summary>
    public bool Development { get; private set; }

    /// <summary>
    /// What the agent has just said about itself, from wherever the
    /// application was already asking. The first answer is the one everything
    /// else is compared with; an agent too old to say its build says nothing
    /// and is left alone.
    /// </summary>
    public void Seen(HealthResponse? health)
    {
        if (health is not null)
        {
            Development = health.Development;
        }

        if (health is not { Build.Length: > 0 } answer || !OperatingSystem.IsBrowser())
        {
            return;
        }

        if (_build.Length == 0)
        {
            _build = answer.Build;
            return;
        }

        if (_build == answer.Build)
        {
            return;
        }

        _build = answer.Build;
        if (answer.Development)
        {
            Reload();
            return;
        }

        if (!_asked)
        {
            _asked = true;
            snackbar.Add($"The agent is now {answer.Version}. Reload to take it.", Severity.Info, options =>
            {
                options.Action = "Reload";
                options.ActionColor = Color.Primary;
                options.RequireInteraction = true;
                options.OnClick = _ =>
                {
                    Reload();
                    return Task.CompletedTask;
                };
            });
        }
    }

    /// <summary>The page again, from the agent: the whole application, not a navigation inside it.</summary>
    private void Reload() => navigation.NavigateTo(navigation.Uri, forceLoad: true);
}
