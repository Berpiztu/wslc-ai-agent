# Plugins

Code that runs inside the agent and is not the agent's: what one installation
needs and nobody else does (an application of its own asking the host for
something only the host can see) lives in that application's repository, as a
plugin, and never in this one.

A plugin is a standard ASP.NET Core **hosting startup**: an assembly with
`[assembly: HostingStartup(typeof(...))]` naming a class that implements
`IHostingStartup`. It refers to nothing of the agent's, only to ASP.NET Core
(`<FrameworkReference Include="Microsoft.AspNetCore.App" />`), so the same
.dll works in any ASP.NET Core host that loads hosting startups.

## What the agent does with one

- **Installed in Settings → Plugins, never by a folder.** Install takes a .dll
  of the agent's machine, refuses one with no hosting startup, copies the files
  beside it and its `runtimes` folder into `plugins/<assembly name>/` of the
  agent's data folder and records it in `plugins.json`, switched on. A folder
  put there by hand is not loaded.
- **Loaded and unloaded while the agent runs.** Installing, switching and
  removing take effect at once, with no new start of the agent; at its start,
  every plugin switched on is loaded. Its assemblies go in a collectible load
  context of their own (read into memory, so its folder is never locked), the
  framework's and the agent's own assemblies shared. Its hosting startups are
  given a web host of the plugin's own (`PluginWebHostBuilder`): what
  `ConfigureServices` adds is built into services of its own, which hold the
  agent's logging, configuration and environment; its `IHostedService`s are
  started, and stopped when it is unloaded. A request already in a plugin
  being unloaded is given 30 seconds to end before the plugin's services go.
- **Behind the agent's login.** The `IStartupFilter`s a plugin registers make
  its pipeline, which the agent runs after its login: what the plugin answers
  it answers, and what it lets go on goes to the next plugin and then to the
  agent. Its endpoints therefore ask for what every `/api/v1` call asks: a
  session, the API token, or a caller at the agent's machine. Inside its
  pipeline `RequestServices` are the plugin's, the agent's behind them. Its
  paths are answered by middleware (`app.Map`, `app.Use`): the agent's own
  routing has matched the request before, so `UseRouting` and `UseEndpoints`
  in a plugin find nothing to do.
- **Every request it answers is in its log.** A request the plugin answers
  itself, not letting it go on, is written under `Plugin NAME`: method, path,
  status, milliseconds, and who asked: the name the caller gives in
  `X-Caller` (a container sends its own, which its address does not tell: a
  container's request may arrive from the loopback), its address and its
  User-Agent.
- **Its log is the agent's.** What a plugin writes through `ILogger<T>` goes to
  the agent's log under `Plugin NAME/its logger` (`Plugin
  IaExtract.EngineFacts/ProviderFacts`: the plugin's name taken off the front
  of a type's), beside the agent's lines about it (`Plugin NAME`: installed,
  switched, loaded, not loaded and why, unloaded, removed). The Logs page's
  **Plugins** filter shows every plugin's lines together, their source saying
  which; Settings → Plugins shows one plugin's.
- **A plugin that fails does not stop the agent.** It is not loaded, and the
  reason is in Settings and in the log.

A plugin runs with everything the agent may do on the machine. Only code that
is trusted is installed.

## Writing one

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Description>What it is, shown in Settings → Plugins.</Description>
  </PropertyGroup>
  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>
</Project>
```

```csharp
[assembly: HostingStartup(typeof(MyPlugin.Startup))]

namespace MyPlugin;

public sealed class Startup : IHostingStartup
{
    public void Configure(IWebHostBuilder builder) => builder.ConfigureServices(services =>
    {
        services.AddSingleton<MyService>();
        services.AddTransient<IStartupFilter, Endpoints>();
    });
}

internal sealed class Endpoints : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        // A branch of its own: no routing of the host's is touched, in the agent or in any other host.
        app.Map("/api/v1/plugins/my-plugin/hello", branch => branch.Run(http => http.Response.WriteAsync("hello")));
        next(app);
    };
}
```

Its paths are its own choice; `/api/v1/plugins/<its name>/…` keeps them apart
from the agent's. Publish it to a folder of its own (`dotnet publish -c
Release -o <folder>`) and install the .dll from there.
