namespace Networker.Core.Tests.Architecture;

public class WorkflowShellTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void Shell_UsesNumberedTopWorkflowInsteadOfNavigationPane()
    {
        string xaml = ReadSource("MainWindow.xaml");
        Assert.DoesNotContain("<NavigationView", xaml, StringComparison.Ordinal);
        Assert.Contains("WorkflowTabs", xaml, StringComparison.Ordinal);
        Assert.Contains("Troubleshooting workflow stages", xaml, StringComparison.Ordinal);
        Assert.Contains("PreviousButton", xaml, StringComparison.Ordinal);
        Assert.Contains("NextButton", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Pages_AreCachedForStatePreservation()
    {
        foreach (string file in new[] { "WorkflowPage.xaml", "AssistantPage.xaml", "SettingsPage.xaml", "DashboardPage.xaml" })
        {
            Assert.Contains("NavigationCacheMode=\"Required\"", ReadSource("Views", file), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Tools_AreGroupedByTroubleshootingStageWithoutVisibleRail()
    {
        string xaml = ReadSource("Views", "WorkflowPage.xaml");
        string code = ReadSource("Views", "WorkflowPage.xaml.cs");
        Assert.Contains("StageTitle", xaml, StringComparison.Ordinal);
        Assert.Contains("StageToolSelector", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectStage(WorkflowStage", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ToolRail", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("QuickDiffPanel", xaml, StringComparison.Ordinal);
        Assert.Contains("header.Equals(\"quick-diff\"", code, StringComparison.Ordinal);
        Assert.Contains("header = \"config-diff\"", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ContentFrameLeftInset", code, StringComparison.Ordinal);
        Assert.DoesNotContain("MainWindow.Instance?.NavigateToStage", code, StringComparison.Ordinal);
    }

    [Fact]
    public void DestructiveDataActions_RetainConfirmations()
    {
        Assert.Contains("Clear troubleshooting workspace?", ReadSource("Views", "DashboardPage.xaml.cs"));
        Assert.Contains("Clear history?", ReadSource("Views", "AssistantPage.xaml.cs"));
        Assert.Contains("Delete credential?", ReadSource("NetworkConfig", "Views", "Tabs", "VaultTab.xaml.cs"));
        Assert.Contains("Delete custom template?", ReadSource("NetworkConfig", "Views", "Tabs", "TemplatesTab.xaml.cs"));
    }

    private static string ReadSource(params string[] parts) => File.ReadAllText(Path.Combine([RepositoryRoot, .. parts]));

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "networker.sln"))) return directory.FullName;
        }
        throw new DirectoryNotFoundException("Could not locate the Networker repository root.");
    }
}
