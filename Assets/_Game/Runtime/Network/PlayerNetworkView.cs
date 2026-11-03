using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 读取所属玩家 MainCamera 的实际局部姿态与视野，供其他客户端还原第一人称观战视角。
/// </summary>
[DefaultExecutionOrder(150)]
public class PlayerNetworkView : NetworkBehaviour
{
    [SerializeField]
    private Transform viewPoint;

    public struct CameraPose : INetworkSerializable, IEquatable<CameraPose>
    {
        public bool Ready;

        public Vector3 Position;

        public Quaternion Rotation;

        public float FieldOfView;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Ready);
            serializer.SerializeValue(ref Position);
            serializer.SerializeValue(ref Rotation);
            serializer.SerializeValue(ref FieldOfView);
        }

        public bool Equals(CameraPose other) => Ready == other.Ready && Position == other.Position
            && Rotation == other.Rotation && FieldOfView == other.FieldOfView;

        public bool IsValid => Ready && IsFinite(Position.x) && IsFinite(Position.y) && IsFinite(Position.z)
            && Position.sqrMagnitude <= 9f && IsFinite(Rotation.x) && IsFinite(Rotation.y)
            && IsFinite(Rotation.z) && IsFinite(Rotation.w)
            && Mathf.Abs(Quaternion.Dot(Rotation, Rotation) - 1f) < 0.01f
            && IsFinite(FieldOfView) && FieldOfView >= 10f && FieldOfView <= 120f;

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private NetworkVariable<CameraPose> cameraPose = new(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private float nextSendTime;

    public Transform ViewPoint => viewPoint;

    public bool HasCameraPose => viewPoint != null && cameraPose.Value.IsValid;

    public float FieldOfView => cameraPose.Value.FieldOfView;

    // 身体由 NetworkTransform 同步；摄像机相对身体的真实偏移和旋转最多每秒发送 20 次。
    public void SetLocalCamera(Camera source)
    {
        if(!IsSpawned || !IsOwner || NetworkManager == null || !NetworkManager.IsConnectedClient
            || NetworkManager.ShutdownInProgress || source == null)
        {
            return;
        }

        var pose = new CameraPose
        {
            Ready = true,
            Position = transform.InverseTransformPoint(source.transform.position),
            Rotation = Quaternion.Inverse(transform.rotation) * source.transform.rotation,
            FieldOfView = source.fieldOfView
        };
        if(!pose.IsValid || Time.unscaledTime < nextSendTime)
        {
            return;
        }

        CameraPose previous = cameraPose.Value;
        if(previous.Ready && Vector3.SqrMagnitude(previous.Position - pose.Position) < 0.000001f
            && Quaternion.Angle(previous.Rotation, pose.Rotation) < 0.1f
            && Mathf.Abs(previous.FieldOfView - pose.FieldOfView) < 0.05f)
        {
            return;
        }

        cameraPose.Value = pose;
        nextSendTime = Time.unscaledTime + 0.05f;
        ApplyCameraPose();
    }

    public override void OnNetworkSpawn()
    {
        nextSendTime = 0f;
        ApplyCameraPose();
    }

    private void LateUpdate()
    {
        if(IsSpawned)
        {
            ApplyCameraPose();
        }
    }

    private void ApplyCameraPose()
    {
        if(!HasCameraPose)
        {
            return;
        }

        CameraPose pose = cameraPose.Value;
        // 兼容观战点被放到其他子节点下的情况，始终以网络玩家根节点为基准。
        viewPoint.SetPositionAndRotation(transform.TransformPoint(pose.Position), transform.rotation * pose.Rotation);
    }
}
