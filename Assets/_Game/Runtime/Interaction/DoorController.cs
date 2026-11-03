using UnityEngine;

/// <summary>
/// 实现门的开关交互、平滑旋转，并提供交互点和提示引用。
/// </summary>
public class DoorController : MonoBehaviour, IInteractable
{
    [SerializeField]
    private float openAngle = -90f;

    [SerializeField]
    private float rotationSpeed = 120f;

    [SerializeField]
    private InteractionPromptUI promptUI;

    [SerializeField]
    private Transform interactionPoint;

    private bool isOpen;

    // 缓存本地旋转，开关门不依赖父物体朝向。
    private Quaternion closedRotation;

    private Quaternion openedRotation;

    public Transform InteractionPoint => interactionPoint;

    public InteractionPromptUI PromptUI => promptUI;

    // 以初始姿态为关闭状态，计算打开姿态。
    private void Awake()
    {
        closedRotation = transform.localRotation;
        openedRotation = closedRotation * Quaternion.Euler(0f, openAngle, 0f);
    }

    // 切换开关目标，实际旋转交给 Update。
    public void Interact()
    {
        NetworkDoor networkDoor = GetComponentInParent<NetworkDoor>();

        if(networkDoor == null)
        {
            isOpen = !isOpen;
            return;
        }

        networkDoor.RequestInteraction();
    }

    // 销毁门时清理已经脱离门层级的提示。
    private void OnDestroy()
    {
        if(!Application.IsPlaying(gameObject) || promptUI == null)
        {
            return;
        }

        promptUI.SetVisible(false);
        Destroy(promptUI.gameObject);
    }

    // 按目标状态平滑转动门体。
    private void Update()
    {
        if(isOpen)
        {
            transform.localRotation = Quaternion.RotateTowards(
                transform.localRotation,
                openedRotation,
                rotationSpeed * Time.deltaTime
            );

            return;
        }

        transform.localRotation = Quaternion.RotateTowards(
            transform.localRotation,
            closedRotation,
            rotationSpeed * Time.deltaTime
        );
    }

    // 设置目标状态，初始化时可立即对齐门体姿态。
    public void ApplyOpenState(bool open, bool immediate = false)
    {
        isOpen = open;

        if(immediate)
        {
            transform.localRotation = isOpen
                ? openedRotation
                : closedRotation;
        }
    }
}
