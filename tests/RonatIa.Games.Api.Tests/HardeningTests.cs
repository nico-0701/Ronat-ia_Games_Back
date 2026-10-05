using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.SignalR;
using RonatIa.Games.Api.Tests.Infrastructure;
using SkiaSharp;

namespace RonatIa.Games.Api.Tests;

/// <summary>Limites do servidor e da conexão em tempo real que o TestServer em memória não aplica: usam um Kestrel de verdade.</summary>
public sealed class HardeningTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    /// <summary>Uma imagem de ruído (PNG), que não comprime: serve para um envio de ~2 MB.</summary>
    private static byte[] NoisePng(int side)
    {
        using var bitmap = new SKBitmap(side, side);
        var random = new Random(1234);
        var pixels = new byte[side * side * 4];
        random.NextBytes(pixels);
        for (var i = 3; i < pixels.Length; i += 4)
        {
            pixels[i] = 255; // sem transparência
        }

        System.Runtime.InteropServices.Marshal.Copy(pixels, 0, bitmap.GetPixels(), pixels.Length);
        return TestImages.Encode(bitmap, SKEncodedImageFormat.Png);
    }

    [Fact]
    public async Task A_json_body_over_one_megabyte_is_refused_and_the_server_header_is_hidden()
    {
        await using var real = factory.WithSettings();
        real.UseKestrel();
        real.StartServer();
        var ana = await real.NewPersonAsync("Ana");
        var huge = new string('x', 2 * 1024 * 1024);

        var response = await CutOff.RefusedAsync(() => ana.PostAsync("/api/v1/groups", new { name = huge }));
        var health = await real.CreateClient().GetAsync("/health/live");

        if (response is not null)
        {
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
            Assert.Equal("request.too_large", await response.ReadCodeAsync());
        }

        Assert.Empty(await ana.ListGroupsAsync()); // o corpo grande demais não criou nada
        Assert.False(health.Headers.Contains("Server"), "o cabeçalho Server não deve existir");
    }

    [Fact]
    public async Task Photo_uploads_keep_working_above_the_global_limit_because_they_raise_it_for_themselves()
    {
        await using var real = factory.WithSettings();
        real.UseKestrel();
        real.StartServer();
        var ana = await real.NewPersonAsync("Ana");
        var png = NoisePng(700);
        Assert.InRange(png.Length, 1_200_000, 3_000_000); // maior que o limite global de 1 MB, menor que o limite da foto

        var response = await ana.PutFileAsync("/api/v1/users/me/avatar", png, "ruido.png", "image/png");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Hub_calls_are_limited_per_connection()
    {
        var limited = factory.WithSettings(("RateLimiting:Enabled", "true"), ("RateLimiting:HubInvocationsPerMinute", "5"));
        var ana = await limited.NewPersonAsync("Ana");
        var beto = await limited.NewPersonAsync("Beto");
        await using var spamming = await LiveConnection.ConnectAsync(limited, ana);
        await using var calm = await LiveConnection.ConnectAsync(limited, beto);

        for (var i = 0; i < 5; i++)
        {
            await spamming.UnsubscribeAsync(Guid.NewGuid());
        }

        var refused = await Assert.ThrowsAsync<HubException>(() => spamming.UnsubscribeAsync(Guid.NewGuid()));

        Assert.Contains("rate_limit.exceeded", refused.Message);
        await calm.UnsubscribeAsync(Guid.NewGuid()); // outra conexão tem a própria cota
    }
}
