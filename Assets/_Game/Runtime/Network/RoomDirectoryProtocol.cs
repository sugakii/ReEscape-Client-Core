using System;
using UnityEngine;

/// <summary>
/// 定义房间查询、创建及加入凭据；查询不建立 NGO 连接，也不占玩家名额。
/// </summary>
public static class RoomDirectoryProtocol
{
    public const int Version = 1;

    public const int MaxPacketBytes = 1024;

    public const int MaxNameLength = 24;

    // 去掉房名两端空白，并检查长度和控制字符。
    public static bool TryNormalizeName(string value, out string name)
    {
        name = (value ?? string.Empty).Trim();
        if(name.Length == 0 || name.Length > MaxNameLength)
        {
            return false;
        }

        foreach(char ch in name)
        {
            if(char.IsControl(ch))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>
/// 保存房间查询或创建请求的参数。
/// </summary>
[Serializable]
public class RoomDirectoryRequest
{
    public int version = RoomDirectoryProtocol.Version;

    public string nonce;

    public string operation;

    public string roomName;
}

/// <summary>
/// 保存房间信息、人数和查询结果。
/// </summary>
[Serializable]
public class RoomDirectoryReply
{
    public int version = RoomDirectoryProtocol.Version;

    public string nonce;

    public bool success;

    public string message;

    public string roomId;

    public string roomName;

    public int players;

    public int capacity;

    public int gamePort;

    public bool joinable;

    public string state;

    public string creatorToken;
}

/// <summary>
/// 保存加入房间时提交的房间编号和创建者凭据。
/// </summary>
[Serializable]
public class RoomJoinRequest
{
    public int version = RoomDirectoryProtocol.Version;

    public string roomId;

    public string creatorToken;
}
