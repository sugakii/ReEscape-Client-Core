using System;
using UnityEngine;

public class PlayerStamina : MonoBehaviour
{
    public float Stamina { get; private set; } = 1f;

    public bool IsSprinting { get; private set; }

    public bool IsChased { get; private set; }

    [SerializeField]
    private float consumeTime = 12f;

    [SerializeField]
    private float recoverTime = 10f;

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

    public void ResetState()
    {
        Stamina = 1f;
        IsSprinting = false;
        IsChased = false;
    }
}
