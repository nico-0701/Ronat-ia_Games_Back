using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using RonatIa.Games.Api.Tests.Infrastructure;
using RonatIa.Games.Application.Auth;
using SkiaSharp;

namespace RonatIa.Games.Api.Tests;

public sealed class AvatarUploadTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private async Task<(HttpClient Client, AuthResponse Auth)> NewUserAsync()
    {
        var auth = await factory.CreateClient().RegisterAsync();
        return (factory.ClientFor(auth.AccessToken), auth);
    }

    private static async Task<UserDto> ReadUserAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<UserDto>())!;
    }

    private Task<int> CountPhotosAsync(Guid userId) =>
        factory.WithDbAsync(db => db.Avatars.CountAsync(avatar => avatar.UploadedByUserId == userId));

    public static TheoryData<string, byte[], string> Formats => new()
    {
        { "jpeg", TestImages.Jpeg(300, 200), "image/jpeg" },
        { "png", TestImages.Png(300, 200), "image/png" },
        { "webp", TestImages.Webp(300, 200), "image/webp" },
        { "gif", TestImages.TinyGif, "image/gif" },
    };

    [Theory]
    [MemberData(nameof(Formats))]
    public async Task Any_supported_format_becomes_a_256_square_webp(string name, byte[] bytes, string contentType)
    {
        var (client, auth) = await NewUserAsync();

        var user = await ReadUserAsync(await client.PutAsync("/api/v1/users/me/avatar", TestImages.Form(bytes, $"avatar.{name}", contentType)));

        Assert.Equal("photo", user.Avatar.Kind);
        Assert.Null(user.Avatar.Preset);
        Assert.StartsWith("/api/v1/avatars/", user.Avatar.Url);

        var image = await factory.CreateClient().GetAsync(user.Avatar.Url);
        Assert.Equal(HttpStatusCode.OK, image.StatusCode);
        Assert.Equal("image/webp", image.Content.Headers.ContentType?.MediaType);
        using var decoded = TestImages.Decode(await image.Content.ReadAsByteArrayAsync());
        Assert.Equal(256, decoded.Width);
        Assert.Equal(256, decoded.Height);
        Assert.Equal(1, await CountPhotosAsync(auth.User.Id));
    }

    [Fact]
    public async Task A_wide_image_is_cropped_from_the_center()
    {
        var (client, _) = await NewUserAsync();

        var user = await ReadUserAsync(await client.PutAsync("/api/v1/users/me/avatar", TestImages.Form(TestImages.Png(600, 300), "wide.png", "image/png")));
        using var decoded = TestImages.Decode(await (await factory.CreateClient().GetAsync(user.Avatar.Url)).Content.ReadAsByteArrayAsync());

        // 600x300, esquerda vermelha e direita azul: o quadrado central (x de 150 a 450) fica metade vermelho, metade azul.
        Assert.True(TestImages.IsRed(decoded.GetPixel(20, 128)), "borda esquerda deveria ser vermelha");
        Assert.True(TestImages.IsBlue(decoded.GetPixel(235, 128)), "borda direita deveria ser azul");
    }

    [Fact]
    public async Task The_exif_orientation_is_applied()
    {
        var (client, _) = await NewUserAsync();
        // Orientation 6 = girar 90 graus no sentido horário: a metade esquerda (vermelha) vai para o topo.
        var rotated = TestImages.Jpeg(400, 200, orientation: 6);

        var user = await ReadUserAsync(await client.PutAsync("/api/v1/users/me/avatar", TestImages.Form(rotated)));
        using var decoded = TestImages.Decode(await (await factory.CreateClient().GetAsync(user.Avatar.Url)).Content.ReadAsByteArrayAsync());

        Assert.True(TestImages.IsRed(decoded.GetPixel(128, 20)), "o topo deveria ser vermelho");
        Assert.True(TestImages.IsBlue(decoded.GetPixel(128, 235)), "a base deveria ser azul");
    }

    [Fact]
    public async Task Metadata_such_as_exif_text_is_stripped()
    {
        var (client, _) = await NewUserAsync();
        const string secret = "SEGREDO-DE-LOCALIZACAO-12345";
        var withExif = TestImages.Jpeg(200, 200, orientation: 1, secret: secret);
        Assert.Contains(secret, Encoding.ASCII.GetString(withExif)); // a entrada realmente carrega o "segredo"

        var user = await ReadUserAsync(await client.PutAsync("/api/v1/users/me/avatar", TestImages.Form(withExif)));
        var output = await (await factory.CreateClient().GetAsync(user.Avatar.Url)).Content.ReadAsByteArrayAsync();
        var asText = Encoding.ASCII.GetString(output);

        Assert.DoesNotContain(secret, asText);
        Assert.DoesNotContain("Exif", asText);
        Assert.DoesNotContain("EXIF", asText);
        Assert.DoesNotContain("XMP", asText);
    }

    [Fact]
    public async Task Uploading_again_replaces_and_deletes_the_previous_photo()
    {
        var (client, auth) = await NewUserAsync();

        var first = await ReadUserAsync(await client.PutAsync("/api/v1/users/me/avatar", TestImages.Form(TestImages.Jpeg())));
        var second = await ReadUserAsync(await client.PutAsync("/api/v1/users/me/avatar", TestImages.Form(TestImages.Png(), "outra.png", "image/png")));

        Assert.NotEqual(first.Avatar.Url, second.Avatar.Url);
        Assert.Equal(1, await CountPhotosAsync(auth.User.Id));
        Assert.Equal(HttpStatusCode.NotFound, (await factory.CreateClient().GetAsync(first.Avatar.Url)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await factory.CreateClient().GetAsync(second.Avatar.Url)).StatusCode);
    }

    [Fact]
    public async Task Deleting_the_avatar_goes_back_to_the_default_and_removes_the_photo()
    {
        var (client, auth) = await NewUserAsync();
        var uploaded = await ReadUserAsync(await client.PutAsync("/api/v1/users/me/avatar", TestImages.Form(TestImages.Jpeg())));

        var cleared = await ReadUserAsync(await client.DeleteAsync("/api/v1/users/me/avatar"));
        var again = await ReadUserAsync(await client.DeleteAsync("/api/v1/users/me/avatar")); // idempotente

        Assert.Equal("preset", cleared.Avatar.Kind);
        Assert.Equal("preset-1", cleared.Avatar.Preset);
        Assert.Equal("preset-1", again.Avatar.Preset);
        Assert.Equal(0, await CountPhotosAsync(auth.User.Id));
        Assert.Equal(HttpStatusCode.NotFound, (await factory.CreateClient().GetAsync(uploaded.Avatar.Url)).StatusCode);
    }

    [Theory]
    [InlineData("texto", "Isto não é uma imagem, é só texto.")]
    [InlineData("svg", """<svg xmlns="http://www.w3.org/2000/svg"><script>alert(1)</script></svg>""")]
    [InlineData("html", "<html><script>alert(1)</script></html>")]
    public async Task Non_images_are_rejected_even_when_labelled_as_images(string name, string content)
    {
        var (client, auth) = await NewUserAsync();

        var response = await client.PutAsync("/api/v1/users/me/avatar", TestImages.Form(Encoding.UTF8.GetBytes(content), $"{name}.jpg", "image/jpeg"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("avatar.invalid_image", await response.ReadCodeAsync());
        Assert.Equal(0, await CountPhotosAsync(auth.User.Id));
    }

    [Fact]
    public async Task An_empty_or_truncated_file_is_rejected()
    {
        var (client, _) = await NewUserAsync();
        var jpeg = TestImages.Jpeg(300, 300);

        var empty = await client.PutAsync("/api/v1/users/me/avatar", TestImages.Form([]));
        var truncated = await client.PutAsync("/api/v1/users/me/avatar", TestImages.Form(jpeg[..(jpeg.Length / 2)]));

        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal("avatar.invalid_image", await empty.ReadCodeAsync());
        Assert.Equal(HttpStatusCode.BadRequest, truncated.StatusCode);
        Assert.Equal("avatar.invalid_image", await truncated.ReadCodeAsync());
    }

    [Fact]
    public async Task A_missing_file_field_is_a_validation_error()
    {
        var (client, _) = await NewUserAsync();

        var response = await client.PutAsync("/api/v1/users/me/avatar", TestImages.Form(TestImages.Jpeg(), field: "outro-campo"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Files_over_the_limit_are_rejected_with_413_and_a_clear_code()
    {
        var (client, auth) = await NewUserAsync();
        var slightlyOver = new byte[(3 * 1024 * 1024) + 1024]; // passa do limite da foto, mas cabe no limite da requisição
        var farOver = new byte[5 * 1024 * 1024];                // estoura o limite da requisição: nem chega a ser lido

        var over = await client.PutAsync("/api/v1/users/me/avatar", TestImages.Form(slightlyOver));
        var way = await client.PutAsync("/api/v1/users/me/avatar", TestImages.Form(farOver));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, over.StatusCode);
        Assert.Equal("avatar.too_large", await over.ReadCodeAsync());
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, way.StatusCode);
        Assert.Equal("request.too_large", await way.ReadCodeAsync());
        Assert.Equal(0, await CountPhotosAsync(auth.User.Id));
    }

    [Fact]
    public async Task The_upload_size_limit_is_configurable()
    {
        var limited = factory.WithSettings(("Avatars:MaxUploadBytes", "100000"));
        var auth = await limited.CreateClient().RegisterAsync();
        var client = limited.ClientFor(auth.AccessToken);

        var photoTooBig = await client.PutAsync("/api/v1/users/me/avatar", TestImages.Form(new byte[150_000]));   // cabe na folga do multipart
        var requestTooBig = await client.PutAsync("/api/v1/users/me/avatar", TestImages.Form(new byte[400_000])); // passa de limite + folga

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, photoTooBig.StatusCode);
        Assert.Equal("avatar.too_large", await photoTooBig.ReadCodeAsync());
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, requestTooBig.StatusCode);
        Assert.Equal("request.too_large", await requestTooBig.ReadCodeAsync());
    }

    [Fact]
    public async Task A_chunked_upload_without_content_length_is_cut_off_by_the_server_with_413()
    {
        // Sem Content-Length o filtro não consegue recusar de antemão; quem interrompe a leitura é o servidor. O TestServer
        // em memória não aplica o limite do corpo, por isso este teste sobe um Kestrel de verdade.
        await using var real = factory.WithSettings();
        real.UseKestrel();
        real.StartServer();
        var auth = await real.CreateClient().RegisterAsync();
        using var client = real.ClientFor(auth.AccessToken);

        using var form = new MultipartFormDataContent { { new StreamContent(new EndlessZeroStream(6 * 1024 * 1024)), "file", "enorme.jpg" } };
        var response = await client.PutAsync("/api/v1/users/me/avatar", form);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal("request.too_large", await response.ReadCodeAsync());
    }

    /// <summary>Fluxo sem tamanho conhecido (não permite Seek), então o HttpClient envia em chunked.</summary>
    private sealed class EndlessZeroStream(long length) : Stream
    {
        private long _position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var toRead = (int)Math.Min(count, length - _position);
            Array.Clear(buffer, offset, toRead);
            _position += toRead;
            return toRead;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Fact]
    public async Task Images_with_huge_dimensions_are_rejected_before_decoding()
    {
        var (client, _) = await NewUserAsync();
        using var bitmap = new SKBitmap(9000, 1); // um arquivo minúsculo que, decodificado, passaria do limite de lado

        var response = await client.PutAsync(
            "/api/v1/users/me/avatar",
            TestImages.Form(TestImages.Encode(bitmap, SKEncodedImageFormat.Png), "larga.png", "image/png"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("avatar.dimensions_too_large", await response.ReadCodeAsync());
    }

    [Fact]
    public async Task The_pixel_limit_is_configurable()
    {
        var limited = factory.WithSettings(("Avatars:MaxSourcePixels", "1000000"));
        var auth = await limited.CreateClient().RegisterAsync();
        using var bitmap = TestImages.TwoColors(2000, 1000); // 2 megapixels

        var response = await limited.ClientFor(auth.AccessToken).PutAsync(
            "/api/v1/users/me/avatar",
            TestImages.Form(TestImages.Encode(bitmap, SKEncodedImageFormat.Png), "grande.png", "image/png"));

        Assert.Equal("avatar.dimensions_too_large", await response.ReadCodeAsync());
    }

    [Fact]
    public async Task Uploading_requires_authentication()
    {
        var response = await factory.CreateClient().PutAsync("/api/v1/users/me/avatar", TestImages.Form(TestImages.Jpeg()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Uploads_are_rate_limited_per_person()
    {
        var limited = factory.WithSettings(("RateLimiting:Enabled", "true"), ("RateLimiting:UploadPerHour", "2"));
        var first = await limited.CreateClient().RegisterAsync();
        var second = await limited.CreateClient().RegisterAsync();
        var clientOne = limited.ClientFor(first.AccessToken);

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
        {
            statuses.Add((await clientOne.PutAsync("/api/v1/users/me/avatar", TestImages.Form(TestImages.Jpeg()))).StatusCode);
        }

        // Outra pessoa, mesmo IP, tem a sua própria cota.
        var other = await limited.ClientFor(second.AccessToken).PutAsync("/api/v1/users/me/avatar", TestImages.Form(TestImages.Jpeg()));

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], statuses);
        Assert.Equal(HttpStatusCode.OK, other.StatusCode);
    }
}
