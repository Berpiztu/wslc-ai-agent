using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Projects;

namespace WslcAgent.Server.Tests;

/// <summary>
/// The reader is where a mistake goes unseen: a file read wrong brings up the
/// wrong containers, and nothing on the way says so.
/// </summary>
public sealed class ComposeReaderTests
{
    private const string Folder = @"C:\apps\shop";

    private static ComposeSource Source(string folder = Folder, Dictionary<string, string>? variables = null, params string[] profiles) =>
        new("", folder, variables ?? new Dictionary<string, string>(), profiles, _ => null);

    [Fact]
    public void A_folder_with_a_link_the_file_mounts_over_is_mounted_as_its_entries()
    {
        // WSLC does not follow a Windows junction: data\IA\finals is one, and the file mounts over it.
        var folders = new Dictionary<string, HostEntry[]>(StringComparer.OrdinalIgnoreCase)
        {
            [@"C:\apps\shop\data"] = [new("IA", ""), new("uploads", ""), new("users.json", "")],
            [@"C:\apps\shop\data\IA"] = [new("finals", @"D:\cloud\finals"), new("notes", "")],
        };
        var source = Source() with { ListFolder = path => folders.GetValueOrDefault(path) };

        var plan = ComposeReader.Read("""
            services:
              web:
                image: nginx
                volumes:
                  - ./data:/app/data
                  - ./data/IA/finals:/app/data/IA/finals
                  - ./data/uploads:/app/data/uploads
                  - ./logs:/app/logs
            """, source);

        // The folder's entries apart, the link from its real folder, and what the file mounts itself left to its own line.
        Assert.Equal(
            [
                @"C:\apps\shop\data\IA\notes:/app/data/IA/notes",
                @"C:\apps\shop\data\users.json:/app/data/users.json",
                @"D:\cloud\finals:/app/data/IA/finals",
                @"C:\apps\shop\data\uploads:/app/data/uploads",
                @"C:\apps\shop\logs:/app/logs",
            ],
            plan.Services[0].Launch.Volumes);
        Assert.Contains(plan.Warnings, warning => warning.Contains("saved again", StringComparison.Ordinal));
    }

    [Fact]
    public void A_file_becomes_the_launch_requests_the_run_form_sends()
    {
        var plan = ComposeReader.Read("""
            services:
              web:
                image: nginx:1.27
                ports:
                  - "8080:80"
                  - target: 443
                    published: 8443
                environment:
                  MODE: prod
                  DB_HOST: db
                volumes:
                  - ./site:/usr/share/nginx/html:ro
                  - cache:/var/cache/nginx
                depends_on:
                  db:
                    condition: service_healthy
                restart: unless-stopped
              db:
                image: postgres:16
                environment:
                  - POSTGRES_PASSWORD=secret
                volumes:
                  - data:/var/lib/postgresql/data
                healthcheck:
                  test: ["CMD-SHELL", "pg_isready -U postgres"]
                  interval: 1m30s
                  timeout: 5s
                  retries: 5
                stop_grace_period: 1m
            volumes:
              data:
              cache:
                name: shared-cache
            """, Source());

        Assert.Equal("shop", plan.Name);
        Assert.Empty(plan.Unsupported);
        Assert.Empty(plan.Warnings);

        // The one waited for starts first, whatever the file's order.
        Assert.Equal(["db", "web"], plan.Services.Select(service => service.Name));

        var web = plan.Services[1];
        Assert.Equal("nginx:1.27", web.Launch.Image);
        Assert.Equal("shop-web-1", web.Launch.Name);
        Assert.Equal(["8080:80", "8443:443"], web.Launch.Publish);
        Assert.Equal(["MODE=prod", "DB_HOST=db"], web.Launch.Env);
        Assert.Equal([@"C:\apps\shop\site:/usr/share/nginx/html:ro", "shared-cache:/var/cache/nginx"], web.Launch.Volumes);
        Assert.Equal("shop_default", web.Launch.Network);
        Assert.Equal(["web"], web.Launch.NetworkAliases);
        Assert.Equal(RestartPolicyInfo.UnlessStopped, web.Launch.RestartPolicy);
        Assert.Equal([new ComposeDependency("db", ComposeDependency.Healthy)], web.DependsOn);

        var db = plan.Services[0];
        Assert.Equal(["shop_data:/var/lib/postgresql/data"], db.Launch.Volumes);
        Assert.Equal("pg_isready -U postgres", db.Launch.HealthCmd);
        Assert.Equal("90s", db.Launch.HealthInterval);
        Assert.Equal("5s", db.Launch.HealthTimeout);
        Assert.Equal("5", db.Launch.HealthRetries);
        Assert.Equal("60", db.Launch.StopTimeout);

        Assert.Equal("shop_default", Assert.Single(plan.Networks).Name);
        Assert.Equal("shop_data", plan.Volumes.Single(volume => volume.Key == "data").Name);
        Assert.Equal("shared-cache", plan.Volumes.Single(volume => volume.Key == "cache").Name);
    }

    [Fact]
    public void What_cannot_be_given_is_named_and_never_dropped()
    {
        var plan = ComposeReader.Read("""
            services:
              app:
                image: app
                privileged: true
                pull_policy: always
                labels:
                  a: b
                hostname: shop
                dns: 1.1.1.1
                shm_size: 128MB
                ulimits:
                  nofile:
                    soft: 1024
                    hard: 2048
                extra_hosts:
                  - "host.docker.internal:host-gateway"
                  - "db:10.0.0.5"
                volumes:
                  - ./conf:/conf
                  - /var/run/docker.sock:/var/run/docker.sock
                restart: on-failure
                expose:
                  - "80"
            """, Source(folder: ""));

        Assert.Equal("project", plan.Name);
        Assert.Contains("app: privileged", plan.Unsupported);
        Assert.Contains("app: pull_policy (not yet)", plan.Unsupported);
        Assert.DoesNotContain(plan.Unsupported, item => item.Contains("labels", StringComparison.Ordinal) || item.Contains("hostname", StringComparison.Ordinal));
        Assert.Contains("app: extra_hosts db:10.0.0.5", plan.Unsupported);
        Assert.Contains(plan.Unsupported, item => item.StartsWith("app: volume ./conf", StringComparison.Ordinal));
        Assert.Contains(plan.Unsupported, item => item.StartsWith("app: volume /var/run/docker.sock", StringComparison.Ordinal));
        Assert.Contains(plan.Unsupported, item => item.StartsWith("app: restart on-failure", StringComparison.Ordinal));
        Assert.DoesNotContain(plan.Unsupported, item => item.Contains("expose", StringComparison.Ordinal));
        Assert.Equal(["app: extra_hosts host.docker.internal:host-gateway"], plan.NotNeeded);

        var app = Assert.Single(plan.Services).Launch;
        Assert.Equal(["a=b"], app.Labels);
        Assert.Equal("shop", app.Hostname);
        Assert.Equal(["1.1.1.1"], app.Dns);
        Assert.Equal("128m", app.ShmSize);
        Assert.Equal(["nofile=1024:2048"], app.Ulimits);
        Assert.Empty(app.Volumes);
        Assert.Equal(RestartPolicyInfo.No, app.RestartPolicy);
    }

    [Fact]
    public void Variables_merge_keys_and_profiles_are_read_as_compose_reads_them()
    {
        var plan = ComposeReader.Read("""
            x-common: &common
              restart: always
              environment:
                TZ: UTC
            services:
              api:
                <<: *common
                image: "api:${TAG:-latest}"
                environment:
                  KEY: ${SECRET}
              debug:
                <<: *common
                image: tool:${TOOL_TAG}
                profiles: [debug]
            """, Source(variables: new Dictionary<string, string> { ["SECRET"] = "s3" }));

        var api = plan.Services.Single(service => service.Name == "api");
        Assert.Equal("api:latest", api.Launch.Image);
        Assert.Equal(RestartPolicyInfo.Always, api.Launch.RestartPolicy);

        // A key written beside the merge replaces the merged one whole.
        Assert.Equal(["KEY=s3"], api.Launch.Env);
        Assert.True(api.Enabled);

        var debug = plan.Services.Single(service => service.Name == "debug");
        Assert.False(debug.Enabled);
        Assert.Equal("tool:", debug.Launch.Image);
        Assert.Equal(["TZ=UTC"], debug.Launch.Env);
        Assert.Contains("The variable TOOL_TAG is not set: read as empty.", plan.Warnings);

        // The file's parameters, as each was read: a value given, the file's default, nothing.
        Assert.Equal(
            [new ComposeVariable("TAG", "latest", false), new ComposeVariable("SECRET", "s3", true), new ComposeVariable("TOOL_TAG", "", false)],
            plan.Variables);
        Assert.Empty(plan.Unsupported);
    }

    /// <summary>The plan is read beside the file: a verdict on the wrong line says the wrong thing of what the user wrote.</summary>
    [Fact]
    public void Every_line_that_says_something_is_given_its_verdict()
    {
        var plan = ComposeReader.Read("""
            name: shop
            services:
              web:
                image: nginx
                restart: always
                privileged: true
                volumes:
                  - ./site:/app
                  - /app/node_modules
                  - data:/data
            volumes:
              data:
            """, Source());

        Assert.Equal(
            [
                ComposeLine.Ok,             // name: shop
                "",                         // services:
                ComposeLine.Converted,      //   web:
                ComposeLine.Ok,             //     image: nginx
                ComposeLine.Converted,      //     restart: always (the agent's doing, not WSLC's)
                ComposeLine.Unsupported,    //     privileged: true
                "",                         //     volumes:
                ComposeLine.Converted,      //       - ./site:/app
                ComposeLine.Unsupported,    //       - /app/node_modules
                ComposeLine.Converted,      //       - data:/data
                "",                         // volumes:
                ComposeLine.Converted,      //   data:
            ],
            plan.Lines.Select(line => line.Verdict));
        Assert.Equal("→ container shop-web-1", plan.Lines[2].Note);
        Assert.Equal(@"→ C:\apps\shop\site:/app", plan.Lines[7].Note);
        Assert.Equal("→ shop_data:/data", plan.Lines[9].Note);
        Assert.Equal("→ volume shop_data", plan.Lines[11].Note);
    }

    [Fact]
    public void A_file_that_cannot_be_read_says_why()
    {
        Assert.Contains("TAG", Assert.Throws<ArgumentException>(() => ComposeReader.Read("services:\n  a:\n    image: a:${TAG:?set it}\n", Source())).Message);
        Assert.Contains("no services", Assert.Throws<ArgumentException>(() => ComposeReader.Read("name: empty\n", Source())).Message);
        Assert.Contains("YAML", Assert.Throws<ArgumentException>(() => ComposeReader.Read("services: [unclosed", Source())).Message);
    }
}
