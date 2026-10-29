using System.Reflection;

namespace Tristin.MCPManager.Unity.Tests;

public class UnityProcessDetectorTests
{
    [Theory]
    [InlineData("Unity.exe -projectPath \"C:\\Unity Projects\\My Game\" -logFile -", "C:\\Unity Projects\\My Game")]
    [InlineData("Unity.exe -projectPath C:\\Projects\\Game -logFile -", "C:\\Projects\\Game")]
    [InlineData("Unity.exe -projectPath 'C:\\Unity Projects\\My Game'", "C:\\Unity Projects\\My Game")]
    public void ExtractProjectPath_preserves_quoted_spaces(string commandLine, string expected)
    {
        var method = typeof(UnityProcessDetector).GetMethod("ExtractProjectPathArgument", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        Assert.Equal(expected, method.Invoke(null, [commandLine]));
    }
}
