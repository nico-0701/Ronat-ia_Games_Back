using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using RonatIa.Games.Application.Abstractions;
using RonatIa.Games.Application.Options;
using RonatIa.Games.Domain.Errors;
using SkiaSharp;

namespace RonatIa.Games.Infrastructure.Images;

/// <summary>
/// Processa fotos de avatar com SkiaSharp (licença MIT). A imagem enviada nunca é guardada nem repassada: ela é decodificada,
/// orientada, recortada em quadrado, reduzida e <b>reencodada</b> em WebP, o que descarta qualquer conteúdo escondido
/// (polyglots, EXIF com localização, perfis). O cabeçalho é lido antes da decodificação para recusar imagens "bomba".
/// </summary>
public sealed class SkiaAvatarProcessor(IOptions<AvatarOptions> options) : IAvatarImageProcessor
{
    // Decodificações simultâneas limitadas: a hospedagem gratuita tem pouca memória e uma imagem grande ocupa dezenas de MB.
    private static readonly SemaphoreSlim Gate = new(2, 2);

    public async Task<ProcessedAvatar> ProcessAsync(Stream image, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var bytes = await ReadLimitedAsync(image, settings.MaxUploadBytes, cancellationToken);
        if (bytes.Length == 0)
        {
            throw InvalidImage();
        }

        await Gate.WaitAsync(cancellationToken);
        try
        {
            return await Task.Run(() => Process(bytes, settings), cancellationToken);
        }
        finally
        {
            Gate.Release();
        }
    }

    private static ProcessedAvatar Process(byte[] bytes, AvatarOptions settings)
    {
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data);
        if (codec is null)
        {
            throw InvalidImage();
        }

        if (codec.EncodedFormat is not (SKEncodedImageFormat.Jpeg or SKEncodedImageFormat.Png or SKEncodedImageFormat.Webp or SKEncodedImageFormat.Gif))
        {
            throw InvalidImage();
        }

        var info = codec.Info;
        if (info.Width <= 0 || info.Height <= 0)
        {
            throw InvalidImage();
        }

        if (info.Width > settings.MaxSourceDimension
            || info.Height > settings.MaxSourceDimension
            || (long)info.Width * info.Height > settings.MaxSourcePixels)
        {
            throw AppException.Validation(
                "avatar.dimensions_too_large",
                $"A imagem é grande demais (máximo de {settings.MaxSourceDimension} pixels de lado).",
                new Dictionary<string, string[]> { ["file"] = ["Use uma imagem menor."] });
        }

        // Decodifica já reduzido quando o formato permite (JPEG), mantendo pelo menos o dobro do tamanho final.
        var scale = Math.Min(1f, settings.Size * 2f / Math.Max(info.Width, info.Height));
        var size = codec.GetScaledDimensions(scale);
        var decodeInfo = new SKImageInfo(size.Width, size.Height, SKColorType.Rgba8888, SKAlphaType.Premul);

        using var bitmap = new SKBitmap(decodeInfo);
        if (codec.GetPixels(decodeInfo, bitmap.GetPixels()) != SKCodecResult.Success)
        {
            throw InvalidImage(); // arquivo corrompido ou truncado
        }

        var origin = codec.EncodedOrigin;
        var rotated = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var orientedWidth = rotated ? size.Height : size.Width;
        var orientedHeight = rotated ? size.Width : size.Height;
        var side = Math.Min(orientedWidth, orientedHeight);

        using var surface = SKSurface.Create(new SKImageInfo(settings.Size, settings.Size, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        // Ordem das transformações (a última chamada vale primeiro sobre os pixels): orientar → recortar o centro → reduzir.
        canvas.Scale(settings.Size / (float)side);
        canvas.Translate(-(orientedWidth - side) / 2f, -(orientedHeight - side) / 2f);
        var orientation = OrientationMatrix(origin, size.Width, size.Height);
        canvas.Concat(in orientation);

        using var skImage = SKImage.FromBitmap(bitmap);
        canvas.DrawImage(skImage, 0, 0, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), null);

        // O WebP gerado pelo Skia não carrega EXIF, XMP nem IPTC.
        using var snapshot = surface.Snapshot();
        using var encoded = snapshot.Encode(SKEncodedImageFormat.Webp, settings.WebpQuality);
        var output = encoded.ToArray();

        return new ProcessedAvatar(output, "image/webp", settings.Size, settings.Size, SHA256.HashData(output));
    }

    /// <summary>Matriz que leva os pixels da imagem como gravada para a orientação correta de exibição (valores 1 a 8 do EXIF).</summary>
    private static SKMatrix OrientationMatrix(SKEncodedOrigin origin, int width, int height) => origin switch
    {
        SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, width, 0, 1, 0, 0, 0, 1),
        SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, width, 0, -1, height, 0, 0, 1),
        SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, height, 0, 0, 1),
        SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
        SKEncodedOrigin.RightTop => new SKMatrix(0, -1, height, 1, 0, 0, 0, 0, 1),
        SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, height, -1, 0, width, 0, 0, 1),
        SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, width, 0, 0, 1),
        _ => SKMatrix.Identity,
    };

    private static async Task<byte[]> ReadLimitedAsync(Stream input, int maxBytes, CancellationToken cancellationToken)
    {
        await using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;

        while ((read = await input.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > maxBytes)
            {
                throw AppException.PayloadTooLarge(
                    "avatar.too_large",
                    $"A foto passa de {maxBytes / (1024 * 1024)} MB.",
                    new Dictionary<string, string[]> { ["file"] = ["Escolha uma foto menor."] });
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static AppException InvalidImage() => AppException.Validation(
        "avatar.invalid_image",
        "O arquivo não é uma imagem válida. Use uma foto em JPEG, PNG, WebP ou GIF.",
        new Dictionary<string, string[]> { ["file"] = ["Imagem inválida."] });
}
