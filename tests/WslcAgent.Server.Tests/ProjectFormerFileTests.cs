using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Projects;

namespace WslcAgent.Server.Tests;

/// <summary>
/// Two Compose files with the same name are one project, and saving the
/// second one leaves the first one's containers over: a preview that stopped
/// naming the former file would go unnoticed until containers were lost.
/// </summary>
public sealed class ProjectFormerFileTests
{
    private const string Test = @"C:\apps\shop\compose-test.yml";
    private const string Mine = @"C:\apps\shop\compose-mine.yml";

    [Fact]
    public void Another_file_names_the_one_the_project_was_saved_from()
    {
        Assert.Equal(Test, ProjectRunner.FormerFile(Kept(file: Test), Mine));
        // Sent from another machine: its path there is the file's.
        Assert.Equal(Test, ProjectRunner.FormerFile(Kept(clientFile: Test), Mine));
    }

    [Fact]
    public void The_same_file_a_new_project_or_a_pasted_file_name_none()
    {
        Assert.Null(ProjectRunner.FormerFile(Kept(file: Test), Test.ToUpperInvariant()));
        Assert.Null(ProjectRunner.FormerFile(null, Mine));
        Assert.Null(ProjectRunner.FormerFile(Kept(), Mine));
        Assert.Null(ProjectRunner.FormerFile(Kept(file: Test), ""));
    }

    private static StoredProject Kept(string file = "", string clientFile = "") =>
        new("shop", file, @"C:\apps\shop", "services: {}", [], [], DateTimeOffset.UtcNow, ClientFile: clientFile);
}
