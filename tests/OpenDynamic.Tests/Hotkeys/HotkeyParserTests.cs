using OpenDynamic.Core.Hotkeys;
using Xunit;

namespace OpenDynamic.Tests.Hotkeys;

public sealed class HotkeyParserTests
{
    [Theory]
    [InlineData("Win+Ctrl+I", HotkeyModifiers.Windows | HotkeyModifiers.Control, (uint)'I', "Win+Ctrl+I")]
    [InlineData("Ctrl+Alt+Space", HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x20u, "Ctrl+Alt+Space")]
    [InlineData("Shift+F5", HotkeyModifiers.Shift, 0x74u, "Shift+F5")]
    [InlineData("Win+Shift+A", HotkeyModifiers.Windows | HotkeyModifiers.Shift, (uint)'A', "Win+Shift+A")]
    [InlineData("ctrl+alt+del", HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x2Eu, "Ctrl+Alt+Del")]
    [InlineData("Windows+Control+Tab", HotkeyModifiers.Windows | HotkeyModifiers.Control, 0x09u, "Win+Ctrl+Tab")]
    [InlineData("Alt+Escape", HotkeyModifiers.Alt, 0x1Bu, "Alt+Escape")]
    [InlineData("Ctrl+MediaPlayPause", HotkeyModifiers.Control, 0xB3u, "Ctrl+Mediaplaypause")]
    public void TryParse_ValidInputs_ParsesCorrectly(string input, HotkeyModifiers expectedMods, uint expectedVk, string expectedNormalized)
    {
        bool success = HotkeyParser.TryParse(input, out var def, out var error);

        Assert.True(success);
        Assert.Null(error);
        Assert.NotNull(def);
        Assert.Equal(expectedVk, def.VirtualKey);
        // Should include expected modifiers plus NoRepeat
        Assert.True((def.Modifiers & expectedMods) == expectedMods);
        Assert.True((def.Modifiers & HotkeyModifiers.NoRepeat) == HotkeyModifiers.NoRepeat);
        Assert.Equal(expectedNormalized, def.NormalizedText);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("Ctrl+")]
    [InlineData("Win+Ctrl")]
    [InlineData("Ctrl+Alt+InvalidKeyNameXYZ")]
    [InlineData("Ctrl+A+B")]
    public void TryParse_InvalidInputs_ReturnsFalseWithError(string? input)
    {
        bool success = HotkeyParser.TryParse(input, out var def, out var error);

        Assert.False(success);
        Assert.Null(def);
        Assert.NotNull(error);
    }

    [Fact]
    public void TryParse_AlwaysIncludesNoRepeatFlag()
    {
        bool success = HotkeyParser.TryParse("Ctrl+Alt+T", out var def, out _);

        Assert.True(success);
        Assert.NotNull(def);
        Assert.True((def.Modifiers & HotkeyModifiers.NoRepeat) == HotkeyModifiers.NoRepeat);
    }
}
