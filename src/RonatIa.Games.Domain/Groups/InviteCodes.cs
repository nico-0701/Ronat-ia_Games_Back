using System.Security.Cryptography;

namespace RonatIa.Games.Domain.Groups;

/// <summary>
/// A "senha do grupo": 8 caracteres sorteados de um alfabeto sem símbolos ambíguos (sem I, L, O, U, 0 e 1), fáceis de
/// ditar e de digitar. São 30^8 ≈ 6,5 × 10^11 combinações; com o limite de tentativas por pessoa, adivinhar é inviável.
/// </summary>
public static class InviteCodes
{
    public const int Length = 8;

    public const string Alphabet = "ABCDEFGHJKMNPQRSTVWXYZ23456789";

    /// <summary>Padrão do <c>CHECK</c> do banco (mantenha em sincronia com <see cref="Alphabet"/>).</summary>
    public const string DatabasePattern = "^[A-HJ-KM-NP-TV-Z2-9]{8}$";

    public static string Generate()
    {
        Span<char> chars = stackalloc char[Length];
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return new string(chars);
    }

    /// <summary>
    /// Aceita o código como a pessoa digitou: sem diferenciar maiúsculas de minúsculas e ignorando espaços, hífens e sublinhados
    /// (<c>abcd-efgh</c>, <c>ABCD EFGH</c>). Devolve falso se não puder ser um código válido.
    /// </summary>
    public static bool TryNormalize(string? input, out string code)
    {
        code = string.Empty;
        if (string.IsNullOrWhiteSpace(input) || input.Length > 40)
        {
            return false;
        }

        Span<char> chars = stackalloc char[Length];
        var count = 0;

        foreach (var raw in input)
        {
            if (raw is ' ' or '-' or '_')
            {
                continue;
            }

            var c = char.ToUpperInvariant(raw);
            if (count == Length || !Alphabet.Contains(c, StringComparison.Ordinal))
            {
                return false;
            }

            chars[count++] = c;
        }

        if (count != Length)
        {
            return false;
        }

        code = new string(chars);
        return true;
    }
}
