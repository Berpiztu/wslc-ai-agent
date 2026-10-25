using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Tests;

public class ShownValuesTests
{
    [Fact]
    public void A_command_s_variables_are_shown_by_name_never_by_value()
    {
        string[] args = ["container", "create", "--env", "API_SERVER_KEY=6fefc471", "-e", "PASSWORD=o7dL_12", "--env=TOKEN=abc", "--env", "FROM_HOST",
            "--build-arg", "NPM_TOKEN=xyz", "--name", "hermes", "img"];

        Assert.Equal("container create --env API_SERVER_KEY=*** -e PASSWORD=*** --env=TOKEN=*** --env FROM_HOST --build-arg NPM_TOKEN=*** --name hermes img",
            ShownValues.Line(args));
    }

    [Fact]
    public void An_inspect_s_env_list_is_shown_by_name_never_by_value()
    {
        var inspect = """
            [{"Config": {"Env": ["PATH=/usr/bin", "HERMES_DASHBOARD_BASIC_AUTH_PASSWORD=o7dL_12\"x"], "Image": "nousresearch/hermes-agent:main"},
              "Name": "/hermes"}]
            """;

        var shown = ShownValues.Output(inspect);

        Assert.Contains("\"Env\": [\"PATH=***\", \"HERMES_DASHBOARD_BASIC_AUTH_PASSWORD=***\"]", shown);
        Assert.Contains("\"Image\": \"nousresearch/hermes-agent:main\"", shown);
        Assert.DoesNotContain("o7dL_12", shown);
        Assert.Equal("no json here", ShownValues.Output("no json here"));
    }
}
