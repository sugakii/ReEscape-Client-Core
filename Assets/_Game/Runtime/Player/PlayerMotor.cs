using System;
using UnityEngine;

/// <summary>
/// 使用 CharacterController 处理行走、冲刺、跳跃和重力。
/// </summary>
[RequireComponent(
    typeof(PlayerInputReader),
    typeof(CharacterController)
    )]
public class PlayerMotor : MonoBehaviour
{
    [SerializeField]
    private Transform cameraTransform;

    [SerializeField]
    // 起跳初速度，不是跳跃高度。
    private float jumpSpeed = 5f;

    private PlayerInputReader inputReader;

    private CharacterController characterController;

    private Vector2 moveInput;

    private float verticalVelocity;

    public float VerticalSpeed => verticalVelocity;

    public float WalkSpeed => moveSpeed;

    private PlayerStamina playerStamina;

    [SerializeField]
    private float moveSpeed = 3f;

    [SerializeField]
    private float sprintSpeed = 5f;

    [SerializeField]
    private float gravity = -9.81f;

    public float CurrentSpeed { get; private set; }

    // 读取实际水平速度，排除跳跃和下落的影响。
    private void RefreshCurrentSpeed()
    {
        Vector3 acturlVelocity = characterController.velocity;
        acturlVelocity.y = 0f;
        CurrentSpeed = acturlVelocity.magnitude;
    }

    // 缓存输入和 CharacterController。
    private void Awake()
    {
        inputReader = GetComponent<PlayerInputReader>();
        characterController = GetComponent<CharacterController>();
        playerStamina = GetComponent<PlayerStamina>();
    }

    // 出生定位时清空竖直速度和记录的水平速度。
    public void ResetMotion()
    {
        verticalVelocity = 0f;
        CurrentSpeed = 0f;
    }

    // 计算起跳、重力与水平移动，再执行位移。
    private void Update()
    {
        if(characterController.isGrounded && inputReader.wasJumpPressed)
        {
            verticalVelocity = jumpSpeed;
        }

        if(characterController.isGrounded && verticalVelocity < 0f)
        {
            verticalVelocity = -2f;
        }
        else
        {
            verticalVelocity += gravity * Time.deltaTime;
        }

        CurrentSpeed = moveSpeed;

        bool wantsToSprint = inputReader.isSprintHold && inputReader.MoveInput.y > 0f;
        bool canSprint = playerStamina.TickSprint(wantsToSprint, Time.deltaTime);

        CurrentSpeed = canSprint ? sprintSpeed : moveSpeed;

        Vector3 forward = cameraTransform.forward;
        Vector3 right = cameraTransform.right;

        // 去掉俯仰分量，确保水平移动。
        forward.y = 0;
        forward.Normalize();

        right.y = 0;
        right.Normalize();

        moveInput = inputReader.MoveInput;

        Vector3 moveDirection = forward * moveInput.y + right * moveInput.x;

        Vector3 velocity = CurrentSpeed * moveDirection;

        velocity.y = verticalVelocity;

        // Move 接收位移；重力由本脚本计算。
        characterController.Move(velocity * Time.deltaTime);

        RefreshCurrentSpeed();
    }
}
