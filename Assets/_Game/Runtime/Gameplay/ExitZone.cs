using UnityEngine;

/// <summary>
/// 使用出口盒形区域判断给定世界坐标是否位于通关空间内。
/// </summary>
public class ExitZone : MonoBehaviour
{
    [SerializeField]
    private BoxCollider zoneCollider;

    // 将世界坐标转换到出口局部空间，判断是否位于区域内。
    public bool ContainPoint(Vector3 worldPosition)
    {
        if(
            !isActiveAndEnabled
            || zoneCollider == null
            || !zoneCollider.enabled
            || !zoneCollider.gameObject.activeInHierarchy
        )
        {
            return false;
        }

        Vector3 localPoint = zoneCollider.transform.InverseTransformPoint(worldPosition) - zoneCollider.center;
        Vector3 halfSize = zoneCollider.size * 0.5f;

        if(
            Mathf.Abs(localPoint.x) <= halfSize.x
            && Mathf.Abs(localPoint.y) <= halfSize.y
            && Mathf.Abs(localPoint.z) <= halfSize.z
        )
        {
            return true;
        }

        return false;
    }
}
