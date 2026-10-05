namespace RonatIa.Games.Application.Abstractions;

/// <summary>Foto de avatar já processada: quadrada, reduzida, em WebP e sem nenhum metadado.</summary>
public sealed record ProcessedAvatar(byte[] Data, string ContentType, int Width, int Height, byte[] Sha256);

public interface IAvatarImageProcessor
{
    /// <summary>
    /// Valida e transforma a imagem enviada. Aceita JPEG, PNG, WebP e GIF (só o primeiro quadro); aplica a orientação do EXIF,
    /// recorta pelo centro em quadrado, reduz, reencoda em WebP e descarta todos os metadados (inclusive a localização do GPS).
    /// Lança <c>avatar.invalid_image</c>, <c>avatar.too_large</c> ou <c>avatar.dimensions_too_large</c>.
    /// </summary>
    Task<ProcessedAvatar> ProcessAsync(Stream image, CancellationToken cancellationToken);
}
