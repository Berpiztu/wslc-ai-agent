using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using WslcAgent.ApiClient;
using Microsoft.Extensions.DependencyInjection;
using WslcAgent.UI;
using WslcAgent.UI.Components;
using WslcAgent.Web;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// The UI is served by the agent, so the API lives at the same origin.
// The timeout must outlast an image pull, which the agent runs synchronously.
builder.Services.AddScoped(services => new HttpClient(UiServiceCollectionExtensions.Http(services)) { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress), Timeout = WslcAgentApi.RequestTimeout });
builder.Services.AddWslcAgentUi();

var host = builder.Build();

// The table-or-cards choice of each list is this device's, and it is read before
// the first paint: asking for it once the app runs would show every list as rows
// for an instant and correct it in front of the user.
await host.Services.GetRequiredService<ViewPreference>().ReadyAsync();

await host.RunAsync();
