using Microsoft.AspNetCore.Components;
using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.UI.Dashboard;

/// <summary>An object of type <c>container</c>: its source is a container, and it reads the container list.</summary>
public abstract class ContainerObject : RegistryObject<IReadOnlyList<ContainerSummary>, ContainerSummary>
{
    [Inject] protected ContainerRows Containers { get; set; } = default!;

    protected override RegistryRows<IReadOnlyList<ContainerSummary>, ContainerSummary> Rows => Containers;

    /// <summary>The container's row; null until the list is read, or while its container is not in it.</summary>
    protected ContainerSummary? Container => Resource;
}
