using Unity.Netcode;
using UnityEngine;
using System;

/// <summary>
/// 按启动参数或专用服务器构建自动启动 Server，停用本机玩家和菜单，保留手动调试入口。
/// </summary>
[RequireComponent(typeof(NetworkSession), typeof(NetworkManager))]
public class DedicatedServerBootstrap : MonoBehaviour
{
    [SerializeField]
    private NetworkSession networkSession;

    [SerializeField]
    private GameObject localPlayerRoot;

    [SerializeField]
    private GameObject clientMenuRoot;

    private bool changedBackgroundSetting;

    private bool originalRunInBackground;

    private bool automaticServer;

    // Start 在场景组件 Awake/OnEnable 后执行，保证大厅已订阅启动事件。
    private void Start()
    {
        if(!NetworkLaunchOptions.TryParse(Environment.GetCommandLineArgs(), out var options, out string error))
        {
            Debug.LogError($"[DedicatedServerBootstrap] {error}", this);
            if(!Application.isEditor)
            {
                Application.Quit(2);
            }

            return;
        }
#if UNITY_SERVER
        automaticServer = true;
        if(options.AutoConnect)
        {
            Debug.LogError("[DedicatedServerBootstrap] 专用服务器构建不能作为客户端启动。", this);
            Application.Quit(2);
            return;
        }
#else
        automaticServer = options.RunServer;
#endif
        if(automaticServer)
        {
            LaunchServer();
            if(!GetComponent<NetworkManager>().IsServer && !Application.isEditor)
            {
                Application.Quit(1);
            }
        }
        // 客户端始终从主菜单选择创建或浏览房间，地址参数只配置目标服务器。
    }

    // 在 Play Mode 中从组件菜单调用；普通客户端不会自动启动服务器。
    [ContextMenu("启动本机独立服务器")]
    public void LaunchServer()
    {
        if(!Application.isPlaying)
        {
            Debug.LogWarning("[DedicatedServerBootstrap] 请先进入 Play Mode。", this);
            return;
        }

        NetworkManager manager = GetComponent<NetworkManager>();
        if(networkSession == null || manager == null
            || localPlayerRoot == null || clientMenuRoot == null)
        {
            Debug.LogError("[DedicatedServerBootstrap] 请绑定 NetworkSession、本机 Player 和菜单根物体。", this);
            return;
        }

        // 停用客户端内容时不能把服务器自身或其他 NetworkManager 一起关闭。
        if(networkSession.gameObject != gameObject
            || transform.IsChildOf(localPlayerRoot.transform)
            || transform.IsChildOf(clientMenuRoot.transform)
            || localPlayerRoot.GetComponentInChildren<NetworkManager>(true) != null
            || clientMenuRoot.GetComponentInChildren<NetworkManager>(true) != null)
        {
            Debug.LogError("[DedicatedServerBootstrap] 启动组件与 NetworkSession 必须同物体，客户端根物体不能包含服务器。", this);
            return;
        }

        if(manager.IsListening || manager.IsClient || manager.IsServer || manager.ShutdownInProgress)
        {
            Debug.LogWarning("[DedicatedServerBootstrap] 网络会话已启动或正在关闭，不能重复启动。", this);
            return;
        }

        bool playerWasActive = localPlayerRoot.activeSelf;
        bool menuWasActive = clientMenuRoot.activeSelf;
        float previousTimeScale = Time.timeScale;
        bool previousBackground = Application.runInBackground;
        bool started = false;

        try
        {
            localPlayerRoot.SetActive(false);
            clientMenuRoot.SetActive(false);
            Time.timeScale = 1f;
            Application.runInBackground = true;

            networkSession.StartServerGame();
            started = manager.IsListening && manager.IsServer && !manager.IsClient;
            var directory = GetComponent<ServerRoomDirectory>();
            if(started && directory != null && directory.isActiveAndEnabled && !directory.IsListening)
            {
                manager.Shutdown();
                started = false;
            }
            if(!started)
            {
                Debug.LogError("[DedicatedServerBootstrap] 服务器启动失败，恢复本机界面。", this);
                return;
            }

            if(!changedBackgroundSetting)
            {
                originalRunInBackground = previousBackground;
                changedBackgroundSetting = true;
            }

            // 限制无界面进程空转帧率；Animator 仍需运行，以同步敌人动作。
            Application.targetFrameRate = 60;
            Debug.Log($"[DedicatedServerBootstrap] READY IsServer={manager.IsServer}, IsClient={manager.IsClient}, IsHost={manager.IsHost}", this);
        }
        finally
        {
            // 端口占用或启动异常时，不能让玩家停留在被关闭的界面。
            if(!started)
            {
                localPlayerRoot.SetActive(playerWasActive);
                clientMenuRoot.SetActive(menuWasActive);
                Time.timeScale = previousTimeScale;
                Application.runInBackground = previousBackground;
            }
        }
    }

    // 退出测试时恢复应用原来的后台设置。
    private void OnDestroy()
    {
        if(changedBackgroundSetting)
        {
            Application.runInBackground = originalRunInBackground;
        }
    }
}
