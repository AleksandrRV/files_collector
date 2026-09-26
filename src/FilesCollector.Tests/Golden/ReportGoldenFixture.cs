using System.Text;
using FilesCollector.Core;
using FilesCollector.Core.Inventory;
using FilesCollector.Core.Planning;
using FilesCollector.Core.Presets;
using FilesCollector.Core.Reporting;
using FilesCollector.Core.Rules;
using FilesCollector.Core.Signatures;
using FilesCollector.Extractors;
using FilesCollector.Infrastructure;
using FilesCollector.Infrastructure.FileSystem;
using FilesCollector.Infrastructure.Inventory;
using FilesCollector.Infrastructure.Reporting;

namespace FilesCollector.Tests.Golden;

/// <summary>
/// Builds a small project on disk that contains everything the report must handle
/// correctly — Git metadata, a submodule ".git" file, a hidden folder, binary files with
/// and without a known extension, a zlib stream, CRLF text, Windows-1251 and UTF-16 text,
/// Markdown with backticks — and runs the production pipeline on it:
/// inventory → plan → Markdown report + manifest.
/// </summary>
/// <remarks>
/// Every input is deterministic (fixed bytes, fixed clock, redacted root), so the
/// Markdown output can be compared byte for byte with <c>Golden/report.golden.md</c>.
/// The class has no test-framework dependency on purpose: it can also be run from a
/// console harness to regenerate the golden file.
/// </remarks>
public sealed class ReportGoldenFixture : IDisposable
{
    public static readonly DateTimeOffset FixedNow = new(2026, 9, 25, 18, 0, 0, TimeSpan.FromHours(7));

    public ReportGoldenFixture()
    {
        BaseDirectory = Path.Combine(Path.GetTempPath(), "FilesCollectorTests", "golden-" + Guid.NewGuid().ToString("N"));
        RootPath = Path.Combine(BaseDirectory, "project");
        ApplicationExecutablePath = Path.Combine(BaseDirectory, "files-collector", "FilesCollector.exe");
        LocalDataPath = Path.Combine(BaseDirectory, "local-app-data");
        Directory.CreateDirectory(Path.GetDirectoryName(ApplicationExecutablePath)!);
        CreateProject();
    }

    public string BaseDirectory { get; }

    public string RootPath { get; }

    public string ApplicationExecutablePath { get; }

    public string LocalDataPath { get; }

    /// <summary>
    /// Windows does not treat dot-names as hidden, so the folder gets the Hidden
    /// attribute there. On other systems the attribute cannot be set, but a leading dot
    /// makes .NET report the folder as hidden; the Markdown output is the same.
    /// </summary>
    public static string HiddenDirectoryName => OperatingSystem.IsWindows() ? "secret" : ".secret";

    public FileInventorySnapshot? Inventory { get; private set; }

    public CollectionPlan? Plan { get; private set; }

    public ReportGenerationResult? Result { get; private set; }

    public string Run()
    {
        var appPaths = new AppPaths(ApplicationExecutablePath, LocalDataPath);
        var fileSystem = new WindowsFileSystem();
        var inventoryStore = new JsonFileInventoryStore(appPaths, fileSystem);
        Inventory = inventoryStore.Refresh(RootPath, null, false, null, CancellationToken.None);

        var scanOptions = new ScanOptions
        {
            IncludeHidden = false,
            IncludeSystem = false,
            RedactRootPath = true,
            IncludeFileMetadataBlocks = true
        };
        var extensionRules = new[] { new ExtensionRule(".json", true, CollectionMode.Signatures) };
        Plan = new CollectionPlanner(fileSystem).CreatePlan(Inventory, new RuleSet(), extensionRules, scanOptions);

        var writer = new MarkdownReportWriter(appPaths, new FixedClock(FixedNow), new ISignatureExtractor[] { new StructuredDataSignatureExtractor() });
        var request = new ReportGenerationRequest(
            RootPath,
            "Golden",
            "Golden prefix",
            "Review the project.\r\nFocus on the public API.\r\n",
            scanOptions.RedactRootPath,
            scanOptions.IncludeFileMetadataBlocks,
            Plan);
        Result = writer.Write(request, null, CancellationToken.None);
        return File.ReadAllText(Result.ReportPath, Encoding.UTF8);
    }

    public string ReadManifest()
    {
        return Result is null
            ? throw new InvalidOperationException("Run the fixture first.")
            : File.ReadAllText(Result.ManifestPath, Encoding.UTF8);
    }

    public void Dispose()
    {
        if (!Directory.Exists(BaseDirectory))
        {
            return;
        }

        foreach (var directory in Directory.EnumerateDirectories(BaseDirectory, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(directory, FileAttributes.Directory);
        }

        Directory.Delete(BaseDirectory, true);
    }

    private void CreateProject()
    {
        // Git metadata: text files, a loose object (zlib) and the binary index. None of it
        // may reach the report, and the folder must not even be traversed.
        WriteText(".git/HEAD", "ref: refs/heads/main\n");
        WriteText(".git/config", "[core]\n\tbare = false\n[remote \"origin\"]\n\turl = https://example.invalid/demo.git\n");
        WriteBytes(".git/objects/3b/18e512dba79e4c8300dd08aeb37f8e728b8dad", ZlibLikeBytes(96, seed: 7));
        WriteBytes(".git/index", Concat("DIRC"u8.ToArray(), [0, 0, 0, 2, 0, 0, 0, 1], PseudoRandomBytes(64, seed: 11, allowZero: true)));

        // A submodule / linked worktree keeps a ".git" *file*; it is VCS metadata too.
        WriteText("vendor/lib/.git", "gitdir: ../../.git/modules/lib\n");
        WriteText("vendor/lib/lib.txt", "library\n");

        // The folder is hidden, the file inside is not: it must inherit the attribute.
        WriteText(HiddenDirectoryName + "/token.txt", "api-token=do-not-collect\n");
        if (OperatingSystem.IsWindows())
        {
            var hiddenDirectory = Path.Combine(RootPath, HiddenDirectoryName);
            File.SetAttributes(hiddenDirectory, File.GetAttributes(hiddenDirectory) | FileAttributes.Hidden);
        }

        // Binary data: a known binary extension, NUL-free noise and a zlib stream without
        // an extension (the old detector let the last two through as Windows-1251 text).
        WriteBytes("assets/logo.png", Concat([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], PseudoRandomBytes(40, seed: 3, allowZero: true)));
        WriteBytes("data/blob.bin", PseudoRandomBytes(512, seed: 5, allowZero: false));
        WriteBytes("data/loose-object", ZlibLikeBytes(180, seed: 9));

        // Text with CRLF line endings: every line of the report must end with LF.
        WriteText("src/Program.cs", "using System;\r\n\r\npublic static class Program\r\n{\r\n    public static void Main() => Console.WriteLine(\"Hi\");\r\n}\r\n");
        WriteText("README.md", "# Demo\r\n\r\n```bash\r\ndotnet run\r\n```\r\n");
        WriteText("config/settings.json", "{\r\n  \"name\": \"demo\",\r\n  \"ports\": [8080, 8081],\r\n  \"features\": { \"auth\": true }\r\n}\r\n");
        WriteText("empty.txt", string.Empty);

        // Legacy and UTF-16 text must still be recognised as text.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        WriteBytes("docs/legacy-1251.txt", Encoding.GetEncoding(1251).GetBytes("Привет, мир!\r\nЭто текст в кодировке Windows-1251.\r\n"));
        WriteBytes("docs/notes-utf16.txt", Concat([0xFF, 0xFE], Encoding.Unicode.GetBytes("UTF-16 заметка\n")));
    }

    private void WriteText(string relativePath, string content)
    {
        WriteBytes(relativePath, new UTF8Encoding(false).GetBytes(content));
    }

    private void WriteBytes(string relativePath, byte[] content)
    {
        var path = Path.Combine(RootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
    }

    /// <summary>A zlib header (0x78 0x01) followed by NUL-free, deflate-like noise.</summary>
    private static byte[] ZlibLikeBytes(int length, uint seed)
    {
        return Concat([0x78, 0x01], PseudoRandomBytes(length - 2, seed, allowZero: false));
    }

    /// <summary>
    /// Deterministic bytes from a fixed linear congruential generator, independent of the
    /// runtime's <see cref="Random"/> implementation.
    /// </summary>
    public static byte[] PseudoRandomBytes(int length, uint seed, bool allowZero)
    {
        var bytes = new byte[length];
        var state = seed;
        for (var index = 0; index < length; index++)
        {
            byte value;
            do
            {
                state = unchecked((state * 1664525u) + 1013904223u);
                value = (byte)(state >> 24);
            }
            while (!allowZero && value == 0);

            bytes[index] = value;
        }

        return bytes;
    }

    private static byte[] Concat(params byte[][] parts)
    {
        return parts.SelectMany(part => part).ToArray();
    }

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset now)
        {
            Now = now;
        }

        public DateTimeOffset Now { get; }
    }
}
