using Berpiztu.Dashboard.Fields;

namespace Berpiztu.Dashboard.Sources;

/// <summary>
/// One family an object's source comes from (the containers, the volumes…),
/// given by the application: the SDK knows a source only as this family's key
/// and a value, and what the value is — a container's registry uid — is the
/// application's. The family hands its rows, a value and a name each; the
/// properties window drops them as a list under the field (ListField), as it
/// drops any list, so a family is its rows and nothing of the screen.
/// </summary>
public interface ISourceFamily
{
    /// <summary>The key objects name in their descriptor (<c>container</c>).</summary>
    string Key { get; }

    /// <summary>What the field is called ("Container").</summary>
    string Label { get; }

    /// <summary>What one of the family is called, for the words around it ("container").</summary>
    string Noun { get; }

    /// <summary>
    /// Every one of the family there is now, its value and the name it reads
    /// by, in the order the list shows them. Read each time the list opens;
    /// empty when there are none, or when they could not be read.
    /// </summary>
    Task<IReadOnlyList<FieldOption<string>>> ListAsync();
}

/// <summary>The families the application registered, by key.</summary>
public sealed class SourceFamilies(IEnumerable<ISourceFamily> families)
{
    private readonly Dictionary<string, ISourceFamily> _byKey = families.ToDictionary(family => family.Key, StringComparer.Ordinal);

    public ISourceFamily? Find(string key) => _byKey.GetValueOrDefault(key);
}
