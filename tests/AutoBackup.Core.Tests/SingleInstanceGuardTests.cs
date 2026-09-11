using AutoBackup.Core.SingleInstance;

namespace AutoBackup.Core.Tests;

public class SingleInstanceGuardTests
{
    [Fact]
    public void SecondGuardWithSameName_IsNotFirstInstance()
    {
        var mutexName = "AutoBackupTests_" + Guid.NewGuid();
        using var first = new SingleInstanceGuard(mutexName);
        using var second = new SingleInstanceGuard(mutexName);

        Assert.True(first.IsFirstInstance);
        Assert.False(second.IsFirstInstance);
    }

    [Fact]
    public void GuardsWithDifferentNames_AreBothFirstInstance()
    {
        using var a = new SingleInstanceGuard("AutoBackupTests_" + Guid.NewGuid());
        using var b = new SingleInstanceGuard("AutoBackupTests_" + Guid.NewGuid());

        Assert.True(a.IsFirstInstance);
        Assert.True(b.IsFirstInstance);
    }
}
