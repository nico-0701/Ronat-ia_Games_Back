using System.Globalization;
using System.Text;
using RonatIa.Games.Domain.Errors;

namespace RonatIa.Games.Domain.Common;

/// <summary>
/// Normalização e validação de nomes digitados por pessoas (perfil, grupo, membro): Unicode NFC, espaços colapsados,
/// sem caracteres de controle nem invisíveis (exceto o ZWJ, usado em emojis compostos).
/// </summary>
public static class NameRules
{
    private const int ZeroWidthJoiner = 0x200D;

    public static string Normalize(string? input, string field, int minLength, int maxLength, string errorCode, string label)
    {
        var text = (input ?? string.Empty).Normalize(NormalizationForm.FormC);

        var builder = new StringBuilder(text.Length);
        var pendingSpace = false;

        foreach (var rune in text.EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(rune))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (IsForbidden(rune))
            {
                throw Invalid(field, errorCode, $"{label} contém caracteres não permitidos.");
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(rune.ToString());
        }

        var value = builder.ToString();
        var length = new StringInfo(value).LengthInTextElements;

        if (length < minLength || length > maxLength)
        {
            throw Invalid(field, errorCode, $"{label} deve ter de {minLength} a {maxLength} caracteres.");
        }

        return value;
    }

    private static bool IsForbidden(Rune rune)
    {
        if (rune.Value == ZeroWidthJoiner)
        {
            return false;
        }

        return Rune.GetUnicodeCategory(rune) is
            UnicodeCategory.Control or
            UnicodeCategory.Format or
            UnicodeCategory.Surrogate or
            UnicodeCategory.PrivateUse or
            UnicodeCategory.OtherNotAssigned;
    }

    private static AppException Invalid(string field, string code, string message) =>
        AppException.Validation(code, message, new Dictionary<string, string[]> { [field] = [message] });
}
