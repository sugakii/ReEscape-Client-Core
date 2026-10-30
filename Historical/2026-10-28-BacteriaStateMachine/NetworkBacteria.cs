using System.Collections.Generic;
using Unity.Netcode;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.AI;


/// <summary>
/// 在服务器上处理细菌的玩家感知、目标选择与寻路追击，客户端通过网络组件同步表现。
/// </summary>
public class NetworkBacteria : NetworkBehaviour
{
    private enum BacteriaStatus
    {
        Patrol,
        Chase,
        Execute
    }

    [SerializeField]
    private NavMeshAgent agent;

    [SerializeField]
    private Animator animator;

    [SerializeField]
    private Transform eyePoint;

    // 视野最大距离
    [SerializeField]
    private float sightDistance = 100f;

    // 视野前方扇形区域，半角默认为60°
    [SerializeField]
    private float sightHalfAngle = 60f;

    // 感知检测间隔
    [SerializeField]
    private float sensingInterval = 0.2f;

    // 重新寻路的时间间隔，单位为秒。
    [SerializeField]
    private float repathInterval = 0.2f;

    // 连续未发现目标达到此时长后，结束本轮追击。
    [SerializeField]
    private float loseSightDelay = 3.75f;

    // 每次选择候选点的范围
    [SerializeField]
    private float patrolRadius = 15f;

    // 候选店的最小范围
    [SerializeField]
    private float patrolMinDistance = 3f;

    // 候选点附近寻找 NavMesh 的范围
    [SerializeField]
    private float patrolSampleDistance = 2f;

    // 一次选点最多尝试次数
    [SerializeField]
    private int patrolSampleAttempts = 8;

    [SerializeField]
    private float patrolWaitTime = 3f;

    [SerializeField]
    private float patrolRetryInterval = 1f;

    [SerializeField]
    private float chaseSpeed = 8f;

    [SerializeField]
    private float patrolSpeed = 4f;

    private ServerLobby serverLobby;

    // 细菌的唯一追击对象
    private NetworkObject target;

    // 当前满足视野条件的全部玩家
    private HashSet<ulong> visibleClientIds = new();

    // 下次允许扫描玩家的游戏时间。
    private float nextSenseTime;

    // 下次允许重新寻路的游戏时间。
    private float nextRepathTime;

    // 最后一次有效发现玩家的时间
    private float lastSeenTime;

    private NavMeshPath chasePath;

    private BacteriaStatus status;

    private Vector3 candidatePosition;

    private bool hasPatrolDestination;

    private float nextPatrolTime;

    private NavMeshPath patrolPath;

    // 复用路径对象，避免每次寻路都重新创建。
    private void Awake()
    {
        chasePath = new NavMeshPath();
        patrolPath = new NavMeshPath();
        nextSenseTime = 0f;
        status = BacteriaStatus.Patrol;
        candidatePosition = default;
    }

    // 仅服务器启用导航，并初始化追击状态。
    public override void OnNetworkSpawn()
    {
        if(
            agent == null
            || animator == null
            || eyePoint == null
        )
        {
            return;
        }

        agent.enabled = IsServer;

        if(!IsServer)
        {
            return;
        }

        serverLobby = NetworkManager.GetComponent<ServerLobby>();

        if(!agent.isOnNavMesh)
        {
            Debug.LogError("[NetworkBacteria] Agent 不在有效的 NavMesh 上");

            agent.enabled = false;
            return;
        }

        target = null;
        visibleClientIds.Clear();

        agent.isStopped = true;
        animator.SetBool("IsMoving", false);
    }

    // 退出网络生成状态时，清理导航与感知记录。
    public override void OnNetworkDespawn()
    {
        if(agent == null)
        {
            return;
        }

        if(agent.isOnNavMesh)
        {
            agent.ResetPath();
        }

        agent.enabled = false;
        target = null;
        visibleClientIds.Clear();
    }

    // 检查追击更新的运行条件，感知和移动调度尚待接入。
    private void Update()
    {
        if(
            agent == null
            || !agent.enabled
            || !agent.isOnNavMesh
        )
        {
            StopMovement();
            return;
        }

        if(
            NetworkManager == null
            || serverLobby == null
            || !IsServer
            || !NetworkManager.IsListening
            || NetworkManager.ShutdownInProgress
            || !serverLobby.IsPlaying
        )
        {
            StopMovement();
            return;
        }

        if(Time.time > nextSenseTime)
        {
            UpdatePerception();
            nextSenseTime = Time.time + sensingInterval;
        }

        UpdateSightTimeout();

        switch(status)
        {
            case BacteriaStatus.Chase:
                UpdateChase();
                break;
            case BacteriaStatus.Patrol:
                UpdatePatrol();
                break;
            case BacteriaStatus.Execute:
                break;
        }

        UpdateMovementAnimation();
    }

    // 依次检查玩家有效性、距离、视角和视线遮挡。
    private bool CanSeePlayer(NetworkObject playerObject)
    {
        if(playerObject == null || !playerObject.IsSpawned)
        {
            return false;
        }

        // 视线指向玩家身体上方，避免直接射向脚底地面。
        Vector3 playerSightPoint = playerObject.transform.position + Vector3.up * 1f;

        Vector3 direction = playerSightPoint - eyePoint.position;

        float distance = direction.magnitude;

        if(direction.magnitude > sightDistance)
        {
            return false;
        }

        float angle = Vector3.Angle(transform.forward, direction);

        if(angle > sightHalfAngle)
        {
            return false;
        }

        // 射线只延伸到玩家位置，忽略 Trigger。
        RaycastHit[] hits = Physics.RaycastAll(
            eyePoint.position,
            direction.normalized,
            distance,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore
        );

        // 排除细菌自身、网络玩家及 Host 本机身体，其余碰撞体视为遮挡。
        foreach(RaycastHit hit in hits)
        {
            Collider hitCollider = hit.collider;

            if(hitCollider.transform.IsChildOf(transform))
            {
                continue;
            }

            if(hitCollider.GetComponentInParent<PlayerNetworkIdentity>())
            {
                continue;
            }

            if(hitCollider.GetComponentInParent<PlayerMotor>())
            {
                continue;
            }

            return false;
        }

        return true;
    }

    // 重建当前可见玩家集合，并依次处理发现玩家的事件。
    private void UpdatePerception()
    {
        visibleClientIds.Clear();

        NetworkObject candidate = null;

        foreach(NetworkClient client in NetworkManager.ConnectedClients.Values)
        {
            NetworkObject playerObject = client.PlayerObject;

            if(CanSeePlayer(playerObject))
            {
                visibleClientIds.Add(playerObject.OwnerClientId);
                candidate = playerObject;
            }
        }

        if(candidate != null)
        {
            HandlePlayerSeen(candidate);
        }
    }

    // 记录本次发现的目标，刷新可见时间，并允许立即重新寻路。
    private void HandlePlayerSeen(NetworkObject playerObject)
    {
        lastSeenTime = Time.time;

        if(status != BacteriaStatus.Chase || playerObject != target)
        {
            EnterChase(playerObject);
            return;
        }
    }

    private void UpdateSightTimeout()
    {
        if(target == null || status != BacteriaStatus.Chase)
        {
            return;
        }

        if(Time.time - lastSeenTime >= loseSightDelay)
        {
            EnterPatrol();
        }
    }

    private void UpdateChase()
    {
        if(
            target == null
            || !target.IsSpawned
            || !NetworkManager.ConnectedClients.ContainsKey(target.OwnerClientId)
        )
        {
            EnterPatrol();
            return;
        }

        if(Time.time < nextRepathTime)
        {
            return;
        }

        nextRepathTime = Time.time + repathInterval;
        agent.speed = chaseSpeed;

        if(!TryUpdateChasePath())
        {
            StopMovement();
        }
    }

    private void EnterPatrol()
    {
        target = null;
        StopMovement();
        hasPatrolDestination = false;
        nextPatrolTime = Time.time;
        status = BacteriaStatus.Patrol;
    }

    private void EnterChase(NetworkObject playerObject)
    {
        target = playerObject;
        status = BacteriaStatus.Chase;
        hasPatrolDestination = false;
        nextRepathTime = Time.time;
    }

    private bool TryUpdateChasePath()
    {
        Vector3 targetPosition = target.transform.position;
        bool calculated = agent.CalculatePath(targetPosition, chasePath);

        if(!calculated || chasePath.status != NavMeshPathStatus.PathComplete)
        {
            return false;
        }

        bool assigned = agent.SetPath(chasePath);

        if(!assigned)
        {
            return false;
        }

        agent.isStopped = false;
        return true;
    }

    private bool TrySetPatrolDestination()
    {
        candidatePosition = default;
        NavMeshQueryFilter filter = new NavMeshQueryFilter();
        filter.agentTypeID = agent.agentTypeID;
        filter.areaMask = agent.areaMask;

        for(int i = 0; i < patrolSampleAttempts; i++)
        {
            Vector2 randomPosition = Random.insideUnitCircle * patrolRadius;
            Vector3 position = transform.position + new Vector3(randomPosition.x, 0f, randomPosition.y);

            bool found = NavMesh.SamplePosition(position, out NavMeshHit hit, patrolSampleDistance, filter);

            if(!found)
            {
                continue;
            }

            float distance = Vector3.Distance(transform.position, hit.position);

            if(distance < patrolMinDistance)
            {
                continue;
            }

            if(!agent.CalculatePath(hit.position, patrolPath))
            {
                continue;
            }

            if(patrolPath.status != NavMeshPathStatus.PathComplete || !agent.SetPath(patrolPath))
            {
                continue;
            }

            candidatePosition = hit.position;
            agent.isStopped = false;
            return true;
        }

        return false;
    }

    private void UpdatePatrol()
    {
        if(!hasPatrolDestination)
        {
            if(Time.time < nextPatrolTime)
            {
                return;
            }

            hasPatrolDestination = TrySetPatrolDestination();

            if(!hasPatrolDestination)
            {
                nextPatrolTime = Time.time + patrolRetryInterval;
            }

            return;
        }

        if(agent.pathPending)
        {
            return;
        }

        if(!agent.hasPath || agent.pathStatus != NavMeshPathStatus.PathComplete)
        {
            StopMovement();
            hasPatrolDestination = false;
            nextPatrolTime = Time.time + patrolRetryInterval;

            return;
        }

        bool isArrived = agent.remainingDistance <= Mathf.Max(agent.stoppingDistance, 0.1f);

        if(!isArrived)
        {
            agent.speed = patrolSpeed;
            return;
        }

        StopMovement();
        hasPatrolDestination = false;
        nextPatrolTime = Time.time + patrolWaitTime;
    }

    private void StopMovement()
    {
        if(
            agent == null
            ||!agent.isActiveAndEnabled
            || !agent.isOnNavMesh
        )
        {
            return;
        }

        agent.isStopped = true;
        agent.ResetPath();
    }

    private void UpdateMovementAnimation()
    {
        bool isMoving;

        if(
            !agent.enabled
            || agent.isStopped
            || agent.velocity.sqrMagnitude < 0.01f
        )
        {
            isMoving = false;
        } else
        {
            isMoving = true;
        }

        animator.SetBool("IsMoving", isMoving);
    }
}
