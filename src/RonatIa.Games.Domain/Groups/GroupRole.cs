namespace RonatIa.Games.Domain.Groups;

/// <summary>
/// Papel de um membro no grupo. Há exatamente um <see cref="Owner"/> ativo por grupo (garantido por índice único parcial);
/// a ordem dos valores é a ordem de poder: <c>role &gt;= Admin</c> significa "administra o grupo".
/// </summary>
public enum GroupRole
{
    Member,
    Admin,
    Owner,
}

/// <summary>Situação do vínculo. A linha nunca é apagada: o histórico de partidas continua apontando para o membro.</summary>
public enum MemberStatus
{
    Active,
    Left,
    Removed,
}
