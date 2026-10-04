namespace RonatIa.Games.Domain.Users;

/// <summary>
/// Foto de avatar já processada (recortada, reduzida e reencodada em WebP, sem metadados). Guardada no próprio banco:
/// são poucos KB por foto e isso mantém tudo transacional e fácil de copiar. O <see cref="Id"/> aleatório funciona
/// como endereço não adivinhável da imagem.
/// </summary>
public sealed class Avatar
{
    private Avatar()
    {
    }

    public Guid Id { get; private set; }

    public Guid? UploadedByUserId { get; private set; }

    public string ContentType { get; private set; } = string.Empty;

    public byte[] Data { get; private set; } = [];

    public int Width { get; private set; }

    public int Height { get; private set; }

    /// <summary>SHA-256 do conteúdo, usado como ETag.</summary>
    public byte[] Sha256 { get; private set; } = [];

    public DateTimeOffset CreatedAt { get; private set; }

    public static Avatar Create(
        Guid? uploadedByUserId,
        string contentType,
        byte[] data,
        int width,
        int height,
        byte[] sha256,
        DateTimeOffset now) => new()
        {
            Id = Guid.CreateVersion7(now),
            UploadedByUserId = uploadedByUserId,
            ContentType = contentType,
            Data = data,
            Width = width,
            Height = height,
            Sha256 = sha256,
            CreatedAt = now,
        };
}
