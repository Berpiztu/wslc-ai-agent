using System.Reflection;
using Berpiztu.Dashboard.Catalogue;
using Berpiztu.Dashboard.Designer;
using Berpiztu.Dashboard.Sources;
using Microsoft.Extensions.DependencyInjection;

namespace Berpiztu.Dashboard;

public static class DashboardServiceCollectionExtensions
{
    /// <summary>
    /// The dashboard, with every object found in <paramref name="objects"/>
    /// (the application's own, beside the SDK's). The application registers
    /// its <see cref="ISourceFamily"/> implementations and gives the designer
    /// its <see cref="Storage.IDashboardStore"/>.
    /// </summary>
    public static IServiceCollection AddBerpiztuDashboard(this IServiceCollection services, params Assembly[] objects)
    {
        services.AddSingleton(new ObjectCatalogue([typeof(DashboardObject).Assembly, .. objects]));
        services.AddScoped<SourceFamilies>();
        services.AddScoped<DashboardInterop>();
        return services;
    }
}
