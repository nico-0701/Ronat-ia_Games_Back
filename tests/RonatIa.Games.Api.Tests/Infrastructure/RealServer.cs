using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace RonatIa.Games.Api.Tests.Infrastructure;

public static class RealServer
{
    /// <summary>
    /// Sobe a API num Kestrel de verdade (o TestServer em memória não aplica os limites do servidor nem fala WebSocket de verdade),
    /// numa porta livre escolhida pelo sistema. Sem isso o Kestrel usa a 5000, e dois testes em paralelo (ou qualquer outro
    /// programa da máquina) disputariam o mesmo endereço. <c>UseKestrel()</c> e <c>UseKestrel(porta)</c> não servem: com o
    /// <c>Program</c> de hospedagem mínima (<c>app.Run()</c>) a porta pedida só seria aplicada depois de o servidor já ter ligado.
    /// </summary>
    public static void StartRealServer(this WebApplicationFactory<Program> factory)
    {
        factory.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        factory.StartServer();
    }
}
