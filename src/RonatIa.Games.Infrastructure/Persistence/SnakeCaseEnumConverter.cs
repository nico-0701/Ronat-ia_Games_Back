using System.Text;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace RonatIa.Games.Infrastructure.Persistence;

/// <summary>Grava enums como texto em snake_case (<c>InProgress</c> vira <c>in_progress</c>), legível no banco e nas CHECKs.</summary>
public sealed class SnakeCaseEnumConverter<TEnum>()
    : ValueConverter<TEnum, string>(value => ToSnakeCase(value.ToString()), text => Parse(text))
    where TEnum : struct, Enum
{
    public static string ToSnakeCase(string pascalCase)
    {
        var builder = new StringBuilder(pascalCase.Length + 4);
        for (var i = 0; i < pascalCase.Length; i++)
        {
            var c = pascalCase[i];
            if (char.IsUpper(c))
            {
                if (i > 0)
                {
                    builder.Append('_');
                }

                builder.Append(char.ToLowerInvariant(c));
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    public static TEnum Parse(string snakeCase) =>
        Enum.Parse<TEnum>(snakeCase.Replace("_", string.Empty, StringComparison.Ordinal), ignoreCase: true);
}
