using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using RonatIa.Games.Application.Options;
using RonatIa.Games.Domain.Errors;
using RonatIa.Games.Infrastructure.Security;

namespace RonatIa.Games.Infrastructure.Tests;

public sealed class PhoneTests
{
    private static LibPhoneNumberNormalizer Normalizer(string region = "BR") =>
        new(Options.Create(new AuthOptions { DefaultRegion = region }));

    private static HmacPhoneHasher Hasher(byte[]? pepper = null) =>
        new(Options.Create(new AuthOptions { PhonePepper = Convert.ToBase64String(pepper ?? new byte[48].Select((_, i) => (byte)(i + 1)).ToArray()) }));

    [Theory]
    [InlineData("+5511988887777")]
    [InlineData("+55 11 98888-7777")]
    [InlineData("(11) 98888-7777")]
    [InlineData("11 98888 7777")]
    [InlineData("11988887777")]
    [InlineData("011988887777")]
    [InlineData("55 11 98888-7777")]
    [InlineData("  +55 (11) 9 8888-7777  ")]
    public void Brazilian_mobile_numbers_in_any_format_normalize_to_the_same_e164(string input)
    {
        var phone = Normalizer().Normalize(input);

        Assert.Equal("+5511988887777", phone.E164);
        Assert.Equal("7777", phone.Last4);
    }

    [Theory]
    [InlineData("1133334444", "+551133334444")]
    [InlineData("+14155552671", "+14155552671")]
    [InlineData("+351 912 345 678", "+351912345678")]
    public void Landlines_and_international_numbers_are_accepted(string input, string expected)
    {
        Assert.Equal(expected, Normalizer().Normalize(input).E164);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("123")]
    [InlineData("abc")]
    [InlineData("+55 11 12345")]
    [InlineData("+55 00 98888-7777")]
    [InlineData("0800 123 4567")]
    [InlineData("+800 1234 5678")]
    [InlineData("11988887777 ramal 12")]
    public void Invalid_or_unsupported_numbers_are_rejected_with_a_stable_code(string? input)
    {
        var exception = Assert.Throws<AppException>(() => Normalizer().Normalize(input));

        Assert.Equal(ErrorKind.Validation, exception.Kind);
        Assert.Equal("auth.invalid_phone", exception.Code);
    }

    [Fact]
    public void An_absurdly_long_input_is_rejected()
    {
        Assert.Throws<AppException>(() => Normalizer().Normalize(new string('9', 200)));
    }

    [Fact]
    public void The_default_region_decides_how_numbers_without_a_country_code_are_read()
    {
        var portugal = Normalizer("PT").Normalize("912 345 678");

        Assert.Equal("+351912345678", portugal.E164);
    }

    [Fact]
    public void The_hash_is_deterministic_and_32_bytes()
    {
        var hasher = Hasher();

        var first = hasher.Hash("+5511988887777");
        var second = hasher.Hash("+5511988887777");

        Assert.Equal(32, first.Length);
        Assert.Equal(first, second);
    }

    [Fact]
    public void Different_numbers_and_different_peppers_give_different_hashes()
    {
        var hasher = Hasher();
        var otherPepper = Hasher(RandomNumberGenerator.GetBytes(48));

        Assert.NotEqual(hasher.Hash("+5511988887777"), hasher.Hash("+5511988887778"));
        Assert.NotEqual(hasher.Hash("+5511988887777"), otherPepper.Hash("+5511988887777"));
    }

    [Fact]
    public void The_hash_is_keyed_not_a_plain_digest_of_the_number()
    {
        var plain = SHA256.HashData(Encoding.UTF8.GetBytes("+5511988887777"));

        Assert.NotEqual(plain, Hasher().Hash("+5511988887777"));
    }
}
