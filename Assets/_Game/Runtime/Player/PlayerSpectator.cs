using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

/// <summary>
/// 死亡后在本机以队友第一人称视角观战，左键切换目标，退出时恢复摄像机和外观。
/// </summary>
[DefaultExecutionOrder(200)]
public class PlayerSpectator : MonoBehaviour
{
    [SerializeField]
    private NetworkManager networkManager;

    [SerializeField]
    private PrototypeMenus prototypeMenus;

    [SerializeField]
    private Camera playerCamera;

    [SerializeField]
    private Camera spectatorCamera;

    private List<NetworkObject> targets = new();

    private NetworkObject currentTarget;

    private bool isSpectating;

    private Transform viewPoint;

    private PlayerNetworkView targetView;

    private Renderer[] targetRenderers;

    private ShadowCastingMode[] originalShadowModes;

    private AudioListener playerListener;

    private AudioListener spectatorListener;

    private bool playerCameraWasEnabled;

    private bool playerListenerWasEnabled;

    private PrototypeMenus.MenuState lastMenuState;

    public bool IsSpectating => isSpectating;

    public NetworkObject CurrentTarget => currentTarget;

    private bool HasConnection => networkManager != null
        && networkManager.IsClient && networkManager.IsConnectedClient
        && !networkManager.ShutdownInProgress && networkManager.SpawnManager != null;

    // 客户端从已生成对象中筛选存活队友，不访问服务端专用的 ConnectedClients。
    private void RefreshTargets()
    {
        targets.Clear();

        if(
            !HasConnection
        )
        {
            return;
        }

        foreach(NetworkObject playerObject in networkManager.SpawnManager.SpawnedObjectsList)
        {
            if(!IsValidTarget(playerObject))
            {
                continue;
            }

            targets.Add(playerObject);
        }

        targets.Sort((a, b) => a.OwnerClientId.CompareTo(b.OwnerClientId));
    }

    private bool IsValidTarget(NetworkObject playerObject)
    {
        return HasConnection && playerObject != null && playerObject.IsSpawned
            && playerObject.IsPlayerObject && playerObject.OwnerClientId != networkManager.LocalClientId
            && playerObject.TryGetComponent(out NetworkPlayerLife life) && life.IsActive
            && playerObject.TryGetComponent(out PlayerNetworkView view) && view.HasCameraPose;
    }

    // 输入在 Update 检测；这里也供目标死亡或断线时自动选择下一名队友。
    private void SelectNextTarget()
    {
        RefreshTargets();

        if(targets.Count == 0)
        {
            SetTarget(null);
            return;
        }

        int index = targets.IndexOf(currentTarget) + 1;

        if(index >= targets.Count)
        {
            index = 0;
        }

        SetTarget(targets[index]);
    }

    // 只改变本机渲染方式，保留投影；切换时恢复旧目标，不关闭网络物体或碰撞体。
    private void SetTarget(NetworkObject target)
    {
        if(currentTarget == target && viewPoint != null)
        {
            return;
        }

        if(targetRenderers != null)
        {
            for(int i = 0; i < targetRenderers.Length; i++)
            {
                if(targetRenderers[i] != null)
                {
                    targetRenderers[i].shadowCastingMode = originalShadowModes[i];
                }
            }
        }

        targetRenderers = null;
        originalShadowModes = null;
        currentTarget = target;
        viewPoint = null;
        targetView = null;
        if(target == null)
        {
            return;
        }

        targetView = target.GetComponent<PlayerNetworkView>();
        viewPoint = targetView.ViewPoint;
        targetRenderers = target.GetComponentsInChildren<Renderer>(true);
        originalShadowModes = new ShadowCastingMode[targetRenderers.Length];
        for(int i = 0; i < targetRenderers.Length; i++)
        {
            originalShadowModes[i] = targetRenderers[i].shadowCastingMode;
            targetRenderers[i].shadowCastingMode = ShadowCastingMode.ShadowsOnly;
        }
    }

    // 由本机死亡流程进入，避免双摄像机和双 AudioListener 同时运行。
    public void BeginSpectating()
    {
        if(isSpectating || !HasConnection)
        {
            return;
        }

        if(playerCamera == null || spectatorCamera == null || playerCamera == spectatorCamera
                    || prototypeMenus == null)
        {
            Debug.LogError("[PlayerSpectator] 请绑定本机摄像机、观战摄像机和菜单。", this);
            return;
        }

        playerCameraWasEnabled = playerCamera.enabled;
        playerListenerWasEnabled = playerListener != null && playerListener.enabled;
        spectatorCamera.CopyFrom(playerCamera);
        // 移动真正负责渲染的相机，不依赖脚本所在父物体和相机子节点的摆放方式。
        spectatorCamera.transform.SetPositionAndRotation(playerCamera.transform.position, playerCamera.transform.rotation);
        playerCamera.enabled = false;
        if(playerListener != null)
        {
            playerListener.enabled = false;
        }

        spectatorCamera.enabled = true;
        if(spectatorListener != null)
        {
            spectatorListener.enabled = true;
        }

        isSpectating = true;
        lastMenuState = prototypeMenus.State;
        SelectNextTarget();
        FollowTarget();
    }

    // 结算、退出及停用时对称恢复，重复调用不会重新启用已经停用的摄像机。
    public void StopSpectating()
    {
        if(!isSpectating)
        {
            return;
        }

        isSpectating = false;
        SetTarget(null);
        targets.Clear();
        if(spectatorCamera != null)
        {
            spectatorCamera.enabled = false;
        }

        if(spectatorListener != null)
        {
            spectatorListener.enabled = false;
        }

        if(playerCamera != null)
        {
            playerCamera.enabled = playerCameraWasEnabled;
        }

        if(playerListener != null)
        {
            playerListener.enabled = playerListenerWasEnabled;
        }
    }

    private void Awake()
    {
        if(playerCamera != null)
        {
            playerListener = playerCamera.GetComponent<AudioListener>();
        }

        if(spectatorCamera != null)
        {
            spectatorListener = spectatorCamera.GetComponent<AudioListener>();
            spectatorCamera.enabled = false;
        }
        if(spectatorListener != null)
        {
            spectatorListener.enabled = false;
        }
    }

    private void Update()
    {
        // 恢复暂停的同一帧不消费左键，避免点击“继续游戏”时顺便切换队友。
        if(!isSpectating || !HasConnection || prototypeMenus == null
            || prototypeMenus.State != PrototypeMenus.MenuState.Playing
            || lastMenuState != PrototypeMenus.MenuState.Playing || Mouse.current == null)
        {
            return;
        }

        if(Mouse.current.leftButton.wasPressedThisFrame)
        {
            SelectNextTarget();
        }
    }

    private void LateUpdate()
    {
        if(!isSpectating)
        {
            return;
        }

        if(!HasConnection || prototypeMenus == null
                    || prototypeMenus.State == PrototypeMenus.MenuState.MainMenu)
        {
            StopSpectating();
            return;
        }

        // 结算期间冻结最后的视角，待黑幕遮住后由结算视图恢复原摄像机。
        if(prototypeMenus.State == PrototypeMenus.MenuState.Result)
        {
            return;
        }

        lastMenuState = prototypeMenus.State;
        if(!IsValidTarget(currentTarget))
        {
            SelectNextTarget();
        }

        FollowTarget();
    }

    // 花屏结束的当帧就对齐目标；目标数据尚未到达时保留最后一个有效画面。
    private void FollowTarget()
    {
        if(viewPoint == null || targetView == null || !targetView.HasCameraPose || spectatorCamera == null)
        {
            return;
        }

        spectatorCamera.transform.SetPositionAndRotation(viewPoint.position, viewPoint.rotation);
        spectatorCamera.fieldOfView = targetView.FieldOfView;
    }

    private void OnDisable() => StopSpectating();
}
