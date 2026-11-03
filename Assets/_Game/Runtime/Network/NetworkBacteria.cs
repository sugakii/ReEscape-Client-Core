using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using Unity.Netcode.Components;


/// <summary>
/// 服务器负责感知、巡逻、追击与处决；各端按服务器时间播放攻击，网络组件同步移动和声音状态。
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

    [SerializeField]
    private float killDistance = 3.4f;

    [SerializeField, Min(3f)]
    private float executionStandOffDistance = 3.4f;

    [SerializeField]
    private Transform executionLookPoint;

    [SerializeField]
    private AnimationClip attackClip;

    private NetworkVariable<double> executionStartTime = new(-1d,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private NetworkPlayerLife executionVictim;

    private bool wasPlayingAttack;

    private bool executionPositionLocked;

    private Vector3 executionPosition;

    private Quaternion executionRotation;

    private bool agentUpdatedPosition;

    private bool agentUpdatedRotation;

    private float originalStoppingDistance;

    public Transform ExecutionLookPoint => executionLookPoint;

    private static readonly int AttackState = Animator.StringToHash("Base Layer.Attack");

    private static readonly int IdleState = Animator.StringToHash("Base Layer.Idle");

    private ServerLobby serverLobby;

    private NetworkObject target;

    private HashSet<ulong> visibleClientIds = new();

    private float nextSenseTime;

    private float nextRepathTime;

    private float lastSeenTime;

    private NavMeshPath chasePath;

    private BacteriaStatus status;

    private NetworkVariable<bool> chaseAudioActive = new(false,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private BacteriaChaseAudio chaseAudio;

    private ClientLobby clientLobby;

    public bool IsChasing => IsSpawned && chaseAudioActive.Value;

    private Vector3 candidatePosition;

    private bool hasPatrolDestination;

    private float nextPatrolTime;

    private NavMeshPath patrolPath;

    private void Awake()
    {
        chasePath = new NavMeshPath();
        patrolPath = new NavMeshPath();
        nextSenseTime = 0f;
        status = BacteriaStatus.Patrol;
        candidatePosition = default;
        chaseAudio = GetComponent<BacteriaChaseAudio>();
        if(agent != null)
        {
            originalStoppingDistance = agent.stoppingDistance;
        }
    }

    // 只有服务器启用 Agent；客户端接受网络移动。
    public override void OnNetworkSpawn()
    {
        clientLobby = NetworkManager.GetComponent<ClientLobby>();
        if(IsServer)
        {
            chaseAudioActive.Value = false;
            executionStartTime.Value = -1d;
        }
        if(agent == null || animator == null || eyePoint == null)
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

    // 网络对象回收时停止处决、声音与寻路。
    public override void OnNetworkDespawn()
    {
        CancelExecution();
        if(chaseAudio != null)
        {
            chaseAudio.ResetAudio();
        }

        target = null;
        visibleClientIds.Clear();
        if(agent == null)
        {
            return;
        }

        if(agent.isActiveAndEnabled && agent.isOnNavMesh)
        {
            agent.ResetPath();
        }

        agent.enabled = false;
    }

    private void Update()
    {
        if(!IsSpawned || !IsServer)
        {
            return;
        }

        if(agent == null || !agent.enabled || !agent.isOnNavMesh
                    || NetworkManager == null || serverLobby == null
                    || !NetworkManager.IsListening || NetworkManager.ShutdownInProgress || !serverLobby.IsPlaying)
        {
            CancelExecution();
            visibleClientIds.Clear();
            StopMovement();
            UpdateMovementAnimation();
            return;
        }

        // 锁定后不能重新扫描目标，暂停菜单也不会停止服务器计时。
        if(status == BacteriaStatus.Execute)
        {
            UpdateExecute();
            UpdateMovementAnimation();
            return;
        }
        if(Time.time > nextSenseTime)
        {
            UpdatePerception();
            nextSenseTime = Time.time + sensingInterval;
        }
        UpdateSightTimeout();
        if(status == BacteriaStatus.Chase)
        {
            UpdateChase();
        }
        else if(status == BacteriaStatus.Patrol)
        {
            UpdatePatrol();
        }

        UpdateMovementAnimation();
    }

    private void LateUpdate()
    {
        if(!IsSpawned)
        {
            return;
        }

        UpdateExecutionAnimation();
        if(IsServer)
        {
            bool chasing = NetworkManager != null && NetworkManager.IsListening
                && !NetworkManager.ShutdownInProgress && serverLobby != null && serverLobby.IsPlaying
                && agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh
                && status == BacteriaStatus.Chase && IsLivingPlayer(target);
            if(chaseAudioActive.Value != chasing)
            {
                chaseAudioActive.Value = chasing;
            }
        }
        if(chaseAudio == null)
        {
            return;
        }

        if(!IsClient || NetworkManager == null || !NetworkManager.IsConnectedClient
                    || NetworkManager.ShutdownInProgress || clientLobby == null || !clientLobby.IsPlaying)
        {
            chaseAudio.ResetAudio();
        }
        else
        {
            chaseAudio.SetChasing(chaseAudioActive.Value);
        }
    }

    private void OnDisable()
    {
        CancelExecution();
        if(IsSpawned && IsServer)
        {
            StopMovement();
            UpdateMovementAnimation();
        }
        if(IsSpawned && IsServer && NetworkManager != null && NetworkManager.IsListening
            && !NetworkManager.ShutdownInProgress)
        {
            chaseAudioActive.Value = false;
        }

        if(chaseAudio != null)
        {
            chaseAudio.ResetAudio();
        }
    }

    // 射线只到玩家身体上方，忽略双方身体和 Trigger，保留墙及门的遮挡。
    private bool CanSeePlayer(NetworkObject playerObject)
    {
        if(!IsLivingPlayer(playerObject))
        {
            return false;
        }

        Vector3 playerSightPoint = playerObject.transform.position + Vector3.up;
        Vector3 direction = playerSightPoint - eyePoint.position;
        float distance = direction.magnitude;
        if(distance > sightDistance || Vector3.Angle(transform.forward, direction) > sightHalfAngle)
        {
            return false;
        }

        return IsSightClear(eyePoint.position, playerSightPoint);
    }

    private bool IsSightClear(Vector3 from, Vector3 to)
    {
        Vector3 direction = to - from;
        RaycastHit[] hits = Physics.RaycastAll(from, direction.normalized, direction.magnitude,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        foreach(RaycastHit hit in hits)
        {
            Collider hitCollider = hit.collider;
            if(hitCollider.transform.IsChildOf(transform)
                || hitCollider.GetComponentInParent<PlayerNetworkIdentity>()
                || hitCollider.GetComponentInParent<PlayerMotor>())
            {
                continue;
            }

            return false;
        }
        return true;
    }

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

    private void HandlePlayerSeen(NetworkObject playerObject)
    {
        lastSeenTime = Time.time;
        if(status != BacteriaStatus.Chase || playerObject != target)
        {
            EnterChase(playerObject);
        }
    }

    private void UpdateSightTimeout()
    {
        if(target != null && status == BacteriaStatus.Chase
            && Time.time - lastSeenTime >= loseSightDelay)
        {
            EnterPatrol();
        }
    }

    private void UpdateChase()
    {
        if(!IsLivingPlayer(target) || !NetworkManager.ConnectedClients.ContainsKey(target.OwnerClientId))
        {
            EnterPatrol();
            return;
        }
        if(TryBeginExecution())
        {
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
        if(agent != null && agent.enabled)
        {
            agent.stoppingDistance = originalStoppingDistance;
        }
    }

    private void EnterChase(NetworkObject playerObject)
    {
        target = playerObject;
        status = BacteriaStatus.Chase;
        hasPatrolDestination = false;
        nextRepathTime = Time.time;
        // 提前减速，避免高速追击或低帧率下越过处决距离。
        agent.stoppingDistance = Mathf.Max(originalStoppingDistance, killDistance - 0.1f);
    }

    private bool TryUpdateChasePath()
    {
        if(!agent.CalculatePath(target.transform.position, chasePath)
            || chasePath.status != NavMeshPathStatus.PathComplete || !agent.SetPath(chasePath))
        {
            return false;
        }

        agent.isStopped = false;
        return true;
    }

    // 候选点必须在同类 Agent 的可达网格上，不能只验证采样成功。
    private bool TrySetPatrolDestination()
    {
        candidatePosition = default;
        NavMeshQueryFilter filter = new NavMeshQueryFilter
        {
            agentTypeID = agent.agentTypeID,
            areaMask = agent.areaMask
        };
        for(int i = 0; i < patrolSampleAttempts; i++)
        {
            Vector2 offset = Random.insideUnitCircle * patrolRadius;
            Vector3 position = transform.position + new Vector3(offset.x, 0f, offset.y);
            if(!NavMesh.SamplePosition(position, out NavMeshHit hit, patrolSampleDistance, filter)
                || Vector3.Distance(transform.position, hit.position) < patrolMinDistance
                || !agent.CalculatePath(hit.position, patrolPath)
                || patrolPath.status != NavMeshPathStatus.PathComplete || !agent.SetPath(patrolPath))
            {
                continue;
            }

            candidatePosition = hit.position;
            agent.speed = patrolSpeed;
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
        if(agent.remainingDistance > Mathf.Max(agent.stoppingDistance, 0.1f))
        {
            return;
        }

        StopMovement();
        hasPatrolDestination = false;
        nextPatrolTime = Time.time + patrolWaitTime;
    }

    private void StopMovement()
    {
        if(agent == null || !agent.isActiveAndEnabled || !agent.isOnNavMesh)
        {
            return;
        }

        agent.ResetPath();
        agent.isStopped = true;
        agent.velocity = Vector3.zero;
    }

    private void UpdateMovementAnimation()
    {
        if(!IsSpawned || !IsServer || animator == null)
        {
            return;
        }

        bool moving = agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh
                    && !agent.isStopped && agent.velocity.sqrMagnitude > 0.01f;
        animator.SetBool("IsMoving", moving);
    }

    public void StopForRoundEnd()
    {
        if(!IsServer || !IsSpawned)
        {
            return;
        }

        CancelExecution();
        chaseAudioActive.Value = false;
        if(chaseAudio != null)
        {
            chaseAudio.ResetAudio();
        }

        target = null;
        visibleClientIds.Clear();
        hasPatrolDestination = false;
        StopMovement();
        UpdateMovementAnimation();
    }

    // 停止感知或处决期间不再给玩家提供追击加成。
    public bool IsPlayerVisible(ulong clientId)
    {
        return NetworkManager != null && IsServer && IsSpawned && isActiveAndEnabled
            && agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh
            && serverLobby != null && serverLobby.IsPlaying && status != BacteriaStatus.Execute
            && visibleClientIds.Contains(clientId);
    }

    private bool IsLivingPlayer(NetworkObject playerObject)
    {
        return playerObject != null && playerObject.IsSpawned
            && playerObject.TryGetComponent(out NetworkPlayerLife life) && life.IsActive && !life.IsBeingExecuted;
    }

    // 进入攻击区域后锁定受害者，玩家不被拉向细菌。
    private bool TryBeginExecution()
    {
        if(!IsLivingPlayer(target) || attackClip == null || executionLookPoint == null
            || Vector3.Distance(transform.position, target.transform.position) > killDistance
            || !CanSeePlayer(target))
        {
            return false;
        }

        NetworkPlayerLife life = target.GetComponent<NetworkPlayerLife>();
        if(!TryGetExecutionPosition(target.transform.position, out Vector3 position))
        {
            return false;
        }

        Vector3 previousPosition = transform.position;
        Vector3 direction = target.transform.position - position;
        direction.y = 0f;
        Quaternion previousRotation = transform.rotation;
        StopMovement();
        if(!agent.Warp(position))
        {
            return false;
        }

        if(direction.sqrMagnitude > 0.0001f)
        {
            transform.rotation = Quaternion.LookRotation(direction);
        }

        if(!life.ServerTryBeginExecution(this, executionLookPoint, attackClip.length))
        {
            agent.Warp(previousPosition);
            transform.rotation = previousRotation;
            return false;
        }

        executionVictim = life;
        executionStartTime.Value = life.Execution.StartTime;
        status = BacteriaStatus.Execute;
        visibleClientIds.Clear();
        hasPatrolDestination = false;
        StopMovement();
        executionPosition = transform.position;
        executionRotation = transform.rotation;
        agentUpdatedPosition = agent.updatePosition;
        agentUpdatedRotation = agent.updateRotation;
        agent.updatePosition = agent.updateRotation = false;
        executionPositionLocked = true;
        if(TryGetComponent(out NetworkTransform networkTransform) && networkTransform.CanCommitToTransform)
        {
            networkTransform.Teleport(executionPosition, executionRotation, transform.localScale);
        }

        return true;
    }

    // 动画手臂前伸超过 2.5 米；选取有余量且不穿墙的站位，玩家位置不变。
    private bool TryGetExecutionPosition(Vector3 victimPosition, out Vector3 position)
    {
        Vector3 away = transform.position - victimPosition;
        away.y = 0f;
        if(away.sqrMagnitude < 0.001f)
        {
            away = -transform.forward;
        }

        away.Normalize();
        float eyeHeight = eyePoint.position.y - transform.position.y;
        float headHeight = executionLookPoint.position.y - transform.position.y;
        var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
        foreach(float distance in new[] { Mathf.Max(3f, executionStandOffDistance), 3.2f, 3f })
        {
            foreach(float angle in new[] { 0f, 30f, -30f, 60f, -60f, 90f, -90f, 120f, -120f, 180f })
            {
                Vector3 candidate = victimPosition + Quaternion.Euler(0f, angle, 0f) * away * distance;
                candidate.y = transform.position.y;
                if(!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 0.25f, filter)
                    || Mathf.Abs(hit.position.y - transform.position.y) > 0.3f
                    || Vector2.Distance(new Vector2(hit.position.x, hit.position.z),
                        new Vector2(victimPosition.x, victimPosition.z)) < 2.95f
                    || agent.Raycast(hit.position, out _)
                    || !IsSightClear(eyePoint.position, hit.position + Vector3.up * eyeHeight)
                    || !IsSightClear(hit.position + Vector3.up * eyeHeight, victimPosition + Vector3.up)
                    || !IsSightClear(victimPosition + Vector3.up * 1.6f,
                        hit.position + Vector3.up * headHeight))
                {
                    continue;
                }

                position = hit.position;
                return true;
            }
        }

        position = default;
        return false;
    }

    // 到达服务器约定的动画结束时间才确认死亡。
    private void UpdateExecute()
    {
        if(executionVictim == null || !executionVictim.IsSpawned || !executionVictim.IsActive
            || !executionVictim.IsBeingExecuted
            || !NetworkManager.ConnectedClients.ContainsKey(executionVictim.OwnerClientId))
        {
            CancelExecution();
            EnterPatrol();
            return;
        }

        StopMovement();
        transform.SetPositionAndRotation(executionPosition, executionRotation);
        agent.nextPosition = executionPosition;
        if(NetworkManager.ServerTime.Time < executionVictim.Execution.DeathTime)
        {
            return;
        }

        executionVictim.ServerTryKill();
        executionVictim = null;
        executionStartTime.Value = -1d;
        RestoreExecutionNavigation();
        EnterPatrol();
    }

    // 中断处决时清理受害者状态，并恢复导航设置。
    private void CancelExecution()
    {
        RestoreExecutionNavigation();
        if(NetworkManager == null
            || !NetworkManager.IsListening || NetworkManager.ShutdownInProgress)
        {
            return;
        }
        // Despawn 回调中源对象可能已不再 IsSpawned，仍要释放尚存活的受害者。
        if(executionVictim != null && executionVictim.IsServer)
        {
            executionVictim.ServerCancelExecution(NetworkObjectId);
        }

        executionVictim = null;
        if(IsSpawned && IsServer)
        {
            executionStartTime.Value = -1d;
        }

        if(status == BacteriaStatus.Execute)
        {
            status = BacteriaStatus.Patrol;
        }
    }

    private void RestoreExecutionNavigation()
    {
        if(!executionPositionLocked)
        {
            return;
        }

        executionPositionLocked = false;
        if(agent == null)
        {
            return;
        }

        if(agent.isActiveAndEnabled && agent.isOnNavMesh)
        {
            agent.nextPosition = transform.position;
        }

        agent.updatePosition = agentUpdatedPosition;
        agent.updateRotation = agentUpdatedRotation;
    }

    // 各端使用服务器时钟采样攻击，不依赖动画事件上报结束。
    private void UpdateExecutionAnimation()
    {
        if(animator == null || attackClip == null || NetworkManager == null)
        {
            return;
        }

        double elapsed = NetworkManager.ServerTime.Time - executionStartTime.Value
                    - PlayerExecutionState.TurnDuration;
        bool attacking = executionStartTime.Value >= 0d && elapsed >= 0d;
        if(attacking)
        {
            animator.Play(AttackState, 0, Mathf.Clamp01((float)elapsed / attackClip.length));
            wasPlayingAttack = true;
        }
        else if(wasPlayingAttack)
        {
            animator.Play(IdleState, 0, 0f);
            wasPlayingAttack = false;
        }
    }
}
