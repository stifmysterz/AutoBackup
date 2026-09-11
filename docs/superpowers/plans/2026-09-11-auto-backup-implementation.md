# Auto Backup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a Windows tray application that incrementally backs up user-chosen folders (default Desktop, Documents) to an external drive on a user-defined schedule, keeping dated snapshot versions with automatic retention cleanup.

**Architecture:** A testable `AutoBackup.Core` class library holds all backup logic (exclusion rules, drive identification, snapshot planning, the copy/hardlink engine, retention cleanup, logging, scheduling, orchestration) with zero WinForms dependency. A thin `AutoBackup.App` WinForms project hosts a system tray icon, a settings dialog, and a log viewer, and calls into `AutoBackup.Core` for all real work. Snapshots use the Time Machine–style hardlink technique: unchanged files are hardlinked into the new snapshot folder (no extra disk space), changed/new files are copied.

**Tech Stack:** C# / .NET 8, WinForms, xUnit for unit tests, self-contained single-file `dotnet publish` for distribution.

**Spec:** [docs/superpowers/specs/2026-09-11-auto-backup-design.md](../specs/2026-09-11-auto-backup-design.md)

## Global Constraints

- Platform: Windows only, .NET 8, C#, WinForms. Output is a self-contained single-file `.exe` (no separate runtime install for the end user).
- **.NET SDK path note:** The SDK was just installed via winget to `C:\Program Files\dotnet\dotnet.exe`. New shell processes spawned by tooling may not have it on `PATH` yet. Every command below that calls `dotnet` is prefixed with `$env:Path += ';C:\Program Files\dotnet'` — keep that prefix (or restart the terminal once PATH has propagated, then it can be dropped).
- Retention: default 30 days, user-configurable, always keep at least 1 snapshot regardless of age.
- Exclusion rules (fixed, not configurable): Hidden or System file attributes; name patterns `*.tmp`, `*.temp`, `~$*`, `Thumbs.db`; any file/folder name containing `cache` (case-insensitive). Plus a user-editable custom exclude list (exact paths or name patterns).
- Target drive is identified by NTFS volume serial number (via the `GetVolumeInformation` Win32 API), never by drive letter, because the letter can change between USB ports.
- Snapshot folder naming: `yyyy-MM-dd_HHmm` under an `AutoBackup` folder at the drive root. In-progress snapshots are built under a `<name>.inprogress` folder and atomically renamed on completion.
- Safety checks required before any backup runs: source/target path overlap check (blocks the run), single-instance guard (Mutex) at app startup.
- Settings persisted as JSON at `%AppData%\AutoBackup\settings.json`. Log persisted as a tab-separated text file at `%AppData%\AutoBackup\backup.log`, rotated when it exceeds a size or age limit.
- Notifications: silent (tray text update only) on full success; a visible Windows balloon notification on failure, partial failure, drive-not-connected, insufficient-space, or overlap-blocked.

---

## Task 1: Project scaffolding

**Files:**
- Create: `AutoBackup.sln`
- Create: `src/AutoBackup.Core/AutoBackup.Core.csproj`
- Create: `src/AutoBackup.App/AutoBackup.App.csproj`
- Create: `tests/AutoBackup.Core.Tests/AutoBackup.Core.Tests.csproj`
- Create: `.gitignore`

**Interfaces:**
- Produces: a buildable three-project solution (`AutoBackup.Core` class library, `AutoBackup.App` WinForms exe referencing `AutoBackup.Core`, `AutoBackup.Core.Tests` xUnit project referencing `AutoBackup.Core`).

- [ ] **Step 1: Scaffold the solution and projects**

```powershell
$env:Path += ';C:\Program Files\dotnet'
Set-Location "C:\Users\ConceptsGroup\Desktop\Auto Backup"
dotnet new sln -n AutoBackup
dotnet new classlib -n AutoBackup.Core -o src/AutoBackup.Core --framework net8.0
dotnet new winforms -n AutoBackup.App -o src/AutoBackup.App --framework net8.0
dotnet new xunit -n AutoBackup.Core.Tests -o tests/AutoBackup.Core.Tests --framework net8.0
dotnet sln add src/AutoBackup.Core/AutoBackup.Core.csproj src/AutoBackup.App/AutoBackup.App.csproj tests/AutoBackup.Core.Tests/AutoBackup.Core.Tests.csproj
dotnet add src/AutoBackup.App/AutoBackup.App.csproj reference src/AutoBackup.Core/AutoBackup.Core.csproj
dotnet add tests/AutoBackup.Core.Tests/AutoBackup.Core.Tests.csproj reference src/AutoBackup.Core/AutoBackup.Core.csproj
```

- [ ] **Step 2: Remove template placeholder files**

Delete these template-generated files (they'll be replaced by real code in later tasks):
- `src/AutoBackup.Core/Class1.cs`
- `src/AutoBackup.App/Form1.cs`
- `src/AutoBackup.App/Form1.Designer.cs`
- `src/AutoBackup.App/Form1.resx`
- `tests/AutoBackup.Core.Tests/UnitTest1.cs`

- [ ] **Step 3: Add .gitignore**

```
bin/
obj/
publish/
*.user
.vs/
```

- [ ] **Step 4: Verify the solution builds**

```powershell
$env:Path += ';C:\Program Files\dotnet'
Set-Location "C:\Users\ConceptsGroup\Desktop\Auto Backup"
dotnet build
```
Expected: Build succeeds with 0 errors (Program.cs in `AutoBackup.App` still references the deleted `Form1` — replace its content with the minimal stub below so it compiles until Task 15 replaces it properly).

Replace `src/AutoBackup.App/Program.cs` with:
```csharp
namespace AutoBackup.App;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run();
    }
}
```

- [ ] **Step 5: Commit**

```bash
git add AutoBackup.sln src/ tests/ .gitignore
git commit -m "Scaffold AutoBackup solution (Core, App, Core.Tests)"
```

---

## Task 2: Core models and settings persistence

**Files:**
- Create: `src/AutoBackup.Core/Models/BackupSettings.cs`
- Create: `src/AutoBackup.Core/Models/DriveInfoRecord.cs`
- Create: `src/AutoBackup.Core/Models/BackupResult.cs`
- Create: `src/AutoBackup.Core/Settings/SettingsStore.cs`
- Create: `tests/AutoBackup.Core.Tests/TestSupport/TempDirectory.cs`
- Test: `tests/AutoBackup.Core.Tests/SettingsStoreTests.cs`

**Interfaces:**
- Produces:
  - `AutoBackup.Core.Models.BackupSettings` with properties `SourceFolders (List<string>)`, `TargetVolumeSerial (string?)`, `TargetVolumeLabel (string?)`, `ScheduleDays (List<DayOfWeek>)`, `ScheduleTime (TimeOnly)`, `RetentionDays (int, default 30)`, `CustomExcludePatterns (List<string>)`, `StartWithWindows (bool)`, `LastRunAt (DateTime?)`.
  - `AutoBackup.Core.Models.DriveInfoRecord` with `DriveLetter (string)`, `VolumeSerial (string)`, `VolumeLabel (string?)`, `FreeBytes (long)`, `TotalBytes (long)`.
  - `AutoBackup.Core.Models.BackupOutcome` enum: `Success, PartialSuccess`.
  - `AutoBackup.Core.Models.BackupResult` with `Outcome (BackupOutcome)`, `FilesCopied (int)`, `FilesLinked (int)`, `FilesFailed (int)`, `Errors (List<string>)`, `SnapshotPath (string)`, `StartedAt (DateTime)`, `CompletedAt (DateTime)`.
  - `AutoBackup.Core.Settings.SettingsStore.GetDefaultSettingsPath() -> string`, `.Load(string path) -> BackupSettings`, `.Save(BackupSettings settings, string path) -> void`.
  - `AutoBackup.Core.Tests.TestSupport.TempDirectory` (`IDisposable`, exposes `Path`), reused by later test tasks.

- [ ] **Step 1: Write the models**

`src/AutoBackup.Core/Models/BackupSettings.cs`:
```csharp
namespace AutoBackup.Core.Models;

public class BackupSettings
{
    public List<string> SourceFolders { get; set; } = new();
    public string? TargetVolumeSerial { get; set; }
    public string? TargetVolumeLabel { get; set; }
    public List<DayOfWeek> ScheduleDays { get; set; } = new();
    public TimeOnly ScheduleTime { get; set; } = new TimeOnly(22, 0);
    public int RetentionDays { get; set; } = 30;
    public List<string> CustomExcludePatterns { get; set; } = new();
    public bool StartWithWindows { get; set; } = true;
    public DateTime? LastRunAt { get; set; }
}
```

`src/AutoBackup.Core/Models/DriveInfoRecord.cs`:
```csharp
namespace AutoBackup.Core.Models;

public class DriveInfoRecord
{
    public required string DriveLetter { get; init; }
    public required string VolumeSerial { get; init; }
    public string? VolumeLabel { get; init; }
    public long FreeBytes { get; init; }
    public long TotalBytes { get; init; }
}
```

`src/AutoBackup.Core/Models/BackupResult.cs`:
```csharp
namespace AutoBackup.Core.Models;

public enum BackupOutcome
{
    Success,
    PartialSuccess
}

public class BackupResult
{
    public required BackupOutcome Outcome { get; init; }
    public int FilesCopied { get; init; }
    public int FilesLinked { get; init; }
    public int FilesFailed { get; init; }
    public List<string> Errors { get; init; } = new();
    public required string SnapshotPath { get; init; }
    public required DateTime StartedAt { get; init; }
    public required DateTime CompletedAt { get; init; }
}
```

- [ ] **Step 2: Write the test support helper**

`tests/AutoBackup.Core.Tests/TestSupport/TempDirectory.cs`:
```csharp
namespace AutoBackup.Core.Tests.TestSupport;

public sealed class TempDirectory : IDisposable
{
    public string Path { get; }

    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "AutoBackupTests_" + Guid.NewGuid());
        Directory.CreateDirectory(Path);
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch { /* best effort cleanup */ }
    }
}
```

- [ ] **Step 3: Write the failing test for SettingsStore**

`tests/AutoBackup.Core.Tests/SettingsStoreTests.cs`:
```csharp
using AutoBackup.Core.Models;
using AutoBackup.Core.Settings;
using AutoBackup.Core.Tests.TestSupport;

namespace AutoBackup.Core.Tests;

public class SettingsStoreTests
{
    [Fact]
    public void Load_ReturnsDefaultSettings_WhenFileDoesNotExist()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "settings.json");

        var settings = SettingsStore.Load(path);

        Assert.Equal(30, settings.RetentionDays);
        Assert.Empty(settings.SourceFolders);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsAllFields()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "nested", "settings.json");
        var original = new BackupSettings
        {
            SourceFolders = new List<string> { @"C:\Users\Me\Desktop", @"C:\Users\Me\Documents" },
            TargetVolumeSerial = "ABCD-1234",
            TargetVolumeLabel = "MyDrive",
            ScheduleDays = new List<DayOfWeek> { DayOfWeek.Monday, DayOfWeek.Friday },
            ScheduleTime = new TimeOnly(22, 30),
            RetentionDays = 45,
            CustomExcludePatterns = new List<string> { "*.log" },
            StartWithWindows = false,
            LastRunAt = new DateTime(2026, 9, 10, 22, 0, 0)
        };

        SettingsStore.Save(original, path);
        var loaded = SettingsStore.Load(path);

        Assert.Equal(original.SourceFolders, loaded.SourceFolders);
        Assert.Equal(original.TargetVolumeSerial, loaded.TargetVolumeSerial);
        Assert.Equal(original.ScheduleDays, loaded.ScheduleDays);
        Assert.Equal(original.ScheduleTime, loaded.ScheduleTime);
        Assert.Equal(original.RetentionDays, loaded.RetentionDays);
        Assert.Equal(original.CustomExcludePatterns, loaded.CustomExcludePatterns);
        Assert.Equal(original.StartWithWindows, loaded.StartWithWindows);
        Assert.Equal(original.LastRunAt, loaded.LastRunAt);
    }
}
```

- [ ] **Step 4: Run test to verify it fails**

```powershell
$env:Path += ';C:\Program Files\dotnet'
Set-Location "C:\Users\ConceptsGroup\Desktop\Auto Backup"
dotnet test tests/AutoBackup.Core.Tests --filter SettingsStoreTests
```
Expected: FAIL to build — `SettingsStore` does not exist yet.

- [ ] **Step 5: Implement SettingsStore**

`src/AutoBackup.Core/Settings/SettingsStore.cs`:
```csharp
using System.Text.Json;
using AutoBackup.Core.Models;

namespace AutoBackup.Core.Settings;

public static class SettingsStore
{
    public static string GetDefaultSettingsPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AutoBackup", "settings.json");

    public static BackupSettings Load(string path)
    {
        if (!File.Exists(path)) return new BackupSettings();
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<BackupSettings>(json) ?? new BackupSettings();
    }

    public static void Save(BackupSettings settings, string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }
}
```

- [ ] **Step 6: Run test to verify it passes**

```powershell
$env:Path += ';C:\Program Files\dotnet'
Set-Location "C:\Users\ConceptsGroup\Desktop\Auto Backup"
dotnet test tests/AutoBackup.Core.Tests --filter SettingsStoreTests
```
Expected: PASS (2 tests).

- [ ] **Step 7: Commit**

```bash
git add src/AutoBackup.Core/Models src/AutoBackup.Core/Settings tests/AutoBackup.Core.Tests/TestSupport tests/AutoBackup.Core.Tests/SettingsStoreTests.cs
git commit -m "Add core models and JSON settings persistence"
```

---

## Task 3: Exclusion rules

**Files:**
- Create: `src/AutoBackup.Core/Exclusion/ExclusionRules.cs`
- Test: `tests/AutoBackup.Core.Tests/ExclusionRulesTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: `AutoBackup.Core.Exclusion.ExclusionRules.ShouldExclude(string fullPath, FileAttributes attributes, IReadOnlyList<string> customPatterns) -> bool`, used by `BackupEngine` and `SpaceChecker` in later tasks.

- [ ] **Step 1: Write the failing tests**

`tests/AutoBackup.Core.Tests/ExclusionRulesTests.cs`:
```csharp
using AutoBackup.Core.Exclusion;

namespace AutoBackup.Core.Tests;

public class ExclusionRulesTests
{
    private static readonly string[] NoCustomPatterns = Array.Empty<string>();

    [Fact]
    public void Excludes_HiddenAttribute()
    {
        Assert.True(ExclusionRules.ShouldExclude(@"C:\Docs\secret.txt", FileAttributes.Hidden, NoCustomPatterns));
    }

    [Fact]
    public void Excludes_SystemAttribute()
    {
        Assert.True(ExclusionRules.ShouldExclude(@"C:\Docs\sys.dat", FileAttributes.System, NoCustomPatterns));
    }

    [Theory]
    [InlineData("notes.tmp")]
    [InlineData("draft.temp")]
    [InlineData("~$report.docx")]
    [InlineData("Thumbs.db")]
    public void Excludes_TempFilePatterns(string fileName)
    {
        Assert.True(ExclusionRules.ShouldExclude($@"C:\Docs\{fileName}", FileAttributes.Normal, NoCustomPatterns));
    }

    [Theory]
    [InlineData("BrowserCache")]
    [InlineData("cache")]
    [InlineData("my_Cache_folder")]
    public void Excludes_NamesContainingCache(string name)
    {
        Assert.True(ExclusionRules.ShouldExclude($@"C:\Docs\{name}", FileAttributes.Directory, NoCustomPatterns));
    }

    [Fact]
    public void Excludes_CustomExactPathMatch()
    {
        var patterns = new[] { @"C:\Docs\Movies" };
        Assert.True(ExclusionRules.ShouldExclude(@"C:\Docs\Movies", FileAttributes.Directory, patterns));
    }

    [Fact]
    public void Excludes_CustomWildcardPattern()
    {
        var patterns = new[] { "*.log" };
        Assert.True(ExclusionRules.ShouldExclude(@"C:\Docs\app.log", FileAttributes.Normal, patterns));
    }

    [Fact]
    public void DoesNotExclude_NormalFile()
    {
        Assert.False(ExclusionRules.ShouldExclude(@"C:\Docs\report.docx", FileAttributes.Normal, NoCustomPatterns));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```powershell
$env:Path += ';C:\Program Files\dotnet'
Set-Location "C:\Users\ConceptsGroup\Desktop\Auto Backup"
dotnet test tests/AutoBackup.Core.Tests --filter ExclusionRulesTests
```
Expected: FAIL to build — `ExclusionRules` does not exist yet.

- [ ] **Step 3: Implement ExclusionRules**

`src/AutoBackup.Core/Exclusion/ExclusionRules.cs`:
```csharp
using System.IO.Enumeration;

namespace AutoBackup.Core.Exclusion;

public static class ExclusionRules
{
    private static readonly string[] BuiltInNamePatterns = { "*.tmp", "*.temp", "~$*", "Thumbs.db" };

    public static bool ShouldExclude(string fullPath, FileAttributes attributes, IReadOnlyList<string> customPatterns)
    {
        if ((attributes & FileAttributes.Hidden) != 0) return true;
        if ((attributes & FileAttributes.System) != 0) return true;

        var name = Path.GetFileName(fullPath);
        if (name.Contains("cache", StringComparison.OrdinalIgnoreCase)) return true;

        foreach (var pattern in BuiltInNamePatterns)
            if (FileSystemName.MatchesSimpleExpression(pattern, name)) return true;

        foreach (var pattern in customPatterns)
        {
            if (string.Equals(pattern, fullPath, StringComparison.OrdinalIgnoreCase)) return true;
            if (FileSystemName.MatchesSimpleExpression(pattern, name)) return true;
        }

        return false;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```powershell
$env:Path += ';C:\Program Files\dotnet'
Set-Location "C:\Users\ConceptsGroup\Desktop\Auto Backup"
dotnet test tests/AutoBackup.Core.Tests --filter ExclusionRulesTests
```
Expected: PASS (all tests).

- [ ] **Step 5: Commit**

```bash
git add src/AutoBackup.Core/Exclusion tests/AutoBackup.Core.Tests/ExclusionRulesTests.cs
git commit -m "Add file/folder exclusion rules"
```

---

## Task 4: Source folder helpers (overlap check + alias mapping)

**Files:**
- Create: `src/AutoBackup.Core/Backup/OverlapChecker.cs`
- Create: `src/AutoBackup.Core/Backup/SourceAliasMapper.cs`
- Test: `tests/AutoBackup.Core.Tests/OverlapCheckerTests.cs`
- Test: `tests/AutoBackup.Core.Tests/SourceAliasMapperTests.cs`

**Interfaces:**
- Produces: `AutoBackup.Core.Backup.OverlapChecker.HasOverlap(IEnumerable<string> sourceFolders, string targetRootPath) -> bool`; `AutoBackup.Core.Backup.SourceAliasMapper.BuildAliases(IReadOnlyList<string> sourceFolders) -> IReadOnlyDictionary<string,string>` (maps each source path to a unique top-level folder name used inside a snapshot). Both used by `BackupOrchestrator`/`BackupEngine` later.

- [ ] **Step 1: Write the failing tests**

`tests/AutoBackup.Core.Tests/OverlapCheckerTests.cs`:
```csharp
using AutoBackup.Core.Backup;

namespace AutoBackup.Core.Tests;

public class OverlapCheckerTests
{
    [Fact]
    public void ReturnsFalse_WhenNoOverlap()
    {
        var sources = new[] { @"C:\Users\Me\Desktop", @"C:\Users\Me\Documents" };
        Assert.False(OverlapChecker.HasOverlap(sources, @"E:\AutoBackup"));
    }

    [Fact]
    public void ReturnsTrue_WhenTargetIsInsideSource()
    {
        var sources = new[] { @"C:\Users\Me" };
        Assert.True(OverlapChecker.HasOverlap(sources, @"C:\Users\Me\AutoBackup"));
    }

    [Fact]
    public void ReturnsTrue_WhenSourceIsInsideTarget()
    {
        var sources = new[] { @"E:\AutoBackup\Desktop" };
        Assert.True(OverlapChecker.HasOverlap(sources, @"E:\AutoBackup"));
    }

    [Fact]
    public void ReturnsTrue_WhenPathsAreIdentical()
    {
        var sources = new[] { @"E:\AutoBackup" };
        Assert.True(OverlapChecker.HasOverlap(sources, @"E:\AutoBackup"));
    }
}
```

`tests/AutoBackup.Core.Tests/SourceAliasMapperTests.cs`:
```csharp
using AutoBackup.Core.Backup;

namespace AutoBackup.Core.Tests;

public class SourceAliasMapperTests
{
    [Fact]
    public void UsesLeafFolderName_ByDefault()
    {
        var sources = new List<string> { @"C:\Users\Me\Desktop", @"C:\Users\Me\Documents" };
        var aliases = SourceAliasMapper.BuildAliases(sources);

        Assert.Equal("Desktop", aliases[@"C:\Users\Me\Desktop"]);
        Assert.Equal("Documents", aliases[@"C:\Users\Me\Documents"]);
    }

    [Fact]
    public void DisambiguatesDuplicateLeafNames()
    {
        var sources = new List<string> { @"C:\Data\Notes", @"D:\Backup\Notes" };
        var aliases = SourceAliasMapper.BuildAliases(sources);

        Assert.Equal("Notes", aliases[@"C:\Data\Notes"]);
        Assert.Equal("Notes (2)", aliases[@"D:\Backup\Notes"]);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```powershell
$env:Path += ';C:\Program Files\dotnet'
Set-Location "C:\Users\ConceptsGroup\Desktop\Auto Backup"
dotnet test tests/AutoBackup.Core.Tests --filter "OverlapCheckerTests|SourceAliasMapperTests"
```
Expected: FAIL to build — types don't exist yet.

- [ ] **Step 3: Implement OverlapChecker and SourceAliasMapper**

`src/AutoBackup.Core/Backup/OverlapChecker.cs`:
```csharp
namespace AutoBackup.Core.Backup;

public static class OverlapChecker
{
    public static bool HasOverlap(IEnumerable<string> sourceFolders, string targetRootPath)
    {
        var normalizedTarget = Normalize(targetRootPath);
        foreach (var source in sourceFolders)
        {
            var normalizedSource = Normalize(source);
            if (normalizedTarget.StartsWith(normalizedSource, StringComparison.OrdinalIgnoreCase)) return true;
            if (normalizedSource.StartsWith(normalizedTarget, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static string Normalize(string path)
    {
        var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return full + Path.DirectorySeparatorChar;
    }
}
```

`src/AutoBackup.Core/Backup/SourceAliasMapper.cs`:
```csharp
namespace AutoBackup.Core.Backup;

public static class SourceAliasMapper
{
    public static IReadOnlyDictionary<string, string> BuildAliases(IReadOnlyList<string> sourceFolders)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var map = new Dictionary<string, string>();
        foreach (var source in sourceFolders)
        {
            var baseName = new DirectoryInfo(source).Name;
            var candidate = baseName;
            var suffix = 2;
            while (!used.Add(candidate))
            {
                candidate = $"{baseName} ({suffix})";
                suffix++;
            }
            map[source] = candidate;
        }
        return map;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```powershell
$env:Path += ';C:\Program Files\dotnet'
Set-Location "C:\Users\ConceptsGroup\Desktop\Auto Backup"
dotnet test tests/AutoBackup.Core.Tests --filter "OverlapCheckerTests|SourceAliasMapperTests"
```
Expected: PASS (all tests).

- [ ] **Step 5: Commit**

```bash
git add src/AutoBackup.Core/Backup/OverlapChecker.cs src/AutoBackup.Core/Backup/SourceAliasMapper.cs tests/AutoBackup.Core.Tests/OverlapCheckerTests.cs tests/AutoBackup.Core.Tests/SourceAliasMapperTests.cs
git commit -m "Add source/target overlap check and source alias mapper"
```

---

## Task 5: Snapshot path planning and hard link helper

**Files:**
- Create: `src/AutoBackup.Core/Backup/SnapshotPathPlanner.cs`
- Create: `src/AutoBackup.Core/Backup/HardLinkHelper.cs`
- Test: `tests/AutoBackup.Core.Tests/SnapshotPathPlannerTests.cs`
- Test: `tests/AutoBackup.Core.Tests/HardLinkHelperTests.cs`

**Interfaces:**
- Produces:
  - `SnapshotPathPlanner.BackupRootFolderName` (`"AutoBackup"`), `.GetBackupRoot(string driveRoot) -> string`, `.FindLatestSnapshot(string backupRoot) -> string?`, `.CreateNewSnapshotWorkingPath(string backupRoot, DateTime timestamp) -> string`, `.GetFinalPath(string workingPath) -> string`, `.GetAllSnapshots(string backupRoot) -> IEnumerable<(string path, DateTime timestamp)>`.
  - `HardLinkHelper.TryCreateHardLink(string newFilePath, string existingFilePath, out string? error) -> bool`.

- [ ] **Step 1: Write the failing tests**

`tests/AutoBackup.Core.Tests/SnapshotPathPlannerTests.cs`:
```csharp
using AutoBackup.Core.Backup;
using AutoBackup.Core.Tests.TestSupport;

namespace AutoBackup.Core.Tests;

public class SnapshotPathPlannerTests
{
    [Fact]
    public void FindLatestSnapshot_ReturnsNull_WhenBackupRootDoesNotExist()
    {
        using var temp = new TempDirectory();
        var backupRoot = Path.Combine(temp.Path, "AutoBackup");

        Assert.Null(SnapshotPathPlanner.FindLatestSnapshot(backupRoot));
    }

    [Fact]
    public void FindLatestSnapshot_ReturnsMostRecentByName()
    {
        using var temp = new TempDirectory();
        var backupRoot = Path.Combine(temp.Path, "AutoBackup");
        Directory.CreateDirectory(Path.Combine(backupRoot, "2026-09-10_2200"));
        Directory.CreateDirectory(Path.Combine(backupRoot, "2026-09-12_2200"));
        Directory.CreateDirectory(Path.Combine(backupRoot, "2026-09-11_2200"));

        var latest = SnapshotPathPlanner.FindLatestSnapshot(backupRoot);

        Assert.Equal(Path.Combine(backupRoot, "2026-09-12_2200"), latest);
    }

    [Fact]
    public void FindLatestSnapshot_IgnoresInProgressFolders()
    {
        using var temp = new TempDirectory();
        var backupRoot = Path.Combine(temp.Path, "AutoBackup");
        Directory.CreateDirectory(Path.Combine(backupRoot, "2026-09-11_2200"));
        Directory.CreateDirectory(Path.Combine(backupRoot, "2026-09-13_2200.inprogress"));

        var latest = SnapshotPathPlanner.FindLatestSnapshot(backupRoot);

        Assert.Equal(Path.Combine(backupRoot, "2026-09-11_2200"), latest);
    }

    [Fact]
    public void CreateNewSnapshotWorkingPath_UsesInProgressSuffix()
    {
        var backupRoot = @"E:\AutoBackup";
        var path = SnapshotPathPlanner.CreateNewSnapshotWorkingPath(backupRoot, new DateTime(2026, 9, 11, 22, 0, 0));

        Assert.Equal(Path.Combine(backupRoot, "2026-09-11_2200.inprogress"), path);
    }

    [Fact]
    public void GetFinalPath_StripsInProgressSuffix()
    {
        var working = @"E:\AutoBackup\2026-09-11_2200.inprogress";
        Assert.Equal(@"E:\AutoBackup\2026-09-11_2200", SnapshotPathPlanner.GetFinalPath(working));
    }

    [Fact]
    public void GetAllSnapshots_ReturnsOnlyCompletedSnapshotsWithParsedTimestamps()
    {
        using var temp = new TempDirectory();
        var backupRoot = Path.Combine(temp.Path, "AutoBackup");
        Directory.CreateDirectory(Path.Combine(backupRoot, "2026-09-11_2200"));
        Directory.CreateDirectory(Path.Combine(backupRoot, "2026-09-12_2200.inprogress"));

        var snapshots = SnapshotPathPlanner.GetAllSnapshots(backupRoot).ToList();

        Assert.Single(snapshots);
        Assert.Equal(new DateTime(2026, 9, 11, 22, 0, 0), snapshots[0].timestamp);
    }
}
```

`tests/AutoBackup.Core.Tests/HardLinkHelperTests.cs`:
```csharp
using AutoBackup.Core.Backup;
using AutoBackup.Core.Tests.TestSupport;

namespace AutoBackup.Core.Tests;

public class HardLinkHelperTests
{
    [Fact]
    public void CreatesLinkThatSharesContentWithOriginal()
    {
        using var temp = new TempDirectory();
        var original = Path.Combine(temp.Path, "original.txt");
        var link = Path.Combine(temp.Path, "link.txt");
        File.WriteAllText(original, "hello");

        var ok = HardLinkHelper.TryCreateHardLink(link, original, out var error);

        Assert.True(ok, error);
        Assert.Equal("hello", File.ReadAllText(link));
    }

    [Fact]
    public void ReturnsFalse_WhenSourceFileDoesNotExist()
    {
        using var temp = new TempDirectory();
        var link = Path.Combine(temp.Path, "link.txt");

        var ok = HardLinkHelper.TryCreateHardLink(link, Path.Combine(temp.Path, "missing.txt"), out var error);

        Assert.False(ok);
        Assert.NotNull(error);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```powershell
$env:Path += ';C:\Program Files\dotnet'
Set-Location "C:\Users\ConceptsGroup\Desktop\Auto Backup"
dotnet test tests/AutoBackup.Core.Tests --filter "SnapshotPathPlannerTests|HardLinkHelperTests"
```
Expected: FAIL to build — types don't exist yet.

- [ ] **Step 3: Implement SnapshotPathPlanner and HardLinkHelper**

`src/AutoBackup.Core/Backup/SnapshotPathPlanner.cs`:
```csharp
using System.Globalization;

namespace AutoBackup.Core.Backup;

public static class SnapshotPathPlanner
{
    private const string SnapshotFolderFormat = "yyyy-MM-dd_HHmm";
    private const string InProgressSuffix = ".inprogress";
    public const string BackupRootFolderName = "AutoBackup";

    public static string GetBackupRoot(string driveRoot) => Path.Combine(driveRoot, BackupRootFolderName);

    public static string? FindLatestSnapshot(string backupRoot)
    {
        if (!Directory.Exists(backupRoot)) return null;
        return Directory.GetDirectories(backupRoot)
            .Where(d => !Path.GetFileName(d).EndsWith(InProgressSuffix, StringComparison.OrdinalIgnoreCase))
            .Where(d => DateTime.TryParseExact(Path.GetFileName(d), SnapshotFolderFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            .OrderByDescending(d => Path.GetFileName(d), StringComparer.Ordinal)
            .FirstOrDefault();
    }

    public static string CreateNewSnapshotWorkingPath(string backupRoot, DateTime timestamp)
    {
        var name = timestamp.ToString(SnapshotFolderFormat, CultureInfo.InvariantCulture);
        return Path.Combine(backupRoot, name + InProgressSuffix);
    }

    public static string GetFinalPath(string workingPath)
    {
        if (!workingPath.EndsWith(InProgressSuffix, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Not a working snapshot path", nameof(workingPath));
        return workingPath[..^InProgressSuffix.Length];
    }

    public static IEnumerable<(string path, DateTime timestamp)> GetAllSnapshots(string backupRoot)
    {
        if (!Directory.Exists(backupRoot)) yield break;
        foreach (var dir in Directory.GetDirectories(backupRoot))
        {
            var name = Path.GetFileName(dir);
            if (name.EndsWith(InProgressSuffix, StringComparison.OrdinalIgnoreCase)) continue;
            if (DateTime.TryParseExact(name, SnapshotFolderFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var ts))
                yield return (dir, ts);
        }
    }
}
```

`src/AutoBackup.Core/Backup/HardLinkHelper.cs`:
```csharp
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace AutoBackup.Core.Backup;

public static class HardLinkHelper
{
    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool CreateHardLink(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);

    public static bool TryCreateHardLink(string newFilePath, string existingFilePath, out string? error)
    {
        var ok = CreateHardLink(newFilePath, existingFilePath, IntPtr.Zero);
        if (!ok)
        {
            error = new Win32Exception(Marshal.GetLastWin32Error()).Message;
            return false;
        }
        error = null;
        return true;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```powershell
$env:Path += ';C:\Program Files\dotnet'
Set-Location "C:\Users\ConceptsGroup\Desktop\Auto Backup"
dotnet test tests/AutoBackup.Core.Tests --filter "SnapshotPathPlannerTests|HardLinkHelperTests"
```
Expected: PASS (all tests).

- [ ] **Step 5: Commit**

```bash
git add src/AutoBackup.Core/Backup/SnapshotPathPlanner.cs src/AutoBackup.Core/Backup/HardLinkHelper.cs tests/AutoBackup.Core.Tests/SnapshotPathPlannerTests.cs tests/AutoBackup.Core.Tests/HardLinkHelperTests.cs
git commit -m "Add snapshot path planner and NTFS hard link helper"
```

---

## Task 6: Disk space estimation

**Files:**
- Create: `src/AutoBackup.Core/Backup/SpaceChecker.cs`
- Test: `tests/AutoBackup.Core.Tests/SpaceCheckerTests.cs`

**Interfaces:**
- Consumes: `ExclusionRules.ShouldExclude` (Task 3).
- Produces: `SpaceChecker.EstimateSourceSizeBytes(IReadOnlyList<string> sourceFolders, IReadOnlyList<string> customExcludePatterns) -> long`, `SpaceChecker.HasEnoughSpace(long estimatedBytes, long availableFreeBytes) -> bool`.

- [ ] **Step 1: Write the failing tests**

`tests/AutoBackup.Core.Tests/SpaceCheckerTests.cs`:
```csharp
using AutoBackup.Core.Backup;
using AutoBackup.Core.Tests.TestSupport;

namespace AutoBackup.Core.Tests;

public class SpaceCheckerTests
{
    [Fact]
    public void EstimateSourceSizeBytes_SumsAllFilesRecursively()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(Path.Combine(temp.Path, "a.txt"), "12345");
        var sub = Directory.CreateDirectory(Path.Combine(temp.Path, "sub"));
        File.WriteAllText(Path.Combine(sub.FullName, "b.txt"), "1234567890");

        var total = SpaceChecker.EstimateSourceSizeBytes(new[] { temp.Path }, Array.Empty<string>());

        Assert.Equal(15, total);
    }

    [Fact]
    public void EstimateSourceSizeBytes_SkipsExcludedFiles()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(Path.Combine(temp.Path, "a.txt"), "12345");
        File.WriteAllText(Path.Combine(temp.Path, "junk.tmp"), "1234567890");

        var total = SpaceChecker.EstimateSourceSizeBytes(new[] { temp.Path }, Array.Empty<string>());

        Assert.Equal(5, total);
    }

    [Theory]
    [InlineData(100, 200, true)]
    [InlineData(200, 100, false)]
    [InlineData(100, 100, true)]
    public void HasEnoughSpace_ComparesEstimateToFreeBytes(long estimated, long free, bool expected)
    {
        Assert.Equal(expected, SpaceChecker.HasEnoughSpace(estimated, free));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```powershell
$env:Path += ';C:\Program Files\dotnet'
Set-Location "C:\Users\ConceptsGroup\Desktop\Auto Backup"
dotnet test tests/AutoBackup.Core.Tests --filter SpaceCheckerTests
```
Expected: FAIL to build — `SpaceChecker` does not exist yet.

- [ ] **Step 3: Implement SpaceChecker**

`src/AutoBackup.Core/Backup/SpaceChecker.cs`:
```csharp
using AutoBackup.Core.Exclusion;

namespace AutoBackup.Core.Backup;

public static class SpaceChecker
{
    public static long EstimateSourceSizeBytes(IReadOnlyList<string> sourceFolders, IReadOnlyList<string> customExcludePatterns)
    {
        long total = 0;
        foreach (var source in sourceFolders)
        {
            if (!Directory.Exists(source)) continue;
            total += SumDirectory(new DirectoryInfo(source), customExcludePatterns);
        }
        return total;
    }

    public static bool HasEnoughSpace(long estimatedBytes, long availableFreeBytes) => availableFreeBytes >= estimatedBytes;

    private static long SumDirectory(DirectoryInfo dir, IReadOnlyList<string> customExcludePatterns)
    {
        long total = 0;
        foreach (var file in dir.GetFiles())
        {
            if (ExclusionRules.ShouldExclude(file.FullName, file.Attributes, customExcludePatterns)) continue;
            total += file.Length;
        }
        foreach (var sub in dir.GetDirectories())
        {
            if (ExclusionRules.ShouldExclude(sub.FullName, sub.Attributes, customExcludePatterns)) continue;
            total += SumDirectory(sub, customExcludePatterns);
        }
        return total;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```powershell
$env:Path += ';C:\Program Files\dotnet'
Set-Location "C:\Users\ConceptsGroup\Desktop\Auto Backup"
dotnet test tests/AutoBackup.Core.Tests --filter SpaceCheckerTests
```
Expected: PASS (all tests).

- [ ] **Step 5: Commit**

```bash
git add src/AutoBackup.Core/Backup/SpaceChecker.cs tests/AutoBackup.Core.Tests/SpaceCheckerTests.cs
git commit -m "Add pre-backup disk space estimation"
```

---

## Task 7: Drive identification

**Files:**
- Create: `src/AutoBackup.Core/Drives/VolumeSerialReader.cs`
- Create: `src/AutoBackup.Core/Drives/IDriveScanner.cs`
- Create: `src/AutoBackup.Core/Drives/DriveScanner.cs`
- Create: `src/AutoBackup.Core/Drives/DriveIdentifier.cs`
- Test: `tests/AutoBackup.Core.Tests/DriveIdentifierTests.cs`

**Interfaces:**
- Consumes: `AutoBackup.Core.Models.DriveInfoRecord` (Task 2).
- Produces: `IDriveScanner` interface with `GetReadyDrives() -> IReadOnlyList<DriveInfoRecord>`, implemented by `DriveScanner` (real hardware, reads volume serial via the `GetVolumeInformation` Win32 API — functionally equivalent to the WMI-based lookup described in the spec, but lighter weight); `DriveIdentifier.FindBySerial(IEnumerable<DriveInfoRecord> drives, string volumeSerial) -> DriveInfoRecord?`. `IDriveScanner` is consumed by `BackupOrchestrator` (Task 13), which is unit-tested against a fake implementation since real removable drives aren't available in CI.

- [ ] **Step 1: Write the failing test for the pure matching logic**

`tests/AutoBackup.Core.Tests/DriveIdentifierTests.cs`:
```csharp
using AutoBackup.Core.Drives;
using AutoBackup.Core.Models;

namespace AutoBackup.Core.Tests;

public class DriveIdentifierTests
{
    private static readonly List<DriveInfoRecord> Drives = new()
    {
        new DriveInfoRecord { DriveLetter = @"D:\", VolumeSerial = "AAAA-1111", VolumeLabel = "Data" },
        new DriveInfoRecord { DriveLetter = @"E:\", VolumeSerial = "BBBB-2222", VolumeLabel = "Backup" }
    };

    [Fact]
    public void FindBySerial_ReturnsMatchingDrive()
    {
        var found = DriveIdentifier.FindBySerial(Drives, "BBBB-2222");
        Assert.NotNull(found);
        Assert.Equal(@"E:\", found!.DriveLetter);
    }

    [Fact]
    public void FindBySerial_IsCaseInsensitive()
    {
        var found = DriveIdentifier.FindBySerial(Drives, "bbbb-2222");
        Assert.NotNull(found);
    }

    [Fact]
    public void FindBySerial_ReturnsNull_WhenNoMatch()
    {
        var found = DriveIdentifier.FindBySerial(Drives, "ZZZZ-9999");
        Assert.Null(found);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```powershell
$env:Path += ';C:\Program Files\dotnet'
Set-Location "C:\Users\ConceptsGroup\Desktop\Auto Backup"
dotnet test tests/AutoBackup.Core.Tests --filter DriveIdentifierTests
```
Expected: FAIL to build — types don't exist yet.

- [ ] **Step 3: Implement the drive identification classes**

`src/AutoBackup.Core/Drives/VolumeSerialReader.cs`:
```csharp
using System.Runtime.InteropServices;
using System.Text;

namespace AutoBackup.Core.Drives;

internal static class VolumeSerialReader
{
    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool GetVolumeInformation(
        string lpRootPathName,
        StringBuilder lpVolumeNameBuffer,
        int nVolumeNameSize,
        out uint lpVolumeSerialNumber,
        out uint lpMaximumComponentLength,
        out uint lpFileSystemFlags,
        StringBuilder lpFileSystemNameBuffer,
        int nFileSystemNameSize);

    public static (string? serial, string? label) Read(string rootPath)
    {
        var volumeName = new StringBuilder(261);
        var fileSystemName = new StringBuilder(261);
        var ok = GetVolumeInformation(rootPath, volumeName, volumeName.Capacity, out var serial, out _, out _, fileSystemName, fileSystemName.Capacity);
        if (!ok) return (null, null);
        return (serial.ToString("X8"), volumeName.ToString());
    }
}
```

`src/AutoBackup.Core/Drives/IDriveScanner.cs`:
```csharp
using AutoBackup.Core.Models;

namespace AutoBackup.Core.Drives;

public interface IDriveScanner
{
    IReadOnlyList<DriveInfoRecord> GetReadyDrives();
}
```

`src/AutoBackup.Core/Drives/DriveScanner.cs`:
```csharp
using AutoBackup.Core.Models;

namespace AutoBackup.Core.Drives;

public class DriveScanner : IDriveScanner
{
    public IReadOnlyList<DriveInfoRecord> GetReadyDrives()
    {
        var result = new List<DriveInfoRecord>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.DriveType != DriveType.Removable && drive.DriveType != DriveType.Fixed) continue;
            if (!drive.IsReady) continue;

            var (serial, label) = VolumeSerialReader.Read(drive.RootDirectory.FullName);
            if (serial == null) continue;

            result.Add(new DriveInfoRecord
            {
                DriveLetter = drive.RootDirectory.FullName,
                VolumeSerial = serial,
                VolumeLabel = label,
                FreeBytes = drive.AvailableFreeSpace,
                TotalBytes = drive.TotalSize
            });
        }
        return result;
    }
}
```

`src/AutoBackup.Core/Drives/DriveIdentifier.cs`:
```csharp
using AutoBackup.Core.Models;

namespace AutoBackup.Core.Drives;

public static class DriveIdentifier
{
    public static DriveInfoRecord? FindBySerial(IEnumerable<DriveInfoRecord> drives, string volumeSerial) =>
        drives.FirstOrDefault(d => string.Equals(d.VolumeSerial, volumeSerial, StringComparison.OrdinalIgnoreCase));
}
```

- [ ] **Step 4: Run test to verify it passes**

```powershell
$env:Path += ';C:\Program Files\dotnet'
Set-Location "C:\Users\ConceptsGroup\Desktop\Auto Backup"
dotnet test tests/AutoBackup.Core.Tests --filter DriveIdentifierTests
```
Expected: PASS (all tests).

- [ ] **Step 5: Commit**

```bash
git add src/AutoBackup.Core/Drives tests/AutoBackup.Core.Tests/DriveIdentifierTests.cs
git commit -m "Add drive scanning and volume-serial-based drive identification"
```

---

## Task 8: Backup engine (incremental copy + hardlink snapshots)

**Files:**
- Create: `src/AutoBackup.Core/Backup/BackupEngine.cs`
- Test: `tests/AutoBackup.Core.Tests/BackupEngineTests.cs`

**Interfaces:**
- Consumes: `SnapshotPathPlanner` (Task 5), `HardLinkHelper` (Task 5), `SourceAliasMapper` (Task 4), `ExclusionRules` (Task 3), `BackupResult`/`BackupOutcome` (Task 2).
- Produces: `BackupEngine.RunBackup(IReadOnlyList<string> sourceFolders, string driveRoot, IReadOnlyList<string> customExcludePatterns) -> BackupResult`. Consumed by `BackupOrchestrator` (Task 13).

- [ ] **Step 1: Write the failing tests**

`tests/AutoBackup.Core.Tests/BackupEngineTests.cs`:
```csharp
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
        Assert.True(Directory.Exists(result.SnapshotPath));
        Assert.Equal("hello", File.ReadAllText(Path.Combine(result.SnapshotPath, "Desktop", "a.txt")));
    }

    [Fact]
    public void SecondBackup_HardLinksUnchangedFiles()
    {
        using var temp = new TempDirectory();
        var source = Directory.CreateDirectory(Path.Combine(temp.Path, "Desktop"));
        File.WriteAllText(Path.Combine(source.FullName, "a.txt"), "hello");
        var driveRoot = Directory.CreateDirectory(Path.Combine(temp.Path, "Drive")).FullName;
        var engine = new BackupEngine();
        engine.RunBackup(new[] { source.FullName }, driveRoot, Array.Empty<string>());

        var result = engine.RunBackup(new[] { source.FullName }, driveRoot, Array.Empty<string>());

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
        engine.RunBackup(new[] { source.FullName }, driveRoot, Array.Empty<string>());

        Thread.Sleep(50);
        File.WriteAllText(Path.Combine(source.FullName, "a.txt"), "hello changed");
        var result = engine.RunBackup(new[] { source.FullName }, driveRoot, Array.Empty<string>());

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
        var firstResult = engine.RunBackup(new[] { source.FullName }, driveRoot, Array.Empty<string>());
        File.Delete(deletedFilePath);

        var secondResult = engine.RunBackup(new[] { source.FullName }, driveRoot, Array.Empty<string>());

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
}
```

- [ ] **Step 2: Run tests to verify they fail**

```powershell
$env:Path += ';C:\Program Files\dotnet'
Set-Location "C:\Users\ConceptsGroup\Desktop\Auto Backup"
dotnet test tests/AutoBackup.Core.Tests --filter BackupEngineTests
```
Expected: FAIL to build — `BackupEngine` does not exist yet.

- [ ] **Step 3: Implement BackupEngine**

`src/AutoBackup.Core/Backup/BackupEngine.cs`:
```csharp
using AutoBackup.Core.Exclusion;
using AutoBackup.Core.Models;

namespace AutoBackup.Core.Backup;

public class BackupEngine
{
    public BackupResult RunBackup(IReadOnlyList<string> sourceFolders, string driveRoot, IReadOnlyList<string> customExcludePatterns)
    {
        var startedAt = DateTime.Now;
        var backupRoot = SnapshotPathPlanner.GetBackupRoot(driveRoot);
        Directory.CreateDirectory(backupRoot);

        var previousSnapshot = SnapshotPathPlanner.FindLatestSnapshot(backupRoot);
        var workingPath = SnapshotPathPlanner.CreateNewSnapshotWorkingPath(backupRoot, startedAt);
        Directory.CreateDirectory(workingPath);

        var aliases = SourceAliasMapper.BuildAliases(sourceFolders);
        int copied = 0, linked = 0, failed = 0;
        var errors = new List<string>();

        foreach (var source in sourceFolders)
        {
            if (!Directory.Exists(source)) continue;
            var alias = aliases[source];
            CopyDirectory(new DirectoryInfo(source), alias, workingPath, previousSnapshot, customExcludePatterns, ref copied, ref linked, ref failed, errors);
        }

        var finalPath = SnapshotPathPlanner.GetFinalPath(workingPath);
        Directory.Move(workingPath, finalPath);

        var outcome = failed > 0 ? BackupOutcome.PartialSuccess : BackupOutcome.Success;
        return new BackupResult
        {
            Outcome = outcome,
            FilesCopied = copied,
            FilesLinked = linked,
            FilesFailed = failed,
            Errors = errors,
            SnapshotPath = finalPath,
            StartedAt = startedAt,
            CompletedAt = DateTime.Now
        };
    }

    private static void CopyDirectory(
        DirectoryInfo sourceDir,
        string relativePath,
        string workingRoot,
        string? previousSnapshotRoot,
        IReadOnlyList<string> customExcludePatterns,
        ref int copied,
        ref int linked,
        ref int failed,
        List<string> errors)
    {
        var destDir = Path.Combine(workingRoot, relativePath);
        Directory.CreateDirectory(destDir);

        foreach (var subDir in sourceDir.GetDirectories())
        {
            if (ExclusionRules.ShouldExclude(subDir.FullName, subDir.Attributes, customExcludePatterns)) continue;
            CopyDirectory(subDir, Path.Combine(relativePath, subDir.Name), workingRoot, previousSnapshotRoot, customExcludePatterns, ref copied, ref linked, ref failed, errors);
        }

        foreach (var file in sourceDir.GetFiles())
        {
            if (ExclusionRules.ShouldExclude(file.FullName, file.Attributes, customExcludePatterns)) continue;

            var destFile = Path.Combine(destDir, file.Name);
            var previousFile = previousSnapshotRoot == null ? null : Path.Combine(previousSnapshotRoot, relativePath, file.Name);

            try
            {
                if (previousFile != null && File.Exists(previousFile) && IsUnchanged(file, previousFile) &&
                    HardLinkHelper.TryCreateHardLink(destFile, previousFile, out _))
                {
                    linked++;
                    continue;
                }

                File.Copy(file.FullName, destFile, overwrite: true);
                var destLength = new FileInfo(destFile).Length;
                if (destLength != file.Length)
                {
                    failed++;
                    errors.Add($"{file.FullName}: 复制后大小不一致 ({destLength} != {file.Length})");
                }
                else
                {
                    copied++;
                }
            }
            catch (Exception ex)
            {
                failed++;
                errors.Add($"{file.FullName}: {ex.Message}");
            }
        }
    }

    private static bool IsUnchanged(FileInfo source, string previousFilePath)
    {
        var previous = new FileInfo(previousFilePath);
        return source.Length == previous.Length && source.LastWriteTimeUtc == previous.LastWriteTimeUtc;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```powershell
$env:Path += ';C:\Program Files\dotnet'
Set-Location "C:\Users\ConceptsGroup\Desktop\Auto Backup"
dotnet test tests/AutoBackup.Core.Tests --filter BackupEngineTests
```
Expected: PASS (all 5 tests).

- [ ] **Step 5: Commit**

```bash
git add src/AutoBackup.Core/Backup/BackupEngine.cs tests/AutoBackup.Core.Tests/BackupEngineTests.cs
git commit -m "Add incremental hardlink-snapshot backup engine"
```

---

## Task 9: Retention cleanup

**Files:**
- Create: `src/AutoBackup.Core/Backup/RetentionCleaner.cs`
- Test: `tests/AutoBackup.Core.Tests/RetentionCleanerTests.cs`

**Interfaces:**
- Consumes: `SnapshotPathPlanner.GetAllSnapshots` (Task 5).
- Produces: `RetentionCleaner.CleanOldSnapshots(string backupRoot, int retentionDays, DateTime now) -> void`. Consumed by `BackupOrchestrator` (Task 13).

- [ ] **Step 1: Write the failing tests**

`tests/AutoBackup.Core.Tests/RetentionCleanerTests.cs`:
```csharp
using AutoBackup.Core.Backup;
using AutoBackup.Core.Tests.TestSupport;

namespace AutoBackup.Core.Tests;

public class RetentionCleanerTests
{
    [Fact]
    public void DeletesSnapshotsOlderThanRetentionDays()
    {
        using var temp = new TempDirectory();
        var backupRoot = Path.Combine(temp.Path, "AutoBackup");
        var oldSnapshot = Directory.CreateDirectory(Path.Combine(backupRoot, "2026-01-01_2200")).FullName;
        var recentSnapshot = Directory.CreateDirectory(Path.Combine(backupRoot, "2026-09-10_2200")).FullName;
        var now = new DateTime(2026, 9, 11);

        RetentionCleaner.CleanOldSnapshots(backupRoot, retentionDays: 30, now: now);

        Assert.False(Directory.Exists(oldSnapshot));
        Assert.True(Directory.Exists(recentSnapshot));
    }

    [Fact]
    public void AlwaysKeepsAtLeastTheMostRecentSnapshot()
    {
        using var temp = new TempDirectory();
        var backupRoot = Path.Combine(temp.Path, "AutoBackup");
        var onlySnapshot = Directory.CreateDirectory(Path.Combine(backupRoot, "2020-01-01_2200")).FullName;
        var now = new DateTime(2026, 9, 11);

        RetentionCleaner.CleanOldSnapshots(backupRoot, retentionDays: 30, now: now);

        Assert.True(Directory.Exists(onlySnapshot));
    }

    [Fact]
    public void SharedHardLinkedFile_SurvivesDeletionOfOneSnapshot()
    {
        using var temp = new TempDirectory();
        var backupRoot = Path.Combine(temp.Path, "AutoBackup");
        var oldSnapshot = Directory.CreateDirectory(Path.Combine(backupRoot, "2026-01-01_2200")).FullName;
        var recentSnapshot = Directory.CreateDirectory(Path.Combine(backupRoot, "2026-09-10_2200")).FullName;
        var oldFile = Path.Combine(oldSnapshot, "a.txt");
        File.WriteAllText(oldFile, "shared");
        var recentFile = Path.Combine(recentSnapshot, "a.txt");
        HardLinkHelper.TryCreateHardLink(recentFile, oldFile, out _);
        var now = new DateTime(2026, 9, 11);

        RetentionCleaner.CleanOldSnapshots(backupRoot, retentionDays: 30, now: now);

        Assert.False(Directory.Exists(oldSnapshot));
        Assert.Equal("shared", File.ReadAllText(recentFile));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```powershell
$env:Path += ';C:\Program Files\dotnet'
Set-Location "C:\Users\ConceptsGroup\Desktop\Auto Backup"
dotnet test tests/AutoBackup.Core.Tests --filter RetentionCleanerTests
```
Expected: FAIL to build — `RetentionCleaner` does not exist yet.

- [ ] **Step 3: Implement RetentionCleaner**

`src/AutoBackup.Core/Backup/RetentionCleaner.cs`:
```csharp
namespace AutoBackup.Core.Backup;

public static class RetentionCleaner
{
    public static void CleanOldSnapshots(string backupRoot, int retentionDays, DateTime now)
    {
        var snapshots = SnapshotPathPlanner.GetAllSnapshots(backupRoot)
            .OrderByDescending(s => s.timestamp)
            .ToList();
        if (snapshots.Count <= 1) return;

        var cutoff = now.AddDays(-retentionDays);
        foreach (var snapshot in snapshots.Skip(1))
        {
            if (snapshot.timestamp < cutoff)
                Directory.Delete(snapshot.path, recursive: true);
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```powershell
$env:Path += ';C:\Program Files\dotnet'
Set-Location "C:\Users\ConceptsGroup\Desktop\Auto Backup"
dotnet test tests/AutoBackup.Core.Tests --filter RetentionCleanerTests
```
Expected: PASS (all tests).

- [ ] **Step 5: Commit**

```bash
git add src/AutoBackup.Core/Backup/RetentionCleaner.cs tests/AutoBackup.Core.Tests/RetentionCleanerTests.cs
git commit -m "Add retention-based snapshot cleanup"
```

---

## Task 10: Backup logging with rotation

**Files:**
- Create: `src/AutoBackup.Core/Logging/BackupLogEntry.cs`
- Create: `src/AutoBackup.Core/Logging/BackupLogger.cs`
- Test: `tests/AutoBackup.Core.Tests/BackupLoggerTests.cs`

**Interfaces:**
- Produces: `BackupLogEntry { DateTime Timestamp, string Outcome, string Message }`; `BackupLogger(string logFilePath)` with `.Append(BackupLogEntry entry) -> void`, `.ReadRecent(int maxLines) -> IReadOnlyList<string>`, `.RotateIfNeeded(long maxSizeBytes, int maxAgeDays, DateTime now) -> void`. Consumed by `BackupOrchestrator` (Task 13) and `LogViewerForm` (Task 14).

- [ ] **Step 1: Write the failing tests**

`tests/AutoBackup.Core.Tests/BackupLoggerTests.cs`:
```csharp
using AutoBackup.Core.Logging;
using AutoBackup.Core.Tests.TestSupport;

namespace AutoBackup.Core.Tests;

public class BackupLoggerTests
{
    [Fact]
    public void Append_WritesReadableLine()
    {
        using var temp = new TempDirectory();
        var logger = new BackupLogger(Path.Combine(temp.Path, "backup.log"));

        logger.Append(new BackupLogEntry { Timestamp = new DateTime(2026, 9, 11, 22, 0, 0), Outcome = "Success", Message = "复制 3，硬链接 10，失败 0" });

        var lines = logger.ReadRecent(10);
        Assert.Single(lines);
        Assert.Contains("Success", lines[0]);
        Assert.Contains("复制 3", lines[0]);
    }

    [Fact]
    public void ReadRecent_ReturnsOnlyLastNLines()
    {
        using var temp = new TempDirectory();
        var logger = new BackupLogger(Path.Combine(temp.Path, "backup.log"));
        for (var i = 0; i < 5; i++)
            logger.Append(new BackupLogEntry { Timestamp = DateTime.Now, Outcome = "Success", Message = $"entry {i}" });

        var lines = logger.ReadRecent(2);

        Assert.Equal(2, lines.Count);
        Assert.Contains("entry 4", lines[1]);
    }

    [Fact]
    public void RotateIfNeeded_ArchivesFile_WhenOverSizeLimit()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "backup.log");
        var logger = new BackupLogger(path);
        File.WriteAllText(path, new string('x', 1000));

        logger.RotateIfNeeded(maxSizeBytes: 100, maxAgeDays: 365, now: DateTime.Now);

        Assert.True(new FileInfo(path).Length == 0 || !File.Exists(path));
        Assert.Single(Directory.GetFiles(temp.Path, "backup.log.*.old"));
    }

    [Fact]
    public void RotateIfNeeded_DoesNothing_WhenUnderLimits()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "backup.log");
        var logger = new BackupLogger(path);
        logger.Append(new BackupLogEntry { Timestamp = DateTime.Now, Outcome = "Success", Message = "ok" });

        logger.RotateIfNeeded(maxSizeBytes: 10_000_000, maxAgeDays: 365, now: DateTime.Now);

        Assert.Single(logger.ReadRecent(10));
        Assert.Empty(Directory.GetFiles(temp.Path, "backup.log.*.old"));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```powershell
$env:Path += ';C:\Program Files\dotnet'
Set-Location "C:\Users\ConceptsGroup\Desktop\Auto Backup"
dotnet test tests/AutoBackup.Core.Tests --filter BackupLoggerTests
```
Expected: FAIL to build — types don't exist yet.

- [ ] **Step 3: Implement logging classes**

`src/AutoBackup.Core/Logging/BackupLogEntry.cs`:
```csharp
namespace AutoBackup.Core.Logging;

public class BackupLogEntry
{
    public required DateTime Timestamp { get; init; }
    public required string Outcome { get; init; }
    public required string Message { get; init; }
}
```

`src/AutoBackup.Core/Logging/BackupLogger.cs`:
```csharp
namespace AutoBackup.Core.Logging;

public class BackupLogger
{
    private readonly string _logFilePath;

    public BackupLogger(string logFilePath) => _logFilePath = logFilePath;

    public void Append(BackupLogEntry entry)
    {
        var dir = Path.GetDirectoryName(_logFilePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var line = $"{entry.Timestamp:yyyy-MM-dd HH:mm:ss}\t{entry.Outcome}\t{entry.Message}";
        File.AppendAllLines(_logFilePath, new[] { line });
    }

    public IReadOnlyList<string> ReadRecent(int maxLines)
    {
        if (!File.Exists(_logFilePath)) return Array.Empty<string>();
        return File.ReadLines(_logFilePath).TakeLast(maxLines).ToList();
    }

    public void RotateIfNeeded(long maxSizeBytes, int maxAgeDays, DateTime now)
    {
        if (!File.Exists(_logFilePath)) return;

        var info = new FileInfo(_logFilePath);
        var tooBig = info.Length > maxSizeBytes;
        var tooOld = (now - info.CreationTimeUtc).TotalDays > maxAgeDays;
        if (!tooBig && !tooOld) return;

        var archivePath = _logFilePath + "." + now.ToString("yyyyMMddHHmmss") + ".old";
        File.Move(_logFilePath, archivePath);

        var dir = Path.GetDirectoryName(_logFilePath)!;
        var baseName = Path.GetFileName(_logFilePath);
        var oldArchives = Directory.GetFiles(dir, baseName + ".*.old")
            .OrderByDescending(f => f)
            .Skip(1);
        foreach (var old in oldArchives) File.Delete(old);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```powershell
$env:Path += ';C:\Program Files\dotnet'
Set-Location "C:\Users\ConceptsGroup\Desktop\Auto Backup"
dotnet test tests/AutoBackup.Core.Tests --filter BackupLoggerTests
```
Expected: PASS (all tests).

- [ ] **Step 5: Commit**

```bash
git add src/AutoBackup.Core/Logging tests/AutoBackup.Core.Tests/BackupLoggerTests.cs
git commit -m "Add rotating backup log"
```

---

## Task 11: Backup scheduler

**Files:**
- Create: `src/AutoBackup.Core/Scheduling/ScheduleConfig.cs`
- Create: `src/AutoBackup.Core/Scheduling/BackupScheduler.cs`
- Test: `tests/AutoBackup.Core.Tests/BackupSchedulerTests.cs`

**Interfaces:**
- Produces: `ScheduleConfig { IReadOnlyList<DayOfWeek> Days, TimeOnly Time }`; `BackupScheduler.IsDueNow(ScheduleConfig config, DateTime now, DateTime? lastRunAt) -> bool`. Consumed by `TrayApplicationContext` (Task 15).

- [ ] **Step 1: Write the failing tests**

`tests/AutoBackup.Core.Tests/BackupSchedulerTests.cs`:
```csharp
using AutoBackup.Core.Scheduling;

namespace AutoBackup.Core.Tests;

public class BackupSchedulerTests
{
    private static readonly ScheduleConfig MondayAt2200 = new()
    {
        Days = new List<DayOfWeek> { DayOfWeek.Monday },
        Time = new TimeOnly(22, 0)
    };

    [Fact]
    public void IsDueNow_True_WhenDayAndMinuteMatchAndNeverRun()
    {
        var now = new DateTime(2026, 9, 14, 22, 0, 0); // a Monday
        Assert.True(BackupScheduler.IsDueNow(MondayAt2200, now, lastRunAt: null));
    }

    [Fact]
    public void IsDueNow_False_WhenWrongDay()
    {
        var now = new DateTime(2026, 9, 15, 22, 0, 0); // a Tuesday
        Assert.False(BackupScheduler.IsDueNow(MondayAt2200, now, lastRunAt: null));
    }

    [Fact]
    public void IsDueNow_False_WhenWrongTime()
    {
        var now = new DateTime(2026, 9, 14, 21, 59, 0);
        Assert.False(BackupScheduler.IsDueNow(MondayAt2200, now, lastRunAt: null));
    }

    [Fact]
    public void IsDueNow_False_WhenAlreadyRanThisMinute()
    {
        var now = new DateTime(2026, 9, 14, 22, 0, 30);
        var lastRunAt = new DateTime(2026, 9, 14, 22, 0, 5);
        Assert.False(BackupScheduler.IsDueNow(MondayAt2200, now, lastRunAt));
    }

    [Fact]
    public void IsDueNow_True_WhenLastRunWasADifferentMinute()
    {
        var now = new DateTime(2026, 9, 14, 22, 0, 30);
        var lastRunAt = new DateTime(2026, 9, 7, 22, 0, 5); // previous Monday
        Assert.True(BackupScheduler.IsDueNow(MondayAt2200, now, lastRunAt));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```powershell
$env:Path += ';C:\Program Files\dotnet'
Set-Location "C:\Users\ConceptsGroup\Desktop\Auto Backup"
dotnet test tests/AutoBackup.Core.Tests --filter BackupSchedulerTests
```
Expected: FAIL to build — types don't exist yet.

- [ ] **Step 3: Implement the scheduler**

`src/AutoBackup.Core/Scheduling/ScheduleConfig.cs`:
```csharp
namespace AutoBackup.Core.Scheduling;

public class ScheduleConfig
{
    public required IReadOnlyList<DayOfWeek> Days { get; init; }
    public required TimeOnly Time { get; init; }
}
```

`src/AutoBackup.Core/Scheduling/BackupScheduler.cs`:
```csharp
namespace AutoBackup.Core.Scheduling;

public static class BackupScheduler
{
    public static bool IsDueNow(ScheduleConfig config, DateTime now, DateTime? lastRunAt)
    {
        if (!config.Days.Contains(now.DayOfWeek)) return false;

        var nowTime = TimeOnly.FromDateTime(now);
        if (nowTime.Hour != config.Time.Hour || nowTime.Minute != config.Time.Minute) return false;

        if (lastRunAt.HasValue &&
            lastRunAt.Value.Date == now.Date &&
            lastRunAt.Value.Hour == now.Hour &&
            lastRunAt.Value.Minute == now.Minute)
        {
            return false;
        }

        return true;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```powershell
$env:Path += ';C:\Program Files\dotnet'
Set-Location "C:\Users\ConceptsGroup\Desktop\Auto Backup"
dotnet test tests/AutoBackup.Core.Tests --filter BackupSchedulerTests
```
Expected: PASS (all tests).

- [ ] **Step 5: Commit**

```bash
git add src/AutoBackup.Core/Scheduling tests/AutoBackup.Core.Tests/BackupSchedulerTests.cs
git commit -m "Add day/time backup scheduler"
```

---

## Task 12: Single-instance guard

**Files:**
- Create: `src/AutoBackup.Core/SingleInstance/SingleInstanceGuard.cs`
- Test: `tests/AutoBackup.Core.Tests/SingleInstanceGuardTests.cs`

**Interfaces:**
- Produces: `SingleInstanceGuard(string mutexName)` (`IDisposable`) exposing `IsFirstInstance (bool)`. Consumed by `Program.cs` (Task 15).

- [ ] **Step 1: Write the failing test**

`tests/AutoBackup.Core.Tests/SingleInstanceGuardTests.cs`:
```csharp
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
```

- [ ] **Step 2: Run test to verify it fails**

```powershell
$env:Path += ';C:\Program Files\dotnet'
Set-Location "C:\Users\ConceptsGroup\Desktop\Auto Backup"
dotnet test tests/AutoBackup.Core.Tests --filter SingleInstanceGuardTests
```
Expected: FAIL to build — `SingleInstanceGuard` does not exist yet.

- [ ] **Step 3: Implement SingleInstanceGuard**

`src/AutoBackup.Core/SingleInstance/SingleInstanceGuard.cs`:
```csharp
namespace AutoBackup.Core.SingleInstance;

public class SingleInstanceGuard : IDisposable
{
    private readonly Mutex _mutex;
    public bool IsFirstInstance { get; }

    public SingleInstanceGuard(string mutexName)
    {
        _mutex = new Mutex(initiallyOwned: true, mutexName, out var createdNew);
        IsFirstInstance = createdNew;
    }

    public void Dispose()
    {
        if (IsFirstInstance)
        {
            try { _mutex.ReleaseMutex(); } catch (ApplicationException) { /* already released */ }
        }
        _mutex.Dispose();
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

```powershell
$env:Path += ';C:\Program Files\dotnet'
Set-Location "C:\Users\ConceptsGroup\Desktop\Auto Backup"
dotnet test tests/AutoBackup.Core.Tests --filter SingleInstanceGuardTests
```
Expected: PASS (both tests).

- [ ] **Step 5: Commit**

```bash
git add src/AutoBackup.Core/SingleInstance tests/AutoBackup.Core.Tests/SingleInstanceGuardTests.cs
git commit -m "Add named-mutex single instance guard"
```

---

## Task 13: Backup orchestrator (ties the core pieces together)

**Files:**
- Create: `src/AutoBackup.Core/Orchestration/OrchestrationResult.cs`
- Create: `src/AutoBackup.Core/Orchestration/BackupOrchestrator.cs`
- Test: `tests/AutoBackup.Core.Tests/BackupOrchestratorTests.cs`

**Interfaces:**
- Consumes: `IDriveScanner`/`DriveIdentifier` (Task 7), `SnapshotPathPlanner`/`OverlapChecker`/`SpaceChecker`/`BackupEngine`/`RetentionCleaner` (Tasks 4-9), `BackupLogger` (Task 10), `BackupSettings` (Task 2).
- Produces: `OrchestrationOutcome` enum (`Success, PartialSuccess, DriveNotConnected, InsufficientSpace, SourceTargetOverlap, NoSourceFolders`); `OrchestrationResult { OrchestrationOutcome Outcome, BackupResult? BackupResult, string Message }`; `BackupOrchestrator(IDriveScanner driveScanner, BackupLogger logger)` with `.RunOnce(BackupSettings settings) -> OrchestrationResult`. Consumed by `TrayApplicationContext` (Task 15), which only needs to translate `OrchestrationResult` into UI notifications — all decision logic (drive missing, overlap, space) lives here and is unit-tested with a fake `IDriveScanner`.

- [ ] **Step 1: Write the failing tests**

`tests/AutoBackup.Core.Tests/BackupOrchestratorTests.cs`:
```csharp
using AutoBackup.Core.Drives;
using AutoBackup.Core.Logging;
using AutoBackup.Core.Models;
using AutoBackup.Core.Orchestration;
using AutoBackup.Core.Tests.TestSupport;

namespace AutoBackup.Core.Tests;

public class FakeDriveScanner : IDriveScanner
{
    public List<DriveInfoRecord> Drives { get; set; } = new();
    public IReadOnlyList<DriveInfoRecord> GetReadyDrives() => Drives;
}

public class BackupOrchestratorTests
{
    private static BackupLogger MakeLogger(string tempDir) => new(Path.Combine(tempDir, "backup.log"));

    [Fact]
    public void RunOnce_ReturnsNoSourceFolders_WhenNoneConfigured()
    {
        using var temp = new TempDirectory();
        var scanner = new FakeDriveScanner();
        var orchestrator = new BackupOrchestrator(scanner, MakeLogger(temp.Path));
        var settings = new BackupSettings { SourceFolders = new List<string>() };

        var result = orchestrator.RunOnce(settings);

        Assert.Equal(OrchestrationOutcome.NoSourceFolders, result.Outcome);
    }

    [Fact]
    public void RunOnce_ReturnsDriveNotConnected_WhenSerialNotFound()
    {
        using var temp = new TempDirectory();
        var source = Directory.CreateDirectory(Path.Combine(temp.Path, "Desktop")).FullName;
        var scanner = new FakeDriveScanner();
        var orchestrator = new BackupOrchestrator(scanner, MakeLogger(temp.Path));
        var settings = new BackupSettings { SourceFolders = new List<string> { source }, TargetVolumeSerial = "MISSING" };

        var result = orchestrator.RunOnce(settings);

        Assert.Equal(OrchestrationOutcome.DriveNotConnected, result.Outcome);
    }

    [Fact]
    public void RunOnce_ReturnsSourceTargetOverlap_WhenTargetInsideSource()
    {
        using var temp = new TempDirectory();
        var source = Directory.CreateDirectory(Path.Combine(temp.Path, "Source")).FullName;
        var driveRoot = source; // deliberately overlapping: drive root equals the source folder
        var scanner = new FakeDriveScanner
        {
            Drives = new List<DriveInfoRecord>
            {
                new() { DriveLetter = driveRoot, VolumeSerial = "SER-1", FreeBytes = 1_000_000_000, TotalBytes = 1_000_000_000 }
            }
        };
        var orchestrator = new BackupOrchestrator(scanner, MakeLogger(temp.Path));
        var settings = new BackupSettings { SourceFolders = new List<string> { source }, TargetVolumeSerial = "SER-1" };

        var result = orchestrator.RunOnce(settings);

        Assert.Equal(OrchestrationOutcome.SourceTargetOverlap, result.Outcome);
    }

    [Fact]
    public void RunOnce_ReturnsInsufficientSpace_WhenFreeBytesTooLow()
    {
        using var temp = new TempDirectory();
        var source = Directory.CreateDirectory(Path.Combine(temp.Path, "Desktop")).FullName;
        File.WriteAllText(Path.Combine(source, "a.txt"), new string('x', 1000));
        var driveRoot = Directory.CreateDirectory(Path.Combine(temp.Path, "Drive")).FullName;
        var scanner = new FakeDriveScanner
        {
            Drives = new List<DriveInfoRecord>
            {
                new() { DriveLetter = driveRoot, VolumeSerial = "SER-1", FreeBytes = 10, TotalBytes = 1000 }
            }
        };
        var orchestrator = new BackupOrchestrator(scanner, MakeLogger(temp.Path));
        var settings = new BackupSettings { SourceFolders = new List<string> { source }, TargetVolumeSerial = "SER-1" };

        var result = orchestrator.RunOnce(settings);

        Assert.Equal(OrchestrationOutcome.InsufficientSpace, result.Outcome);
    }

    [Fact]
    public void RunOnce_RunsFullBackupAndCleansUp_WhenEverythingIsFine()
    {
        using var temp = new TempDirectory();
        var source = Directory.CreateDirectory(Path.Combine(temp.Path, "Desktop")).FullName;
        File.WriteAllText(Path.Combine(source, "a.txt"), "hello");
        var driveRoot = Directory.CreateDirectory(Path.Combine(temp.Path, "Drive")).FullName;
        var scanner = new FakeDriveScanner
        {
            Drives = new List<DriveInfoRecord>
            {
                new() { DriveLetter = driveRoot, VolumeSerial = "SER-1", FreeBytes = 1_000_000_000, TotalBytes = 1_000_000_000 }
            }
        };
        var orchestrator = new BackupOrchestrator(scanner, MakeLogger(temp.Path));
        var settings = new BackupSettings { SourceFolders = new List<string> { source }, TargetVolumeSerial = "SER-1", RetentionDays = 30 };

        var result = orchestrator.RunOnce(settings);

        Assert.Equal(OrchestrationOutcome.Success, result.Outcome);
        Assert.NotNull(result.BackupResult);
        Assert.Equal(1, result.BackupResult!.FilesCopied);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```powershell
$env:Path += ';C:\Program Files\dotnet'
Set-Location "C:\Users\ConceptsGroup\Desktop\Auto Backup"
dotnet test tests/AutoBackup.Core.Tests --filter BackupOrchestratorTests
```
Expected: FAIL to build — types don't exist yet.

- [ ] **Step 3: Implement the orchestrator**

`src/AutoBackup.Core/Orchestration/OrchestrationResult.cs`:
```csharp
using AutoBackup.Core.Models;

namespace AutoBackup.Core.Orchestration;

public enum OrchestrationOutcome
{
    Success,
    PartialSuccess,
    DriveNotConnected,
    InsufficientSpace,
    SourceTargetOverlap,
    NoSourceFolders
}

public class OrchestrationResult
{
    public required OrchestrationOutcome Outcome { get; init; }
    public BackupResult? BackupResult { get; init; }
    public required string Message { get; init; }
}
```

`src/AutoBackup.Core/Orchestration/BackupOrchestrator.cs`:
```csharp
using AutoBackup.Core.Backup;
using AutoBackup.Core.Drives;
using AutoBackup.Core.Logging;
using AutoBackup.Core.Models;

namespace AutoBackup.Core.Orchestration;

public class BackupOrchestrator
{
    private readonly IDriveScanner _driveScanner;
    private readonly BackupLogger _logger;

    public BackupOrchestrator(IDriveScanner driveScanner, BackupLogger logger)
    {
        _driveScanner = driveScanner;
        _logger = logger;
    }

    public OrchestrationResult RunOnce(BackupSettings settings)
    {
        var sourceFolders = settings.SourceFolders.Where(Directory.Exists).ToList();
        if (sourceFolders.Count == 0)
            return new OrchestrationResult { Outcome = OrchestrationOutcome.NoSourceFolders, Message = "没有有效的源文件夹" };

        if (string.IsNullOrEmpty(settings.TargetVolumeSerial))
        {
            LogSkip("尚未绑定备份硬盘");
            return new OrchestrationResult { Outcome = OrchestrationOutcome.DriveNotConnected, Message = "尚未绑定备份硬盘" };
        }

        var targetDrive = DriveIdentifier.FindBySerial(_driveScanner.GetReadyDrives(), settings.TargetVolumeSerial);
        if (targetDrive == null)
        {
            LogSkip("备份硬盘未连接");
            return new OrchestrationResult { Outcome = OrchestrationOutcome.DriveNotConnected, Message = "备份硬盘未连接" };
        }

        var backupRoot = SnapshotPathPlanner.GetBackupRoot(targetDrive.DriveLetter);
        if (OverlapChecker.HasOverlap(sourceFolders, backupRoot))
        {
            LogSkip("源文件夹与备份目标重叠，已阻止本次备份");
            return new OrchestrationResult { Outcome = OrchestrationOutcome.SourceTargetOverlap, Message = "源文件夹与备份目标重叠，已阻止本次备份" };
        }

        var estimated = SpaceChecker.EstimateSourceSizeBytes(sourceFolders, settings.CustomExcludePatterns);
        if (!SpaceChecker.HasEnoughSpace(estimated, targetDrive.FreeBytes))
        {
            LogSkip("硬盘剩余空间不足，已跳过本次备份");
            return new OrchestrationResult { Outcome = OrchestrationOutcome.InsufficientSpace, Message = "硬盘剩余空间不足，已跳过本次备份" };
        }

        var engine = new BackupEngine();
        var backupResult = engine.RunBackup(sourceFolders, targetDrive.DriveLetter, settings.CustomExcludePatterns);

        RetentionCleaner.CleanOldSnapshots(backupRoot, settings.RetentionDays, DateTime.Now);

        var outcome = backupResult.Outcome == BackupOutcome.Success ? OrchestrationOutcome.Success : OrchestrationOutcome.PartialSuccess;
        var message = $"复制 {backupResult.FilesCopied}，硬链接 {backupResult.FilesLinked}，失败 {backupResult.FilesFailed}";
        _logger.Append(new BackupLogEntry { Timestamp = DateTime.Now, Outcome = outcome.ToString(), Message = message });

        return new OrchestrationResult { Outcome = outcome, BackupResult = backupResult, Message = message };
    }

    private void LogSkip(string message) =>
        _logger.Append(new BackupLogEntry { Timestamp = DateTime.Now, Outcome = "Skipped", Message = message });
}
```

- [ ] **Step 4: Run tests to verify they pass**

```powershell
$env:Path += ';C:\Program Files\dotnet'
Set-Location "C:\Users\ConceptsGroup\Desktop\Auto Backup"
dotnet test tests/AutoBackup.Core.Tests --filter BackupOrchestratorTests
```
Expected: PASS (all 5 tests).

- [ ] **Step 5: Run the full Core test suite**

```powershell
$env:Path += ';C:\Program Files\dotnet'
Set-Location "C:\Users\ConceptsGroup\Desktop\Auto Backup"
dotnet test tests/AutoBackup.Core.Tests
```
Expected: PASS, all tests across every earlier task still green.

- [ ] **Step 6: Commit**

```bash
git add src/AutoBackup.Core/Orchestration tests/AutoBackup.Core.Tests/BackupOrchestratorTests.cs
git commit -m "Add backup orchestrator wiring drive check, overlap, space, engine and cleanup"
```

---

## Task 14: Settings and log viewer forms

**Files:**
- Create: `src/AutoBackup.App/SettingsForm.cs`
- Create: `src/AutoBackup.App/SettingsForm.Designer.cs`
- Create: `src/AutoBackup.App/LogViewerForm.cs`
- Create: `src/AutoBackup.App/LogViewerForm.Designer.cs`

**Interfaces:**
- Consumes: `BackupSettings` (Task 2), `DriveScanner`/`DriveIdentifier` (Task 7), `BackupLogger` (Task 10).
- Produces: `SettingsForm(BackupSettings current)` exposing `.Settings (BackupSettings)` after a `DialogResult.OK`; `LogViewerForm(BackupLogger logger)` (read-only viewer). Consumed by `TrayApplicationContext` (Task 15).

This task is UI construction — there is no automated test cycle for WinForms designer code. Build the forms, then verify manually as described in Step 3.

- [ ] **Step 1: Write SettingsForm**

`src/AutoBackup.App/SettingsForm.Designer.cs`:
```csharp
namespace AutoBackup.App;

partial class SettingsForm
{
    private System.ComponentModel.IContainer components = null;
    private ListBox _sourceListBox;
    private Button _addSourceButton;
    private Button _removeSourceButton;
    private Label _targetDriveLabel;
    private Button _changeDriveButton;
    private CheckedListBox _scheduleDaysListBox;
    private DateTimePicker _scheduleTimePicker;
    private NumericUpDown _retentionDaysUpDown;
    private ListBox _excludeListBox;
    private Button _addExcludeButton;
    private Button _removeExcludeButton;
    private CheckBox _startWithWindowsCheckBox;
    private Button _saveButton;
    private Button _cancelButton;
    private Button _backupNowButton;

    protected override void Dispose(bool disposing)
    {
        if (disposing && components != null) components.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        _sourceListBox = new ListBox { Left = 10, Top = 30, Width = 300, Height = 80 };
        var sourceLabel = new Label { Left = 10, Top = 10, Width = 200, Text = "备份来源：" };
        _addSourceButton = new Button { Left = 320, Top = 30, Width = 100, Text = "添加文件夹" };
        _removeSourceButton = new Button { Left = 320, Top = 60, Width = 100, Text = "删除选中" };

        _targetDriveLabel = new Label { Left = 10, Top = 120, Width = 400, Text = "备份目标：未设置" };
        _changeDriveButton = new Button { Left = 320, Top = 145, Width = 100, Text = "更改硬盘" };

        var scheduleLabel = new Label { Left = 10, Top = 180, Width = 200, Text = "备份计划（星期几）：" };
        _scheduleDaysListBox = new CheckedListBox { Left = 10, Top = 200, Width = 150, Height = 100 };
        _scheduleDaysListBox.Items.AddRange(new object[] { "周一", "周二", "周三", "周四", "周五", "周六", "周日" });
        var timeLabel = new Label { Left = 170, Top = 200, Width = 60, Text = "时间：" };
        _scheduleTimePicker = new DateTimePicker { Left = 170, Top = 220, Width = 100, Format = DateTimePickerFormat.Time, ShowUpDown = true };

        var retentionLabel = new Label { Left = 170, Top = 260, Width = 100, Text = "保留天数：" };
        _retentionDaysUpDown = new NumericUpDown { Left = 170, Top = 280, Width = 100, Minimum = 1, Maximum = 3650, Value = 30 };

        var excludeLabel = new Label { Left = 10, Top = 310, Width = 200, Text = "自定义排除清单：" };
        _excludeListBox = new ListBox { Left = 10, Top = 330, Width = 300, Height = 80 };
        _addExcludeButton = new Button { Left = 320, Top = 330, Width = 100, Text = "添加" };
        _removeExcludeButton = new Button { Left = 320, Top = 360, Width = 100, Text = "删除选中" };

        _startWithWindowsCheckBox = new CheckBox { Left = 10, Top = 420, Width = 200, Text = "开机自动启动" };

        _backupNowButton = new Button { Left = 10, Top = 450, Width = 100, Text = "立即备份一次" };
        _saveButton = new Button { Left = 250, Top = 450, Width = 80, Text = "保存", DialogResult = DialogResult.OK };
        _cancelButton = new Button { Left = 340, Top = 450, Width = 80, Text = "取消", DialogResult = DialogResult.Cancel };

        Text = "Auto Backup 设置";
        ClientSize = new Size(440, 500);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        AcceptButton = _saveButton;
        CancelButton = _cancelButton;

        Controls.AddRange(new Control[]
        {
            sourceLabel, _sourceListBox, _addSourceButton, _removeSourceButton,
            _targetDriveLabel, _changeDriveButton,
            scheduleLabel, _scheduleDaysListBox, timeLabel, _scheduleTimePicker,
            retentionLabel, _retentionDaysUpDown,
            excludeLabel, _excludeListBox, _addExcludeButton, _removeExcludeButton,
            _startWithWindowsCheckBox,
            _backupNowButton, _saveButton, _cancelButton
        });
    }
}
```

`src/AutoBackup.App/SettingsForm.cs`:
```csharp
using AutoBackup.Core.Drives;
using AutoBackup.Core.Models;
using AutoBackup.Core.Orchestration;
using AutoBackup.Core.Logging;
using CoreSettings = AutoBackup.Core.Settings.SettingsStore;

namespace AutoBackup.App;

public partial class SettingsForm : Form
{
    private static readonly DayOfWeek[] DayOrder =
    {
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
        DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday
    };

    private readonly IDriveScanner _driveScanner = new DriveScanner();
    private DriveInfoRecord? _selectedDrive;

    public BackupSettings Settings { get; private set; }

    public SettingsForm(BackupSettings current)
    {
        InitializeComponent();
        Settings = current;

        _sourceListBox.Items.AddRange(current.SourceFolders.ToArray());
        _targetDriveLabel.Text = string.IsNullOrEmpty(current.TargetVolumeLabel)
            ? "备份目标：未设置"
            : $"备份目标：{current.TargetVolumeLabel}（未连接时会在备份时提醒）";
        for (var i = 0; i < DayOrder.Length; i++)
            _scheduleDaysListBox.SetItemChecked(i, current.ScheduleDays.Contains(DayOrder[i]));
        _scheduleTimePicker.Value = DateTime.Today.Add(current.ScheduleTime.ToTimeSpan());
        _retentionDaysUpDown.Value = current.RetentionDays;
        _excludeListBox.Items.AddRange(current.CustomExcludePatterns.ToArray());
        _startWithWindowsCheckBox.Checked = current.StartWithWindows;

        _addSourceButton.Click += (_, _) =>
        {
            using var dialog = new FolderBrowserDialog();
            if (dialog.ShowDialog() == DialogResult.OK) _sourceListBox.Items.Add(dialog.SelectedPath);
        };
        _removeSourceButton.Click += (_, _) =>
        {
            if (_sourceListBox.SelectedItem != null) _sourceListBox.Items.Remove(_sourceListBox.SelectedItem);
        };
        _changeDriveButton.Click += (_, _) => PickTargetDrive();
        _addExcludeButton.Click += (_, _) =>
        {
            var pattern = Microsoft.VisualBasic.Interaction.InputBox("输入要排除的文件夹路径或文件名模式（如 *.log）：", "添加排除项");
            if (!string.IsNullOrWhiteSpace(pattern)) _excludeListBox.Items.Add(pattern);
        };
        _removeExcludeButton.Click += (_, _) =>
        {
            if (_excludeListBox.SelectedItem != null) _excludeListBox.Items.Remove(_excludeListBox.SelectedItem);
        };
        _backupNowButton.Click += (_, _) => RunBackupNow();
        _saveButton.Click += (_, _) => SaveIntoSettings();
    }

    private void PickTargetDrive()
    {
        var drives = _driveScanner.GetReadyDrives();
        if (drives.Count == 0)
        {
            MessageBox.Show("没有检测到可用磁盘。", "Auto Backup", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        using var picker = new Form { Text = "选择备份硬盘", Width = 400, Height = 200, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false };
        var list = new ListBox { Left = 10, Top = 10, Width = 360, Height = 100 };
        foreach (var drive in drives)
            list.Items.Add($"{drive.DriveLetter} {drive.VolumeLabel} ({drive.FreeBytes / (1024 * 1024 * 1024)} GB 可用)");
        var okButton = new Button { Left = 200, Top = 120, Width = 80, Text = "选择", DialogResult = DialogResult.OK };
        picker.Controls.Add(list);
        picker.Controls.Add(okButton);
        picker.AcceptButton = okButton;

        if (picker.ShowDialog(this) == DialogResult.OK && list.SelectedIndex >= 0)
        {
            _selectedDrive = drives[list.SelectedIndex];
            _targetDriveLabel.Text = $"备份目标：{_selectedDrive.VolumeLabel} ({_selectedDrive.DriveLetter})";
        }
    }

    private void RunBackupNow()
    {
        var settings = BuildSettingsFromForm();
        var logger = new BackupLogger(Path.Combine(Path.GetDirectoryName(CoreSettings.GetDefaultSettingsPath())!, "backup.log"));
        var orchestrator = new BackupOrchestrator(_driveScanner, logger);
        var result = orchestrator.RunOnce(settings);
        MessageBox.Show(result.Message, "备份结果", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void SaveIntoSettings() => Settings = BuildSettingsFromForm();

    private BackupSettings BuildSettingsFromForm()
    {
        var days = new List<DayOfWeek>();
        for (var i = 0; i < DayOrder.Length; i++)
            if (_scheduleDaysListBox.GetItemChecked(i)) days.Add(DayOrder[i]);

        return new BackupSettings
        {
            SourceFolders = _sourceListBox.Items.Cast<string>().ToList(),
            TargetVolumeSerial = _selectedDrive?.VolumeSerial ?? Settings.TargetVolumeSerial,
            TargetVolumeLabel = _selectedDrive?.VolumeLabel ?? Settings.TargetVolumeLabel,
            ScheduleDays = days,
            ScheduleTime = TimeOnly.FromDateTime(_scheduleTimePicker.Value),
            RetentionDays = (int)_retentionDaysUpDown.Value,
            CustomExcludePatterns = _excludeListBox.Items.Cast<string>().ToList(),
            StartWithWindows = _startWithWindowsCheckBox.Checked,
            LastRunAt = Settings.LastRunAt
        };
    }
}
```

- [ ] **Step 2: Write LogViewerForm**

`src/AutoBackup.App/LogViewerForm.Designer.cs`:
```csharp
namespace AutoBackup.App;

partial class LogViewerForm
{
    private System.ComponentModel.IContainer components = null;
    private TextBox _logTextBox;

    protected override void Dispose(bool disposing)
    {
        if (disposing && components != null) components.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        _logTextBox = new TextBox
        {
            Left = 0, Top = 0, Width = 600, Height = 400,
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            Font = new Font(FontFamily.GenericMonospace, 9)
        };
        Text = "备份日志";
        ClientSize = new Size(600, 400);
        Controls.Add(_logTextBox);
    }
}
```

`src/AutoBackup.App/LogViewerForm.cs`:
```csharp
using AutoBackup.Core.Logging;

namespace AutoBackup.App;

public partial class LogViewerForm : Form
{
    public LogViewerForm(BackupLogger logger)
    {
        InitializeComponent();
        var lines = logger.ReadRecent(200);
        _logTextBox.Text = lines.Count == 0 ? "（还没有备份记录）" : string.Join(Environment.NewLine, lines);
    }
}
```

- [ ] **Step 3: Build and manually verify**

```powershell
$env:Path += ';C:\Program Files\dotnet'
Set-Location "C:\Users\ConceptsGroup\Desktop\Auto Backup"
dotnet build
```
Expected: builds with 0 errors. (These two forms aren't wired into `Program.cs` yet — that happens in Task 15 — so there's nothing to run standalone yet; a full manual pass happens there.)

- [ ] **Step 4: Commit**

```bash
git add src/AutoBackup.App/SettingsForm.cs src/AutoBackup.App/SettingsForm.Designer.cs src/AutoBackup.App/LogViewerForm.cs src/AutoBackup.App/LogViewerForm.Designer.cs
git commit -m "Add settings and log viewer forms"
```

---

## Task 15: Tray application shell and scheduler wiring

**Files:**
- Modify: `src/AutoBackup.App/Program.cs`
- Create: `src/AutoBackup.App/TrayApplicationContext.cs`
- Create: `src/AutoBackup.Core/Settings/StartupRegistration.cs`

**Interfaces:**
- Consumes: `SingleInstanceGuard` (Task 12), `SettingsStore` (Task 2), `DriveScanner` (Task 7), `BackupLogger` (Task 10), `BackupScheduler`/`ScheduleConfig` (Task 11), `BackupOrchestrator`/`OrchestrationOutcome` (Task 13), `SettingsForm`/`LogViewerForm` (Task 14).
- Produces: `StartupRegistration.Apply(bool enabled)` (writes/removes a per-user `HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run` entry pointing at the running exe's path); the running application shell — no further tasks consume this directly, it's the composition root.

- [ ] **Step 1: Implement StartupRegistration**

`src/AutoBackup.Core/Settings/StartupRegistration.cs`:
```csharp
using Microsoft.Win32;

namespace AutoBackup.Core.Settings;

public static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "AutoBackup";

    public static void Apply(bool enabled, string executablePath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        if (key == null) return;

        if (enabled)
            key.SetValue(ValueName, $"\"{executablePath}\"");
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
```

- [ ] **Step 2: Replace Program.cs**

`src/AutoBackup.App/Program.cs`:
```csharp
using AutoBackup.Core.SingleInstance;

namespace AutoBackup.App;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        using var guard = new SingleInstanceGuard("AutoBackup_SingleInstance_Mutex_6F1C0B2E");
        if (!guard.IsFirstInstance)
        {
            MessageBox.Show("Auto Backup 已经在运行中，请查看系统托盘。", "Auto Backup", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext());
    }
}
```

- [ ] **Step 3: Write TrayApplicationContext**

`src/AutoBackup.App/TrayApplicationContext.cs`:
```csharp
using AutoBackup.Core.Drives;
using AutoBackup.Core.Logging;
using AutoBackup.Core.Models;
using AutoBackup.Core.Orchestration;
using AutoBackup.Core.Scheduling;
using AutoBackup.Core.Settings;

namespace AutoBackup.App;

public class TrayApplicationContext : ApplicationContext
{
    private readonly string _settingsPath = SettingsStore.GetDefaultSettingsPath();
    private readonly BackupLogger _logger;
    private readonly IDriveScanner _driveScanner = new DriveScanner();
    private readonly System.Threading.Timer _schedulerTimer;
    private readonly NotifyIcon _trayIcon;
    private BackupSettings _settings;

    public TrayApplicationContext()
    {
        _settings = SettingsStore.Load(_settingsPath);
        _logger = new BackupLogger(Path.Combine(Path.GetDirectoryName(_settingsPath)!, "backup.log"));

        var menu = new ContextMenuStrip();
        menu.Items.Add("立即备份", null, (_, _) => RunBackup(manualTrigger: true));
        menu.Items.Add("打开设置", null, OnOpenSettingsClicked);
        menu.Items.Add("查看备份日志", null, (_, _) => new LogViewerForm(_logger).ShowDialog());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, OnExitClicked);

        _trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = BuildTrayText(),
            Visible = true,
            ContextMenuStrip = menu
        };

        _schedulerTimer = new System.Threading.Timer(OnTimerTick, null, TimeSpan.Zero, TimeSpan.FromSeconds(60));
    }

    private void OnTimerTick(object? state)
    {
        var config = new ScheduleConfig { Days = _settings.ScheduleDays, Time = _settings.ScheduleTime };
        if (BackupScheduler.IsDueNow(config, DateTime.Now, _settings.LastRunAt))
        {
            RunBackup(manualTrigger: false);
        }
    }

    private void RunBackup(bool manualTrigger)
    {
        var orchestrator = new BackupOrchestrator(_driveScanner, _logger);
        var result = orchestrator.RunOnce(_settings);

        _settings.LastRunAt = DateTime.Now;
        SettingsStore.Save(_settings, _settingsPath);
        _trayIcon.Text = BuildTrayText();

        switch (result.Outcome)
        {
            case OrchestrationOutcome.Success:
                if (manualTrigger) _trayIcon.ShowBalloonTip(3000, "Auto Backup", "备份完成", ToolTipIcon.Info);
                break;
            case OrchestrationOutcome.PartialSuccess:
                _trayIcon.ShowBalloonTip(5000, "Auto Backup", $"备份部分完成：{result.Message}", ToolTipIcon.Warning);
                break;
            case OrchestrationOutcome.DriveNotConnected:
                _trayIcon.ShowBalloonTip(5000, "Auto Backup", "备份硬盘未连接，请插入后等待下次备份，或点击“立即备份”重试。", ToolTipIcon.Warning);
                break;
            case OrchestrationOutcome.InsufficientSpace:
                _trayIcon.ShowBalloonTip(5000, "Auto Backup", "备份硬盘剩余空间不足，本次备份已跳过。", ToolTipIcon.Warning);
                break;
            case OrchestrationOutcome.SourceTargetOverlap:
                _trayIcon.ShowBalloonTip(5000, "Auto Backup", "源文件夹与备份目标重叠，已阻止本次备份，请检查设置。", ToolTipIcon.Error);
                break;
            case OrchestrationOutcome.NoSourceFolders:
                if (manualTrigger) _trayIcon.ShowBalloonTip(5000, "Auto Backup", "还没有设置备份来源文件夹。", ToolTipIcon.Warning);
                break;
        }
    }

    private string BuildTrayText()
    {
        var last = _settings.LastRunAt.HasValue ? _settings.LastRunAt.Value.ToString("MM-dd HH:mm") : "从未";
        return $"Auto Backup - 上次备份: {last}";
    }

    private void OnOpenSettingsClicked(object? sender, EventArgs e)
    {
        using var form = new SettingsForm(_settings);
        if (form.ShowDialog() == DialogResult.OK)
        {
            _settings = form.Settings;
            SettingsStore.Save(_settings, _settingsPath);
            StartupRegistration.Apply(_settings.StartWithWindows, Application.ExecutablePath);
            _trayIcon.Text = BuildTrayText();
        }
    }

    private void OnExitClicked(object? sender, EventArgs e)
    {
        _schedulerTimer.Dispose();
        _trayIcon.Visible = false;
        Application.Exit();
    }
}
```

- [ ] **Step 4: Build the app**

```powershell
$env:Path += ';C:\Program Files\dotnet'
Set-Location "C:\Users\ConceptsGroup\Desktop\Auto Backup"
dotnet build
```
Expected: builds with 0 errors.

- [ ] **Step 5: Manually verify the tray shell**

```powershell
$env:Path += ';C:\Program Files\dotnet'
Set-Location "C:\Users\ConceptsGroup\Desktop\Auto Backup"
dotnet run --project src/AutoBackup.App
```
Checklist:
- A tray icon appears in the system tray.
- Right-click shows 立即备份 / 打开设置 / 查看备份日志 / 退出.
- 打开设置 opens `SettingsForm`; add a source folder, pick a drive (any local drive works for this smoke test), save.
- 立即备份 runs a backup and shows a result message box; check that an `AutoBackup` folder with a timestamped snapshot appeared on the chosen drive.
- 查看备份日志 shows the log entry just written.
- 退出 closes the app and removes the tray icon.
- Launch `dotnet run --project src/AutoBackup.App` a second time while the first is running: a "already running" message box appears and the second instance exits (single-instance guard working).
- Check the "开机自动启动" checkbox and save; confirm a value named `AutoBackup` appears under `HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run` (e.g. via `reg query "HKCU\Software\Microsoft\Windows\CurrentVersion\Run"`). Uncheck and save again; confirm the value is removed.

- [ ] **Step 6: Commit**

```bash
git add src/AutoBackup.App/Program.cs src/AutoBackup.App/TrayApplicationContext.cs src/AutoBackup.Core/Settings/StartupRegistration.cs
git commit -m "Wire tray shell, settings, scheduler, orchestrator and startup registration together"
```

---

## Task 16: Publish and final hardware integration test

**Files:**
- Create: `src/AutoBackup.App/AutoBackup.App.csproj` (modify publish-related properties)

**Interfaces:**
- Produces: a distributable self-contained single-file `.exe` under `publish/`.

- [ ] **Step 1: Add publish settings to the App project**

Open `src/AutoBackup.App/AutoBackup.App.csproj` and ensure the `<PropertyGroup>` includes:
```xml
<PublishSingleFile>true</PublishSingleFile>
<SelfContained>true</SelfContained>
<RuntimeIdentifier>win-x64</RuntimeIdentifier>
<IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
```

- [ ] **Step 2: Publish**

```powershell
$env:Path += ';C:\Program Files\dotnet'
Set-Location "C:\Users\ConceptsGroup\Desktop\Auto Backup"
dotnet publish src/AutoBackup.App/AutoBackup.App.csproj -c Release -o publish
```
Expected: a single `AutoBackup.App.exe` (plus a couple of support files) appears in `publish/`.

- [ ] **Step 3: Run the published exe standalone**

Double-click `publish/AutoBackup.App.exe` (or run it from PowerShell) on this machine and confirm the tray icon appears without any separate .NET install.

- [ ] **Step 4: Full manual hardware test (from the spec's testing section)**

With a real external drive plugged in:
1. Open 设置, add the real drive as target, save.
2. 立即备份 — check the snapshot folder and file contents on the drive are correct.
3. Modify one source file and delete another, then back up again — confirm the modified file was re-copied, the deleted file is absent from the new snapshot but still present in the previous one.
4. Unplug the drive and wait for (or simulate, by setting the schedule to the next minute) a scheduled backup — confirm a balloon notification appears instead of a crash.
5. Set 保留天数 to `1`, wait a day (or temporarily edit `settings.json`'s snapshot folder dates for a faster check), run cleanup via 立即备份, and confirm old snapshots are removed while the most recent one — and any data it shares via hardlink with a deleted snapshot — remains intact and readable.

- [ ] **Step 5: Commit**

```bash
git add src/AutoBackup.App/AutoBackup.App.csproj
git commit -m "Configure self-contained single-file publish"
```
