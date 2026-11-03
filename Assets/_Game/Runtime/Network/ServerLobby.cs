using System.Collections.Generic;
using Unity.Netcode;
using Unity.Collections;
using UnityEngine;

/// <summary>
/// 承载服务端的房间成员管理，包括展示编号分配、成员增删、排序快照和房间结束时的名单清理。
/// 接收 NetworkSession 的服务端业务通知，维护本进程中的服务端成员数据，供房间业务使用。
/// 处理准备请求并广播阶段与名单，由服务端判断并推进游戏阶段。
/// 审批新连接并记录已批准名额，限制人数和允许加入的阶段。
/// 开始游戏前请求生成门和钥匙，均成功后推进阶段。
/// </summary>
public class ServerLobby : MonoBehaviour
{
    [SerializeField, Min(1)]
    private int maxSize = 4;

    private NetworkSession networkSession;

    private NetworkManager networkManager;

    // key 是网络编号 ClientId，不能用它判断展示编号是否被占用。
    private Dictionary<ulong, LobbyMember> members = new();

    // 保存注册时的管理器，供停止会话时注销。
    private CustomMessagingManager registeredMessagingManager;

    // 服务端维护阶段，客户端通过完整快照获得副本。
    private LobbyPhase phase = LobbyPhase.Wating;

    private RoundResult roundResult = RoundResult.None;

    private ServerRoundRules roundRules;

    public RoundResult Result => roundResult;

    // 已获准且尚未离开的客户端，包含正在完成连接的占位。
    private HashSet<ulong> admittedClientId = new();

    // 供服务器上的交互物体检查当前游戏阶段。
    public bool IsPlaying => phase == LobbyPhase.Playing;

    public bool IsWaiting => phase == LobbyPhase.Wating;

    public int MemberCount => members.Count;

    public int AdmittedCount => admittedClientId.Count;

    public int Capacity => maxSize;

    // 成功登记成员后，打印并广播新名单。
    private void HandleServerClientConnected(ulong clientId)
    {
        if(!AddMember(clientId))
        {
            return;
        }

        PrintMembers();
        BroadcastMembers();
    }

    // 成功移除成员后，打印并广播新名单。
    private void HandleServerClientDisconnected(ulong clientId)
    {
        admittedClientId.Remove(clientId);

        if(!RemoveMember(clientId))
        {
            return;
        }

        PrintMembers();
        BroadcastMembers();
    }

    // 填写审批结果；批准时占位，正式成员由连接成功回调登记。
    private void HandleConnectionApproval(
        NetworkManager.ConnectionApprovalRequest request,
        NetworkManager.ConnectionApprovalResponse response
    )
    {
        bool approved = CanAcceptConnection(
            request.ClientNetworkId,
            out string reason
        );
        ServerRoomDirectory directory = GetComponent<ServerRoomDirectory>();
        if(approved && directory != null && directory.RequiresRoom)
        {
            approved = directory.ApproveJoin(request.Payload, out reason);
        }

        response.Approved = approved;
        response.Reason = reason;
        response.Pending = false;

        if(!response.Approved)
        {
            response.CreatePlayerObject = false;
            Debug.Log(response.Reason);
            return;
        }

        admittedClientId.Add(request.ClientNetworkId);
        response.CreatePlayerObject = true;
    }

    // 检查服务器、房间阶段和容量，通过 out 返回原因。
    private bool CanAcceptConnection(ulong clientId, out string reason)
    {
        if(
            networkManager == null
            || !networkManager.IsListening
            || networkManager.ShutdownInProgress
        )
        {
            reason = "服务器不可用";
            return false;
        }

        if(clientId == NetworkManager.ServerClientId && networkManager.IsHost)
        {
            reason = "同意加入";
            return true;
        }

        if(phase != LobbyPhase.Wating)
        {
            reason = "游戏已开始，无法加入";
            return false;
        }

        if(members.Count >= maxSize || admittedClientId.Count >= maxSize)
        {
            reason = "房间已满";
            return false;
        }

        reason = "";
        return true;
    }

    // 寻找最小空闲展示编号；没有空号时返回 -1。
    private int GetAvailablePlayerNumber()
    {
        for(int i = 1; i <= maxSize; i++)
        {
            bool isUsed = false;

            foreach(LobbyMember member in members.Values)
            {
                if(member.PlayerNumber == i)
                {
                    isUsed = true;
                    break;
                }
            }

            if(!isUsed)
            {
                return i;
            }
        }

        return -1;
    }

    // 复制名单并按 PlayerNumber 升序排序。
    public List<LobbyMember> GetMemberSnapshot()
    {
        List<LobbyMember> snapshot = new List<LobbyMember>(members.Values);

        snapshot.Sort((a, b) => a.PlayerNumber.CompareTo(b.PlayerNumber));

        return snapshot;
    }

    // 登记连接并分配编号；重复或满员时返回 false。
    private bool AddMember(ulong clientId)
    {
        if(members.ContainsKey(clientId))
        {
            return false;
        }

        int playerNumber = GetAvailablePlayerNumber();

        if(playerNumber == -1)
        {
            return false;
        }

        // 房主是房间成员身份，独立服务器自身不占玩家名额。
        bool isLeader = members.Count == 0;
        LobbyMember member = new LobbyMember(clientId, playerNumber, isLeader);
        members[clientId] = member;

        return true;
    }

    // 按 ClientId 删除，返回本次是否实际删除。
    private bool RemoveMember(ulong clientId)
    {
        if(!members.TryGetValue(clientId, out LobbyMember departingMember))
        {
            return false;
        }

        members.Remove(clientId);
        if(departingMember.IsLeader && members.Count > 0)
        {
            // 原房主离开后，由展示编号最小的剩余成员接任，不重新编号。
            LobbyMember successor = GetMemberSnapshot()[0];
            members[successor.ClientId] = new LobbyMember(
                successor.ClientId, successor.PlayerNumber, true, successor.IsReady);
        }
        return true;
    }

    // 清空上一场会话的成员。
    private void ClearMembers()
    {
        members.Clear();
    }

    // 打印人数和排序后的名单。
    private void PrintMembers()
    {
        Debug.Log($"[ServerLobby] 成员数: {members.Count}");

        List<LobbyMember> snapshot = GetMemberSnapshot();

        foreach(LobbyMember member in snapshot)
        {
            Debug.Log($"[ServerLobby] 玩家 {member.PlayerNumber} | ClientId={member.ClientId} | IsLeader={member.IsLeader} | IsReady={member.IsReady}");
        }
    }

    // 清空后打印结果，预期人数为 0。
    public bool ResetEmptyRoom()
    {
        if(networkManager == null || !networkManager.IsServer || !networkManager.IsListening
            || networkManager.ShutdownInProgress || members.Count != 0 || admittedClientId.Count != 0)
        {
            return false;
        }

        var world = GetComponent<ServerWorldSpawner>();
        if(world != null)
        {
            world.ClearWorld();
        }

        phase = LobbyPhase.Wating;
        roundResult = RoundResult.None;
        return true;
    }

    // 清空后打印结果，预期人数为 0。
    private void HandleServerStopped()
    {
        UnregisterReadyHandler();
        admittedClientId.Clear();
        phase = LobbyPhase.Wating;
        roundResult = RoundResult.None;
        ClearMembers();
        PrintMembers();
    }

    // 服务器启动后，注册本次会话的准备请求接收。
    private void HandleServerStarted()
    {
        UnregisterReadyHandler();
        phase = LobbyPhase.Wating;
        roundResult = RoundResult.None;
        registeredMessagingManager = networkManager.CustomMessagingManager;

        if(registeredMessagingManager == null)
        {
            return;
        }

        registeredMessagingManager.RegisterNamedMessageHandler(
            LobbyProtocol.ReadyRequest,
            HandleReadyRequestReceived
        );
        registeredMessagingManager.RegisterNamedMessageHandler(
            LobbyProtocol.StartRequest,
            HandleStartRequestReceived
        );
    }

    // 注销旧管理器上的准备请求处理。
    private void UnregisterReadyHandler()
    {
        if(registeredMessagingManager == null)
        {
            return;
        }

        registeredMessagingManager.UnregisterNamedMessageHandler(
            LobbyProtocol.ReadyRequest
        );
        registeredMessagingManager.UnregisterNamedMessageHandler(LobbyProtocol.StartRequest);

        registeredMessagingManager = null;
    }

    // 无正文的开始请求只认 NGO 提供的发送者身份，开始条件仍由服务器复核。
    private void HandleStartRequestReceived(ulong senderClientId, FastBufferReader reader)
    {
        if(networkManager == null || !networkManager.IsServer
            || !networkManager.IsListening || networkManager.ShutdownInProgress
            || reader.Length - reader.Position != 0
            || !networkManager.ConnectedClients.ContainsKey(senderClientId)
            || !members.TryGetValue(senderClientId, out LobbyMember member)
            || !member.IsLeader)
        {
            return;
        }

        TryStartGame();
    }

    // 读取目标准备值，按发送者身份更新，改变后广播。
    private void HandleReadyRequestReceived(ulong senderClientId, FastBufferReader reader)
    {
        if(
            !networkManager.IsListening
            || !networkManager.IsServer
            || networkManager.ShutdownInProgress
        )
        {
            return;
        }

        if(
            reader.Length - reader.Position != sizeof(bool)
            || !reader.TryBeginRead(sizeof(bool))
        )
        {
            return;
        }

        reader.ReadValueSafe(out bool isReady);

        bool changed = SetMemberReady(senderClientId, isReady);

        if(!changed)
        {
            return;
        }

        BroadcastMembers();
    }

    // 广播阶段、最终结果和完整名单，三个 int 头部共 12 字节。
    private void BroadcastMembers()
    {
        if(
        !networkManager.IsListening
        || !networkManager.IsServer
        || networkManager.ShutdownInProgress
        || networkManager.CustomMessagingManager == null
        || networkManager.ConnectedClientsIds.Count == 0
        )
        {
            return;
        }

        List<LobbyMember> snapshot = GetMemberSnapshot();

        // writer 只装本次消息；using 负责释放，成员变化后重新发送完整快照。
        using var writer = new FastBufferWriter(128, Allocator.Temp);

        writer.WriteValueSafe((int)phase);
        writer.WriteValueSafe((int)roundResult);

        // 接收端须按相同类型和顺序读取。
        writer.WriteValueSafe(snapshot.Count);

        foreach(LobbyMember member in snapshot)
        {
            writer.WriteValueSafe(member.ClientId);
            writer.WriteValueSafe(member.PlayerNumber);
            writer.WriteValueSafe(member.IsLeader);
            writer.WriteValueSafe(member.IsReady);
        }

        networkManager.CustomMessagingManager.SendNamedMessageToAll(
            LobbyProtocol.MemberSnapshot,
            writer,
            NetworkDelivery.ReliableFragmentedSequenced
        );
    }

    // 仅在等待阶段修改普通成员准备状态；返回是否实际改变。
    private bool SetMemberReady(ulong clientId, bool isReady)
    {
        if(
            networkManager == null
            || !networkManager.IsListening
            || !networkManager.IsServer
            || networkManager.ShutdownInProgress
            || !networkManager.ConnectedClients.ContainsKey(clientId)
            || !members.TryGetValue(clientId, out LobbyMember member)
            || member.IsReady == isReady
            || member.IsLeader
            || phase != LobbyPhase.Wating
        )
        {
            return false;
        }

        LobbyMember newMember = new LobbyMember(
            clientId,
            member.PlayerNumber,
            member.IsLeader,
            isReady
        );

        // 替换字典记录，只给局部变量赋值不会更新名单。
        members[clientId] = newMember;
        return true;
    }

    // 房主存在且其他成员全部准备时允许开始，房主单人也可通过。
    public bool CanStartGame()
    {
        if(
            networkManager == null
            || !networkManager.IsListening
            || members.Count == 0
            || !networkManager.IsServer
            || networkManager.ShutdownInProgress
            || phase != LobbyPhase.Wating
            || admittedClientId.Count != members.Count
        )
        {
            return false;
        }

        bool isLeaderIn = false;

        foreach(LobbyMember member in members.Values)
        {
            if(member.IsLeader)
            {
                isLeaderIn = true;
                continue;
            }

            if(!member.IsReady)
            {
                return false;
            }
        }

        if(!isLeaderIn)
        {
            return false;
        }

        return true;
    }

    // 开始条件通过后生成门和钥匙，均成功后切换阶段并广播。
    private void TryStartGame()
    {
        if(!CanStartGame())
        {
            return;
        }

        ServerWorldSpawner serverWorldSpawner = networkManager.GetComponent<ServerWorldSpawner>();

        if(
            serverWorldSpawner == null
            || !serverWorldSpawner.TrySpawnWorld()
        )
        {
            return;
        }

        if(!networkManager.IsServer || !networkManager.IsListening || networkManager.ShutdownInProgress)
        {
            return;
        }

        roundResult = RoundResult.None;
        phase = LobbyPhase.Playing;
        BroadcastMembers();
        Debug.Log("开始成功");
    }

    // 在本帧玩法更新后检查结果，切换阶段后不再重复计算或广播结算。
    private void LateUpdate()
    {
        if(!IsPlaying || networkManager == null || !networkManager.IsServer
            || !networkManager.IsListening || networkManager.ShutdownInProgress
            || roundRules == null || !roundRules.isActiveAndEnabled)
        {
            return;
        }

        RoundResult evaluated = roundRules.EvaluateResult();
        if(evaluated != RoundResult.None)
        {
            ServerTryEndRound(evaluated);
        }
    }

    // 服务器统一提交一次结果；死亡、交互和细菌逻辑随后通过 IsPlaying 停止。
    public bool ServerTryEndRound(RoundResult result)
    {
        if(networkManager == null || !networkManager.IsServer
            || !networkManager.IsListening || networkManager.ShutdownInProgress
            || !IsPlaying || (result != RoundResult.Victory && result != RoundResult.Defeat))
        {
            return false;
        }

        roundResult = result;
        phase = LobbyPhase.Ended;
        foreach(NetworkObject spawnedObject in networkManager.SpawnManager.SpawnedObjectsList)
        {
            if(spawnedObject.TryGetComponent(out NetworkBacteria bacteria))
            {
                bacteria.StopForRoundEnd();
            }
        }
        BroadcastMembers();
        Debug.Log($"[ServerLobby] 本局结束：{result}", this);
        return true;
    }

    // 订阅服务端业务事件。
    private void OnEnable()
    {
        networkSession.ServerClientConnected += HandleServerClientConnected;
        networkSession.ServerClientDisconnected += HandleServerClientDisconnected;
        networkSession.ServerStarted += HandleServerStarted;
        networkSession.ServerStopped += HandleServerStopped;
        networkManager.ConnectionApprovalCallback += HandleConnectionApproval;
    }

    // 对称取消订阅。
    private void OnDisable()
    {
        networkSession.ServerClientConnected -= HandleServerClientConnected;
        networkSession.ServerClientDisconnected -= HandleServerClientDisconnected;
        networkSession.ServerStarted -= HandleServerStarted;
        networkSession.ServerStopped -= HandleServerStopped;
        networkManager.ConnectionApprovalCallback -= HandleConnectionApproval;
        UnregisterReadyHandler();
    }

    // 缓存会话入口和 NGO 管理器。
    private void Awake()
    {
        networkSession = GetComponent<NetworkSession>();
        networkManager = GetComponent<NetworkManager>();
        roundRules = GetComponent<ServerRoundRules>();
    }
}
