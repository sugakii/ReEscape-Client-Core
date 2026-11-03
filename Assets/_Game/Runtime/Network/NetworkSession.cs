using UnityEngine;
using Unity.Netcode;
using System;
using Unity.Netcode.Transports.UTP;
using System.Text;

/// <summary>
/// 管理 Host/Client 的启动与会话退出，将 NGO 连接、断线和服务器停止回调转换为业务事件。
/// 事件供 ClientLobby 和 ServerLobby 在本进程内订阅，统一本机网络会话的生命周期入口。
/// 区分主动退出与异常断开，保存供界面显示的断线原因。
/// </summary>
[RequireComponent(typeof(NetworkManager))]
public class NetworkSession : MonoBehaviour
{
    private NetworkManager networkManager;

    // 本机通过离开按钮退出时，不显示断线错误。
    private bool isLocalLeaveRequested;

    private string roomAddress;

    private ushort roomPort;

    // 本机连接成功，参数表示是否为 Host。
    public event Action<bool> LocalClientConnected;

    // 在本机断线通知前保存，供 ClientLobby 读取。
    public string LastDisconnectReason { get; private set; }

    // 本机断开，无参数。
    public event Action LocalClientDisconnected;

    // 服务端得知某玩家加入，参数是网络编号。
    public event Action<ulong> ServerClientConnected;

    // 服务端得知某玩家离开，参数是网络编号。
    public event Action<ulong> ServerClientDisconnected;

    // 服务器已停止，供业务层清理名单。
    public event Action ServerStopped;

    // 模块启动可注册消息，但不等于连接成功。
    public event Action ClientStarted;

    // 服务器模块启动后，通知 ServerLobby 注册准备请求。
    public event Action ServerStarted;

    // 启动 Host，日志记录启动结果。
    public void StartHostGame()
    {
        roomAddress = null;
        networkManager.NetworkConfig.ConnectionData = Array.Empty<byte>();
        if(!PrepareTransport(true))
        {
            return;
        }

        LastDisconnectReason = string.Empty;
        isLocalLeaveRequested = false;
        bool isStartHost = networkManager.StartHost();
        if(!isStartHost)
        {
            ReportStartFailure("Host 启动失败，请检查端口是否被占用。");
        }
        else
        {
            Debug.Log("[NetworkSession] Host 已启动。", this);
        }
    }

    // 只启动服务器，不创建本机客户端。
    public void StartServerGame()
    {
        if(!PrepareTransport(true))
        {
            return;
        }

        LastDisconnectReason = string.Empty;
        isLocalLeaveRequested = false;

        if(
            networkManager == null
            || networkManager.IsListening
            || networkManager.ShutdownInProgress
        )
        {
            return;
        }

        bool isStartServer = networkManager.StartServer()
        && networkManager.IsServer
        && !networkManager.IsClient
        && !networkManager.IsHost;

        if(!isStartServer)
        {
            ReportStartFailure("服务器启动失败，请检查监听地址和端口占用。");
        }
    }

    // 启动客户端连接流程，返回 true 不代表已经连上。
    public void StartClientGame()
    {
        if(!PrepareTransport(false))
        {
            return;
        }

        LastDisconnectReason = string.Empty;
        isLocalLeaveRequested = false;
        bool isStartClient = networkManager.StartClient();
        if(!isStartClient)
        {
            ReportStartFailure("客户端连接流程启动失败。");
        }
    }

    // 列表中的地址、端口和房间凭据一次应用，避免加入已被关闭后重建的同端口房间。
    public bool ConnectToRoom(string address, RoomDirectoryReply room)
    {
        if(networkManager.IsListening || networkManager.ShutdownInProgress)
        {
            return false;
        }

        roomAddress = address;
        roomPort = (ushort)room.gamePort;
        networkManager.NetworkConfig.ConnectionData = Encoding.UTF8.GetBytes(JsonUtility.ToJson(new RoomJoinRequest
        {
            roomId = room.roomId,
            creatorToken = room.creatorToken ?? ""
        }));
        StartClientGame();
        return networkManager.IsClient;
    }

    // 每次开始会话前配置 Transport；运行中的会话不能修改地址或重复启动。
    private bool PrepareTransport(bool server)
    {
        if(networkManager == null || networkManager.IsListening || networkManager.IsClient
            || networkManager.IsServer || networkManager.ShutdownInProgress)
        {
            return false;
        }
        if(!NetworkLaunchOptions.TryParse(Environment.GetCommandLineArgs(), out var options, out string error))
        {
            ReportStartFailure(error);
            return false;
        }
        UnityTransport transport = networkManager.NetworkConfig.NetworkTransport as UnityTransport;
        if(transport == null)
        {
            ReportStartFailure("当前会话必须配置 Unity Transport。");
            return false;
        }
        string address = !server && !string.IsNullOrEmpty(roomAddress) ? roomAddress : options.Address;
        ushort port = !server && !string.IsNullOrEmpty(roomAddress) ? roomPort : options.Port;
        transport.SetConnectionData(true, address, port,
            server ? options.ListenAddress : "127.0.0.1");
        Application.runInBackground = true;
        Debug.Log(server
            ? $"[NetworkSession] 监听 UDP {options.ListenAddress}:{options.Port}"
            : $"[NetworkSession] 正在连接 {address}:{port}", this);
        return true;
    }

    // 启动失败尚未产生断线回调，直接把原因显示在现有菜单中。
    private void ReportStartFailure(string reason)
    {
        LastDisconnectReason = reason;
        Debug.LogError($"[NetworkSession] {reason}", this);
        RoomMenuController menu = FindAnyObjectByType<RoomMenuController>();
        if(menu != null)
        {
            menu.ShowConnectionMessage(reason);
        }
    }

    // 当有玩家加入时触发该事件，首先本端身份，如果是普通客户端，那么他将不会收到其他玩家的连接成功通知。
    private void HandleClientConnected(ulong clientId)
    {
        Debug.Log($"[NetworkSession] 玩家已连接 ClientId={clientId}", this);
        // 房主事件通知
        if(networkManager.IsServer)
        {
            ServerClientConnected?.Invoke(clientId);
        }

        // Host 同时是 Server 和 Client，因此使用两个独立 if。
        // 普通客户端但是是本机连接的通知。
        if(
            networkManager.IsClient
            && clientId == networkManager.LocalClientId
        )
        {
            LocalClientConnected?.Invoke(networkManager.IsHost);
        }
    }

    // 通知服务端移除成员；本机断开时通知客户端清理。
    private void HandleClientDisconnected(ulong clientId)
    {
        Debug.Log($"[NetworkSession] 玩家已断开 ClientId={clientId}", this);
        if(networkManager.IsServer)
        {
            ServerClientDisconnected?.Invoke(clientId);
        }

        if(
            networkManager.IsClient
            && clientId == networkManager.LocalClientId
        )
        {
            if(isLocalLeaveRequested)
            {
                LastDisconnectReason = string.Empty;
            }
            else
            {
                string reason = networkManager.DisconnectReason;
                LastDisconnectReason = string.IsNullOrWhiteSpace(reason)
                ? "连接已断开"
                : reason;
            }

            LocalClientDisconnected?.Invoke();
        }
    }

    // 转发服务器模块启动通知。
    private void HandleServerStarted()
    {
        ServerStarted?.Invoke();
    }

    // 转发停止事件；wasHost 表示停止前是否为 Host。
    private void HandleServerStopped(bool wasHost)
    {
        ServerStopped?.Invoke();
    }

    // 通知客户端注册本次会话的消息接收。
    private void HandleClientStarted()
    {
        ClientStarted?.Invoke();
    }

    // 关闭本机会话；Host 退出也会关闭服务器。
    public void LeaveRoom()
    {
        if(
            !networkManager.IsListening
            || networkManager.ShutdownInProgress
        )
        {
            return;
        }

        isLocalLeaveRequested = true;
        networkManager.Shutdown();
    }

    // 订阅 NGO 生命周期回调。
    private void OnEnable()
    {
        networkManager.OnClientConnectedCallback += HandleClientConnected;   // 真正连接成功，可以进入等待界面
        networkManager.OnClientDisconnectCallback += HandleClientDisconnected;
        networkManager.OnServerStopped += HandleServerStopped;
        networkManager.OnClientStarted += HandleClientStarted;   // 客户端模块启动，可以注册消息接收
        networkManager.OnServerStarted += HandleServerStarted;
    }

    // 对称取消订阅。
    private void OnDisable()
    {
        networkManager.OnClientConnectedCallback -= HandleClientConnected;
        networkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
        networkManager.OnServerStopped -= HandleServerStopped;
        networkManager.OnClientStarted -= HandleClientStarted;
        networkManager.OnServerStarted -= HandleServerStarted;
    }

    // 缓存同物体的 NetworkManager。
    private void Awake()
    {
        networkManager = GetComponent<NetworkManager>();
    }
}
