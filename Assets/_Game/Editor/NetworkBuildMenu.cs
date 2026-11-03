using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 构建 Windows 客户端和服务器；缺少专用服务器模块时输出可用的无界面运行版本。
/// </summary>
public static class NetworkBuildMenu
{
    private const string GameScene = "Assets/Material/Environment/_Level0Refined/Scenes/Level0_Refined.unity";

    [MenuItem("ReEscape/Build/Windows Client")]
    public static void BuildClient() => Build(false);

    [MenuItem("ReEscape/Build/Windows Server")]
    public static void BuildServer() => Build(true);

    [MenuItem("ReEscape/Build/Windows Client and Server")]
    public static void BuildBoth()
    {
        Build(false);
        Build(true);
    }

    // 不切换当前编辑器的构建子目标；只为这次构建传入 subtarget。
    private static void Build(bool server)
    {
        if(EditorApplication.isPlaying || EditorApplication.isCompiling)
        {
            throw new InvalidOperationException("请退出运行并等待脚本编译结束后构建。");
        }

        if(!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64))
        {
            throw new InvalidOperationException("当前 Unity 未安装 Windows Build Support。");
        }

        // 构建使用磁盘场景，先保存当前已打开的游戏场景，防止漏掉本轮 Inspector 配置。
        var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(GameScene);
        if(scene.IsValid() && scene.isDirty && !EditorSceneManager.SaveScene(scene))
        {
            throw new InvalidOperationException("游戏场景保存失败，已取消构建。");
        }

        string root = Path.GetFullPath(Path.Combine(Application.dataPath, "../Builds"));
        string directory = Path.Combine(root, server ? "Server" : "Client");
        Directory.CreateDirectory(directory);
        bool dedicated = server && HasDedicatedServerSupport();
        if(server && !dedicated)
        {
            Debug.LogWarning("[NetworkBuild] 缺少 Windows Dedicated Server 模块，输出 Player 构建；启动脚本将使用 --server -batchmode -nographics。");
        }

        var options = new BuildPlayerOptions
        {
            scenes = new[] { GameScene },
            locationPathName = Path.Combine(directory, server ? "ReEscapeServer.exe" : "ReEscapeClient.exe"),
            target = BuildTarget.StandaloneWindows64,
            subtarget = (int)(dedicated ? StandaloneBuildSubtarget.Server : StandaloneBuildSubtarget.Player),
            options = BuildOptions.None
        };
        BuildReport report = BuildPipeline.BuildPlayer(options);
        if(report.summary.result != BuildResult.Succeeded)
        {
            throw new InvalidOperationException($"{(server ? "Server" : "Client")} 构建失败：{report.summary.result}");
        }

        string launcher = server ? "Start-Server.ps1" : "Start-Client.ps1";
        File.Copy(Path.Combine(Application.dataPath, "../Tools/Network", launcher), Path.Combine(directory, launcher), true);
        Debug.Log($"[NetworkBuild] SUCCESS {(server ? "Server" : "Client")} dedicated={dedicated} path={options.locationPathName}");
    }

    // Windows 模块以 server variation 提供专用服务器播放器。
    public static bool HasDedicatedServerSupport()
    {
        string variations = Path.Combine(Path.GetDirectoryName(EditorApplication.applicationPath),
            "Data/PlaybackEngines/windowsstandalonesupport/Variations");
        return Directory.Exists(variations)
            && Directory.GetDirectories(variations).Any(path => Path.GetFileName(path).StartsWith("win64_server", StringComparison.OrdinalIgnoreCase));
    }
}
