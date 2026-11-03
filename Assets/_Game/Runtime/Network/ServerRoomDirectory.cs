using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 独立服务器通过额外 UDP 端口提供单房间目录；创建后保留房主名额，空房间回收后可再次创建。
/// </summary>
[RequireComponent(typeof(NetworkSession), typeof(ServerLobby))]
public class ServerRoomDirectory : MonoBehaviour
{
    private NetworkManager networkManager;

    private NetworkSession networkSession;

    private ServerLobby serverLobby;

    private UdpClient socket;

    private ushort gamePort;

    private string roomId;

    private string roomName;

    private string creatorToken;

    private string createNonce;

    private IPEndPoint creatorEndpoint;

    private bool creatorApproved;

    private bool hadMembers;

    private double reservationExpires;

    public bool IsListening => socket != null;

    public bool RequiresRoom => isActiveAndEnabled && networkManager != null && networkManager.IsServer && !networkManager.IsHost;

    public string RoomName => roomName ?? string.Empty;

    private void Awake()
    {
        networkManager = GetComponent<NetworkManager>();
        networkSession = GetComponent<NetworkSession>();
        serverLobby = GetComponent<ServerLobby>();
    }

    private void OnEnable()
    {
        networkSession.ServerStarted += StartDirectory;
        networkSession.ServerStopped += StopDirectory;
    }

    private void OnDisable()
    {
        networkSession.ServerStarted -= StartDirectory;
        networkSession.ServerStopped -= StopDirectory;
        StopDirectory();
    }

    // 服务器启动房间查询端口。
    private void StartDirectory()
    {
        StopDirectory();
        if(!RequiresRoom)
        {
            return;
        }

        if(!NetworkLaunchOptions.TryParse(Environment.GetCommandLineArgs(), out var options, out string error))
        {
            Debug.LogError("[ServerRoomDirectory] " + error, this);
            return;
        }
        try
        {
            gamePort = options.Port;
            socket = new UdpClient(new IPEndPoint(IPAddress.Parse(options.ListenAddress), options.DirectoryPort));
            socket.Client.Blocking = false;
            Debug.Log($"[ServerRoomDirectory] READY UDP {options.ListenAddress}:{options.DirectoryPort}", this);
        }
        catch(SocketException ex)
        {
            StopDirectory();
            Debug.LogError("[ServerRoomDirectory] 查询端口启动失败：" + ex.Message, this);
        }
    }

    // 关闭查询端口，并清理当前房间记录。
    private void StopDirectory()
    {
        socket?.Dispose();
        socket = null;
        ClearRoom();
    }

    // 只在主线程处理少量小包，不把浏览列表的玩家接入 NGO 会话。
    private void Update()
    {
        if(socket == null || !RequiresRoom || !networkManager.IsListening || networkManager.ShutdownInProgress)
        {
            return;
        }

        if(!string.IsNullOrEmpty(roomId))
        {
            if(serverLobby.MemberCount > 0)
            {
                hadMembers = true;
            }

            if(serverLobby.MemberCount == 0 && serverLobby.AdmittedCount == 0
                            && (hadMembers || creatorApproved || Time.realtimeSinceStartupAsDouble >= reservationExpires))
            {
                serverLobby.ResetEmptyRoom();
                ClearRoom();
            }
        }
        for(int i = 0; i < 16 && socket.Available > 0; i++)
        {
            try
            {
                IPEndPoint sender = new(IPAddress.Any, 0);
                byte[] bytes = socket.Receive(ref sender);
                if(bytes.Length == 0 || bytes.Length > RoomDirectoryProtocol.MaxPacketBytes)
                {
                    continue;
                }

                var request = JsonUtility.FromJson<RoomDirectoryRequest>(Encoding.UTF8.GetString(bytes));
                if(request == null || request.version != RoomDirectoryProtocol.Version
                    || string.IsNullOrEmpty(request.nonce) || request.nonce.Length > 40)
                {
                    continue;
                }

                RoomDirectoryReply reply = ProcessRequest(request, sender);
                byte[] response = Encoding.UTF8.GetBytes(JsonUtility.ToJson(reply));
                socket.Send(response, response.Length, sender);
            }
            catch(ArgumentException) { }
            catch(SocketException ex)
            {
                if(ex.SocketErrorCode != SocketError.WouldBlock
                    && ex.SocketErrorCode != SocketError.ConnectionReset)
                {
                    Debug.LogWarning("[ServerRoomDirectory] 查询失败：" + ex.Message, this);
                }
            }
        }
    }

    // 按请求类型处理房间查询或创建。
    private RoomDirectoryReply ProcessRequest(RoomDirectoryRequest request, IPEndPoint sender)
    {
        bool success = request.operation == "query";
        string error = "";
        bool isCreatorRetry = request.operation == "create" && request.nonce == createNonce
            && creatorEndpoint != null && creatorEndpoint.Equals(sender);
        if(request.operation == "create")
        {
            if(isCreatorRetry)
            {
                success = true;
            }
            else if(!string.IsNullOrEmpty(roomId))
            {
                error = "已有房间，请从房间列表加入。";
            }
            else if(!RoomDirectoryProtocol.TryNormalizeName(request.roomName, out string normalized))
            {
                error = "请输入 1～24 个字符的房间名称。";
            }
            else if(!serverLobby.ResetEmptyRoom())
            {
                error = "服务器正在清理上一局，请稍后重试。";
            }
            else
            {
                roomId = Guid.NewGuid().ToString("N");
                roomName = normalized;
                creatorToken = Guid.NewGuid().ToString("N");
                createNonce = request.nonce;
                creatorEndpoint = sender;
                reservationExpires = Time.realtimeSinceStartupAsDouble + 45d;
                success = true;
            }
        }
        var reply = new RoomDirectoryReply
        {
            nonce = request.nonce,
            success = success,
            message = error,
            roomId = roomId ?? "",
            roomName = roomName ?? "",
            players = serverLobby.MemberCount,
            capacity = serverLobby.Capacity,
            gamePort = gamePort,
            joinable = !string.IsNullOrEmpty(roomId) && creatorApproved && serverLobby.MemberCount > 0 && serverLobby.IsWaiting
                && serverLobby.AdmittedCount < serverLobby.Capacity,
            state = !creatorApproved || serverLobby.MemberCount == 0 ? "创建中" : serverLobby.IsWaiting
                ? (serverLobby.AdmittedCount >= serverLobby.Capacity ? "已满员" : "等待中")
                : serverLobby.IsPlaying ? "游戏中" : "已结束"
        };
        if(success && request.operation == "create")
        {
            reply.creatorToken = creatorToken;
        }

        return reply;
    }

    // 首次连接必须持有创建回执，避免浏览列表的人抢走创建者的房主身份。
    public bool ApproveJoin(byte[] payload, out string reason)
    {
        reason = "房间已关闭，请刷新房间列表。";
        if(!IsListening || string.IsNullOrEmpty(roomId) || payload == null
            || payload.Length == 0 || payload.Length > RoomDirectoryProtocol.MaxPacketBytes)
        {
            return false;
        }

        RoomJoinRequest request;
        try
        {
            request = JsonUtility.FromJson<RoomJoinRequest>(Encoding.UTF8.GetString(payload));
        }
        catch(ArgumentException) { return false; }
        if(request == null || request.version != RoomDirectoryProtocol.Version || request.roomId != roomId)
        {
            return false;
        }

        if(serverLobby.MemberCount == 0 && request.creatorToken != creatorToken)
        {
            reason = "房主正在进入房间，请稍后刷新。";
            return false;
        }
        creatorApproved = true;
        reason = "";
        return true;
    }

    private void ClearRoom()
    {
        roomId = roomName = creatorToken = createNonce = null;
        creatorEndpoint = null;
        creatorApproved = hadMembers = false;
        reservationExpires = 0d;
    }
}
