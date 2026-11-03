/// <summary>
/// 定义大厅消息名、人数上限和字段长度，供收发两端共用。
/// </summary>
public static class LobbyProtocol
{
    // 消息格式改变时，收发双方须一起升级版本。
    public const string MemberSnapshot = "Lobby.MemberSnapshot.v5";

    // 阶段、结算结果、人数依次按 int 传输。
    public const int SnapshotHeaderSize = sizeof(int) * 3;

    // 阶段按 int 传输，占 4 字节；另一个人数 int 也占 4 字节。
    public const int PhaseSize = sizeof(int);   // int 占4字节

    // 客户端发给服务器的准备请求，内容只有一个目标 bool。
    public const string ReadyRequest = "Lobby.ReadyRequest.v1";

    // 开始请求无正文，服务器根据消息发送者核验房主身份。
    public const string StartRequest = "Lobby.StartRequest.v1";

    public const int MaxMembers = 4;

    // 每名成员：ClientId、PlayerNumber、IsLeader、IsReady。
    public const int MemberSize = 14;   // ulong 8字节、int 4字节、bool 1字节、bool 1字节
}
