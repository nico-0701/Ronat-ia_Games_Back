namespace RonatIa.Games.Domain.Sessions;

public enum SessionStatus
{
    /// <summary>Lobby: monta o grupo de jogadores, os times e a configuração.</summary>
    Waiting,

    InProgress,

    Finished,

    Cancelled,
}

public enum PlayerStatus
{
    Joined,
    Left,
    Removed,
}
