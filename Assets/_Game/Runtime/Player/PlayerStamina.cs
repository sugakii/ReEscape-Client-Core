using System;
using UnityEngine;

/// <summary>
/// 处理冲刺体力的消耗、恢复与被追击时的冲刺规则。
/// </summary>
public class PlayerStamina : MonoBehaviour
{
    public float Stamina { get; private set; } = 1f;

    public bool IsSprinting { get; private set; }

    public bool IsChased { get; private set; }

    [SerializeField]
    private float consumeTime = 12f;

    [SerializeField]
    private float recoverTime = 10f;

    // 根据冲刺输入更新体力，返回本帧是否允许冲刺。
    public bool TickSprint(bool wantsToSprint, float deltaTime)
    {
        if(IsChased)
        {
            IsSprinting = wantsToSprint;
            return IsSprinting;
        }

        bool cannotStart = !IsSprinting && Stamina < 0.8f;

        if(!wantsToSprint || Stamina <= 0f || cannotStart)
        {
            IsSprinting = false;
            Stamina = Mathf.Clamp01(Stamina + deltaTime / recoverTime);
            return false;
        }

        Stamina = Mathf.Clamp01(Stamina - deltaTime / consumeTime);
        IsSprinting = Stamina > 0f;
        return IsSprinting;
    }

    // 应用被追击状态，追击结束时恢复全部体力。
    public void SetChased(bool chased)
    {
        if(!chased)
        {
            Stamina = IsChased ? 1f : Stamina;
            IsChased = chased;
            return;
        }

        Stamina = 1f;
        IsChased = chased;
    }

    // 开始新一局时重置体力和冲刺状态。
    public void ResetState()
    {
        Stamina = 1f;
        IsSprinting = false;
        IsChased = false;
    }
}
