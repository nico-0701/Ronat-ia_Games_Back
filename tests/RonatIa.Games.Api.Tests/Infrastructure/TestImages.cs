using System.Net.Http.Headers;
using System.Text;
using SkiaSharp;

namespace RonatIa.Games.Api.Tests.Infrastructure;

/// <summary>Imagens de teste geradas na hora: a metade esquerda é vermelha e a direita azul, o que permite provar recorte e rotação.</summary>
public static class TestImages
{
    /// <summary>GIF 1x1 válido (89a), para o formato que o Skia não sabe gerar.</summary>
    public static readonly byte[] TinyGif = Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7");

    public static SKBitmap TwoColors(int width, int height)
    {
        var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Blue);
        using var paint = new SKPaint { Color = SKColors.Red };
        canvas.DrawRect(0, 0, width / 2f, height, paint);
        return bitmap;
    }

    public static byte[] Encode(SKBitmap bitmap, SKEncodedImageFormat format, int quality = 95)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, quality);
        return data.ToArray();
    }

    public static byte[] Png(int width = 64, int height = 64)
    {
        using var bitmap = TwoColors(width, height);
        return Encode(bitmap, SKEncodedImageFormat.Png);
    }

    public static byte[] Webp(int width = 64, int height = 64)
    {
        using var bitmap = TwoColors(width, height);
        return Encode(bitmap, SKEncodedImageFormat.Webp);
    }

    /// <summary>JPEG de teste; pode levar um segmento EXIF com orientação e um texto "secreto" (para provar que o EXIF é descartado).</summary>
    public static byte[] Jpeg(int width = 64, int height = 64, int? orientation = null, string? secret = null)
    {
        using var bitmap = TwoColors(width, height);
        var jpeg = Encode(bitmap, SKEncodedImageFormat.Jpeg);
        return orientation is null && secret is null ? jpeg : InsertExif(jpeg, orientation ?? 1, secret ?? string.Empty);
    }

    /// <summary>Insere um segmento APP1/EXIF (TIFF little-endian) logo após o SOI, com Orientation e ImageDescription.</summary>
    private static byte[] InsertExif(byte[] jpeg, int orientation, string description)
    {
        var text = Encoding.ASCII.GetBytes(description + "\0");
        using var tiff = new MemoryStream();
        using var writer = new BinaryWriter(tiff);

        writer.Write((byte)'I');
        writer.Write((byte)'I');
        writer.Write((ushort)42);
        writer.Write(8u);              // offset do IFD0
        writer.Write((ushort)2);       // 2 entradas
        // Orientation (0x0112), SHORT, 1 valor
        writer.Write((ushort)0x0112);
        writer.Write((ushort)3);
        writer.Write(1u);
        writer.Write((ushort)orientation);
        writer.Write((ushort)0);
        // ImageDescription (0x010E), ASCII, valor guardado depois do IFD (offset 38)
        writer.Write((ushort)0x010E);
        writer.Write((ushort)2);
        writer.Write((uint)text.Length);
        writer.Write(38u);
        writer.Write(0u);              // sem próximo IFD
        writer.Write(text);
        writer.Flush();

        var payload = tiff.ToArray();
        var segmentLength = 2 + 6 + payload.Length;

        using var output = new MemoryStream();
        output.Write(jpeg, 0, 2); // SOI
        output.WriteByte(0xFF);
        output.WriteByte(0xE1);
        output.WriteByte((byte)(segmentLength >> 8));
        output.WriteByte((byte)(segmentLength & 0xFF));
        output.Write(Encoding.ASCII.GetBytes("Exif\0\0"));
        output.Write(payload);
        output.Write(jpeg, 2, jpeg.Length - 2);
        return output.ToArray();
    }

    public static MultipartFormDataContent Form(byte[] bytes, string fileName = "avatar.jpg", string contentType = "image/jpeg", string field = "file")
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return new MultipartFormDataContent { { file, field, fileName } };
    }

    public static SKBitmap Decode(byte[] bytes) =>
        SKBitmap.Decode(bytes) ?? throw new InvalidOperationException("Não foi possível decodificar a imagem devolvida.");

    /// <summary>Predominantemente vermelho / azul (o WebP com perdas altera um pouco as cores).</summary>
    public static bool IsRed(SKColor color) => color.Red > 150 && color.Blue < 100;

    public static bool IsBlue(SKColor color) => color.Blue > 150 && color.Red < 100;
}
