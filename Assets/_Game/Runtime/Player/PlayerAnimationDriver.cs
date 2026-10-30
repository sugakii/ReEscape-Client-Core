using UnityEngine;

/// <summary>
/// 用本机移动状态驱动第一和第三人称动画，提供网络参数复制与死亡时的动作清理。
/// </summary>
[RequireComponent(typeof(PlayerMotor), typeof(CharacterController))]
public class PlayerAnimationDriver : MonoBehaviour
{
    [SerializeField]
    private Animator animator;

    [SerializeField]
    private Animator showAnimator;

    private PlayerMotor motor;

    private CharacterController controller;

    private static readonly int MoveX = Animator.StringToHash("MoveX");

    private static readonly int MoveZ = Animator.StringToHash("MoveZ");

    private static readonly int Grounded = Animator.StringToHash("Grounded");

    private static readonly int VerticalSpeed = Animator.StringToHash("VerticalSpeed");

    private void Awake()
    {
        motor = GetComponent<PlayerMotor>();
        controller = GetComponent<CharacterController>();
    }

    private void LateUpdate()
    {
        if(!controller.enabled)
        {
            return;
        }

        // 停用移动组件后，CharacterController.velocity 可能仍保留上一次 Move 的速度。
        bool canMove = motor.isActiveAndEnabled;
        Vector3 velocity = canMove ? controller.velocity : Vector3.zero;
        velocity.y = 0f;

        Vector3 localVelocity = transform.InverseTransformDirection(velocity);
        
        float referenceSpeed = Mathf.Max(motor.WalkSpeed, 0.01f);
        float dampingTime = canMove ? 0.08f : 0f;

        UpdateAnimator(animator, localVelocity, referenceSpeed, dampingTime);
        UpdateAnimator(showAnimator, localVelocity, referenceSpeed, dampingTime);
    }

    private void UpdateAnimator(
        Animator target,
        Vector3 localVelocity,
        float referenceSpeed,
        float dampingTime
    )
    {
        if(target == null)
        {
            return;
        }

        target.SetFloat(
            MoveX,
            localVelocity.x / referenceSpeed,
            dampingTime,
            Time.deltaTime
        );

        target.SetFloat(
            MoveZ,
            localVelocity.z / referenceSpeed,
            dampingTime,
            Time.deltaTime
        );

        target.SetBool(Grounded, controller.isGrounded);
        target.SetFloat(VerticalSpeed, motor.VerticalSpeed);
    }

    // 死亡后停用 CharacterController，主动清除动画中上一帧的移动与跳跃参数。
    public void ResetMotionParameters()
    {
        foreach(Animator target in new[] { animator, showAnimator })
        {
            if(target == null) continue;
            target.SetFloat(MoveX, 0f);
            target.SetFloat(MoveZ, 0f);
            target.SetBool(Grounded, true);
            target.SetFloat(VerticalSpeed, 0f);
        }
    }

    public void CopyParametersTo(Animator target)
    {
        if(target == null || showAnimator == null)
        {
            return;
        }

        target.SetFloat(MoveX, showAnimator.GetFloat(MoveX));
        target.SetFloat(MoveZ, showAnimator.GetFloat(MoveZ));
        target.SetBool(Grounded, showAnimator.GetBool(Grounded));
        target.SetFloat(
            VerticalSpeed,
            showAnimator.GetFloat(VerticalSpeed)
        );
    }
}
