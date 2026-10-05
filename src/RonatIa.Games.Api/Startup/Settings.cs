namespace RonatIa.Games.Api.Startup;

/// <summary>Informações para os clientes (Web e Android).</summary>
public sealed class ClientSettings
{
    /// <summary>Versão mínima do app aceita; abaixo disso o cliente pede atualização (APKs não se atualizam sozinhos).</summary>
    public string MinClientVersion { get; set; } = "0.0.0";
}

/// <summary>Documentação interativa da API (Scalar). Sempre ligada em Development.</summary>
public sealed class DocsSettings
{
    public bool Enabled { get; set; }
}

/// <summary>Origens autorizadas a chamar a API pelo navegador.</summary>
public sealed class CorsSettings
{
    /// <summary>Origens exatas (esquema + host + porta), ex.: <c>https://app.exemplo.com</c>.</summary>
    public string[] AllowedOrigins { get; set; } = [];

    /// <summary>Expressões regulares para origens variáveis (ex.: previews do Cloudflare Pages).</summary>
    public string[] AllowedOriginPatterns { get; set; } = [];
}

/// <summary>Limites de requisições por IP (seção <c>RateLimiting</c>). Sem o servidor atrás de um proxy, o IP é o do proxy.</summary>
public sealed class RateLimitingSettings
{
    public bool Enabled { get; set; } = true;

    public int AuthLoginPerMinute { get; set; } = 30;

    public int AuthRegisterPerHour { get; set; } = 10;

    public int AuthRefreshPerMinute { get; set; } = 60;

    /// <summary>Envios de foto por pessoa, por hora.</summary>
    public int UploadPerHour { get; set; } = 20;

    /// <summary>Grupos criados por pessoa, por hora.</summary>
    public int GroupCreatePerHour { get; set; } = 10;

    /// <summary>Tentativas de entrar em grupo (ou conferir a senha) por pessoa, por minuto.</summary>
    public int GroupJoinPerMinute { get; set; } = 10;

    /// <summary>Partidas criadas por pessoa, por hora.</summary>
    public int SessionCreatePerHour { get; set; } = 30;

    /// <summary>Ações de jogo por pessoa, por minuto (jogos rápidos precisam de folga).</summary>
    public int SessionActionPerMinute { get; set; } = 240;
}
