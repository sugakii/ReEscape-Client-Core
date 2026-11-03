using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// 查询云服务器的一间房，校验创建名称并展示实时人数；玩家主动创建或选择后才连接游戏。
/// </summary>
public class RoomBrowserController : MonoBehaviour
{
    [SerializeField]
    private NetworkSession networkSession;

    [SerializeField]
    private RoomMenuController roomMenu;

    [SerializeField]
    private GameObject listPanel;

    [SerializeField]
    private InputField roomNameInput;

    [SerializeField]
    private Text validationText;

    [SerializeField]
    private Button createButton;

    [SerializeField]
    private Button createBackButton;

    [SerializeField]
    private Button refreshButton;

    [SerializeField]
    private Button joinButton;

    [SerializeField]
    private Button listBackButton;

    [SerializeField]
    private GameObject roomRow;

    [SerializeField]
    private UnityEngine.UI.Image rowBackground;

    [SerializeField]
    private Text rowName;

    [SerializeField]
    private Text rowCount;

    [SerializeField]
    private Text rowState;

    [SerializeField]
    private Text roomCountText;

    [SerializeField]
    private Text emptyText;

    [SerializeField]
    private Text statusText;

    [SerializeField]
    private string serverAddress = "47.114.104.192";

    [SerializeField]
    private ushort directoryPort = 7778;

    private UdpClient socket;

    private IPEndPoint endpoint;

    private RoomDirectoryRequest pending;

    private byte[] pendingBytes;

    private RoomDirectoryReply currentRoom;

    private string selectedId;

    private float nextSend;

    private float requestDeadline;

    private float nextRefresh;

    private bool connecting;

    public bool IsBusy => pending != null || connecting;

    private void Awake()
    {
        if(NetworkLaunchOptions.TryParse(Environment.GetCommandLineArgs(), out var options, out string error))
        {
            if(options.HasAddress)
            {
                serverAddress = options.Address;
            }

            directoryPort = options.DirectoryPort;
        }
        else
        {
            SetMessage(error);
        }

        RenderRoom(null);
    }

    private void OnEnable()
    {
        networkSession.LocalClientConnected += HandleConnected;
        networkSession.LocalClientDisconnected += HandleDisconnected;
    }

    private void OnDisable()
    {
        networkSession.LocalClientConnected -= HandleConnected;
        networkSession.LocalClientDisconnected -= HandleDisconnected;
        CloseQuery();
    }

    public void ShowRoomList()
    {
        if(connecting)
        {
            return;
        }

        roomMenu.ClearConnectionMessage();
        roomMenu.ShowRoomList();
        selectedId = null;
        BeginRequest("query", "");
        if(EventSystem.current != null)
        {
            EventSystem.current.SetSelectedGameObject(listBackButton.gameObject);
        }
    }

    public void ShowCreateRoom()
    {
        if(connecting)
        {
            return;
        }

        CloseQuery();
        SetMessage("");
        roomMenu.ShowCreateRoom();
        roomNameInput.Select();
        roomNameInput.ActivateInputField();
    }

    public void RefreshRooms()
    {
        if(!IsBusy)
        {
            BeginRequest("query", "");
        }
    }

    public void CreateRoom()
    {
        if(IsBusy)
        {
            return;
        }

        if(!RoomDirectoryProtocol.TryNormalizeName(roomNameInput.text, out string normalized))
        {
            SetMessage("请输入 1～24 个字符的房间名称。");
            roomNameInput.ActivateInputField();
            return;
        }
        roomMenu.ClearConnectionMessage();
        roomNameInput.text = normalized;
        BeginRequest("create", normalized);
    }

    public void SelectRoom()
    {
        if(currentRoom == null || IsBusy)
        {
            return;
        }

        selectedId = currentRoom.roomId;
        RefreshButtons();
    }

    public void JoinRoom()
    {
        if(IsBusy || currentRoom == null || selectedId != currentRoom.roomId || !currentRoom.joinable)
        {
            return;
        }

        Connect(currentRoom);
    }

    public void BackToMainMenu()
    {
        if(connecting || pending != null && pending.operation == "create")
        {
            return;
        }

        CloseQuery();
        roomMenu.ShowMainManu();
    }

    // 自动查询使用真实时间，主菜单 Time.timeScale 为零也可刷新。
    private void Update()
    {
        if(pending == null)
        {
            if(!connecting && listPanel.activeInHierarchy && Time.unscaledTime >= nextRefresh)
            {
                BeginRequest("query", "");
            }

            return;
        }
        if(Time.unscaledTime >= requestDeadline)
        {
            pending = null;
            nextRefresh = Time.unscaledTime + 4f;
            SetMessage("无法获取房间信息，请检查服务器及 UDP 查询端口 " + directoryPort + "。");
            RenderRoom(null);
            RefreshButtons();
            return;
        }
        try
        {
            if(Time.unscaledTime >= nextSend)
            {
                socket.Send(pendingBytes, pendingBytes.Length, endpoint);
                nextSend = Time.unscaledTime + 1f;
            }
            for(int i = 0; i < 8 && socket.Available > 0; i++)
            {
                IPEndPoint source = new(IPAddress.Any, 0);
                byte[] bytes = socket.Receive(ref source);
                if(!source.Equals(endpoint) || bytes.Length > RoomDirectoryProtocol.MaxPacketBytes)
                {
                    continue;
                }

                var reply = JsonUtility.FromJson<RoomDirectoryReply>(Encoding.UTF8.GetString(bytes));
                if(reply == null || reply.version != RoomDirectoryProtocol.Version || reply.nonce != pending.nonce)
                {
                    continue;
                }

                if(reply.capacity < 1 || reply.capacity > LobbyProtocol.MaxMembers || reply.players < 0
                                    || reply.players > reply.capacity || reply.gamePort < 1 || reply.gamePort > 65535)
                {
                    continue;
                }

                string operation = pending.operation;
                pending = null;
                nextRefresh = Time.unscaledTime + 2f;
                if(!reply.success)
                {
                    SetMessage(reply.message);
                }
                else if(operation == "create")
                {
                    if(string.IsNullOrEmpty(reply.roomId) || string.IsNullOrEmpty(reply.creatorToken))
                    {
                        SetMessage("服务器未返回有效的创建凭据，请重试。");
                    }
                    else
                    {
                        Connect(reply);
                    }
                }
                else
                {
                    RenderRoom(reply);
                    SetMessage("");
                }
                RefreshButtons();
                break;
            }
        }
        catch(SocketException ex)
        {
            if(ex.SocketErrorCode != SocketError.ConnectionReset && ex.SocketErrorCode != SocketError.WouldBlock)
            {
                FailQuery("房间查询失败：" + ex.Message);
            }
        }
        catch(ArgumentException) { FailQuery("服务器返回的房间信息无效。"); }
    }

    private void BeginRequest(string operation, string name)
    {
        if(IsBusy)
        {
            return;
        }

        try
        {
            if(socket == null)
            {
                endpoint = new IPEndPoint(IPAddress.Parse(serverAddress), directoryPort);
                socket = new UdpClient(AddressFamily.InterNetwork);
                socket.Client.Bind(new IPEndPoint(IPAddress.Any, 0));
                socket.Client.Blocking = false;
            }
            pending = new RoomDirectoryRequest { nonce = Guid.NewGuid().ToString("N"), operation = operation, roomName = name };
            pendingBytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(pending));
            requestDeadline = Time.unscaledTime + 5f;
            nextSend = 0f;
            SetMessage(operation == "create" ? "正在创建房间…" : "正在刷新房间…");
            RefreshButtons();
        }
        catch(Exception ex) when(ex is SocketException || ex is FormatException)
        {
            FailQuery("无法查询服务器，请检查地址与端口。");
        }
    }

    private void Connect(RoomDirectoryReply room)
    {
        connecting = true;
        SetMessage("正在加入「" + room.roomName + "」…");
        roomMenu.SetRoomName(room.roomName);
        if(!networkSession.ConnectToRoom(serverAddress, room))
        {
            connecting = false;
            SetMessage(networkSession.LastDisconnectReason);
        }
        RefreshButtons();
    }

    private void RenderRoom(RoomDirectoryReply room)
    {
        currentRoom = room != null && !string.IsNullOrEmpty(room.roomId) ? room : null;
        bool exists = currentRoom != null;
        roomRow.SetActive(exists);
        emptyText.gameObject.SetActive(!exists);
        roomCountText.text = exists ? "房间：1" : "房间：0";
        if(exists)
        {
            rowName.text = currentRoom.roomName;
            rowCount.text = currentRoom.players + " / " + currentRoom.capacity;
            rowState.text = currentRoom.state;
        }
        else
        {
            selectedId = null;
        }

        RefreshButtons();
    }

    private void RefreshButtons()
    {
        bool selected = currentRoom != null && selectedId == currentRoom.roomId;
        if(rowBackground != null)
        {
            rowBackground.color = selected
            ? new Color(0.34f, 0.3f, 0.18f, 0.96f) : new Color(0.08f, 0.085f, 0.08f, 0.94f);
        }

        joinButton.interactable = !IsBusy && selected && currentRoom.joinable;
        refreshButton.interactable = !IsBusy;
        createButton.interactable = !IsBusy;
        createBackButton.interactable = !connecting && !(pending != null && pending.operation == "create");
        listBackButton.interactable = !connecting;
    }

    private void SetMessage(string message)
    {
        if(statusText != null)
        {
            statusText.text = message;
        }

        if(validationText != null)
        {
            validationText.text = message;
        }
    }

    private void FailQuery(string message)
    {
        CloseQuery();
        nextRefresh = Time.unscaledTime + 4f;
        SetMessage(message);
    }

    private void HandleConnected(bool host)
    {
        connecting = false;
        CloseQuery();
        SetMessage("");
    }

    private void HandleDisconnected()
    {
        connecting = false;
        CloseQuery();
    }

    private void CloseQuery()
    {
        pending = null;
        socket?.Dispose();
        socket = null;
        RefreshButtons();
    }
}
