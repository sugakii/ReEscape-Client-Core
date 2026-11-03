using System;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.Video;
using Object = UnityEngine.Object;

/// <summary>
/// 编辑器内运行真实 NGO 会话，检查处决锁定、取消、死亡时点、花屏和结算顺序。
/// </summary>
public static class ExecutionFlowChecks
{
    private static NetworkManager server;

    private static NetworkManager peer;

    private static NetworkBacteria bacteria;

    private static NetworkPlayerLife victim;

    private static PrototypeMenus menus;

    private static LocalDeathTransition transition;

    private static PlayerSpectator spectator;

    private static int stage;

    private static int cancelCount;

    private static double deadline;

    private static double stageTime;

    private static double deathTime;

    private static bool solo;

    private static bool dedicated;

    private static Vector3 lockedBacteriaPosition;

    private static int capturedViews;

    private static float minimumMeshDepth;

    public static void Start(bool singlePlayer, bool serverOnly = false)
    {
        if(!EditorApplication.isPlaying)
        {
            throw new InvalidOperationException("Enter Play Mode first.");
        }

        solo = singlePlayer;
        dedicated = serverOnly;
        Application.runInBackground = true;
        if(dedicated)
        {
            Time.timeScale = 1f;
        }

        stage = cancelCount = 0;
        capturedViews = 0;
        minimumMeshDepth = float.MaxValue;
        server = Object.FindAnyObjectByType<NetworkSession>().GetComponent<NetworkManager>();
        // 此组测试直接连接独立服务器，目录流程由 RoomBrowserFlowChecks 单独覆盖。
        if(serverOnly && server.TryGetComponent(out ServerRoomDirectory directory))
        {
            directory.enabled = false;
        }

        menus = Object.FindAnyObjectByType<PrototypeMenus>();
        transition = Object.FindAnyObjectByType<LocalDeathTransition>();
        spectator = Object.FindAnyObjectByType<PlayerSpectator>();
        var utp = (UnityTransport)server.NetworkConfig.NetworkTransport;
        utp.SetConnectionData("127.0.0.1", 18053, "127.0.0.1");
        Require(dedicated ? server.StartServer() : server.StartHost(), "Network start failed.");
        if(!solo)
        {
            var go = new GameObject("ExecutionCheckPeer");
            peer = go.AddComponent<NetworkManager>();
            var transport = go.AddComponent<UnityTransport>();
            transport.SetConnectionData("127.0.0.1", 18053);
            peer.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = transport,
                PlayerPrefab = server.NetworkConfig.PlayerPrefab,
                ConnectionApproval = true,
                EnableSceneManagement = false,
                ForceSamePrefabs = server.NetworkConfig.ForceSamePrefabs,
                TickRate = server.NetworkConfig.TickRate
            };
            foreach(var list in server.NetworkConfig.Prefabs.NetworkPrefabsLists)
            {
                peer.NetworkConfig.Prefabs.NetworkPrefabsLists.Add(list);
            }

            Require(peer.StartClient(), "Peer start failed.");
        }
        SessionState.SetString("ExecutionCheck.Result", "RUNNING");
        deadline = EditorApplication.timeSinceStartup + 35d;
        EditorApplication.update += Tick;
    }

    private static void Require(bool condition, string message)
    {
        if(!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void Next(int value)
    {
        stage = value;
        stageTime = EditorApplication.timeSinceStartup;
    }

    private static void Tick()
    {
        try
        {
            Require(EditorApplication.timeSinceStartup < deadline, "Timeout at stage " + stage);
            double waited = EditorApplication.timeSinceStartup - stageTime;
            if(stage == 0)
            {
                if(!solo && !peer.IsConnectedClient)
                {
                    return;
                }

                if(!dedicated && !server.IsConnectedClient)
                {
                    return;
                }

                if(!solo)
                {
                    using var writer = new FastBufferWriter(1, Allocator.Temp);
                    writer.WriteValueSafe(true);
                    peer.CustomMessagingManager.SendNamedMessage(LobbyProtocol.ReadyRequest,
                        NetworkManager.ServerClientId, writer, NetworkDelivery.ReliableSequenced);
                }
                Next(1);
            }
            else if(stage == 1 && waited > 0.4d)
            {
                if(dedicated)
                {
                    using var writer = new FastBufferWriter(1, Allocator.Temp);
                    peer.CustomMessagingManager.SendNamedMessage(LobbyProtocol.StartRequest,
                        NetworkManager.ServerClientId, writer, NetworkDelivery.ReliableSequenced);
                }
                else
                {
                    server.GetComponent<ClientLobby>().RequestStartGame();
                }

                Next(2);
            }
            else if(stage == 2)
            {
                if(!server.GetComponent<ServerLobby>().IsPlaying)
                {
                    return;
                }

                if(!dedicated && menus.State != PrototypeMenus.MenuState.Playing)
                {
                    return;
                }

                if(waited < 0.3d)
                {
                    return;
                }

                foreach(var obj in server.SpawnManager.SpawnedObjectsList)
                {
                    if(obj.TryGetComponent(out NetworkBacteria b))
                    {
                        bacteria = b;
                    }
                }

                victim = (dedicated ? server.ConnectedClients[peer.LocalClientId].PlayerObject
                                        : server.LocalClient.PlayerObject).GetComponent<NetworkPlayerLife>();
                if(!dedicated)
                {
                    var input = GameObject.Find("Player").GetComponent<PlayerInput>();
                    input.actions.Disable();
                }
                if(!solo)
                {
                    foreach(var obj in peer.SpawnManager.SpawnedObjectsList)
                    {
                        if(obj.TryGetComponent(out PlayerNetworkIdentity identity))
                        {
                            identity.enabled = false;
                        }

                        foreach(var col in obj.GetComponentsInChildren<Collider>())
                        {
                            col.enabled = false;
                        }
                    }
                    if(!dedicated)
                    {
                        server.ConnectedClients[peer.LocalClientId].PlayerObject.transform.position
                        = victim.transform.position + Vector3.right * 20f;
                    }
                    // 测试端没有本机 Player 桥接，补发它自己的真实相机姿态，供死亡后选择观战目标。
                    var peerCamera = new GameObject("ExecutionCheckPeerCamera").AddComponent<Camera>();
                    peerCamera.enabled = false;
                    var peerPlayer = peer.LocalClient.PlayerObject;
                    peerCamera.transform.SetPositionAndRotation(peerPlayer.transform.TransformPoint(new Vector3(0f, 1.6f, 0.25f)),
                        peerPlayer.transform.rotation);
                    peerPlayer.GetComponent<PlayerNetworkView>().SetLocalCamera(peerCamera);
                }
                PlaceBacteria();
                Next(3);
            }
            else if(stage == 3)
            {
                if(!victim.IsBeingExecuted)
                {
                    return;
                }

                deathTime = victim.Execution.DeathTime;
                lockedBacteriaPosition = bacteria.transform.position;
                Require(Vector2.Distance(new Vector2(lockedBacteriaPosition.x, lockedBacteriaPosition.z),
                    new Vector2(victim.transform.position.x, victim.transform.position.z)) >= 2.95f,
                    "Execution began without safe separation.");
                Require(!victim.ServerTryBeginExecution(bacteria, bacteria.transform, 2.8f), "Duplicate lock accepted.");
                Next(4);
            }
            else if(stage == 4)
            {
                if(server.ServerTime.Time - victim.Execution.StartTime < 0.5d)
                {
                    return;
                }

                Require(victim.IsActive, "Victim died before attack finished.");
                Require(bacteria.GetComponentInChildren<Animator>().GetCurrentAnimatorStateInfo(0)
                    .IsName("Base Layer.Attack"), "Attack animation not playing.");
                if(!dedicated)
                {
                    Require(!menus.CanControlPlayer, "Execution did not lock controls.");
                    Require(GameObject.Find("Player").GetComponent<PlayerExecutionView>().IsActive, "Camera not locked.");
                    menus.PauseGame();
                    menus.ResumeGame();
                    Require(!menus.CanControlPlayer, "Pause unlocked execution.");
                }
                if(cancelCount < 2 && !solo)
                {
                    if(cancelCount == 0)
                    {
                        bacteria.enabled = false;
                    }
                    else
                    {
                        bacteria.NetworkObject.Despawn(true);
                    }

                    Next(5);
                }
                else
                {
                    if(!dedicated)
                    {
                        ScreenCapture.CaptureScreenshot("Logs/ExecutionAttack.png");
                    }

                    Next(6);
                }
            }
            else if(stage == 5 && waited > 0.3d)
            {
                Require(victim.IsActive && !victim.IsBeingExecuted, "Cancelled execution kept victim locked.");
                if(!dedicated)
                {
                    Require(menus.CanControlPlayer, "Cancelled execution did not restore controls.");
                }

                cancelCount++;
                if(bacteria == null)
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Material/Prefabs/Enemy/NetworkBacteria.prefab");
                    bacteria = Object.Instantiate(prefab, victim.transform.position, Quaternion.identity).GetComponent<NetworkBacteria>();
                    bacteria.NetworkObject.Spawn();
                }
                bacteria.enabled = true;
                PlaceBacteria();
                Next(3);
            }
            else if(stage == 6)
            {
                if(victim.IsActive)
                {
                    Require(Vector3.Distance(lockedBacteriaPosition, bacteria.transform.position) < 0.02f,
                        "Bacteria drifted toward victim during execution.");
                    if(!dedicated)
                    {
                        CheckExecutionView();
                    }

                    Require(server.ServerTime.Time <= deathTime + 0.3d, "Server did not finish execution on time.");
                    return;
                }
                Require(server.ServerTime.Time >= deathTime - 0.05d, "Server confirmed death too early.");
                bacteria.enabled = false;
                Next(7);
            }
            else if(stage == 7 && waited > 0.3d)
            {
                if(!dedicated)
                {
                    Require(transition.IsPlaying, "Death static did not start.");
                    Require(!spectator.IsSpectating, "Spectating began before static finished.");
                    var vp = GameObject.Find("Player").GetComponent<VideoPlayer>();
                    Require(vp.isPrepared && vp.isPlaying && vp.texture != null, "User video did not play.");
                    Require(!menus.CanControlPlayer, "Dead player can move.");
                    ScreenCapture.CaptureScreenshot("Logs/ExecutionStatic.png");
                }
                if(!solo)
                {
                    var replica = peer.SpawnManager.SpawnedObjects[victim.NetworkObjectId].GetComponent<NetworkPlayerLife>();
                    Require(!replica.IsActive && replica.Execution.Started, "Death state was not replicated.");
                }
                Next(8);
            }
            else if(stage == 8 && waited > 1.3d)
            {
                if(!dedicated)
                {
                    Require(!transition.IsPlaying, "Static did not finish.");
                    if(solo)
                    {
                        Require(menus.State == PrototypeMenus.MenuState.Result, "Last death did not end round.");
                        var result = Object.FindAnyObjectByType<RoundResultView>();
                        Require(result != null && result.IsShowing, "Result did not appear after static.");
                    }
                    else
                    {
                        Require(spectator.IsSpectating && spectator.CurrentTarget != null, "Did not enter teammate view.");
                    }
                }
                Finish(null);
            }
        }
        catch(Exception e) { Finish(e); }
    }

    private static void PlaceBacteria()
    {
        var agent = bacteria.GetComponent<NavMeshAgent>();
        Vector3 desired = victim.transform.position + victim.transform.forward * 2.1f;
        var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
        Require(NavMesh.SamplePosition(desired, out NavMeshHit hit, 1f, filter), "No NavMesh near victim.");
        Require(agent.Warp(hit.position), "Could not place test bacteria.");
        Vector3 facing = victim.transform.position - hit.position;
        facing.y = 0;
        bacteria.transform.rotation = Quaternion.LookRotation(facing);
    }

    // 整段攻击都检查头部是否在镜头内，以及模型有没有伸到摄像机后方。
    private static void CheckExecutionView()
    {
        var camera = GameObject.Find("Player").GetComponentInChildren<Camera>();
        minimumMeshDepth = Mathf.Min(minimumMeshDepth, VerifyExecutionCamera(camera, bacteria));
        float elapsed = (float)(server.ServerTime.Time - victim.Execution.StartTime);
        int index = elapsed >= 2.4f ? 2 : elapsed >= 1.5f ? 1 : elapsed >= 0.8f ? 0 : -1;
        if(index >= 0 && (capturedViews & (1 << index)) == 0)
        {
            capturedViews |= 1 << index;
            ScreenCapture.CaptureScreenshot("Logs/ExecutionViewPhase" + index + ".png");
        }
    }

    public static float VerifyExecutionCamera(Camera camera, NetworkBacteria bacteria)
    {
        Vector3 head = camera.WorldToViewportPoint(bacteria.ExecutionLookPoint.position);
        Require(head.z > 0.5f && head.x > 0.2f && head.x < 0.8f && head.y > 0.2f && head.y < 0.8f,
            "Animated head left victim camera: " + head);
        var renderer = bacteria.GetComponentInChildren<SkinnedMeshRenderer>();
        var mesh = new UnityEngine.Mesh();
        float nearest = float.MaxValue;
        try
        {
            renderer.BakeMesh(mesh, true);
            foreach(var vertex in mesh.vertices)
            {
                float depth = Vector3.Dot(renderer.transform.TransformPoint(vertex) - camera.transform.position,
                    camera.transform.forward);
                nearest = Mathf.Min(nearest, depth);
                Require(depth > camera.nearClipPlane + 0.02f, "Attack model entered victim camera.");
            }
        }
        finally { Object.DestroyImmediate(mesh); }
        return nearest;
    }

    private static void Finish(Exception failure)
    {
        EditorApplication.update -= Tick;
        string message = failure == null ? "PASS: " + (dedicated ? "Dedicated server" : solo ? "Last-player result" : "Cancel/Despawn/Spectate")
            : "FAIL stage " + stage + ": " + failure.Message;
        SessionState.SetString("ExecutionCheck.Result", message);
        if(failure == null && !dedicated)
        {
            Debug.Log("Minimum attack mesh distance in camera space: " + minimumMeshDepth);
        }

        if(peer != null && peer.IsListening)
        {
            peer.Shutdown();
        }

        if(server != null)
        {
            server.GetComponent<NetworkSession>().LeaveRoom();
        }

        if(failure == null)
        {
            Debug.Log(message);
        }
        else
        {
            Debug.LogException(failure);
        }
    }
}
