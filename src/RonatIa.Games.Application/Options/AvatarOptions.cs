namespace RonatIa.Games.Application.Options;

/// <summary>Fotos de avatar (seção <c>Avatars</c>).</summary>
public sealed class AvatarOptions
{
    public const string SectionName = "Avatars";

    /// <summary>Tamanho máximo do arquivo enviado (3 MB). Também limita o corpo da requisição.</summary>
    public int MaxUploadBytes { get; set; } = 3 * 1024 * 1024;

    /// <summary>Lado, em pixels, da imagem final (quadrada, recortada pelo centro).</summary>
    public int Size { get; set; } = 256;

    /// <summary>Maior lado aceito na imagem enviada; protege contra imagens "bomba" que explodem na memória ao decodificar.</summary>
    public int MaxSourceDimension { get; set; } = 8000;

    /// <summary>Máximo de pixels da imagem enviada (largura × altura).</summary>
    public long MaxSourcePixels { get; set; } = 25_000_000;

    /// <summary>Qualidade do WebP (1 a 100).</summary>
    public int WebpQuality { get; set; } = 80;
}
