namespace RonatIa.Games.Domain.Sessions;

/// <summary>
/// Um jogador de uma partida: sempre um <b>membro do grupo</b> (com conta ou um perfil sem conta, que o anfitrião
/// adiciona para quem não tem celular ou não está no app). A linha nunca é apagada: sair só muda o <see cref="Status"/>.
/// </summary>
public sealed class SessionPlayer
{
    private SessionPlayer()
    {
    }

    public Guid Id { get; private set; }

    public Guid SessionId { get; private set; }

    public Guid MemberId { get; private set; }

    /// <summary>Time (0, 1...), ou nulo quando o jogo não tem times ou o jogador ainda não foi alocado.</summary>
    public int? TeamNo { get; private set; }

    /// <summary>Ordem de entrada no lobby, de 0 em diante.</summary>
    public int Seat { get; private set; }

    public PlayerStatus Status { get; private set; }

    public DateTimeOffset JoinedAt { get; private set; }

    public DateTimeOffset? LeftAt { get; private set; }

    public bool IsActive => Status == PlayerStatus.Joined;

    public static SessionPlayer Join(Guid sessionId, Guid memberId, int seat, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(now),
        SessionId = sessionId,
        MemberId = memberId,
        Seat = seat,
        Status = PlayerStatus.Joined,
        JoinedAt = now,
    };

    /// <summary>Quem saiu (ou foi removido) e volta ao lobby: reativa a mesma linha, sem time, no fim da fila.</summary>
    public void Rejoin(int seat, DateTimeOffset now)
    {
        Status = PlayerStatus.Joined;
        Seat = seat;
        TeamNo = null;
        LeftAt = null;
        JoinedAt = now;
    }

    public void Leave(DateTimeOffset now) => End(PlayerStatus.Left, now);

    public void Remove(DateTimeOffset now) => End(PlayerStatus.Removed, now);

    public void AssignTeam(int? team) => TeamNo = team;

    private void End(PlayerStatus status, DateTimeOffset now)
    {
        Status = status;
        LeftAt = now;
        TeamNo = null;
    }
}
