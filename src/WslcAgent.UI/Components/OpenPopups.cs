namespace WslcAgent.UI.Components;

/// <summary>
/// How many row menus are open on the page. While one is, the list pages
/// skip their periodic refresh: a refresh rebuilds the rows and would close
/// the menu under the user's pointer. A menu closes the usual ways only, by
/// choosing an entry or clicking outside it.
/// </summary>
public sealed class OpenPopups
{
    private int _open;

    public bool AnyOpen => _open > 0;

    public void Set(bool open) => _open = Math.Max(0, _open + (open ? 1 : -1));
}
