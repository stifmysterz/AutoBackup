using AutoBackup.Core.Backup;
using AutoBackup.Core.Models;
using AutoBackup.Core.Tests.TestSupport;

namespace AutoBackup.Core.Tests;

public class BackupEngineTests
{
    [Fact]
    public void FirstBackup_CopiesAllFilesIntoSnapshotFolder()
    {
        using var temp = new TempDirectory();
        var source = Directory.CreateDirectory(Path.Combine(temp.Path, "Desktop"));
        File.WriteAllText(Path.Combine(source.FullName, "a.txt"), "hello");
        var driveRoot = Directory.CreateDirectory(Path.Combine(temp.Path, "Drive")).FullName;

        var engine = new BackupEngine();
        var result = engine.RunBackup(new[] { source.FullName }, driveRoot, Array.Empty<string>());

        Assert.Equal(BackupOutcome.Success, result.Outcome);
        Assert.Equal(1, result.FilesCopied);
        Assert.Equal(0, result.FilesLinked);
        Assert.Equal(5, result.BytesCopied);
        Assert.True(Directory.Exists(result.SnapshotPath));
        Assert.Equal("hello", File.ReadAllText(Path.Combine(result.SnapshotPath, "Desktop", "a.txt")));
    }

    [Fact]
    public void HardLinkedFiles_DoNotCountTowardBytesCopied()
    {
        using var temp = new TempDirectory();
        var source = Directory.CreateDirectory(Path.Combine(temp.Path, "Desktop"));
        File.WriteAllText(Path.Combine(source.FullName, "a.txt"), "hello");
        var driveRoot = Directory.CreateDirectory(Path.Combine(temp.Path, "Drive")).FullName;
        var engine = new BackupEngine();
        engine.RunBackup(new[] { source.FullName }, driveRoot, Array.Empty<string>(), now: new DateTime(2026, 9, 11, 22, 0, 0));

        var result = engine.RunBackup(new[] { source.FullName }, driveRoot, Array.Empty<string>(), now: new DateTime(2026, 9, 11, 22, 1, 0));

        Assert.Equal(1, result.FilesLinked);
        Assert.Equal(0, result.BytesCopied);
    }

    [Fact]
    public void SecondBackup_HardLinksUnchangedFiles()
    {
        using var temp = new TempDirectory();
        var source = Directory.CreateDirectory(Path.Combine(temp.Path, "Desktop"));
        File.WriteAllText(Path.Combine(source.FullName, "a.txt"), "hello");
        var driveRoot = Directory.CreateDirectory(Path.Combine(temp.Path, "Drive")).FullName;
        var engine = new BackupEngine();
        engine.RunBackup(new[] { source.FullName }, driveRoot, Array.Empty<string>(), now: new DateTime(2026, 9, 11, 22, 0, 0));

        var result = engine.RunBackup(new[] { source.FullName }, driveRoot, Array.Empty<string>(), now: new DateTime(2026, 9, 11, 22, 1, 0));

        Assert.Equal(0, result.FilesCopied);
        Assert.Equal(1, result.FilesLinked);
    }

    [Fact]
    public void SecondBackup_RecopiesModifiedFilesOnly()
    {
        using var temp = new TempDirectory();
        var source = Directory.CreateDirectory(Path.Combine(temp.Path, "Desktop"));
        File.WriteAllText(Path.Combine(source.FullName, "a.txt"), "hello");
        File.WriteAllText(Path.Combine(source.FullName, "b.txt"), "world");
        var driveRoot = Directory.CreateDirectory(Path.Combine(temp.Path, "Drive")).FullName;
        var engine = new BackupEngine();
        engine.RunBackup(new[] { source.FullName }, driveRoot, Array.Empty<string>(), now: new DateTime(2026, 9, 11, 22, 0, 0));

        Thread.Sleep(50);
        File.WriteAllText(Path.Combine(source.FullName, "a.txt"), "hello changed");
        var result = engine.RunBackup(new[] { source.FullName }, driveRoot, Array.Empty<string>(), now: new DateTime(2026, 9, 11, 22, 1, 0));

        Assert.Equal(1, result.FilesCopied);
        Assert.Equal(1, result.FilesLinked);
        Assert.Equal("hello changed", File.ReadAllText(Path.Combine(result.SnapshotPath, "Desktop", "a.txt")));
    }

    [Fact]
    public void DeletedSourceFiles_AreAbsentFromNewSnapshotButKeptInOldOne()
    {
        using var temp = new TempDirectory();
        var source = Directory.CreateDirectory(Path.Combine(temp.Path, "Desktop"));
        var deletedFilePath = Path.Combine(source.FullName, "gone.txt");
        File.WriteAllText(deletedFilePath, "bye");
        var driveRoot = Directory.CreateDirectory(Path.Combine(temp.Path, "Drive")).FullName;
        var engine = new BackupEngine();
        var firstResult = engine.RunBackup(new[] { source.FullName }, driveRoot, Array.Empty<string>(), now: new DateTime(2026, 9, 11, 22, 0, 0));
        File.Delete(deletedFilePath);

        var secondResult = engine.RunBackup(new[] { source.FullName }, driveRoot, Array.Empty<string>(), now: new DateTime(2026, 9, 11, 22, 1, 0));

        Assert.True(File.Exists(Path.Combine(firstResult.SnapshotPath, "Desktop", "gone.txt")));
        Assert.False(File.Exists(Path.Combine(secondResult.SnapshotPath, "Desktop", "gone.txt")));
    }

    [Fact]
    public void ExcludedFiles_AreNeverCopied()
    {
        using var temp = new TempDirectory();
        var source = Directory.CreateDirectory(Path.Combine(temp.Path, "Desktop"));
        File.WriteAllText(Path.Combine(source.FullName, "a.txt"), "hello");
        File.WriteAllText(Path.Combine(source.FullName, "junk.tmp"), "temp");
        var driveRoot = Directory.CreateDirectory(Path.Combine(temp.Path, "Drive")).FullName;

        var engine = new BackupEngine();
        var result = engine.RunBackup(new[] { source.FullName }, driveRoot, Array.Empty<string>());

        Assert.Equal(1, result.FilesCopied);
        Assert.False(File.Exists(Path.Combine(result.SnapshotPath, "Desktop", "junk.tmp")));
    }

    // --- C1 regression: filesystem failures during enumeration must not crash RunBackup ---

    [Fact]
    public void UnreadableSubdirectory_DoesNotCrashRunBackup_AndIsReportedAsPartialSuccess()
    {
        using var temp = new TempDirectory();
        var source = Directory.CreateDirectory(Path.Combine(temp.Path, "Desktop"));
        File.WriteAllText(Path.Combine(source.FullName, "a.txt"), "hello");
        var lockedDir = Directory.CreateDirectory(Path.Combine(source.FullName, "locked"));
        File.WriteAllText(Path.Combine(lockedDir.FullName, "secret.txt"), "shh");
        var driveRoot = Directory.CreateDirectory(Path.Combine(temp.Path, "Drive")).FullName;

        // Deny read/list access to the subdirectory using a real ACL so GetFiles()/GetDirectories()
        // throws UnauthorizedAccessException when the engine descends into it - simulating a
        // permission-denied folder without requiring elevated test-runner privileges to set up.
        var acl = lockedDir.GetAccessControl();
        var denyRule = new System.Security.AccessControl.FileSystemAccessRule(
            System.Security.Principal.WindowsIdentity.GetCurrent().User!,
            System.Security.AccessControl.FileSystemRights.ListDirectory | System.Security.AccessControl.FileSystemRights.Read,
            System.Security.AccessControl.AccessControlType.Deny);
        acl.AddAccessRule(denyRule);
        bool aclApplied = false;
        try
        {
            lockedDir.SetAccessControl(acl);
            aclApplied = true;
        }
        catch (System.Security.Principal.IdentityNotMappedException)
        {
            // Some CI/sandbox accounts can't resolve their own SID for ACL writes - skip
            // the ACL-based simulation in that environment.
        }
        catch (UnauthorizedAccessException)
        {
        }

        try
        {
            var engine = new BackupEngine();
            var result = engine.RunBackup(new[] { source.FullName }, driveRoot, Array.Empty<string>());

            if (!aclApplied)
            {
                // ACL denial couldn't be set up in this environment - at minimum confirm the
                // normal case still succeeds without throwing.
                Assert.Equal(BackupOutcome.Success, result.Outcome);
                return;
            }

            Assert.Equal(BackupOutcome.PartialSuccess, result.Outcome);
            Assert.NotEmpty(result.Errors);
            Assert.True(result.FilesFailed > 0);
            // The sibling file outside the locked directory must still have been backed up.
            Assert.Equal("hello", File.ReadAllText(Path.Combine(result.SnapshotPath, "Desktop", "a.txt")));
        }
        finally
        {
            if (aclApplied)
            {
                var resetAcl = lockedDir.GetAccessControl();
                resetAcl.RemoveAccessRule(denyRule);
                lockedDir.SetAccessControl(resetAcl);
            }
        }
    }

    [Fact]
    public void PreviousSnapshotFileGoneBeforeHardLink_FallsBackToCopyInsteadOfCrashing()
    {
        // Simulates a failure in the "link to previous snapshot" path: if the previous
        // snapshot's corresponding file disappears out from under a hardlink attempt (e.g.
        // deleted by another process), CopyDirectory must not let that crash the whole run -
        // it should record the file as either linked from a still-valid path, copied fresh,
        // or (if truly gone) simply skip it, but never throw.
        using var temp = new TempDirectory();
        var source = Directory.CreateDirectory(Path.Combine(temp.Path, "Desktop"));
        File.WriteAllText(Path.Combine(source.FullName, "a.txt"), "hello");
        var driveRoot = Directory.CreateDirectory(Path.Combine(temp.Path, "Drive")).FullName;
        var engine = new BackupEngine();
        var first = engine.RunBackup(new[] { source.FullName }, driveRoot, Array.Empty<string>(), now: new DateTime(2026, 9, 11, 22, 0, 0));

        // Remove the previous snapshot's file right after the first backup completes, so the
        // second run's File.Exists(previousFile) check for it will be false and it must fall
        // through to a fresh copy rather than crash.
        File.Delete(Path.Combine(first.SnapshotPath, "Desktop", "a.txt"));

        var result = engine.RunBackup(new[] { source.FullName }, driveRoot, Array.Empty<string>(), now: new DateTime(2026, 9, 11, 22, 1, 0));

        Assert.Equal(BackupOutcome.Success, result.Outcome);
        Assert.Equal(1, result.FilesCopied);
        Assert.Equal(0, result.FilesLinked);
    }

    // --- C2 regression: stale .inprogress folder + overwrite-through-hardlink corruption ---

    [Fact]
    public void StaleInProgressFolderWithHardlinkIntoPreviousSnapshot_IsCleanedUp_AndPreviousSnapshotUnchanged()
    {
        using var temp = new TempDirectory();
        var source = Directory.CreateDirectory(Path.Combine(temp.Path, "Desktop"));
        File.WriteAllText(Path.Combine(source.FullName, "a.txt"), "original content");
        var driveRoot = Directory.CreateDirectory(Path.Combine(temp.Path, "Drive")).FullName;
        var engine = new BackupEngine();

        var first = engine.RunBackup(new[] { source.FullName }, driveRoot, Array.Empty<string>(), now: new DateTime(2026, 9, 11, 22, 0, 0));
        var previousFile = Path.Combine(first.SnapshotPath, "Desktop", "a.txt");
        Assert.Equal("original content", File.ReadAllText(previousFile));

        // Simulate a stale .inprogress folder from an interrupted/collided run, pre-populated
        // with a hardlink into the previous real snapshot (exactly the scenario C2 describes).
        var backupRoot = Path.Combine(driveRoot, "AutoBackup");
        var staleWorkingPath = Path.Combine(backupRoot, "2026-09-11_2201.inprogress");
        Directory.CreateDirectory(Path.Combine(staleWorkingPath, "Desktop"));
        var staleHardlinkPath = Path.Combine(staleWorkingPath, "Desktop", "a.txt");
        var linkOk = HardLinkHelper.TryCreateHardLink(staleHardlinkPath, previousFile, out var linkError);
        Assert.True(linkOk, $"failed to set up test hardlink: {linkError}");

        // Now change the source file so the new run copies fresh content into the same
        // relative path that the stale .inprogress folder's hardlink also points at.
        File.WriteAllText(Path.Combine(source.FullName, "a.txt"), "new content");

        var second = engine.RunBackup(new[] { source.FullName }, driveRoot, Array.Empty<string>(), now: new DateTime(2026, 9, 11, 22, 1, 0));

        // The old snapshot's file must be completely unaffected by whatever happened to the
        // stale working folder's hardlink.
        Assert.Equal("original content", File.ReadAllText(previousFile));
        Assert.Equal("new content", File.ReadAllText(Path.Combine(second.SnapshotPath, "Desktop", "a.txt")));
    }

    [Fact]
    public void TwoBackupsInSameMinute_DoesNotThrow_AndSkipsDuplicateSnapshot()
    {
        using var temp = new TempDirectory();
        var source = Directory.CreateDirectory(Path.Combine(temp.Path, "Desktop"));
        File.WriteAllText(Path.Combine(source.FullName, "a.txt"), "hello");
        var driveRoot = Directory.CreateDirectory(Path.Combine(temp.Path, "Drive")).FullName;
        var engine = new BackupEngine();
        var sameMinute = new DateTime(2026, 9, 11, 22, 0, 0);

        var first = engine.RunBackup(new[] { source.FullName }, driveRoot, Array.Empty<string>(), now: sameMinute);
        var second = engine.RunBackup(new[] { source.FullName }, driveRoot, Array.Empty<string>(), now: sameMinute);

        Assert.Equal(BackupOutcome.Success, second.Outcome);
        Assert.Equal(first.SnapshotPath, second.SnapshotPath);
        // The stale/duplicate .inprogress working folder from the second call must not be
        // left behind to collide with a future run.
        Assert.False(Directory.Exists(first.SnapshotPath + ".inprogress"));
    }
}
