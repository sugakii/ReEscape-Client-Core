using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 记录网络玩家的身份，按归属控制身体显示，并在死亡后隐藏身体、关闭碰撞。
/// </summary>
public class PlayerNetworkIdentity : NetworkBehaviour
{
    [SerializeField]
    private CapsuleCollider bodyCollider;

    [SerializeField]
    private Renderer bodyRenderer;

    private NetworkPlayerLife playerLife;

    // 缓存同一网络玩家上的生命组件。
    private void Awake()
    {
        playerLife = GetComponent<NetworkPlayerLife>();
    }

    // 网络身份就绪后，按归属设置身体与碰撞并打印身份。
    public override void OnNetworkSpawn()
    {
        RefreshOwnerPresentation();
        PrintNetworkIdentity("Spawn");
    }

    // 对象退出网络生成状态时打印记录。
    public override void OnNetworkDespawn()
    {
        if(bodyCollider != null)
        {
            bodyCollider.enabled = false;
        }

        if(bodyRenderer != null)
        {
            bodyRenderer.enabled = false;
        }

        PrintNetworkIdentity("Despawn");
    }

    // 每端读取同步后的生死状态，死亡时同时关闭身体渲染和碰撞。
    private void LateUpdate()
    {
        if(IsSpawned)
        {
            RefreshOwnerPresentation();
        }
    }

    // 本机由场景 Player 的 CharacterController 处理碰撞，禁用自己的网络副本以免互相阻挡。
    private void RefreshOwnerPresentation()
    {
        bool visible = !IsOwner && playerLife != null && playerLife.IsActive;
        if(bodyCollider != null && bodyCollider.enabled != visible)
        {
            bodyCollider.enabled = visible;
        }

        if(bodyRenderer != null && bodyRenderer.enabled != visible)
        {
            bodyRenderer.enabled = visible;
        }
    }

    // stage 区分生成和移除，其余信息用于核对各端的编号与身份。
    private void PrintNetworkIdentity(string stage)
    {
        Debug.Log(stage);
        Debug.Log(NetworkObjectId);
        Debug.Log(OwnerClientId);
        Debug.Log(NetworkManager.LocalClientId);
        Debug.Log(IsOwner);
        Debug.Log(IsLocalPlayer);
        Debug.Log(IsServer);
        Debug.Log(IsClient);
    }
}
