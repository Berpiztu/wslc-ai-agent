using Berpiztu.Dashboard.Model;

namespace Berpiztu.Dashboard.Storage;

/// <summary>
/// Where the application keeps how each kind of object is born, view by view
/// (<see cref="ObjectDefault"/>): read by every dashboard, and written from
/// the board of every object where they are designed, by a build that ships
/// them (a development one) and by no other.
/// </summary>
public interface IObjectDefaults
{
    /// <summary>Whether they can be written here; false where they are only read.</summary>
    bool Writable { get; }

    /// <summary>Read once; later calls keep what was read.</summary>
    Task LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>How this kind is born in this view; null where none was designed, and it is born as its descriptor says.</summary>
    ObjectDefault? For(string view, string type);

    /// <summary>How this kind is born in this view from now on; false where it could not be written.</summary>
    Task<bool> SetAsync(string view, string type, ObjectDefault value, CancellationToken cancellationToken = default);
}
