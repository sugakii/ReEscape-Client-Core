using System;
using System.Net;
using System.Net.Sockets;

/// <summary>
/// 解析服务器模式、连接地址和端口；参数错误时阻止启动网络会话。
/// </summary>
public sealed class NetworkLaunchOptions
{
    public bool RunServer { get; private set; }

    public bool AutoConnect { get; private set; }

    public string Address { get; private set; } = "127.0.0.1";

    public bool HasAddress { get; private set; }

    public string ListenAddress { get; private set; } = "0.0.0.0";

    public ushort Port { get; private set; } = 7777;

    public ushort DirectoryPort { get; private set; } = 7778;

    // 忽略 Unity 自身参数，仅解析本项目使用的双横线参数。
    public static bool TryParse(string[] args, out NetworkLaunchOptions options, out string error)
    {
        options = new NetworkLaunchOptions();
        error = string.Empty;
        for(int i = 0; i < args.Length; i++)
        {
            string argument = args[i];
            if(argument == "--server")
            {
                options.RunServer = true;
                continue;
            }
            if(argument == "--connect")
            {
                options.AutoConnect = true;
                continue;
            }
            if(argument != "--address" && argument != "--listen" && argument != "--port" && argument != "--directory-port")
            {
                continue;
            }
            if(++i >= args.Length || args[i].StartsWith("--", StringComparison.Ordinal))
            {
                error = $"{argument} 后缺少参数值。";
                return false;
            }
            string value = args[i];
            if(argument == "--port" || argument == "--directory-port")
            {
                if(!ushort.TryParse(value, out ushort port) || port == 0)
                {
                    error = "端口必须是 1～65535 的整数。";
                    return false;
                }
                if(argument == "--port")
                {
                    options.Port = port;
                }
                else
                {
                    options.DirectoryPort = port;
                }
            }
            else
            {
                if(!IPAddress.TryParse(value, out IPAddress ip)
                    || ip.AddressFamily != AddressFamily.InterNetwork
                    || (argument == "--address" && ip.Equals(IPAddress.Any)))
                {
                    error = $"{argument} 必须是有效的 IPv4 地址，客户端目标不能是 0.0.0.0。";
                    return false;
                }
                if(argument == "--address")
                {
                    options.Address = ip.ToString();
                    options.HasAddress = true;
                }
                else
                {
                    options.ListenAddress = ip.ToString();
                }
            }
        }
        if(options.RunServer && options.AutoConnect)
        {
            error = "--server 和 --connect 不能同时使用。";
            return false;
        }
        if(options.Port == options.DirectoryPort)
        {
            error = "游戏端口与房间查询端口不能相同。";
            return false;
        }
        return true;
    }
}
