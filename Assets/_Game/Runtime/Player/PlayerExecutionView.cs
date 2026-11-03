using UnityEngine;

/// <summary>
/// 在本机锁定原地位置，先沿最短方向转向细菌，再抬头注视处决观察点。
/// </summary>
public class PlayerExecutionView : MonoBehaviour
{
    [SerializeField]
    private Camera playerCamera;

    [SerializeField]
    private CameraController cameraController;

    private Quaternion initialRotation;

    private Quaternion initialCameraRotation;

    private PlayerExecutionState state;

    private CharacterController controller;

    private bool controllerWasEnabled;

    public bool IsActive { get; private set; }

    public void Begin(PlayerExecutionState execution)
    {
        if(IsActive || playerCamera == null)
        {
            return;
        }

        state = execution;
        initialRotation = transform.rotation;
        initialCameraRotation = playerCamera.transform.localRotation;
        controller = GetComponent<CharacterController>();
        controllerWasEnabled = controller != null && controller.enabled;
        if(controller != null)
        {
            controller.enabled = false;
        }

        IsActive = true;
    }

    // elapsed 由服务器时间计算，即使打开暂停或晚收到状态，也不会重播整个动作。
    public void Tick(float elapsed, Vector3 lookPoint)
    {
        if(!IsActive || playerCamera == null)
        {
            return;
        }

        transform.position = state.Position;
        Vector3 direction = lookPoint - transform.position;
        direction.y = 0f;
        Quaternion targetRotation = direction.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(direction) : initialRotation;
        float turn = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / PlayerExecutionState.TurnDuration));
        transform.rotation = Quaternion.Slerp(initialRotation, targetRotation, turn);

        Vector3 localDirection = transform.InverseTransformDirection(lookPoint - playerCamera.transform.position);
        float pitch = -Mathf.Atan2(localDirection.y,
            new Vector2(localDirection.x, localDirection.z).magnitude) * Mathf.Rad2Deg;
        float look = Mathf.SmoothStep(0f, 1f,
            Mathf.Clamp01((elapsed - PlayerExecutionState.TurnDuration) / PlayerExecutionState.LookDuration));
        playerCamera.transform.localRotation = Quaternion.Slerp(initialCameraRotation,
            Quaternion.Euler(Mathf.Clamp(pitch, -75f, 85f), 0f, 0f), look);
    }

    public void End()
    {
        if(!IsActive)
        {
            return;
        }

        IsActive = false;
        if(controller != null)
        {
            controller.enabled = controllerWasEnabled;
        }

        if(cameraController != null)
        {
            cameraController.SyncFromTransform();
        }
    }

    private void OnDisable() => End();
}
