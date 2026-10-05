using RonatIa.Games.Domain.Groups;

namespace RonatIa.Games.Domain.Tests;

public sealed class InviteCodesTests
{
    [Fact]
    public void Generated_codes_have_8_characters_from_the_unambiguous_alphabet()
    {
        for (var i = 0; i < 500; i++)
        {
            var code = InviteCodes.Generate();

            Assert.Equal(InviteCodes.Length, code.Length);
            Assert.All(code, c => Assert.Contains(c, InviteCodes.Alphabet));
        }
    }

    [Fact]
    public void The_alphabet_leaves_out_look_alike_and_awkward_characters()
    {
        Assert.Equal(30, InviteCodes.Alphabet.Length);
        Assert.Equal(InviteCodes.Alphabet.Length, InviteCodes.Alphabet.Distinct().Count());
        Assert.All("ILOU01", c => Assert.DoesNotContain(c, InviteCodes.Alphabet));
    }

    [Fact]
    public void The_database_pattern_accepts_exactly_the_generated_alphabet()
    {
        var pattern = new System.Text.RegularExpressions.Regex(InviteCodes.DatabasePattern);

        for (var i = 0; i < 200; i++)
        {
            Assert.Matches(pattern, InviteCodes.Generate());
        }

        Assert.DoesNotMatch(pattern, "ABCDEFGI");   // I
        Assert.DoesNotMatch(pattern, "ABCDEFGU");   // U
        Assert.DoesNotMatch(pattern, "ABCDEFG0");   // 0
        Assert.DoesNotMatch(pattern, "abcdefgh");   // minúsculas
        Assert.DoesNotMatch(pattern, "ABCDEFG");    // curto
        Assert.DoesNotMatch(pattern, "ABCDEFGHJ");  // longo
    }

    [Fact]
    public void Generated_codes_are_not_repeated()
    {
        var codes = Enumerable.Range(0, 2000).Select(_ => InviteCodes.Generate()).ToHashSet();

        Assert.Equal(2000, codes.Count);
    }

    [Theory]
    [InlineData("ABCDEFGH", "ABCDEFGH")]
    [InlineData("abcdefgh", "ABCDEFGH")]
    [InlineData("  abcd-efgh  ", "ABCDEFGH")]
    [InlineData("ABCD EFGH", "ABCDEFGH")]
    [InlineData("abcd_efgh", "ABCDEFGH")]
    [InlineData("2345-6789", "23456789")]
    public void Codes_typed_by_people_are_normalized(string typed, string expected)
    {
        Assert.True(InviteCodes.TryNormalize(typed, out var code));
        Assert.Equal(expected, code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ABCDEFG")]          // curto
    [InlineData("ABCDEFGHJ")]        // longo
    [InlineData("ABCDEFGO")]         // O não existe no alfabeto
    [InlineData("ABCDEFG1")]         // 1 não existe
    [InlineData("ABCD!FGH")]         // símbolo
    [InlineData("ÁBCDEFGH")]         // acento
    public void Codes_that_cannot_be_valid_are_rejected(string? typed)
    {
        Assert.False(InviteCodes.TryNormalize(typed, out var code));
        Assert.Equal(string.Empty, code);
    }

    [Fact]
    public void Very_long_input_is_rejected_without_work()
    {
        Assert.False(InviteCodes.TryNormalize(new string('A', 10_000), out _));
    }
}
