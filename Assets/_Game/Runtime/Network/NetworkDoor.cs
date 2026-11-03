using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 接收开关门请求，服务器确认团队取得钥匙并校验交互条件后，同步门状态与各端表现。
/// </summary>
public class NetworkDoor : NetworkBehaviour
{
    [SerializeField]
    private DoorController doorController;

    [SerializeField]
    private float maxInteractDistance = 2.5f;

    private NetworkVariable<bool> isOpen = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public bool IsOpen => NetworkManager != null && isOpen.Value;

    // 订阅状态变化，并立即应用生成时的门状态。
    public override void OnNetworkSpawn()
    {
        isOpen.OnValueChanged += HandleOpenStateChanged;
        doorController.ApplyOpenState(isOpen.Value, true);
    }

    // 网络对象移除时取消订阅。
    public override void OnNetworkDespawn()
    {
        isOpen.OnValueChanged -= HandleOpenStateChanged;
    }

    // 收到新状态后，由门控制器平滑旋转。
    private void HandleOpenStateChanged(bool previous, bool current)
    {
        doorController.ApplyOpenState(current);
    }

    // 根据当前状态向服务器请求打开或关闭。
    public void RequestInteraction()
    {
        if(
            NetworkManager == null
            || !IsSpawned
            || !NetworkManager.IsListening
            || !NetworkManager.IsConnectedClient
            || NetworkManager.ShutdownInProgress
        )
        {
            return;
        }

        SetOpenStateRpc(!isOpen.Value);
    }

    // 服务器校验发送者、阶段、距离和团队钥匙，通过后修改共享状态。
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void SetOpenStateRpc(bool desiredOpen, RpcParams rpcParams = default)
    {
        if(
            NetworkManager == null
            || !NetworkManager.IsListening
            || !IsServer
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

        // 死者即使发来 RPC，也不能改变门的状态。
        if(!playerObject.TryGetComponent(out NetworkPlayerLife life) || !life.IsActive || life.IsBeingExecuted)
        {
            return;
        }

        if(doorController == null || doorController.InteractionPoint == null)
        {
            return;
        }

        float distance = Vector3.Distance(
            playerObject.transform.position,
            doorController.InteractionPoint.position
        );

        if(distance > maxInteractDistance)
        {
            return;
        }

        ServerWorldSpawner serverWorldSpawner = NetworkManager.GetComponent<ServerWorldSpawner>();

        if(serverWorldSpawner == null || !serverWorldSpawner.HasKey)
        {
            return;
        }

        if(isOpen.Value == desiredOpen)
        {
            return;
        }

        isOpen.Value = desiredOpen;
    }
}
