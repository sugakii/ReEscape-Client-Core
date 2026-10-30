using UnityEngine;

/// <summary>
/// 约定可交互物体的交互点、提示 UI 和交互入口。
/// </summary>
public interface IInteractable
{
    Transform InteractionPoint { get; }
    InteractionPromptUI PromptUI { get; }

    // 执行物体自身的交互行为。
    void Interact();
}
