using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 统一读取移动、视角、冲刺、交互和跳跃输入，供其他组件使用。
/// </summary>
[RequireComponent(typeof(PlayerInput))]
public class PlayerInputReader : MonoBehaviour
{
    public Vector2 LookInput { get; private set; }
    public Vector2 MoveInput { get; private set; }

    private PlayerInput playerInput;

    private InputAction lookAction;
    
    private InputAction moveAction;

    private InputAction sprintAction;

    private InputAction interactAction;

    private InputAction jumpAction;

    // Hold 持续为 true；Pressed 只在刚按下的那一帧为 true。
    public bool isSprintHold { get; private set; }

    public bool wasInteractPressed { get; private set; }

    public bool wasJumpPressed { get; private set; }

    // 缓存输入 Action，名称须与输入资源一致。
    private void Awake()
    {
        playerInput = GetComponent<PlayerInput>();

        lookAction = playerInput.actions["Look"];
        moveAction = playerInput.actions["Move"];
        sprintAction = playerInput.actions["Sprint"];
        interactAction = playerInput.actions["Interact"];
        jumpAction = playerInput.actions["Jump"];
    }

    // 刷新方向、按住状态和本帧按下状态。
    private void Update()
    {
        LookInput = lookAction.ReadValue<Vector2>();
        MoveInput = moveAction.ReadValue<Vector2>();

        isSprintHold = sprintAction.IsPressed();
        wasInteractPressed = interactAction.WasPressedThisFrame();
        wasJumpPressed = jumpAction.WasPressedThisFrame();
    }
}
