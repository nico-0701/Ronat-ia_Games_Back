namespace RonatIa.Games.Application.Options;

/// <summary>Configuração de contas e sessões (seção <c>Auth</c>).</summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>
    /// Segredo do HMAC do telefone, em base64 (no mínimo 32 bytes). <b>Nunca troque depois de haver usuários</b>:
    /// os telefones só existem como HMAC, então trocar o segredo faz todo mundo "perder" a conta.
    /// </summary>
    public string PhonePepper { get; set; } = string.Empty;

    /// <summary>Região usada para interpretar telefones digitados sem o código do país.</summary>
    public string DefaultRegion { get; set; } = "BR";

    /// <summary>Versão dos termos de uso aceita no cadastro (registro para LGPD).</summary>
    public string TermsVersion { get; set; } = "2026-10";

    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(90);

    /// <summary>
    /// Janela em que reapresentar o refresh token anterior é tratado como nova tentativa legítima (resposta perdida
    /// em rede instável) e não como roubo. Fora dela, a sessão inteira é revogada.
    /// </summary>
    public TimeSpan RefreshReuseGrace { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>Quantos aparelhos podem estar conectados ao mesmo tempo; ao passar disso, o menos usado é desconectado.</summary>
    public int MaxSessionsPerUser { get; set; } = 10;
}
