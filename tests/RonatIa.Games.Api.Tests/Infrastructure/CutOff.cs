namespace RonatIa.Games.Api.Tests.Infrastructure;

public static class CutOff
{
    /// <summary>
    /// Um envio grande demais para o servidor pode ser recusado de duas formas válidas: a resposta 413 ou o corte da conexão antes
    /// de a resposta chegar (no Linux, o servidor fecha o socket com o cliente ainda escrevendo e o RST descarta a resposta).
    /// Devolve a resposta, ou <c>null</c> se a conexão foi cortada: o teste confere a resposta quando existe e, nos dois casos,
    /// que nada foi aceito.
    /// </summary>
    public static async Task<HttpResponseMessage?> RefusedAsync(Func<Task<HttpResponseMessage>> send)
    {
        try
        {
            return await send();
        }
        catch (HttpRequestException exception) when (exception.InnerException is IOException)
        {
            return null;
        }
    }
}
