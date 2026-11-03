using System;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

/// <summary>
/// 用三个真实 NGO 端检查实际摄像机姿态同步、死亡观战、左键切换和目标失效恢复。
/// </summary>
public static class PlayerSpectatorChecks
{
    private static NetworkManager host;

    private static readonly NetworkManager[] peers = new NetworkManager[2];

    private static readonly Camera[] sourceCameras = new Camera[2];

    private static PrototypeMenus menus;

    private static PlayerSpectator spectator;

    private static Camera spectatorCamera;

    private static Camera localCamera;

    private static Mouse mouse;

    private static int stage;

    private static double stageTime;

    private static double deadline;

    private static Vector3 originalPlayerPosition;

    private static NetworkObject selected;

    private static Renderer[] localRenderers;

    private static bool[] localRendererStates;

    private static bool checkBodyOnly;

    public static void Start(bool bodyOnly = false)
    {
        checkBodyOnly = bodyOnly;
        Require(EditorApplication.isPlaying, "Enter Play Mode first.");
        Application.runInBackground = true;
        host = Object.FindAnyObjectByType<NetworkSession>().GetComponent<NetworkManager>();
        Require(!host.IsListening, "Network already running.");
        menus = Object.FindAnyObjectByType<PrototypeMenus>();
        spectator = Object.FindAnyObjectByType<PlayerSpectator>();
        var so = new SerializedObject(spectator);
        spectatorCamera = (Camera)so.FindProperty("spectatorCamera").objectReferenceValue;
        localCamera = (Camera)so.FindProperty("playerCamera").objectReferenceValue;
        ((UnityTransport)host.NetworkConfig.NetworkTransport).SetConnectionData("127.0.0.1", 18053, "127.0.0.1");
        Require(host.StartHost(), "Host failed to start.");
        for(int i = 0; i < peers.Length; i++)
        {
            var go = new GameObject("SpectatorCheckPeer" + i);
            var peer = go.AddComponent<NetworkManager>();
            var transport = go.AddComponent<UnityTransport>();
            transport.SetConnectionData("127.0.0.1", 18053);
            peer.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = transport,
                PlayerPrefab = host.NetworkConfig.PlayerPrefab,
                ConnectionApproval = true,
                EnableSceneManagement = false,
                ForceSamePrefabs = host.NetworkConfig.ForceSamePrefabs,
                TickRate = host.NetworkConfig.TickRate
            };
            foreach(var list in host.NetworkConfig.Prefabs.NetworkPrefabsLists)
            {
                peer.NetworkConfig.Prefabs.NetworkPrefabsLists.Add(list);
            }

            peers[i] = peer;
            Require(peer.StartClient(), "Peer failed to start.");
            sourceCameras[i] = new GameObject("RemoteMainCamera" + i).AddComponent<Camera>();
            sourceCameras[i].enabled = false;
        }
        mouse = InputSystem.AddDevice<Mouse>();
        SessionState.SetString("SpectatorCheck.Result", "RUNNING");
        deadline = EditorApplication.timeSinceStartup + 35d;
        Next(0);
        EditorApplication.update += Tick;
    }

    private static void Tick()
    {
        try
        {
            Require(EditorApplication.timeSinceStartup < deadline, "Timeout stage " + stage);
            double elapsed = EditorApplication.timeSinceStartup - stageTime;
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

                        foreach(var col in obj.GetComponentsInChildren<Collider>())
                        {
                            col.enabled = false;
                        }
                    }
                }
            }

            if(stage == 0)
            {
                foreach(var peer in peers)
                {
                    if(!peer.IsConnectedClient)
                    {
                        return;
                    }
                }

                foreach(var peer in peers)
                {
                    using var writer = new FastBufferWriter(1, Allocator.Temp);
                    writer.WriteValueSafe(true);
                    peer.CustomMessagingManager.SendNamedMessage(LobbyProtocol.ReadyRequest,
                        NetworkManager.ServerClientId, writer, NetworkDelivery.ReliableSequenced);
                }
                Next(1);
            }
            else if(stage == 1 && elapsed > 0.4d)
            {
                host.GetComponent<ClientLobby>().RequestStartGame();
                Next(2);
            }
            else if(stage == 2 && elapsed > 0.4d)
            {
                if(menus.State != PrototypeMenus.MenuState.Playing)
                {
                    return;
                }

                foreach(var obj in host.SpawnManager.SpawnedObjectsList)
                {
                    if(obj.TryGetComponent(out NetworkBacteria bacteria))
                    {
                        bacteria.enabled = false;
                    }
                }

                GameObject.Find("Player").GetComponent<PlayerInput>().actions.Disable();
                originalPlayerPosition = GameObject.Find("Player").transform.position;
                localRenderers = GameObject.Find("Player").GetComponentsInChildren<Renderer>(true);
                localRendererStates = new bool[localRenderers.Length];
                for(int i = 0; i < localRenderers.Length; i++)
                {
                    localRendererStates[i] = localRenderers[i].enabled;
                }

                for(int i = 0; i < peers.Length; i++)
                {
                    PublishCamera(i, false);
                }
                // 故意打乱相机子节点，验证跟随的是相机本身而不是错误的父物体。
                spectatorCamera.transform.localPosition = new Vector3(7f, 3f, -9f);
                Next(3);
            }
            else if(stage == 3 && elapsed > 0.5d)
            {
                foreach(var peer in peers)
                {
                    Require(host.ConnectedClients[peer.LocalClientId].PlayerObject.GetComponent<PlayerNetworkView>().HasCameraPose,
                    "Remote camera pose was not replicated.");
                }

                Require(host.LocalClient.PlayerObject.GetComponent<NetworkPlayerLife>().ServerTryKill(), "Could not kill test victim.");
                Next(4);
            }
            else if(stage == 4 && elapsed > 0.3d)
            {
                Require(spectator.IsSpectating && spectator.CurrentTarget != null, "Death did not select teammate.");
                foreach(var renderer in localRenderers)
                {
                    Require(!renderer.enabled, "Dead local body or shadow is still visible.");
                }

                VerifyCamera(0);
                ScreenCapture.CaptureScreenshot("Logs/SpectatorFirstPerson.png");
                PublishCamera(0, true);
                Next(5);
            }
            else if(stage == 5 && elapsed > 0.5d)
            {
                VerifyCamera(0);
                if(checkBodyOnly)
                {
                    selected = spectator.CurrentTarget;
                    Next(9);
                    return;
                }
                InputSystem.QueueStateEvent(mouse, new MouseState().WithButton(MouseButton.Left));
                Next(6);
            }
            else if(stage == 6 && elapsed > 0.2d)
            {
                InputSystem.QueueStateEvent(mouse, new MouseState());
                VerifyCamera(1);
                selected = spectator.CurrentTarget;
                menus.PauseGame();
                Next(7);
            }
            else if(stage == 7 && elapsed > 0.15d)
            {
                InputSystem.QueueStateEvent(mouse, new MouseState().WithButton(MouseButton.Left));
                Next(8);
            }
            else if(stage == 8 && elapsed > 0.2d)
            {
                Require(spectator.CurrentTarget == selected, "Pause click changed spectator target.");
                InputSystem.QueueStateEvent(mouse, new MouseState());
                menus.ResumeGame();
                Next(9);
            }
            else if(stage == 9 && elapsed > 0.2d)
            {
                Require(selected.GetComponent<NetworkPlayerLife>().ServerTryKill(),
                    "Could not kill selected teammate.");
                Next(10);
            }
            else if(stage == 10 && elapsed > 0.3d)
            {
                VerifyCamera(selected.OwnerClientId == peers[0].LocalClientId ? 1 : 0);
                var deadTeammate = selected;
                Require(deadTeammate.IsSpawned, "Death removed network state needed by the round.");
                foreach(var renderer in deadTeammate.GetComponentsInChildren<Renderer>(true))
                {
                    Require(!renderer.enabled, "Dead remote body is still visible after spectator switched away.");
                }

                foreach(var collider in deadTeammate.GetComponentsInChildren<Collider>(true))
                {
                    Require(!collider.enabled, "Dead remote body still blocks players.");
                }

                Require(Vector3.Distance(originalPlayerPosition, GameObject.Find("Player").transform.position) < 0.01f,
                                    "Spectator moved the dead Player body.");
                host.GetComponent<NetworkSession>().LeaveRoom();
                Next(11);
            }
            else if(stage == 11 && elapsed > 0.4d)
            {
                Require(!spectator.IsSpectating && !spectatorCamera.enabled && localCamera.enabled,
                    "Leaving did not restore local camera.");
                for(int i = 0; i < localRenderers.Length; i++)
                {
                    Require(localRenderers[i].enabled == localRendererStates[i], "Leaving did not restore local body visibility.");
                }

                Finish(null);
            }
        }
        catch(Exception ex) { Finish(ex); }
    }

    private static void PublishCamera(int index, bool moved)
    {
        NetworkObject obj = peers[index].LocalClient.PlayerObject;
        Vector3 position = originalPlayerPosition + Vector3.right * (3f + index * 3f) + (moved ? Vector3.forward : Vector3.zero);
        Quaternion rotation = Quaternion.Euler(0f, moved ? 135f : 90f * index, 0f);
        obj.GetComponent<NetworkTransform>().Teleport(position, rotation, Vector3.one);
        Camera camera = sourceCameras[index];
        camera.transform.SetPositionAndRotation(position + rotation * new Vector3(0.1f, 1.55f, 0.3f),
            rotation * Quaternion.Euler(moved ? -25f : 23f, 0f, 0f));
        camera.fieldOfView = index == 0 ? 73f : 67f;
        obj.GetComponent<PlayerNetworkView>().SetLocalCamera(camera);
    }

    private static void VerifyCamera(int index)
    {
        Require(spectator.CurrentTarget != null && spectator.CurrentTarget.OwnerClientId == peers[index].LocalClientId,
            "Wrong spectator target.");
        Camera source = sourceCameras[index];
        Require(Vector3.Distance(spectatorCamera.transform.position, source.transform.position) < 0.03f,
            "Spectator camera position differs from remote MainCamera: " + spectatorCamera.transform.position + " vs " + source.transform.position);
        Require(Quaternion.Angle(spectatorCamera.transform.rotation, source.transform.rotation) < 0.2f,
            "Spectator camera rotation differs from remote MainCamera.");
        Require(Mathf.Abs(spectatorCamera.fieldOfView - source.fieldOfView) < 0.01f, "Camera FOV differs.");
        Require(spectatorCamera.enabled && !localCamera.enabled, "Wrong active camera.");
    }

    private static void Next(int next)
    {
        stage = next;
        stageTime = EditorApplication.timeSinceStartup;
    }

    private static void Require(bool condition, string message)
    {
        if(!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void Finish(Exception failure)
    {
        EditorApplication.update -= Tick;
        foreach(var peer in peers)
        {
            if(peer != null && peer.IsListening)
            {
                peer.Shutdown();
            }
        }

        if(host != null && host.IsListening)
        {
            host.GetComponent<NetworkSession>().LeaveRoom();
        }

        if(mouse != null && mouse.added)
        {
            InputSystem.RemoveDevice(mouse);
        }

        string result = failure == null ? checkBodyOnly
                    ? "PASS: local-and-remote-body-hidden/collider/auto-spectate/leave-restores-body"
                    : "PASS: spectator/camera/click/pause/local-and-remote-body-hidden/collider/leave-restores-body"
                    : "FAIL stage " + stage + ": " + failure.Message;
        SessionState.SetString("SpectatorCheck.Result", result);
        if(failure == null)
        {
            Debug.Log(result);
        }
        else
        {
            Debug.LogException(failure);
        }
    }
}
