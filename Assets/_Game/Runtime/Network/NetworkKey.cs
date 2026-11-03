using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 由服务器校验钥匙拾取，同步已拾取状态并更新各端外观与碰撞体。
/// </summary>
public class NetworkKey : NetworkBehaviour, IInteractable
{
    [SerializeField]
    private GameObject visualRoot;

    [SerializeField]
    private Collider pickupCollider;

    [SerializeField]
    private Transform interactionPoint;

    [SerializeField]
    private InteractionPromptUI promptUI;

    [SerializeField]
    private float maxInteractDistance = 2.5f;

    // 只有服务器可修改；拾取后保留网络对象，供生成器查询团队钥匙状态。
    private NetworkVariable<bool> isCollected = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public Transform InteractionPoint => interactionPoint;

    public InteractionPromptUI PromptUI => promptUI;

    public bool IsCollected => isCollected.Value;

    // 已拾取时隐藏模型、关闭碰撞和提示；未拾取时由玩家交互脚本控制提示。
    private void ApplyCollectedState(bool collected)
    {
        visualRoot.SetActive(!collected);
        pickupCollider.enabled = !collected;

        if(collected)
        {
            promptUI.SetVisible(false);
        }
    }

    // 将同步后的拾取状态应用到本端。
    private void HandleCollectedStateChanged(bool previous, bool current)
    {
        ApplyCollectedState(current);
    }

    // 服务器检查阶段、发送者和距离，只允许成功拾取一次。
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void CollectRpc(RpcParams rpcParams = default)
    {
        if(
            NetworkManager == null
            || !IsServer
            || !NetworkManager.IsListening
            || !IsSpawned
            || NetworkManager.ShutdownInProgress
        )
        {
            return;
        }

        ServerLobby serverLobby = NetworkManager.GetComponent<ServerLobby>();

        if(serverLobby == null || !serverLobby.IsPlaying)
        {
            return;
        }

        ulong senderClientId = rpcParams.Receive.SenderClientId;

        if(!NetworkManager.ConnectedClients.TryGetValue(
            senderClientId,
            out NetworkClient client
        ))
        {
            return;
        }

        NetworkObject playerObject = client.PlayerObject;

        if(playerObject == null || !playerObject.IsSpawned)
        {
            return;
        }

        // 不能只依靠客户端关闭交互组件，服务器也要拒绝死者请求。
        if(!playerObject.TryGetComponent(out NetworkPlayerLife life) || !life.IsActive || life.IsBeingExecuted)
        {
            return;
        }

        if(interactionPoint == null)
        {
            return;
        }

        float distance = Vector3.Distance(
            playerObject.transform.position,
            interactionPoint.position
        );

        if(distance > maxInteractDistance)
        {
            return;
        }

        if(isCollected.Value)
        {
            return;
        }

        isCollected.Value = true;
        Debug.Log($"[NetworkKey] 拾取成功, ClientId={senderClientId}");
    }

    // 客户端只发送请求，不直接修改共享状态。
    public void Interact()
    {
        if(
            NetworkManager == null
            || !IsSpawned
            || !NetworkManager.IsListening
            || !NetworkManager.IsConnectedClient
            || NetworkManager.ShutdownInProgress
            || isCollected.Value
        )
        {
            return;
        }

        CollectRpc();
    }

    // 订阅变化并主动应用初始值，不能只等待变化事件。
    public override void OnNetworkSpawn()
    {
        isCollected.OnValueChanged += HandleCollectedStateChanged;
        ApplyCollectedState(isCollected.Value);
    }

    // 移除网络对象时取消订阅并隐藏提示。
    public override void OnNetworkDespawn()
    {
        isCollected.OnValueChanged -= HandleCollectedStateChanged;

        if(promptUI != null)
        {
            promptUI.SetVisible(false);
        }
    }

    // 清理运行时脱离层级的提示，再执行 NGO 自身的销毁清理。
    public override void OnDestroy()
    {
        if(Application.IsPlaying(gameObject) && promptUI != null)
        {
            promptUI.SetVisible(false);
            Destroy(promptUI.gameObject);
        }

        base.OnDestroy();
    }
}
