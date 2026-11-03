using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using Object = UnityEngine.Object;

/// <summary>
/// 用独立服务端及真实 NGO 客户端检查创建、列表人数、满员、开局拒绝和空房重建。
/// </summary>
public static class RoomBrowserFlowChecks
{
    private static NetworkManager client;

    private static NetworkSession session;

    private static RoomBrowserController browser;

    private static RoomMenuController roomMenu;

    private static Transform canvas;

    private static readonly List<NetworkManager> peers = new();

    private static UdpClient query;

    private static int stage;

    private static double stageTime;

    private static double deadline;

    private static RoomDirectoryReply firstRoom;

    private static string lastRejection;

    private static bool checkJoin;

    private static bool checkExecution;

    private static bool checkSpectator;

    private static Camera spectatorSource;

    private static NetworkBacteria executionAttacker;

    private static Vector3 executionAnchor;

    private static float nearestExecutionMesh;

    private static bool remoteAnchorSet;

    private static bool remoteCaptured;

    public static void Start()
    {
        checkJoin = false;
        checkExecution = false;
        checkSpectator = false;
        Require(EditorApplication.isPlaying, "Enter Play Mode first.");
        client = Object.FindAnyObjectByType<NetworkSession>().GetComponent<NetworkManager>();
        session = client.GetComponent<NetworkSession>();
        browser = Object.FindAnyObjectByType<RoomBrowserController>();
        roomMenu = Object.FindAnyObjectByType<RoomMenuController>();
        canvas = roomMenu.GetComponentInChildren<Canvas>(true).transform;
        Require(!client.IsClient && !client.IsServer, "Client auto-connected at startup.");
        var so = new SerializedObject(browser);
        so.FindProperty("serverAddress").stringValue = "127.0.0.1";
        so.FindProperty("directoryPort").intValue = 18071;
        so.ApplyModifiedPropertiesWithoutUndo();
        query = new UdpClient(AddressFamily.InterNetwork);
        query.Client.ReceiveTimeout = 1500;
        query.Connect("127.0.0.1", 18071);
        RoomDirectoryReply empty = Ask("query");
        Require(string.IsNullOrEmpty(empty.roomId), "Expected fresh empty server.");
        Require(!Ask("create", " ").success, "Server accepted empty room name.");
        Require(!Ask("create", new string('x', 25)).success, "Server accepted oversized name.");
        browser.ShowRoomList();
        peers.Clear();
        deadline = EditorApplication.timeSinceStartup + 55d;
        SessionState.SetString("RoomBrowserCheck.Result", "RUNNING");
        Next(0);
        EditorApplication.update += Tick;
    }

    public static void StartJoinCheck()
    {
        Start();
        browser.BackToMainMenu();
        query.Close();
        query = new UdpClient(AddressFamily.InterNetwork);
        query.Client.ReceiveTimeout = 1500;
        query.Connect("127.0.0.1", 18071);
        firstRoom = Ask("create", "先建后加入");
        Require(firstRoom.success, "Could not prepare room for Join test.");
        AddPeer(firstRoom, true);
        checkJoin = true;
        Next(20);
    }

    public static void StartExecutionCheck()
    {
        Start();
        browser.BackToMainMenu();
        browser.ShowCreateRoom();
        canvas.Find("CreateRoomPanel/RoomNameInput").GetComponent<InputField>().text = "处决视角验证";
        browser.CreateRoom();
        checkExecution = true;
        nearestExecutionMesh = float.MaxValue;
        remoteAnchorSet = remoteCaptured = false;
        Next(30);
    }

    public static void StartSpectatorCheck()
    {
        StartExecutionCheck();
        checkSpectator = true;
        Next(40);
    }

    private static RoomDirectoryReply Ask(string operation, string name = "")
    {
        var request = new RoomDirectoryRequest { nonce = Guid.NewGuid().ToString("N"), operation = operation, roomName = name };
        byte[] bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(request));
        query.Send(bytes, bytes.Length);
        IPEndPoint source = new(IPAddress.Any, 0);
        var reply = JsonUtility.FromJson<RoomDirectoryReply>(Encoding.UTF8.GetString(query.Receive(ref source)));
        Require(reply.nonce == request.nonce, "Directory response mismatch.");
        return reply;
    }

    private static void Require(bool condition, string message)
    {
        if(!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void Next(int next)
    {
        stage = next;
        stageTime = EditorApplication.timeSinceStartup;
    }

    private static void Tick()
    {
        try
        {
            Require(EditorApplication.timeSinceStartup < deadline, "Timeout at stage " + stage);
            foreach(var peer in peers)
            {
                if(peer != null && peer.SpawnManager != null)
                {
                    foreach(var obj in peer.SpawnManager.SpawnedObjectsList)
                    {
                        if(obj.TryGetComponent(out PlayerNetworkIdentity identity))
                        {
                            identity.enabled = false;
                        }

                        foreach(var collider in obj.GetComponentsInChildren<Collider>())
                        {
                            collider.enabled = false;
                        }
                    }
                }
            }

            double elapsed = EditorApplication.timeSinceStartup - stageTime;
            if(stage == 40 && client.IsConnectedClient && !browser.IsBusy)
            {
                AddPeer(Ask("query"));
                Next(41);
            }
            else if(stage == 41 && peers[0].IsConnectedClient)
            {
                using var writer = new FastBufferWriter(1, Allocator.Temp);
                writer.WriteValueSafe(true);
                peers[0].CustomMessagingManager.SendNamedMessage(LobbyProtocol.ReadyRequest,
                    NetworkManager.ServerClientId, writer, NetworkDelivery.ReliableSequenced);
                Next(42);
            }
            else if(stage == 42 && elapsed > 0.4d)
            {
                client.GetComponent<ClientLobby>().RequestStartGame();
                Next(31);
            }
            else if(stage == 30 && client.IsConnectedClient && !browser.IsBusy)
            {
                var start = canvas.Find("LobbyPanel/HostStartButton").GetComponent<Button>();
                if(!start.gameObject.activeSelf || !start.interactable)
                {
                    return;
                }

                client.GetComponent<ClientLobby>().RequestStartGame();
                Next(31);
            }
            else if(stage == 31 && elapsed > 0.5d)
            {
                if(Object.FindAnyObjectByType<PrototypeMenus>().State != PrototypeMenus.MenuState.Playing)
                {
                    return;
                }

                foreach(var obj in client.SpawnManager.SpawnedObjectsList)
                {
                    if(obj.TryGetComponent(out NetworkBacteria b))
                    {
                        executionAttacker = b;
                    }
                }

                if(executionAttacker == null)
                {
                    return;
                }

                GameObject.Find("Player").GetComponent<PlayerInput>().actions.Disable();
                if(checkSpectator)
                {
                    PublishSpectatorCamera(false);
                }

                PlaceVictimNearBacteria();
                Next(32);
            }
            else if(stage == 32)
            {
                var life = client.LocalClient.PlayerObject.GetComponent<NetworkPlayerLife>();
                if(life.IsBeingExecuted)
                {
                    executionAnchor = executionAttacker.transform.position;
                    Next(33);
                }
                else if(elapsed > 0.6d)
                {
                    PlaceVictimNearBacteria();
                    Next(32);
                }
            }
            else if(stage == 33)
            {
                var life = client.LocalClient.PlayerObject.GetComponent<NetworkPlayerLife>();
                if(!life.IsActive)
                {
                    Next(34);
                    return;
                }
                if(client.ServerTime.Time - life.Execution.StartTime < 0.6d)
                {
                    return;
                }

                if(!remoteAnchorSet)
                {
                    executionAnchor = executionAttacker.transform.position;
                    remoteAnchorSet = true;
                }
                var camera = GameObject.Find("Player").GetComponentInChildren<Camera>();
                nearestExecutionMesh = Mathf.Min(nearestExecutionMesh,
                    ExecutionFlowChecks.VerifyExecutionCamera(camera, executionAttacker));
                Require(Vector3.Distance(executionAnchor, executionAttacker.transform.position) < 0.05f,
                    "Remote bacteria moved after locking.");
                if(!remoteCaptured && client.ServerTime.Time - life.Execution.StartTime > 1.5d)
                {
                    remoteCaptured = true;
                    ScreenCapture.CaptureScreenshot("Logs/ExecutionRemoteView.png");
                }
            }
            else if(stage == 34 && elapsed > 0.3d)
            {
                Require(Object.FindAnyObjectByType<LocalDeathTransition>().IsPlaying, "Remote death static missing.");
                Next(35);
            }
            else if(stage == 35 && elapsed > 1.4d)
            {
                Require(!Object.FindAnyObjectByType<LocalDeathTransition>().IsPlaying, "Remote death static did not finish.");
                if(checkSpectator)
                {
                    VerifySpectatorCamera();
                    ScreenCapture.CaptureScreenshot("Logs/SpectatorDedicatedClient.png");
                    PublishSpectatorCamera(true);
                    Next(36);
                    return;
                }
                Require(Object.FindAnyObjectByType<PrototypeMenus>().State == PrototypeMenus.MenuState.Result,
                    "Remote execution did not finish into result.");
                Require(nearestExecutionMesh > 0.05f && nearestExecutionMesh < float.MaxValue,
                    "Remote execution geometry was not verified.");
                Debug.Log("Remote execution minimum camera depth=" + nearestExecutionMesh);
                Finish(null);
            }
            else if(stage == 36 && elapsed > 0.6d)
            {
                VerifySpectatorCamera();
                Finish(null);
            }
            else if(stage == 20 && elapsed > 0.4d)
            {
                if(Ask("query").players != 1)
                {
                    return;
                }

                browser.ShowRoomList();
                Next(21);
            }
            else if(stage == 21 && !browser.IsBusy)
            {
                browser.SelectRoom();
                browser.JoinRoom();
                Next(22);
            }
            else if(stage == 22 && client.IsConnectedClient && !browser.IsBusy)
            {
                Require(Ask("query").players == 2, "UI Join did not increase room count.");
                Require(canvas.Find("LobbyPanel/ReadyButton").gameObject.activeSelf
                    && !canvas.Find("LobbyPanel/HostStartButton").gameObject.activeSelf, "Joiner received leader controls.");
                Require(canvas.Find("LobbyPanel/RoomNameText").GetComponent<Text>().text == "先建后加入", "Joiner room name incorrect.");
                ScreenCapture.CaptureScreenshot("Logs/RoomLobbyName.png");
                Next(23);
            }
            else if(stage == 23 && elapsed > 0.3d)
            {
                session.LeaveRoom();
                foreach(var peer in peers)
                {
                    if(peer.IsListening)
                    {
                        peer.Shutdown();
                    }
                }

                Next(24);
            }
            else if(stage == 24 && elapsed > 0.5d)
            {
                browser.BackToMainMenu();
                var so = new SerializedObject(browser);
                so.FindProperty("directoryPort").intValue = 18079;
                so.ApplyModifiedPropertiesWithoutUndo();
                browser.ShowRoomList();
                Next(25);
            }
            else if(stage == 25 && elapsed > 5d && !browser.IsBusy)
            {
                Require(canvas.Find("RoomListPanel/RefreshButton").GetComponent<Button>().interactable, "Timeout left Refresh disabled.");
                Require(canvas.Find("RoomListPanel/StatusText").GetComponent<Text>().text.Contains("18079"), "Timeout did not explain query port.");
                ScreenCapture.CaptureScreenshot("Logs/RoomListUnavailable.png");
                Next(26);
            }
            else if(stage == 26 && elapsed > 0.3d)
            {
                browser.BackToMainMenu();
                Finish(null);
            }
            else if(stage == 0 && !browser.IsBusy)
            {
                Require(!client.IsClient, "Browsing occupied a player slot.");
                Require(canvas.Find("RoomListPanel/EmptyStateText").gameObject.activeSelf, "Empty state missing.");
                ScreenCapture.CaptureScreenshot("Logs/RoomListEmpty.png");
                Next(1);
            }
            else if(stage == 1 && elapsed > 0.2d)
            {
                browser.ShowCreateRoom();
                var input = canvas.Find("CreateRoomPanel/RoomNameInput").GetComponent<InputField>();
                input.text = " ";
                browser.CreateRoom();
                Require(!browser.IsBusy && !client.IsClient, "Invalid name attempted connection.");
                input.text = "夜间探索";
                browser.CreateRoom();
                Next(2);
            }
            else if(stage == 2 && client.IsConnectedClient && !browser.IsBusy)
            {
                firstRoom = Ask("query");
                Require(firstRoom.roomName == "夜间探索" && firstRoom.players == 1 && firstRoom.capacity == 4
                    && firstRoom.joinable, "Created room listing is incorrect.");
                Require(canvas.Find("LobbyPanel/RoomNameText").GetComponent<Text>().text == "夜间探索", "Lobby room name missing.");
                Require(!Ask("create", "另一间房").success, "Concurrent second room accepted.");
                browser.ShowRoomList();
                Next(3);
            }
            else if(stage == 3 && !browser.IsBusy)
            {
                Require(canvas.Find("RoomListPanel/RoomScrollView/Viewport/Content/RoomRow/RoomCount").GetComponent<Text>().text == "1 / 4", "UI player count incorrect.");
                browser.SelectRoom();
                Require(canvas.Find("RoomListPanel/JoinRoomButton").GetComponent<Button>().interactable, "Selecting row did not enable Join.");
                ScreenCapture.CaptureScreenshot("Logs/RoomListPopulated.png");
                Next(4);
            }
            else if(stage == 4 && elapsed > 0.3d)
            {
                roomMenu.ShowLobby(true);
                for(int i = 0; i < 3; i++)
                {
                    AddPeer(firstRoom);
                }

                Next(5);
            }
            else if(stage == 5 && elapsed > 0.5d)
            {
                if(Ask("query").players != 4)
                {
                    return;
                }

                Require(!Ask("query").joinable, "Full room is joinable.");
                lastRejection = null;
                AddPeer(firstRoom);
                Next(6);
            }
            else if(stage == 6 && !string.IsNullOrEmpty(lastRejection))
            {
                Require(lastRejection.Contains("满"), "Full rejection missing.");
                peers[0].Shutdown();
                Next(7);
            }
            else if(stage == 7 && elapsed > 0.4d)
            {
                var info = Ask("query");
                if(info.players != 3)
                {
                    return;
                }

                Require(info.joinable, "Leaving player did not reopen room.");
                foreach(var peer in peers)
                {
                    if(peer.IsConnectedClient)
                    {
                        using var writer = new FastBufferWriter(1, Allocator.Temp);
                        writer.WriteValueSafe(true);
                        peer.CustomMessagingManager.SendNamedMessage(LobbyProtocol.ReadyRequest, NetworkManager.ServerClientId,
                            writer, NetworkDelivery.ReliableSequenced);
                    }
                }

                Next(8);
            }
            else if(stage == 8 && elapsed > 0.3d)
            {
                client.GetComponent<ClientLobby>().RequestStartGame();
                Next(9);
            }
            else if(stage == 9 && elapsed > 0.4d)
            {
                var info = Ask("query");
                if(info.state != "游戏中")
                {
                    return;
                }

                Require(!info.joinable, "Playing room allows joining.");
                lastRejection = null;
                AddPeer(firstRoom);
                Next(10);
            }
            else if(stage == 10 && !string.IsNullOrEmpty(lastRejection))
            {
                Require(lastRejection.Contains("开始"), "Playing rejection missing.");
                foreach(var peer in peers)
                {
                    if(peer != null && peer.IsListening)
                    {
                        peer.Shutdown();
                    }
                }

                session.LeaveRoom();
                Next(11);
            }
            else if(stage == 11 && elapsed > 0.5d)
            {
                var info = Ask("query");
                if(!string.IsNullOrEmpty(info.roomId))
                {
                    return;
                }

                Require(info.players == 0, "Empty room retained players.");
                browser.ShowCreateRoom();
                canvas.Find("CreateRoomPanel/RoomNameInput").GetComponent<InputField>().text = "第二间房";
                browser.CreateRoom();
                Next(12);
            }
            else if(stage == 12 && client.IsConnectedClient && !browser.IsBusy)
            {
                var info = Ask("query");
                Require(info.roomName == "第二间房" && info.roomId != firstRoom.roomId && info.players == 1 && info.joinable,
                    "Room could not be recreated after Playing.");
                lastRejection = null;
                AddPeer(firstRoom);
                Next(13);
            }
            else if(stage == 13 && !string.IsNullOrEmpty(lastRejection))
            {
                Require(lastRejection.Contains("关闭"), "Stale room ID was accepted.");
                Finish(null);
            }
        }
        catch(Exception ex) { Finish(ex); }
    }

    private static void AddPeer(RoomDirectoryReply info, bool creator = false)
    {
        var go = new GameObject("RoomBrowserCheckPeer");
        var peer = go.AddComponent<NetworkManager>();
        var transport = go.AddComponent<UnityTransport>();
        transport.SetConnectionData("127.0.0.1", (ushort)info.gamePort);
        peer.NetworkConfig = new NetworkConfig
        {
            NetworkTransport = transport,
            PlayerPrefab = client.NetworkConfig.PlayerPrefab,
            EnableSceneManagement = false,
            ConnectionApproval = true,
            ForceSamePrefabs = client.NetworkConfig.ForceSamePrefabs,
            ConnectionData = Encoding.UTF8.GetBytes(JsonUtility.ToJson(new RoomJoinRequest
            {
                roomId = info.roomId,
                creatorToken = creator ? info.creatorToken : ""
            }))
        };
        foreach(var list in client.NetworkConfig.Prefabs.NetworkPrefabsLists)
        {
            peer.NetworkConfig.Prefabs.NetworkPrefabsLists.Add(list);
        }

        peer.OnClientDisconnectCallback += id => lastRejection = peer.DisconnectReason;
        peer.CustomMessagingManager?.RegisterNamedMessageHandler(LobbyProtocol.MemberSnapshot, (id, reader) => { });
        Require(peer.StartClient(), "Peer failed to start.");
        peers.Add(peer);
    }

    private static void PlaceVictimNearBacteria()
    {
        var agent = executionAttacker.GetComponent<NavMeshAgent>();
        var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
        Vector3 desired = executionAttacker.transform.position + executionAttacker.transform.forward * 1.8f;
        if(!NavMesh.SamplePosition(desired, out NavMeshHit hit, 0.5f, filter))
        {
            return;
        }

        var player = GameObject.Find("Player");
        var controller = player.GetComponent<CharacterController>();
        controller.enabled = false;
        player.transform.position = hit.position + Vector3.up * 0.1f;
        controller.enabled = true;
        player.GetComponent<PlayerMotor>().ResetMotion();
        var obj = client.LocalClient.PlayerObject;
        obj.GetComponent<Unity.Netcode.Components.NetworkTransform>().Teleport(player.transform.position,
            player.transform.rotation, obj.transform.localScale);
    }

    private static void PublishSpectatorCamera(bool moved)
    {
        if(spectatorSource == null)
        {
            spectatorSource = new GameObject("DedicatedSpectatorPeerCamera").AddComponent<Camera>();
            spectatorSource.enabled = false;
        }
        var obj = peers[0].LocalClient.PlayerObject;
        Vector3 position = GameObject.Find("SpawnPoint_Player2").transform.position + (moved ? Vector3.right * 0.4f : Vector3.zero);
        Quaternion rotation = Quaternion.Euler(0f, moved ? 60f : 0f, 0f);
        obj.GetComponent<Unity.Netcode.Components.NetworkTransform>().Teleport(position, rotation, Vector3.one);
        spectatorSource.transform.SetPositionAndRotation(position + rotation * new Vector3(0.1f, 1.6f, 0.25f),
            rotation * Quaternion.Euler(moved ? -18f : 20f, 0f, 0f));
        spectatorSource.fieldOfView = 68f;
        obj.GetComponent<PlayerNetworkView>().SetLocalCamera(spectatorSource);
    }

    private static void VerifySpectatorCamera()
    {
        var spectator = Object.FindAnyObjectByType<PlayerSpectator>();
        Require(spectator.IsSpectating && spectator.CurrentTarget != null
            && spectator.CurrentTarget.OwnerClientId == peers[0].LocalClientId, "Remote death did not select living teammate.");
        var camera = (Camera)new SerializedObject(spectator).FindProperty("spectatorCamera").objectReferenceValue;
        Require(Vector3.Distance(camera.transform.position, spectatorSource.transform.position) < 0.03f
            && Quaternion.Angle(camera.transform.rotation, spectatorSource.transform.rotation) < 0.2f
            && Mathf.Abs(camera.fieldOfView - spectatorSource.fieldOfView) < 0.01f,
            "Dedicated spectator differs from target MainCamera.");
    }

    private static void Finish(Exception ex)
    {
        EditorApplication.update -= Tick;
        query?.Dispose();
        query = null;
        foreach(var peer in peers)
        {
            if(peer != null && peer.IsListening)
            {
                peer.Shutdown();
            }
        }

        if(session != null)
        {
            session.LeaveRoom();
        }

        string message = ex == null ? checkSpectator ? "PASS: dedicated-client execution/static/spectate/actual-camera/moving-target"
                    : checkExecution ? "PASS: dedicated-client execution/head-framing/mesh-clearance/death-result"
                    : checkJoin ? "PASS: list-select-join/name/member-controls/timeout/recovery"
                    : "PASS: startup/empty/create/name/count/full/playing/cleanup/recreate/stale-room"
                    : "FAIL stage " + stage + ": " + ex.Message;
        SessionState.SetString("RoomBrowserCheck.Result", message);
        if(ex == null)
        {
            Debug.Log(message);
        }
        else
        {
            Debug.LogException(ex);
        }
    }
}
