using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 检测附近交互物体，更新提示和高亮，并处理交互按键。
/// </summary>
public class PlayerInteraction : MonoBehaviour
{
    // 为交互提供射线起点和方向。
    [SerializeField]
    private Transform cameraTransform;

    [SerializeField]
    private float interactDistance = 0.5f;

    [SerializeField]
    private float promptDistance = 3f;

    private PlayerInputReader inputReader;

    // 用于把交互点投影到画面上。
    private Camera playerCamera;

    // 这一帧附近的候选物品。
    // 同一物体可能有多个 Collider，HashSet 用来去重。
    private HashSet<IInteractable> nearbyTargets = new();

    // 上一帧显示过按键提示的物体。
    private HashSet<IInteractable> visibleTargets = new();

    private IInteractable highlightedTarget;
    
    // 缓存输入组件和玩家摄像机。
    private void Awake()
    {
        inputReader = GetComponent<PlayerInputReader>();
        playerCamera = cameraTransform.GetComponent<Camera>();
    }

    // 停用时隐藏提示并清空目标缓存。
    private void OnDisable()
    {
        foreach(IInteractable target in visibleTargets)
        {
            HidePrompt(target);
        }

        visibleTargets.Clear();
        nearbyTargets.Clear();
        highlightedTarget = null;
    }

    // 先更新目标，再处理本帧按键。
    private void LateUpdate()
    {
        UpdateInteractionPrompts();
        HandleInteraction();
    }

    // 决定哪些提示显示，哪个高亮。
    private void UpdateInteractionPrompts()
    {
        nearbyTargets.Clear();

        Collider[] colliders = Physics.OverlapSphere(
            cameraTransform.position,
            promptDistance,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore
        );

        foreach(Collider collider in colliders)
        {
            IInteractable target = collider.GetComponentInParent<IInteractable>();

            if(target == null)
            {
                continue;
            }

            nearbyTargets.Add(target);
        }

        foreach(IInteractable previousTargets in visibleTargets)
        {
            if(!nearbyTargets.Contains(previousTargets))
            {
                HidePrompt(previousTargets);
            }
        }

        // 当前会显示所有满足条件的候选提示。
        visibleTargets.Clear();

        foreach(IInteractable target in nearbyTargets)
        {
            bool shouldShow = CanShowPrompt(target);

            target.PromptUI.SetVisible(shouldShow);

            if(shouldShow)
            {
                visibleTargets.Add(target);
            }
        }

        highlightedTarget = FindHighlightedTarget();

        foreach(IInteractable target in visibleTargets)
        {
            target.PromptUI.SetHighlighted(target == highlightedTarget);
        }
    }

    // 决定 E 实际操作谁。
    private void HandleInteraction()
    {
        if(!inputReader.wasInteractPressed)
        {
            return;
        }

        if(highlightedTarget != null)
        {
            highlightedTarget.PromptUI.PlayPressedEffect();
            highlightedTarget.Interact();
            return;
        }

        // 后备检测：当前实现即使没有高亮，也可能在命中后执行交互。
        bool didHit = Physics.Raycast(
            cameraTransform.position,
            cameraTransform.forward,
            out RaycastHit hit,
            interactDistance,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore
        );

        if(!didHit)
        {
            return;
        }

        IInteractable target = hit.collider.GetComponentInParent<IInteractable>();

        if(target == null)
        {
            return;
        }

        if(visibleTargets.Contains(target))
        {
            target.PromptUI.PlayPressedEffect();
        }

        target.Interact();
    }

    // 检查交互点是否在画面和裁剪范围内，不判断遮挡。
    private bool IsInView(IInteractable target)
    {
        Vector3 viewportPoint = playerCamera.WorldToViewportPoint(
            target.InteractionPoint.position
        );

        return viewportPoint.z >= playerCamera.nearClipPlane
        && viewportPoint.z <= playerCamera.farClipPlane
        && viewportPoint.x >= 0f
        && viewportPoint.x <= 1f
        && viewportPoint.y >= 0f
        && viewportPoint.y <= 1f;
    }

    // 检查提示距离、画面范围和视线遮挡。
    private bool CanShowPrompt(IInteractable target)
    {
        float distance = Vector3.Distance(
            cameraTransform.position,
            target.InteractionPoint.position
        );

        if(distance > promptDistance)
        {
            return false;
        }

        if(!IsInView(target))
        {
            return false;
        }

        bool didHit = Physics.Linecast(
            cameraTransform.position,
            target.InteractionPoint.position,
            out RaycastHit hit,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore
        );

        if(!didHit)
        {
            return true;
        }

        IInteractable hitTarget = hit.collider.GetComponentInParent<IInteractable>();

        return hitTarget == target;
    }

    // 选择准星射线命中的近距离可见目标。
    private IInteractable FindHighlightedTarget()
    {
        bool didHit = Physics.Raycast(
            cameraTransform.position,
            cameraTransform.forward,
            out RaycastHit hit,
            interactDistance,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore
        );

        if(!didHit)
        {
            return null;
        }

        IInteractable target = hit.collider.GetComponentInParent<IInteractable>();

        if(target == null || !visibleTargets.Contains(target))
        {
            return null;
        }

        return target;
    }

    // 关闭提示，忽略已销毁或没有提示的目标。
    private void HidePrompt(IInteractable target)
    {
        MonoBehaviour component = target as MonoBehaviour;

        if(component == null)
        {
            return;
        }

        InteractionPromptUI prompt = target.PromptUI;

        if(prompt == null)
        {
            return;
        }

        prompt.SetVisible(false);
    }
}

