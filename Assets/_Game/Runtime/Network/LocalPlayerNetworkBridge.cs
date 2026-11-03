using Unity.Netcode;
using UnityEngine;
using Unity.Netcode.Components;

/// <summary>
/// 连接本机 Player 与其网络对象，处理出生定位、变换和动画同步，以及追击和死亡状态的本机应用。
/// </summary>
// 在摄像机更新左右朝向后执行 LateUpdate。
[DefaultExecutionOrder(100)]
public class LocalPlayerNetworkBridge : MonoBehaviour
{
    [SerializeField]
    private NetworkManager networkManager;

    [SerializeField]
    private PrototypeMenus prototypeMenus;

    [SerializeField]
    private Camera localCamera;

    [SerializeField]
    private PlayerSpectator playerSpectator;

    // 按玩家2、3、4排列的三个位置标记。
    [SerializeField]
    private Transform[] extraSpawnPoints = new Transform[LobbyProtocol.MaxMembers - 1];

    private CharacterController characterController;

    private PlayerMotor playerMotor;

    // 玩家1的固定出生位置。
    private Vector3 initialPlayerPosition;

    // 本机收到的玩家编号，默认0表示尚未指定。
    private int pendingPlayerNumber;

    // 准备阶段缓存的位置，执行出生时直接使用。
    private Vector3 pendingSpawnPosition;

    // 本局是否已经完成出生定位。
    private bool hasAppliedSpawn;

    private PlayerStamina playerStamina;

    private PlayerAnimationDriver playerAnimationDriver;

    private PlayerFootstepDriver footstepDriver;

    private FootstepAudio footstepAudio;

    [SerializeField]
    private PlayerExecutionView executionView;

    [SerializeField]
    private LocalDeathTransition deathTransition;

    [SerializeField]
    private RoomMenuController roomMenuController;

    private bool hasAppliedLifeState;

    private bool localPlayerAlive = true;

    private bool controllerEnabledBeforeDeath;

    private Renderer[] hiddenLocalRenderers;

    private bool[] localRendererStates;

    // 暂停只停用本机控制，仍需把停止移动后的动画参数同步给其他客户端。
    private bool CanSyncLocalPlayer()
    {
        if(
            networkManager == null
            || prototypeMenus == null
            || !networkManager.IsClient
            || !networkManager.IsConnectedClient
            || networkManager.ShutdownInProgress
            || (
                prototypeMenus.State != PrototypeMenus.MenuState.Playing
                && prototypeMenus.State != PrototypeMenus.MenuState.Paused
                && prototypeMenus.State != PrototypeMenus.MenuState.Result
            )
        )
        {
            return false;
        }

        return true;
    }

    // 每次获取本次会话的玩家对象，退出重连后不沿用旧对象。
    private NetworkObject GetLocalPlayerObject()
    {
        NetworkObject localPlayerObject = networkManager.LocalClient.PlayerObject;

        if(
            localPlayerObject == null
            || !localPlayerObject.IsSpawned
            || !localPlayerObject.IsOwner
        )
        {
            return null;
        }

        return localPlayerObject;
    }

    // 处理本机 Player 与网络玩家对象之间的位置和旋转赋值。
    private void SyncLocalPlayer(NetworkObject playerObject)
    {
        playerObject.transform.position = transform.position;
        playerObject.transform.rotation = transform.rotation;

        if(localCamera != null && playerObject.TryGetComponent(out PlayerNetworkView view))
        {
            view.SetLocalCamera(localCamera);
        }

        NetworkAnimator networkAnimator = playerObject.GetComponent<NetworkAnimator>();

        if(networkAnimator != null && playerAnimationDriver != null)
        {
            playerAnimationDriver.CopyParametersTo(networkAnimator.Animator);
        }
    }

    // 玩家1使用启动时的位置，玩家2～4使用对应场景标记的位置。
    private bool TryGetSpawnPosition(int playerNumber, out Vector3 position)
    {
        if(playerNumber <= 0 || playerNumber > LobbyProtocol.MaxMembers)
        {
            position = default;
            return false;
        }

        if(playerNumber == 1)
        {
            position = initialPlayerPosition;
            return true;
        }

        if(
            extraSpawnPoints == null
            || extraSpawnPoints.Length != LobbyProtocol.MaxMembers - 1
            || extraSpawnPoints[playerNumber - 2] == null
        )
        {
            position = default;
            return false;
        }

        position = extraSpawnPoints[playerNumber - 2].position;
        return true;
    }

    // 校验并缓存出生位置，供网络对象就绪后使用。
    public bool PrepareSpawn(int playerNumber)
    {
        if(hasAppliedSpawn)
        {
            return true;
        }

        if(!TryGetSpawnPosition(playerNumber, out pendingSpawnPosition))
        {
            pendingSpawnPosition = default;
            return false;
        }

        pendingPlayerNumber = playerNumber;
        return true;
    }

    // 重置本局编号和完成标记，并清除旧会话的操作限制。
    public void ResetSpawnState()
    {
        RestoreLocalLifeState();
        ResetFootsteps();
        pendingPlayerNumber = 0;
        pendingSpawnPosition = default;
        hasAppliedSpawn = false;

        if(playerStamina != null)
        {
            playerStamina.ResetState();
        }

        if(prototypeMenus != null)
        {
            prototypeMenus.SetPlayerControlAllowed(true);
        }
    }

    // 定位本机 Player、清空移动残留，再同步网络对象的瞬移。
    private bool TryApplySpawn(NetworkObject playerObject)
    {
        if(
            characterController == null
            || playerMotor == null
            || playerStamina == null
            || pendingPlayerNumber <= 0
            || pendingPlayerNumber > LobbyProtocol.MaxMembers
        )
        {
            return false;
        }

        NetworkTransform networkTransform = playerObject.GetComponent<NetworkTransform>();

        if(
            networkTransform == null
            || !networkTransform.isActiveAndEnabled
            || !networkTransform.CanCommitToTransform
        )
        {
            return false;
        }

        // 保存原状态，finally 保证定位后尝试恢复控制器。
        bool wasEnabled = characterController.enabled;

        try
        {
            characterController.enabled = false;
            transform.position = pendingSpawnPosition;
        }
        finally
        {
            characterController.enabled = wasEnabled;
        }

        playerMotor.ResetMotion();
        playerStamina.ResetState();

        networkTransform.Teleport(
            transform.position,
            transform.rotation,
            playerObject.transform.localScale
        );

        ResetFootsteps();

        hasAppliedSpawn = true;
        return true;
    }

    // 缓存移动组件，只在启动时保存玩家1的初始位置。
    private void Awake()
    {
        characterController = GetComponent<CharacterController>();
        playerMotor = GetComponent<PlayerMotor>();
        playerStamina = GetComponent<PlayerStamina>();
        initialPlayerPosition = transform.position;
        playerAnimationDriver = GetComponent<PlayerAnimationDriver>();
        footstepDriver = GetComponent<PlayerFootstepDriver>();
        footstepAudio = GetComponent<FootstepAudio>();
        if(executionView == null)
        {
            executionView = GetComponent<PlayerExecutionView>();
        }

        if(deathTransition == null)
        {
            deathTransition = GetComponent<LocalDeathTransition>();
        }

        if(deathTransition != null)
        {
            deathTransition.Finished += HandleDeathTransitionFinished;
        }
    }

    // 检查同步条件和对象身份，再执行出生处理与变换赋值。
    private void LateUpdate()
    {
        if(!CanSyncLocalPlayer())
        {
            ApplyChaseState(false);
            return;
        }

        NetworkObject localPlayerObject = GetLocalPlayerObject();

        if(localPlayerObject == null)
        {
            ApplyChaseState(false);
            return;
        }

        if(!hasAppliedSpawn && (prototypeMenus.State == PrototypeMenus.MenuState.Result || !TryApplySpawn(localPlayerObject)))
        {
            return;
        }

        NetworkPlayerLife life = localPlayerObject.GetComponent<NetworkPlayerLife>();
        ApplyExecutionState(life);
        ApplyLifeState(life != null && life.IsActive, life != null && life.Execution.Started);
        if(!localPlayerAlive)
        {
            ApplyChaseState(false);
            // 保留网络对象与相机，只发送已清空的动作参数，不再跟随本机位置。
            NetworkAnimator deadAnimator = localPlayerObject.GetComponent<NetworkAnimator>();
            if(deadAnimator != null && playerAnimationDriver != null)
            {
                playerAnimationDriver.CopyParametersTo(deadAnimator.Animator);
            }

            return;
        }

        if(prototypeMenus.State == PrototypeMenus.MenuState.Result)
        {
            return;
        }

        // 直接读取当前值，初次绑定、暂停和重连都不依赖变化事件是否曾经触发。
        PlayerChaseState chaseState = localPlayerObject.GetComponent<PlayerChaseState>();
        ApplyChaseState(chaseState != null && chaseState.IsChased);
        SyncLocalPlayer(localPlayerObject);
    }

    // 只在变化时交给体力组件，避免每帧重复执行回满逻辑。
    private void ApplyChaseState(bool chased)
    {
        if(playerStamina != null && playerStamina.IsChased != chased)
        {
            playerStamina.SetChased(chased);
        }
    }

    // 结算时停止动作和体力，保留身体与相机，由菜单状态阻止后续输入。
    public void ApplyRoundEnd()
    {
        ResetFootsteps();
        // 观战摄像机在结算黑幕完全遮住后停止，避免淡黑时跳回死亡位置。
        if(playerMotor != null)
        {
            playerMotor.ResetMotion();
        }

        if(playerStamina != null)
        {
            playerStamina.ResetState();
        }

        if(playerAnimationDriver != null)
        {
            playerAnimationDriver.ResetMotionParameters();
        }

        if(networkManager != null && networkManager.IsClient && networkManager.IsConnectedClient
                    && !networkManager.ShutdownInProgress)
        {
            NetworkObject playerObject = GetLocalPlayerObject();
            NetworkAnimator networkAnimator = playerObject != null ? playerObject.GetComponent<NetworkAnimator>() : null;
            if(networkAnimator != null && playerAnimationDriver != null)
            {
                playerAnimationDriver.CopyParametersTo(networkAnimator.Animator);
            }
        }
    }

    // 生死限制独立于菜单状态，因此打开暂停再恢复也不会解除死亡限制。
    private void ApplyLifeState(bool alive, bool executed)
    {
        if(hasAppliedLifeState && localPlayerAlive == alive)
        {
            return;
        }

        hasAppliedLifeState = true;
        localPlayerAlive = alive;
        prototypeMenus.SetPlayerControlAllowed(alive && !executed);
        if(alive)
        {
            return;
        }

        ResetFootsteps();
        HideLocalBody();

        if(playerMotor != null)
        {
            playerMotor.ResetMotion();
        }

        if(playerStamina != null)
        {
            playerStamina.ResetState();
        }

        if(playerAnimationDriver != null)
        {
            playerAnimationDriver.ResetMotionParameters();
        }

        if(characterController != null)
        {
            controllerEnabledBeforeDeath = characterController.enabled;
            characterController.enabled = false;
        }
        if(executed && deathTransition != null)
        {
            if(roomMenuController != null)
            {
                roomMenuController.SetRoundResultPresentationBlocked(true);
            }

            prototypeMenus.ResumeGame();
            deathTransition.Begin();
        }
        else if(playerSpectator != null)
        {
            playerSpectator.BeginSpectating();
        }
    }

    // 仅退出本局时恢复本机控制器，新生成的网络对象提供下一局生命状态。
    private void RestoreLocalLifeState()
    {
        if(executionView != null)
        {
            executionView.End();
        }

        if(deathTransition != null)
        {
            deathTransition.ResetPresentation();
        }

        if(roomMenuController != null)
        {
            roomMenuController.SetRoundResultPresentationBlocked(false);
        }

        if(playerSpectator != null)
        {
            playerSpectator.StopSpectating();
        }

        RestoreLocalBody();
        if(hasAppliedLifeState && !localPlayerAlive && characterController != null)
        {
            characterController.enabled = controllerEnabledBeforeDeath;
        }

        hasAppliedLifeState = false;
        localPlayerAlive = true;
        if(prototypeMenus != null)
        {
            prototypeMenus.SetPlayerControlAllowed(true);
        }
    }

    // 本机玩家被关闭时不能保留上一局的冲刺状态。
    private void OnDisable()
    {
        RestoreLocalLifeState();
        ResetFootsteps();
        if(playerStamina != null)
        {
            playerStamina.ResetState();
        }
    }

    // 锁定期间禁用控制；取消处决时只恢复仍然存活的玩家。
    private void ApplyExecutionState(NetworkPlayerLife life)
    {
        bool executing = life != null && life.IsBeingExecuted;
        if(executing)
        {
            prototypeMenus.SetPlayerControlAllowed(false);
            if(roomMenuController != null)
            {
                roomMenuController.SetRoundResultPresentationBlocked(true);
            }

            if(executionView != null)
            {
                if(!executionView.IsActive)
                {
                    ResetFootsteps();
                    if(playerMotor != null)
                    {
                        playerMotor.ResetMotion();
                    }

                    if(playerAnimationDriver != null)
                    {
                        playerAnimationDriver.ResetMotionParameters();
                    }

                    executionView.Begin(life.Execution);
                }
                Vector3 lookPoint = life.Execution.LookPoint;
                if(networkManager.SpawnManager.SpawnedObjects.TryGetValue(life.Execution.BacteriaId, out NetworkObject attacker)
                    && attacker.TryGetComponent(out NetworkBacteria bacteria) && bacteria.ExecutionLookPoint != null)
                {
                    lookPoint = bacteria.ExecutionLookPoint.position;
                }

                executionView.Tick((float)(networkManager.ServerTime.Time - life.Execution.StartTime), lookPoint);
            }
        }
        else if(executionView != null && executionView.IsActive)
        {
            executionView.End();
            if(life != null && life.IsActive)
            {
                prototypeMenus.SetPlayerControlAllowed(true);
                if(roomMenuController != null)
                {
                    roomMenuController.SetRoundResultPresentationBlocked(false);
                }
            }
        }
    }

    private void HandleDeathTransitionFinished()
    {
        if(prototypeMenus != null && prototypeMenus.State != PrototypeMenus.MenuState.Result
            && playerSpectator != null)
        {
            playerSpectator.BeginSpectating();
        }

        if(roomMenuController != null)
        {
            roomMenuController.SetRoundResultPresentationBlocked(false, true);
        }
    }

    private void OnDestroy()
    {
        if(deathTransition != null)
        {
            deathTransition.Finished -= HandleDeathTransitionFinished;
        }
    }

    private void ResetFootsteps()
    {
        if(footstepDriver != null)
        {
            footstepDriver.ResetTracking();
        }

        if(footstepAudio != null)
        {
            footstepAudio.StopSteps();
        }
    }

    // 场景 Player 另有第一人称模型和投影身体，死亡时也要一起隐藏。
    private void HideLocalBody()
    {
        hiddenLocalRenderers = GetComponentsInChildren<Renderer>(true);
        localRendererStates = new bool[hiddenLocalRenderers.Length];
        for(int i = 0; i < hiddenLocalRenderers.Length; i++)
        {
            localRendererStates[i] = hiddenLocalRenderers[i].enabled;
            hiddenLocalRenderers[i].enabled = false;
        }
    }

    // 退出本局后恢复原有显隐状态，避免下一局仍然看不到自己的身体。
    private void RestoreLocalBody()
    {
        if(hiddenLocalRenderers == null)
        {
            return;
        }

        for(int i = 0; i < hiddenLocalRenderers.Length; i++)
        {
            if(hiddenLocalRenderers[i] != null)
            {
                hiddenLocalRenderers[i].enabled = localRendererStates[i];
            }
        }

        hiddenLocalRenderers = null;
        localRendererStates = null;
    }
}
