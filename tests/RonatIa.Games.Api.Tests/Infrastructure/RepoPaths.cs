namespace RonatIa.Games.Api.Tests.Infrastructure;

public static class RepoPaths
{
    /// <summary>Raiz do repositório (pasta que contém RonatIa.Games.sln), encontrada subindo a partir do diretório de execução.</summary>
    public static string Root { get; } = FindRoot();

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "RonatIa.Games.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Não foi possível localizar a raiz do repositório (RonatIa.Games.sln).");
    }
}
