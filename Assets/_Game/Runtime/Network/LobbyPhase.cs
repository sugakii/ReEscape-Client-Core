/// <summary>
/// 房间会话阶段，由服务端决定并随快照同步给客户端。
/// </summary>
public enum LobbyPhase
{
    Wating,
    Playing,
    Ended
}
