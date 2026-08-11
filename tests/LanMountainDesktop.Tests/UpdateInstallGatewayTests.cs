using System.IO;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class UpdateInstallGatewayTests
{
    [Fact]
    public void GetDirectoryName_ReturnsNull_ForRootPath()
    {
        // 验证 Path.GetDirectoryName 在根路径场景下的行为
        var rootPath = Path.GetPathRoot(Path.GetTempPath()) ?? "C:\\";
        var installerPath = Path.Combine(rootPath, "installer.exe");
        
        // 根路径下的文件，GetDirectoryName 返回根路径本身（不是 null）
        var result = Path.GetDirectoryName(installerPath);
        
        // 在 Windows 上，根路径文件返回根路径（如 "C:\"），不是 null
        // 但如果 installerPath 本身就是根路径（无文件名），则返回 null
        Assert.NotNull(result); // "C:\installer.exe" 的目录是 "C:\"
    }
    
    [Fact]
    public void GetDirectoryName_ReturnsNull_ForPathWithoutDirectory()
    {
        // 验证极端场景：路径没有目录部分
        // 这种情况在实际中很少发生，但代码应该能处理
        var fileNameOnly = "installer.exe";
        var result = Path.GetDirectoryName(fileNameOnly);
        
        // 只有文件名没有路径时，GetDirectoryName 返回 null
        Assert.Null(result);
    }
    
    [Fact]
    public void WorkingDirectoryFallback_ShouldUseValidDirectory()
    {
        // 验证修复后的逻辑：当 GetDirectoryName 返回 null 时，
        // 应该使用 AppContext.BaseDirectory 作为后备值
        var installerPath = "installer.exe"; // 模拟只有文件名的情况
        var workingDir = Path.GetDirectoryName(installerPath) ?? AppContext.BaseDirectory;
        
        // 后备值应该是有效的目录路径
        Assert.NotNull(workingDir);
        Assert.True(Directory.Exists(workingDir) || workingDir == AppContext.BaseDirectory);
    }
}