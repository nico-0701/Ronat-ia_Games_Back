using Microsoft.Extensions.Options;
using PhoneNumbers;
using RonatIa.Games.Application.Abstractions;
using RonatIa.Games.Application.Options;
using RonatIa.Games.Domain.Errors;

namespace RonatIa.Games.Infrastructure.Security;

/// <summary>
/// Normaliza telefones para E.164 com a libphonenumber. Aceita máscara e número sem código do país (usa a região padrão, BR);
/// só aceita números válidos de celular ou fixo.
/// </summary>
public sealed class LibPhoneNumberNormalizer(IOptions<AuthOptions> options) : IPhoneNormalizer
{
    private static readonly PhoneNumberUtil Util = PhoneNumberUtil.GetInstance();

    public NormalizedPhone Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input) || input.Length > 40)
        {
            throw Invalid();
        }

        PhoneNumber number;
        try
        {
            number = Util.Parse(input, options.Value.DefaultRegion);
        }
        catch (NumberParseException)
        {
            throw Invalid();
        }

        if (!Util.IsValidNumber(number))
        {
            throw Invalid();
        }

        var type = Util.GetNumberType(number);
        if (type is not (PhoneNumberType.MOBILE or PhoneNumberType.FIXED_LINE_OR_MOBILE or PhoneNumberType.FIXED_LINE))
        {
            throw Invalid();
        }

        var e164 = Util.Format(number, PhoneNumberFormat.E164);
        return new NormalizedPhone(e164, e164[^4..]);
    }

    private static AppException Invalid() => AppException.Validation(
        "auth.invalid_phone",
        "Informe um telefone válido, com DDD.",
        new Dictionary<string, string[]> { ["phone"] = ["Telefone inválido."] });
}
