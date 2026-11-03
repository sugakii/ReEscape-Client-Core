using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 同步一次处决的服务器起点、锁定位置与观察点，各端据此追上当前播放进度。
/// </summary>
public struct PlayerExecutionState : INetworkSerializable, IEquatable<PlayerExecutionState>
{
    public const float TurnDuration = 0.25f;

    public const float LookDuration = 0.2f;

    public bool Started;

    public ulong BacteriaId;

    public double StartTime;

    public float AttackDuration;

    public Vector3 Position;

    public Vector3 LookPoint;

    public double DeathTime => StartTime + TurnDuration + AttackDuration;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref Started);
        serializer.SerializeValue(ref BacteriaId);
        serializer.SerializeValue(ref StartTime);
        serializer.SerializeValue(ref AttackDuration);
        serializer.SerializeValue(ref Position);
        serializer.SerializeValue(ref LookPoint);
    }

    public bool Equals(PlayerExecutionState other) => Started == other.Started
        && BacteriaId == other.BacteriaId && StartTime == other.StartTime
        && AttackDuration == other.AttackDuration && Position == other.Position && LookPoint == other.LookPoint;
}
