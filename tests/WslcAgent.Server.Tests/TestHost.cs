using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Tests;

/// <summary>The agent under test: a scripted CLI and its own state folder, never the user's.</summary>
public static class TestHost
{
    /// <summary>Sent by a test that wants to be a caller from another machine.</summary>
    public const string RemoteHeader = "X-Test-Remote";

    /// <summary>A caller at the agent's machine, as the agent's own UI there is: no login asked.</summary>
    public static HttpClient ClientWith(this WebApplicationFactory<Program> factory, IWslcRunner runner) =>
        factory.Agent(runner).CreateClient();

    /// <summary>The agent with a scripted CLI; its callers are local unless they send <see cref="RemoteHeader"/>.</summary>
    public static WebApplicationFactory<Program> Agent(this WebApplicationFactory<Program> factory, IWslcRunner runner) =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Wslc:DataDirectory", TempDataDirectory());
            // Never the machine's own package folder: an agent installer there
            // would be offered to the updater under test.
            builder.UseSetting("Wslc:ClientPackagesPath", TempDataDirectory());
            builder.ConfigureServices(services =>
            {
                services.Replace(ServiceDescriptor.Singleton(runner));
                services.AddSingleton<IStartupFilter, CallerAddress>();

                // The event reader asks the scripted CLI in the background, at
                // its own pace — whether the session runs, then its events — and
                // a test that counts what a call made the agent run counted
                // those too, one more command than the call's.
                foreach (var reader in services.Where(descriptor => descriptor.ImplementationType == typeof(WslcEventReader)).ToList())
                {
                    services.Remove(reader);
                }
            });
        });

    public static string TempDataDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "wslc-agent-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>The in-memory server gives no client address: loopback, or a LAN one for a remote test.</summary>
    private sealed class CallerAddress : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((HttpContext http, RequestDelegate call) =>
            {
                http.Connection.RemoteIpAddress = http.Request.Headers.ContainsKey(RemoteHeader) ? IPAddress.Parse("192.168.1.50") : IPAddress.Loopback;
                return call(http);
            });
            next(app);
        };
    }
}
