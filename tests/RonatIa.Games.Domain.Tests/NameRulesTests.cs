using RonatIa.Games.Domain.Common;
using RonatIa.Games.Domain.Errors;

namespace RonatIa.Games.Domain.Tests;

public sealed class NameRulesTests
{
    private static string Normalize(string? input, int min = 2, int max = 30) =>
        NameRules.Normalize(input, "name", min, max, "test.invalid_name", "O nome");

    [Fact]
    public void Trims_and_collapses_whitespace()
    {
        Assert.Equal("Zé Milton", Normalize("  Zé   Milton "));
        Assert.Equal("Ana Lima", Normalize("Ana\n\tLima"));
    }

    [Fact]
    public void Normalizes_to_unicode_nfc()
    {
        var decomposed = "José"; // "e" + acento combinante

        var result = Normalize(decomposed);

        Assert.Equal("José", result);
        Assert.Equal(4, result.Length);
    }

    [Theory]
    [InlineData("ab", true)]
    [InlineData("a", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    public void Enforces_minimum_length(string input, bool valid)
    {
        if (valid)
        {
            Assert.Equal(input, Normalize(input));
        }
        else
        {
            Assert.Equal("test.invalid_name", Assert.Throws<AppException>(() => Normalize(input)).Code);
        }
    }

    [Fact]
    public void Enforces_maximum_length_by_visible_characters()
    {
        Assert.Equal(new string('a', 30), Normalize(new string('a', 30)));
        Assert.Throws<AppException>(() => Normalize(new string('a', 31)));
    }

    [Fact]
    public void Null_is_invalid()
    {
        Assert.Throws<AppException>(() => Normalize(null));
    }

    [Theory]
    [InlineData("Ana\u0007")] // controle (BEL)
    [InlineData("A​na")] // espaço de largura zero
    [InlineData("Ana‮")] // override de direita para esquerda
    [InlineData("A﻿na")] // BOM
    [InlineData("Ana")] // uso privado
    public void Rejects_control_and_invisible_characters(string input)
    {
        var exception = Assert.Throws<AppException>(() => Normalize(input));

        Assert.Equal(ErrorKind.Validation, exception.Kind);
        Assert.True(exception.Errors!.ContainsKey("name"));
    }

    [Fact]
    public void Accepts_emoji_including_zwj_sequences_counted_as_one_character()
    {
        var family = "\U0001F468‍\U0001F469‍\U0001F467‍\U0001F466"; // família: 1 caractere visível

        Assert.Equal("A" + family, Normalize("A" + family, min: 2, max: 2));
        Assert.Equal("Ana 🎉", Normalize("Ana 🎉"));
    }
}
