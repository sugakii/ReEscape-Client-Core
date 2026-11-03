using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 保存服务器确认的生命与处决状态；死亡记录保留到本局退出，供本机播放死亡转场。
/// </summary>
public class NetworkPlayerLife : NetworkBehaviour
{
    // true 表示存活，只有服务器能够改为死亡。
    private NetworkVariable<bool> isActive = new NetworkVariable<bool>(
        true,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private NetworkVariable<PlayerExecutionState> execution = new(default,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private NetworkObject playerObject;

    private ServerLobby serverLobby;

    public bool IsActive => IsSpawned && isActive.Value;

    public bool IsBeingExecuted => IsActive && execution.Value.Started;

    public PlayerExecutionState Execution => execution.Value;

    // 网络对象生成后，取得本次会话所属的大厅。
    public override void OnNetworkSpawn()
    {
        serverLobby = NetworkManager.GetComponent<ServerLobby>();
    }

    // 缓存同一物体上的网络对象。
    private void Awake()
    {
        playerObject = GetComponent<NetworkObject>();
    }

    // 仅在服务器游戏阶段确认一次死亡，重复调用返回 false。
    public bool ServerTryKill()
    {
        if(
            NetworkManager == null
            || serverLobby == null
            || !IsServer
            || !NetworkManager.IsListening
            || NetworkManager.ShutdownInProgress
            || !playerObject.IsSpawned
            || !serverLobby.IsPlaying
            || !isActive.Value
        )
        {
            return false;
        }

        isActive.Value = false;
        return true;
    }

    // 由已通过距离和遮挡检查的细菌调用，重复锁定会被拒绝。
    public bool ServerTryBeginExecution(NetworkBacteria bacteria, Transform lookPoint, float attackDuration)
    {
        if(
            NetworkManager == null
            || serverLobby == null
            || !IsServer
            || !NetworkManager.IsListening
            || NetworkManager.ShutdownInProgress
            || !playerObject.IsSpawned
            || !serverLobby.IsPlaying
            || bacteria == null || !bacteria.IsSpawned || bacteria.NetworkManager != NetworkManager
            || lookPoint == null || attackDuration <= 0f || !float.IsFinite(attackDuration)
        )
        {
            return false;
        }

        if(isActive.Value && !execution.Value.Started)
        {
            execution.Value = new PlayerExecutionState
            {
                Started = true,
                BacteriaId = bacteria.NetworkObjectId,
                StartTime = NetworkManager.ServerTime.Time,
                AttackDuration = attackDuration,
                Position = transform.position,
                LookPoint = lookPoint.position
            };
            return true;
        }

        return false;
    }

    // 细菌中途移除或本局结束时释放活人；死者的处决记录仍用于花屏。
    public void ServerCancelExecution(ulong bacteriaId)
    {
        if(IsServer && IsSpawned && IsActive && execution.Value.Started
            && execution.Value.BacteriaId == bacteriaId)
        {
            execution.Value = default;
        }
    }
}
