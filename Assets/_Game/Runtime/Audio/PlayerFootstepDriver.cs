using UnityEngine;

/// <summary>
/// 用本机角色的实际水平位移累计脚步距离，过滤空中、暂停、死亡和出生瞬移。
/// </summary>
[DefaultExecutionOrder(120)]
[RequireComponent(
    typeof(CharacterController),
    typeof(PlayerMotor),
    typeof(FootstepAudio)
)]
public class PlayerFootstepDriver : MonoBehaviour
{
    [SerializeField]
    private float oneStepDistance = 1.6f;

    [SerializeField]
    private float distanceLimit = 2f;

    private CharacterController controller;

    private PlayerMotor playerMotor;

    private FootstepAudio stepAudio;

    private Vector3 lastPosition;

    private float accumulatedDistance = 0f;

    // 重新启用时从当前位置开始，不补播停用期间的脚步。
    private void OnEnable()
    {
        ResetTracking();
    }

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        playerMotor = GetComponent<PlayerMotor>();
        stepAudio = GetComponent<FootstepAudio>();
    }

    private void LateUpdate()
    {
        if(
            controller == null
            || !controller.enabled
            || playerMotor == null
            || !playerMotor.isActiveAndEnabled
            || stepAudio == null
            || oneStepDistance <= 0f
            || distanceLimit <= 0f
        )
        {
            ResetTracking();
            if(stepAudio != null)
            {
                stepAudio.StopSteps();
            }

            return;
        }

        if(!controller.isGrounded)
        {
            ResetTracking();
            return;
        }

        Vector3 displacement = transform.position - lastPosition;
        lastPosition = transform.position;
        displacement.y = 0f;
        float oneFrameDistanse = Vector3.Magnitude(displacement);
        if(oneFrameDistanse >= distanceLimit)
        {
            ResetTracking();
            return;
        }

        accumulatedDistance += oneFrameDistanse;
        if(accumulatedDistance >= oneStepDistance)
        {
            stepAudio.PlayStep();
            accumulatedDistance %= oneStepDistance;
        }
    }

    // 出生定位由桥接主动调用；距离上限只作为额外保护。
    public void ResetTracking()
    {
        lastPosition = transform.position;
        accumulatedDistance = 0f;
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
