using System.ComponentModel.DataAnnotations;

namespace RonatIa.Games.Application.Users;

/// <summary>Alteração parcial do perfil: só os campos informados mudam.</summary>
public sealed record UpdateProfileRequest
{
    /// <summary>Novo nome de exibição (2 a 30 caracteres).</summary>
    [StringLength(100)]
    public string? DisplayName { get; init; }

    /// <summary>Escolhe um avatar pronto (ex.: <c>preset-3</c>); isso descarta a foto enviada, se houver.</summary>
    [StringLength(20)]
    public string? AvatarPreset { get; init; }
}

/// <summary>Confirmação para excluir a conta (ação definitiva).</summary>
public sealed record DeleteAccountRequest
{
    /// <summary>Digite exatamente <c>EXCLUIR</c> para confirmar.</summary>
    [Required, StringLength(20)]
    public string Confirmation { get; init; } = string.Empty;
}

/// <param name="Default">Avatar usado quando a pessoa não escolhe nenhum.</param>
/// <param name="Keys">Chaves dos avatares prontos (as imagens vivem no Front, com o mesmo nome).</param>
public sealed record AvatarPresetsDto(string Default, IReadOnlyList<string> Keys);

/// <summary>Conteúdo de uma foto de avatar (para o endpoint que serve a imagem).</summary>
public sealed record AvatarContent(byte[] Data, string ContentType, string ETag);
