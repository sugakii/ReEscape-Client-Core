using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 响应本机玩家的连接与断线，通过 RoomMenuController 切换等待界面、主菜单和身份对应的按钮。
/// 同时服务于普通 Client 与 Host 的本机客户端侧，承担客户端的房间界面响应。
/// 发送准备请求，收到服务器快照后同步阶段、名单和页面，并准备本机出生位置。
/// 连接开始或成功时清除旧提示，本机断线后显示会话保存的原因。
/// </summary>
public class ClientLobby : MonoBehaviour
{
    [SerializeField]
    private RoomMenuController roomMenuController;

    [SerializeField]
    private LocalPlayerNetworkBridge localPlayerNetworkBridge;

    private NetworkSession networkSession;

    private NetworkManager networkManager;

    // 停止时当前管理器可能已为空，注销时仍使用注册时的实例。
    private CustomMessagingManager registeredMessagingManager;

    private List<LobbyMember> receivedMembers = new();

    // 记录本机连接成功通知是否已经到达。
    private bool isLocalConnected = false;

    // 与 receivedMembers 一起保存最近一次有效快照。
    private LobbyPhase receivedPhase = LobbyPhase.Wating;

    private RoundResult receivedResult = RoundResult.None;

    public bool IsPlaying => isLocalConnected && receivedPhase == LobbyPhase.Playing;

    // 标记本机已连接，再按缓存阶段和名单刷新页面。
    private void HandleLocalClientConnected(bool isHost)
    {
        roomMenuController.ClearConnectionMessage();

        if(isHost)
        {
            Debug.Log("[ClientLobby] Host");
        }
        else
        {
            Debug.Log("[ClientLobby] Client");
        }

        isLocalConnected = true;
        RefreshMemberView();
    }

    // 断开后清理名单和出生状态、注销接收并返回主菜单。
    private void HandleLocalDisclientConnected()
    {
        Debug.Log("[ClientLobby] 本机已断开连接");

        isLocalConnected = false;
        receivedMembers.Clear();
        UnregisterSnapshotHandler();
        RefreshMemberView();
        roomMenuController.ShowMainManu();
        receivedPhase = LobbyPhase.Wating;
        receivedResult = RoundResult.None;

        if(localPlayerNetworkBridge != null)
        {
            localPlayerNetworkBridge.ResetSpawnState();
        }

        // 先恢复主菜单，再显示本次退出的原因。
        roomMenuController.ShowConnectionMessage(networkSession.LastDisconnectReason);
    }

    // 撤销旧注册并重置出生状态，再注册本次会话的快照接收。
    private void HandleClientStarted()
    {
        roomMenuController.ClearConnectionMessage();

        UnregisterSnapshotHandler();
        isLocalConnected = false;
        receivedMembers.Clear();
        receivedPhase = LobbyPhase.Wating;
        receivedResult = RoundResult.None;
        registeredMessagingManager = networkManager.CustomMessagingManager;

        if(registeredMessagingManager == null)
        {
            return;
        }

        registeredMessagingManager.RegisterNamedMessageHandler(
            LobbyProtocol.MemberSnapshot,
            HandleMemberSnapshotReceived
        );

        if(localPlayerNetworkBridge != null)
        {
            localPlayerNetworkBridge.ResetSpawnState();
        }
    }

    // 使用注册时缓存的管理器取消接收。
    private void UnregisterSnapshotHandler()
    {
        if(registeredMessagingManager == null)
        {
            return;
        }

        registeredMessagingManager.UnregisterNamedMessageHandler(
            LobbyProtocol.MemberSnapshot
        );

        registeredMessagingManager = null;
    }

    // 校验并解析服务器快照，完整成功后替换缓存。
    private void HandleMemberSnapshotReceived(ulong senderClientId, FastBufferReader reader)
    {
        if(
            senderClientId != NetworkManager.ServerClientId
            || !networkManager.IsClient
        )
        {
            return;
        }

        if(!reader.TryBeginRead(LobbyProtocol.SnapshotHeaderSize))
        {
            return;
        }

        reader.ReadValueSafe(out int phaseValue);

        if(phaseValue < (int)LobbyPhase.Wating || phaseValue > (int)LobbyPhase.Ended)
        {
            return;
        }

        // 暂存新阶段，后续校验失败时保留旧缓存。
        LobbyPhase newPhase = (LobbyPhase)phaseValue;

        reader.ReadValueSafe(out int resultValue);
        if(resultValue < (int)RoundResult.None || resultValue > (int)RoundResult.Defeat)
        {
            return;
        }
        RoundResult newResult = (RoundResult)resultValue;
        if((newPhase == LobbyPhase.Ended && newResult == RoundResult.None)
            || (newPhase != LobbyPhase.Ended && newResult != RoundResult.None))
        {
            return;
        }

        reader.ReadValueSafe(out int count);

        if(count < 0 || count > LobbyProtocol.MaxMembers)
        {
            return;
        }

        int expectedBytes = count * LobbyProtocol.MemberSize;

        if(
            !reader.TryBeginRead(expectedBytes)
            || reader.Length - reader.Position != expectedBytes
        )
        {
            return;
        }

        // 先解析到临时列表，失败时不破坏旧缓存。
        List<LobbyMember> newMembers = new List<LobbyMember>();

        for(int i = 0; i < count; i++)
        {
            reader.ReadValueSafe(out ulong clientId);
            reader.ReadValueSafe(out int playerNumber);
            reader.ReadValueSafe(out bool isHost);
            reader.ReadValueSafe(out bool isReady);

            if(!(playerNumber >= 1 && playerNumber <= LobbyProtocol.MaxMembers))
            {
                return;
            }

            foreach(LobbyMember lobbyMember in newMembers)
            {
                if(
                    clientId == lobbyMember.ClientId
                    || playerNumber == lobbyMember.PlayerNumber
                )
                {
                    return;
                }
            }

            LobbyMember member = new LobbyMember(clientId, playerNumber, isHost, isReady);
            newMembers.Add(member);
        }

        // 整条快照有效后，同时替换阶段和名单。
        receivedPhase = newPhase;
        receivedResult = newResult;
        receivedMembers = newMembers;

        // Host 可能先收到快照，再收到连接成功通知，所以先缓存。
        if(isLocalConnected)
        {
            RefreshMemberView();
        }
    }

    // 先切换页面，再用名单确定按钮的最终状态；断线时也可清空显示。
    private void RefreshMemberView()
    {
        ApplySessionView();

        roomMenuController.RefreshMembers(
            receivedMembers,
            networkManager.LocalClientId
        );
    }

    // 从最新名单中查找本机成员，找不到时返回 null。
    private LobbyMember GetLocalMember()
    {
        foreach(LobbyMember member in receivedMembers)
        {
            if(member.ClientId == networkManager.LocalClientId)
            {
                return member;
            }
        }

        return null;
    }

    // 按钮入口：以服务器确认的状态为基础，请求准备或取消准备。
    public void ToggleReady()
    {
        if(
            networkManager == null
            || !networkManager.IsListening
            || registeredMessagingManager == null
            || !networkManager.IsClient
            || !isLocalConnected
            || networkManager.ShutdownInProgress
            || receivedPhase != LobbyPhase.Wating
        )
        {
            return;
        }

        LobbyMember member = GetLocalMember();

        if(member == null || member.IsLeader)
        {
            return;
        }

        // 不提前修改本机缓存，等待服务器快照确认结果。
        SendReadyRequest(!member.IsReady);
    }

    // Host 和独立服务器的房主使用同一个按钮入口，服务器最终决定能否开始。
    public void RequestStartGame()
    {
        if(networkManager == null || !networkManager.IsClient
            || !networkManager.IsListening || networkManager.ShutdownInProgress
            || !isLocalConnected || registeredMessagingManager == null
            || receivedPhase != LobbyPhase.Wating)
        {
            return;
        }

        LobbyMember member = GetLocalMember();
        if(member == null || !member.IsLeader)
        {
            return;
        }

        using var writer = new FastBufferWriter(1, Allocator.Temp);
        registeredMessagingManager.SendNamedMessage(
            LobbyProtocol.StartRequest, NetworkManager.ServerClientId,
            writer, NetworkDelivery.ReliableSequenced);
    }

    // 请求只发送目标准备值；发送者身份由 NGO 提供给服务端。
    private void SendReadyRequest(bool isReady)
    {
        using var writer = new FastBufferWriter(128, Allocator.Temp);

        writer.WriteValueSafe(isReady);

        networkManager.CustomMessagingManager.SendNamedMessage(
            LobbyProtocol.ReadyRequest,
            NetworkManager.ServerClientId,
            writer,
            NetworkDelivery.ReliableSequenced
        );
    }

    // 按服务器阶段显示大厅；出生准备成功后进入本机游戏界面。
    private void ApplySessionView()
    {
        if(networkManager == null || !isLocalConnected)
        {
            return;
        }

        LobbyMember member = GetLocalMember();

        if(member == null)
        {
            return;
        }

        if(receivedPhase == LobbyPhase.Wating)
        {
            roomMenuController.ShowLobby(member.IsLeader);
            return;
        }

        if(receivedPhase == LobbyPhase.Ended)
        {
            if(localPlayerNetworkBridge != null)
            {
                localPlayerNetworkBridge.ApplyRoundEnd();
            }

            roomMenuController.ShowRoundResult(receivedResult);
            return;
        }

        if(receivedPhase == LobbyPhase.Playing && receivedMembers.Contains(member))
        {
            if(localPlayerNetworkBridge == null)
            {
                return;
            }

            if(!localPlayerNetworkBridge.PrepareSpawn(member.PlayerNumber))
            {
                return;
            }

            roomMenuController.ShowGame();
        }
    }

    // 订阅客户端生命周期事件。
    private void OnEnable()
    {
        networkSession.LocalClientConnected += HandleLocalClientConnected;
        networkSession.LocalClientDisconnected += HandleLocalDisclientConnected;
        networkSession.ClientStarted += HandleClientStarted;
    }

    // 取消事件订阅和命名消息注册。
    private void OnDisable()
    {
        networkSession.LocalClientConnected -= HandleLocalClientConnected;
        networkSession.LocalClientDisconnected -= HandleLocalDisclientConnected;
        networkSession.ClientStarted -= HandleClientStarted;
        UnregisterSnapshotHandler();
    }

    // 缓存同物体组件，消息管理器等网络启动后再取。
    private void Awake()
    {
        networkSession = GetComponent<NetworkSession>();
        networkManager = GetComponent<NetworkManager>();
    }
}
