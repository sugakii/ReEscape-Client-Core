using System.Collections.Generic;
using Unity.Netcode;
using System;
using UnityEngine;

/// <summary>
/// 统一检查并生成本局的门和随机物体；失败时回收本次新增实例，提供团队钥匙状态。
/// </summary>
public class ServerWorldSpawner : MonoBehaviour
{
    [SerializeField]
    private NetworkObject doorPrefab;

    [SerializeField]
    // 使用场景中通关点标记的世界位置和朝向。
    private Transform doorSpawnPoint;

    [SerializeField]
    private RandomPointNetworkSpawner[] randomSpawns;

    [SerializeField]
    private RandomPointNetworkSpawner keySpawner;

    private NetworkObject spawnedDoor;

    private NetworkManager networkManager;

    // 服务器通过本局生成的钥匙实例判断团队是否已取得钥匙。
    public bool HasKey
    {
        get
        {
            if(keySpawner == null)
            {
                return false;
            }

            NetworkObject keyObject = keySpawner.SpawnedObject;

            if(keyObject == null || !keyObject.IsSpawned)
            {
                return false;
            }

            NetworkKey key = keyObject.GetComponent<NetworkKey>();

            return key != null && key.IsCollected;
        }
    }

    public bool IsExitDoorOpen
    {
        get
        {
            if(spawnedDoor == null || !spawnedDoor.IsSpawned)
            {
                return false;
            }

            NetworkDoor door = spawnedDoor.GetComponent<NetworkDoor>();

            return door != null && door.IsOpen;
        }
    }

    // 先完成全部配置检查，再生成；失败时只回收本次新增对象。
    public bool TrySpawnWorld()
    {
        if(!ValidateConfiguration())
        {
            return false;
        }

        bool hadDoor = spawnedDoor != null && spawnedDoor.IsSpawned;
        List<RandomPointNetworkSpawner> createdSpawners = new();
        bool succeeded = false;
        try
        {
            if(!hadDoor)
            {
                spawnedDoor = Instantiate(doorPrefab, doorSpawnPoint.position, doorSpawnPoint.rotation);
                spawnedDoor.Spawn();
                if(!spawnedDoor.IsSpawned)
                {
                    throw new InvalidOperationException("门没有成功生成。");
                }
            }

            foreach(RandomPointNetworkSpawner spawner in randomSpawns)
            {
                if(spawner.SpawnedObject == null || !spawner.SpawnedObject.IsSpawned)
                {
                    createdSpawners.Add(spawner);
                }

                if(!spawner.TrySpawn())
                {
                    Debug.LogError($"[ServerWorldSpawner] {spawner.name} 生成失败，回收本次新增对象。", spawner);
                    return false;
                }
            }

            succeeded = true;
            return true;
        }
        catch(Exception exception)
        {
            Debug.LogError($"[ServerWorldSpawner] 生成失败：{exception}", this);
            return false;
        }
        finally
        {
            if(!succeeded)
            {
                for(int i = createdSpawners.Count - 1; i >= 0; i--)
                {
                    createdSpawners[i].DespawnObject();
                }

                if(!hadDoor && spawnedDoor != null)
                {
                    if(spawnedDoor.IsSpawned)
                    {
                        spawnedDoor.Despawn(true);
                    }
                    else
                    {
                        Destroy(spawnedDoor.gameObject);
                    }
                    spawnedDoor = null;
                }
            }
        }
    }

    // 检查只读配置，不在此阶段创建任何实例。
    private bool ValidateConfiguration()
    {
        if(networkManager == null || !networkManager.IsListening
            || !networkManager.IsServer || networkManager.ShutdownInProgress)
        {
            Debug.LogError("[ServerWorldSpawner] 当前服务器不可生成物体。", this);
            return false;
        }

        if(doorPrefab == null || doorSpawnPoint == null
            || !networkManager.NetworkConfig.Prefabs.Contains(doorPrefab.gameObject))
        {
            Debug.LogError("[ServerWorldSpawner] 请绑定门和出生点，并注册门的网络预制体。", this);
            return false;
        }

        if(randomSpawns == null || randomSpawns.Length == 0 || keySpawner == null)
        {
            Debug.LogError("[ServerWorldSpawner] 随机生成器列表或钥匙生成器未配置。", this);
            return false;
        }

        HashSet<RandomPointNetworkSpawner> seen = new();
        foreach(RandomPointNetworkSpawner spawner in randomSpawns)
        {
            if(spawner == null || !seen.Add(spawner))
            {
                Debug.LogError("[ServerWorldSpawner] 生成器列表含空项或重复引用。", this);
                return false;
            }

            if(!spawner.ValidateConfiguration(networkManager))
            {
                return false;
            }
        }

        if(!seen.Contains(keySpawner))
        {
            Debug.LogError("[ServerWorldSpawner] 钥匙生成器不在生成列表中。", this);
            return false;
        }

        return true;
    }

    // 最后一名玩家退出后，回收本局物体，使同一服务器可以创建下一间房。
    public void ClearWorld()
    {
        if(networkManager == null || !networkManager.IsServer || !networkManager.IsListening
            || networkManager.ShutdownInProgress)
        {
            return;
        }

        if(randomSpawns != null)
        {
            foreach(var spawner in randomSpawns)
            {
                if(spawner != null)
                {
                    spawner.DespawnObject();
                }
            }
        }

        if(spawnedDoor != null)
        {
            if(spawnedDoor.IsSpawned)
            {
                spawnedDoor.Despawn(true);
            }
            else
            {
                Destroy(spawnedDoor.gameObject);
            }

            spawnedDoor = null;
        }
    }

    // 缓存同一物体上的网络管理器。
    private void Awake()
    {
        networkManager = GetComponent<NetworkManager>();
    }
}
