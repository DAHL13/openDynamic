using OpenDynamic.Core.AgentApprovals;
using Xunit;

namespace OpenDynamic.Tests.AgentApprovals;

public class PresentationTests
{
    [Fact]
    public void Presentation_FormatsRunCommandCorrectly()
    {
        var request = new ApprovalRequest(
            id: "1",
            conversationId: "conv",
            stepIdx: 1,
            toolName: "run_command",
            commandLine: "git status",
            cwd: "C:\\Projects\\MyApp",
            workspacePaths: ["C:\\Projects\\MyApp"]);

        var pres = ApprovalPresentation.Create(request);

        Assert.Equal("Comando de terminal", pres.ToolDisplayName);
        Assert.Equal("MyApp", pres.ProjectFolder);
        Assert.Equal("git status (en C:\\Projects\\MyApp)", pres.FullSummary);
        Assert.Equal("git status", pres.OptionActionSummary);
        Assert.False(pres.IsTruncated);
        Assert.False(pres.RequiresExpandedReview);
    }

    [Fact]
    public void Presentation_FormatsWriteToFileCorrectly()
    {
        var request = new ApprovalRequest(
            id: "2",
            conversationId: "conv",
            stepIdx: 2,
            toolName: "write_to_file",
            targetFile: "C:\\Projects\\MyApp\\src\\Config.cs",
            codeContent: "line1\nline2\nline3",
            workspacePaths: ["C:\\Projects\\MyApp"]);

        var pres = ApprovalPresentation.Create(request);

        Assert.Equal("Crear archivo", pres.ToolDisplayName);
        Assert.Contains("Crear src\\Config.cs (3 líneas)", pres.FullSummary);
        Assert.Equal(RiskLevel.Medium, pres.Risk);
        Assert.False(pres.RequiresExpandedReview); // not truncated, medium risk
    }

    [Fact]
    public void Presentation_FormatsReplaceFileContentCorrectly()
    {
        var request = new ApprovalRequest(
            id: "3",
            conversationId: "conv",
            stepIdx: 3,
            toolName: "replace_file_content",
            targetFile: "C:\\Projects\\MyApp\\README.md",
            workspacePaths: ["C:\\Projects\\MyApp"]);

        var pres = ApprovalPresentation.Create(request);

        Assert.Equal("Editar archivo", pres.ToolDisplayName);
        Assert.Equal("Editar README.md", pres.FullSummary);
        Assert.Equal(RiskLevel.Medium, pres.Risk);
        Assert.False(pres.RequiresExpandedReview);
    }

    [Fact]
    public void Presentation_TruncatesTo200Characters_AndRequiresExpandedReview()
    {
        string longCommand = "echo " + new string('X', 250);
        var request = new ApprovalRequest(
            id: "4",
            conversationId: "conv",
            stepIdx: 4,
            toolName: "run_command",
            commandLine: longCommand);

        var pres = ApprovalPresentation.Create(request);

        Assert.True(pres.IsTruncated);
        Assert.True(pres.RequiresExpandedReview);
        Assert.StartsWith(longCommand[..200], pres.CollapsedSummary);
        Assert.Contains("(+", pres.CollapsedSummary);
        Assert.Contains("caracteres)", pres.CollapsedSummary);
    }

    [Fact]
    public void Presentation_RequiresExpandedReview_ForHighRiskEvenIfShort()
    {
        var request = new ApprovalRequest(
            id: "5",
            conversationId: "conv",
            stepIdx: 5,
            toolName: "run_command",
            commandLine: "rm -rf /");

        var pres = ApprovalPresentation.Create(request);

        Assert.False(pres.IsTruncated);
        Assert.Equal(RiskLevel.High, pres.Risk);
        Assert.True(pres.RequiresExpandedReview);
    }

    [Fact]
    public void Presentation_LimitsOptionActionSummaryTo60Chars()
    {
        string longCommand = "dotnet build --configuration Release --verbosity normal --runtime win-x64 --no-restore";
        var request = new ApprovalRequest(
            id: "6",
            conversationId: "conv",
            stepIdx: 6,
            toolName: "run_command",
            commandLine: longCommand);

        var pres = ApprovalPresentation.Create(request);

        Assert.True(pres.OptionActionSummary.Length <= 60);
        Assert.EndsWith("...", pres.OptionActionSummary);
    }
}
