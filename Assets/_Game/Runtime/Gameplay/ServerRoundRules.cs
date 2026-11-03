using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 检查当前玩家生死与出口条件，只计算结果，由大厅提交阶段并同步。
/// </summary>
[RequireComponent(
    typeof(NetworkManager),
    typeof(ServerLobby),
    typeof(ServerWorldSpawner)
)]
public class ServerRoundRules : MonoBehaviour
{
    [SerializeField]
    private ExitZone exitZone;

    private NetworkManager networkManager;

    private ServerLobby serverLobby;

    private ServerWorldSpawner spawner;

    public RoundResult EvaluateResult()
    {
        if(
            networkManager == null
            || !networkManager.IsListening
            || networkManager.ShutdownInProgress
            || serverLobby == null
            || !serverLobby.IsPlaying
            || spawner == null
            || networkManager.ConnectedClients.Count < 1
            || exitZone == null
        )
        {
            return RoundResult.None;
        }

        int aliveCount = 0;
        bool allAlivePlayerInside = true;

        foreach(NetworkClient client in networkManager.ConnectedClients.Values)
        {
            NetworkObject playerObject = client.PlayerObject;

            if(playerObject == null || !playerObject.IsSpawned)
            {
                return RoundResult.None;
            }

            NetworkPlayerLife playerLife = playerObject.GetComponent<NetworkPlayerLife>();

            if(playerLife == null)
            {
                return RoundResult.None;
            }

            if(!playerLife.IsActive)
            {
                continue;
            }

            // 处决尚未结束时仍然存活，但本局不能提前判定逃生成功。
            if(playerLife.IsBeingExecuted)
            {
                return RoundResult.None;
            }

            aliveCount++;
            Vector3 position = playerObject.transform.position;

            if(!exitZone.ContainPoint(position))
            {
                allAlivePlayerInside = false;
            }
        }

        if(aliveCount == 0)
        {
            return RoundResult.Defeat;
        }

        if(
            !allAlivePlayerInside
            || !spawner.HasKey
            || !spawner.IsExitDoorOpen
        )
        {
            return RoundResult.None;
        }

        return RoundResult.Victory;
    }

    private void Awake()
    {
        networkManager = GetComponent<NetworkManager>();
        serverLobby = GetComponent<ServerLobby>();
        spawner = GetComponent<ServerWorldSpawner>();
    }
}
