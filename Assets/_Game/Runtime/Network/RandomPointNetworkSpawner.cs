using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 在配置的候选点中随机生成一个网络物体，支持失败回收和重复调用。
/// </summary>
public class RandomPointNetworkSpawner : MonoBehaviour
{
    [SerializeField]
    private NetworkManager networkManager;

    [SerializeField]
    private NetworkObject prefab;

    [SerializeField]
    private Transform[] spawnPoints;

    private NetworkObject spawnedObject;

    public NetworkObject SpawnedObject => spawnedObject;

    // expectedManager 用于检查生成器是否属于同一个服务器。
    public bool ValidateConfiguration(NetworkManager expectedManager = null)
    {
        if(networkManager == null || (expectedManager != null && networkManager != expectedManager))
        {
            Debug.LogError($"[{name}] 网络管理器未绑定或与世界生成器不一致。", this);
            return false;
        }

        if(prefab == null || !networkManager.NetworkConfig.Prefabs.Contains(prefab.gameObject))
        {
            Debug.LogError($"[{name}] 预制体未绑定或未注册到 Network Prefabs。", this);
            return false;
        }

        if(spawnPoints == null || spawnPoints.Length == 0)
        {
            Debug.LogError($"[{name}] 没有配置候选出生点。", this);
            return false;
        }

        for(int i = 0; i < spawnPoints.Length; i++)
        {
            if(spawnPoints[i] == null)
            {
                Debug.LogError($"[{name}] 出生点数组的第 {i + 1} 项为空。", this);
                return false;
            }
        }
        return true;
    }

    // 已生成时不重复创建；Instantiate 或 Spawn 失败也不能留下本机残留物。
    public bool TrySpawn()
    {
        if(networkManager == null || !networkManager.IsListening
            || !networkManager.IsServer || networkManager.ShutdownInProgress)
        {
            Debug.LogError($"[{name}] 服务器不可用，无法生成。", this);
            return false;
        }

        if(spawnedObject != null && spawnedObject.IsSpawned)
        {
            return true;
        }

        if(!ValidateConfiguration())
        {
            return false;
        }

        try
        {
            Transform point = spawnPoints[UnityEngine.Random.Range(0, spawnPoints.Length)];
            spawnedObject = Instantiate(prefab, point.position, point.rotation);
            spawnedObject.Spawn();
            if(!spawnedObject.IsSpawned)
            {
                throw new InvalidOperationException("Spawn 后对象未进入网络生成状态。");
            }
            return true;
        }
        catch(Exception exception)
        {
            Debug.LogError($"[{name}] 生成失败：{exception}", this);
            DespawnObject();
            return false;
        }
    }

    // 由服务器回收网络实例；尚未 Spawn 的实例直接销毁。
    public void DespawnObject()
    {
        if(spawnedObject == null)
        {
            return;
        }

        if(spawnedObject.IsSpawned)
        {
            spawnedObject.Despawn(true);
        }
        else
        {
            Destroy(spawnedObject.gameObject);
        }
        spawnedObject = null;
    }
}
