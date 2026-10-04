using RonatIa.Games.Domain.Users;

namespace RonatIa.Games.Domain.Tests;

public sealed class AvatarPresetsTests
{
    [Fact]
    public void Exposes_numbered_preset_keys()
    {
        Assert.Equal(AvatarPresets.Count, AvatarPresets.Keys.Count);
        Assert.Equal("preset-1", AvatarPresets.Keys[0]);
        Assert.Equal($"preset-{AvatarPresets.Count}", AvatarPresets.Keys[^1]);
        Assert.Contains(AvatarPresets.Default, AvatarPresets.Keys);
    }

    [Theory]
    [InlineData("preset-1", true)]
    [InlineData("preset-6", true)]
    [InlineData("preset-0", false)]
    [InlineData("preset-7", false)]
    [InlineData("PRESET-1", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Validates_keys_exactly(string? key, bool expected)
    {
        Assert.Equal(expected, AvatarPresets.IsValid(key));
    }
}
