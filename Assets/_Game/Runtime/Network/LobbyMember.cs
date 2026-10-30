/// <summary>
/// 表示一位房间成员的数据，保存 NGO 网络编号 ClientId、展示编号 PlayerNumber 和房主身份 IsLeader。
/// 由 ServerLobby 创建和保存，成员身份通过构造器初始化并只读提供。
/// 同时保存服务器确认的准备状态 IsReady。
/// </summary>
public class LobbyMember
{
    public ulong ClientId { get; }   // 网络编号

    public int PlayerNumber { get; }   // 几号玩家

    public bool IsLeader { get; }

    // 普通玩家的准备状态，新加入时默认未准备。
    public bool IsReady { get; }

    // 初始化只读的成员身份数据。
    public LobbyMember(ulong ClientId, int PlayerNumber, bool IsLeader, bool IsReady = false)
    {
        this.ClientId = ClientId;
        this.PlayerNumber = PlayerNumber;
        this.IsLeader = IsLeader;
        this.IsReady = IsReady;
    }
}
