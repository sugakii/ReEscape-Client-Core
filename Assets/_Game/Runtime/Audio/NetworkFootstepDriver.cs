using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

/// <summary>
/// 在客户端依据远端玩家已同步的位置和着地动画重建三维脚步，自己的网络副本和服务器不播放。
/// </summary>
[DefaultExecutionOrder(120)]
[RequireComponent(typeof(NetworkObject), typeof(FootstepAudio))]
public class NetworkFootstepDriver : MonoBehaviour
{
    [SerializeField, Min(0.01f)]
    private float oneStepDistance = 1.6f;

    [SerializeField, Min(0.01f)]
    private float distanceLimit = 2f;

    private NetworkObject playerObject;

    private NetworkPlayerLife life;

    private NetworkAnimator networkAnimator;

    private FootstepAudio stepAudio;

    private ClientLobby clientLobby;

    private Vector3 lastPosition;

    private float accumulatedDistance;

    private bool wasTracking;

    private static readonly int Grounded = Animator.StringToHash("Grounded");

    private void Awake()
    {
        playerObject = GetComponent<NetworkObject>();
        life = GetComponent<NetworkPlayerLife>();
        networkAnimator = GetComponent<NetworkAnimator>();
        stepAudio = GetComponent<FootstepAudio>();
    }

    private void LateUpdate()
    {
        NetworkManager manager = playerObject != null ? playerObject.NetworkManager : null;
        if(clientLobby == null && manager != null)
        {
            clientLobby = manager.GetComponent<ClientLobby>();
        }

        if(playerObject == null || !playerObject.IsSpawned || manager == null || !manager.IsClient
                    || !manager.IsConnectedClient || manager.ShutdownInProgress || playerObject.IsOwner
                    || life == null || !life.IsActive || clientLobby == null || !clientLobby.IsPlaying
                    || stepAudio == null || oneStepDistance <= 0f || distanceLimit <= 0f)
        {
            ResetTracking();
            if(stepAudio != null)
            {
                stepAudio.StopSteps();
            }

            return;
        }

        // 第一次看到远端角色只记录位置，不把生成和初次同步当作移动。
        if(!wasTracking)
        {
            lastPosition = transform.position;
            wasTracking = true;
            return;
        }

        Vector3 displacement = transform.position - lastPosition;
        lastPosition = transform.position;
        displacement.y = 0f;
        float distance = displacement.magnitude;
        if(networkAnimator == null || networkAnimator.Animator == null
            || !networkAnimator.Animator.GetBool(Grounded) || distance >= distanceLimit)
        {
            accumulatedDistance = 0f;
            return;
        }

        accumulatedDistance += distance;
        if(accumulatedDistance >= oneStepDistance)
        {
            stepAudio.PlayStep();
            accumulatedDistance %= oneStepDistance;
        }
    }

    private void ResetTracking()
    {
        lastPosition = transform.position;
        accumulatedDistance = 0f;
        wasTracking = false;
    }

    private void OnDisable()
    {
        ResetTracking();
        if(stepAudio != null)
        {
            stepAudio.StopSteps();
        }
    }
}
