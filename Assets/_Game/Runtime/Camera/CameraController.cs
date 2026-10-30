using UnityEngine;

/// <summary>
/// 控制 Player 左右转向和摄像机上下俯仰。
/// </summary>
public class CameraController : MonoBehaviour
{
  
    [SerializeField]
    private float mouseSensitivity = 1f;

    [SerializeField]
    private PlayerInputReader inputReader;

    [SerializeField]
    private Transform playerRoot;

    private float xRotation;

    private float yRotation;

    public float MouseSensitivity => mouseSensitivity;

    // 设置界面调整速度时保留当前朝向，避免镜头跳转。
    public void SetMouseSensitivity(float value)
    {
        if(float.IsFinite(value)) mouseSensitivity = Mathf.Clamp(value, 0.05f, 1f);
    }

    // 保存初始朝向，将俯仰角转换为可限制的正负角度。
    private void Awake()
    {
        SyncFromTransform();
    }

    // 外部处决镜头结束后同步缓存，避免恢复鼠标时跳回处决前的朝向。
    public void SyncFromTransform()
    {
        if(playerRoot == null) return;
        yRotation = playerRoot.eulerAngles.y;
        xRotation = Mathf.Clamp(Mathf.DeltaAngle(0f, transform.localEulerAngles.x), -40f, 85f);
        transform.localRotation = Quaternion.Euler(xRotation, 0f, 0f);
    }

    // 累积视角输入：Player 只转 Y 轴，摄像机只转 X 轴。
    private void LateUpdate()
    {
        Vector2 lookAction = inputReader.LookInput;

        float mouseX = lookAction.x;
        float mouseY = lookAction.y;

        yRotation += mouseX * mouseSensitivity;
        xRotation -= mouseY * mouseSensitivity;

        xRotation = Mathf.Clamp(
            xRotation,
            -40f,
            85f
        );

        // 子摄像机继承 Player 的水平朝向，自身只保留俯仰。
        transform.localRotation = 
        Quaternion.Euler(
            xRotation,
            0f,
            0f
        );

        playerRoot.rotation = 
        Quaternion.Euler(
            0f,
            yRotation,
            0f
        );
    }
}
