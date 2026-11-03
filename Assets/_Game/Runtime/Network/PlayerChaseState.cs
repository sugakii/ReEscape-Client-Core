using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 由服务器查询细菌视野，将该玩家的被追击状态同步给所属客户端。
/// </summary>
[DefaultExecutionOrder(50)]
public class PlayerChaseState : NetworkBehaviour
{
    private NetworkVariable<bool> isChased = new(
        false,
        NetworkVariableReadPermission.Owner,
        NetworkVariableWritePermission.Server);

    public bool IsChased => IsSpawned && isChased.Value;

    // 在细菌 Update 扫描之后汇总结果；没有细菌或细菌失效时自然变为 false。
    private void LateUpdate()
    {
        if(!IsSpawned || !IsServer || NetworkManager == null
            || !NetworkManager.IsListening || NetworkManager.ShutdownInProgress)
        {
            return;
        }

        bool visible = false;
        NetworkPlayerLife life = GetComponent<NetworkPlayerLife>();
        if(life != null && life.IsActive)
        {
            foreach(NetworkObject spawnedObject in NetworkManager.SpawnManager.SpawnedObjectsList)
            {
                if(spawnedObject.TryGetComponent(out NetworkBacteria bacteria)
                    && bacteria.IsPlayerVisible(OwnerClientId))
                {
                    visible = true;
                    break;
                }
            }
        }

        // 值变化时才标记网络变量，客户端无需发送被追击请求。
        if(isChased.Value != visible)
        {
            isChased.Value = visible;
        }
    }
}
