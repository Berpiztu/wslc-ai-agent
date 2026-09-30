using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Containers;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Tests;

/// <summary>
/// The check the form runs before it launches, and the fields a failed launch
/// points at. The first case is the one that went unseen: the container came
/// up healthy, the published port reached 18789, and the command, cut short in
/// its field, said 18782.
/// </summary>
public sealed class LaunchChecksTests
{
    [Fact]
    public void A_port_the_command_names_and_publish_does_not_is_warned_about_with_the_flag_quoted()
    {
        var request = new ContainerLaunchRequest
        {
            Image = "ghcr.io/openclaw/openclaw:latest",
            Publish = ["127.0.0.1:28789:18789"],
            Env = ["OPENCLAW_GATEWAY_PORT=18789"],
            Command = "node dist/index.js gateway --bind lan --port 18782 --allow-unconfigured",
        };

        var check = LaunchChecks.Inspect(request);

        Assert.Empty(check.Errors);
        Assert.Contains(check.Warnings, w => w.Field == LaunchFields.Command && w.Message.Contains("'--port 18782'") && w.Message.Contains("18789"));
        Assert.Contains(check.Warnings, w => w.Field == LaunchFields.Env && w.Message.Contains("OPENCLAW_GATEWAY_PORT=18789") && w.Message.Contains("'--port 18782'"));
    }

    [Fact]
    public void A_published_port_nothing_listens_on_is_warned_about_only_when_something_says_where_it_listens()
    {
        var silent = LaunchChecks.Inspect(new ContainerLaunchRequest { Image = "nginx", Publish = ["8080:80"] });
        var spoken = LaunchChecks.Inspect(new ContainerLaunchRequest { Image = "app", Publish = ["8080:80"], Env = ["PORT=3000"] });

        Assert.Empty(silent.Warnings);
        Assert.Contains(spoken.Warnings, w => w.Field == LaunchFields.Publish && w.Message.Contains("container port 80") && w.Message.Contains("PORT=3000"));
    }

    [Fact]
    public void Values_in_the_wrong_shape_are_errors_on_their_fields()
    {
        var request = new ContainerLaunchRequest
        {
            Image = "alpine",
            Name = "-bad",
            Publish = ["abc", "8080:80", "8080:81"],
            Env = ["NOEQUALS", "A=1", "A=2"],
            Memory = "12xyz",
            Cpus = "two",
            HealthInterval = "30",
            StopTimeout = "-5",
        };

        var check = LaunchChecks.Inspect(request);

        Assert.Contains(check.Errors, e => e.Field == LaunchFields.Name);
        Assert.Contains(check.Errors, e => e.Field == LaunchFields.Publish && e.Message.Contains("'abc'"));
        Assert.Contains(check.Errors, e => e.Field == LaunchFields.Publish && e.Message.Contains("8080 is published twice"));
        Assert.Contains(check.Errors, e => e.Field == LaunchFields.Env && e.Message.Contains("'NOEQUALS'"));
        Assert.Contains(check.Warnings, w => w.Field == LaunchFields.Env && w.Message.Contains("A is set more than once"));
        Assert.Contains(check.Errors, e => e.Field == LaunchFields.Memory && e.Message.Contains("'12xyz'"));
        Assert.Contains(check.Errors, e => e.Field == LaunchFields.Cpus);
        Assert.Contains(check.Errors, e => e.Field == LaunchFields.HealthInterval);
        Assert.Contains(check.Errors, e => e.Field == LaunchFields.StopTimeout);
    }

    /// <summary>The CLI's own words, captured on a machine, each on the field the form shows it in.</summary>
    [Theory]
    [InlineData("wslc container create --name web alpine: Conflict. The container name \"/web\" is already in use by container \"1fec2e60ac27\". You have to remove (or rename) that container to be able to reuse that name.\nError code: ERROR_ALREADY_EXISTS", LaunchFields.Name)]
    [InlineData("wslc container run --detach --publish 127.0.0.1:29998:80 alpine: Failed to map port '127.0.0.1:29998/tcp', Only one usage of each socket address (protocol/network address/port) is normally permitted.\nError code: WSAEADDRINUSE", LaunchFields.Publish)]
    [InlineData("wslc container create --network no-such-net alpine: Network not found: 'no-such-net'\nError code: WSLC_E_NETWORK_NOT_FOUND", LaunchFields.Networks)]
    [InlineData("wslc container create --volume Z:\\nowhere:/data alpine: Failed to create volume 'Z:\\nowhere': The system cannot find the path specified.\nError code: ERROR_PATH_NOT_FOUND", LaunchFields.Volumes)]
    [InlineData("wslc container create --memory 12xyz alpine: Invalid memory option value: '12xyz'. Expected a memory size (e.g. 256M, 1G)\n\nUsage: wslc container create [options] <image>", LaunchFields.Memory)]
    [InlineData("wslc container create no-such:latest: Image 'no-such:latest' not found, pulling\npull access denied for no-such, repository does not exist or may require 'docker login': denied\nError code: WSLC_E_IMAGE_NOT_FOUND", LaunchFields.Image)]
    [InlineData("wslc container run --detach --user nosuchuser alpine: unable to find user nosuchuser: no matching entries in passwd file\nError code: E_FAIL", LaunchFields.User)]
    public void A_failed_launch_names_the_field_the_cli_complained_about(string message, string field)
    {
        var fields = LaunchErrors.FieldsOf(message, new ContainerLaunchRequest { Image = "alpine" });

        Assert.Equal([field], fields.Keys);
        Assert.DoesNotContain("Error code", fields[field]);
        Assert.DoesNotContain("wslc container", fields[field]);
    }

    /// <summary>The executable runc could not find is the entrypoint's when the form has one, the command's otherwise.</summary>
    [Fact]
    public void A_missing_executable_points_at_the_entrypoint_or_the_command_that_named_it()
    {
        const string message = "wslc container run --detach --entrypoint tini alpine -s --: failed to create task for container: OCI runtime create failed: runc create failed: unable to start container process: error during container init: exec: \"tini\": executable file not found in $PATH: unknown\nError code: E_INVALIDARG";

        var withEntrypoint = LaunchErrors.FieldsOf(message, new ContainerLaunchRequest { Image = "alpine", Entrypoint = "tini -s --", Command = "node dist/index.js" });
        var commandOnly = LaunchErrors.FieldsOf(message.Replace("\"tini\"", "\"nosuchbin\""), new ContainerLaunchRequest { Image = "alpine", Command = "nosuchbin --flag" });

        Assert.Equal([LaunchFields.Entrypoint], withEntrypoint.Keys);
        Assert.Equal([LaunchFields.Command], commandOnly.Keys);
        Assert.StartsWith("failed to create task", withEntrypoint[LaunchFields.Entrypoint]);
    }

    [Fact]
    public void A_failure_that_names_no_field_stays_general()
    {
        var fields = LaunchErrors.FieldsOf("wslc container run alpine: something else went wrong", new ContainerLaunchRequest { Image = "alpine" });

        Assert.Empty(fields);
    }

    [Fact]
    public void A_failure_keeps_its_fields_when_it_is_wrapped()
    {
        var result = new WslcResult(["container", "run"], 1, "", "Network not found: 'x'", TimeSpan.Zero);
        var failure = new WslcException("wslc container run --network x alpine: Network not found: 'x'", result);

        var withFields = LaunchErrors.WithFields(failure, new ContainerLaunchRequest { Image = "alpine", Network = "x" });

        Assert.Equal([LaunchFields.Networks], withFields.Fields.Keys);
        Assert.Same(withFields, LaunchErrors.WithFields(withFields, new ContainerLaunchRequest { Image = "alpine" }));
    }
}
