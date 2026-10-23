namespace Origami.Tests;

public sealed class StoragePathsTests
{
    [Theory]
    [InlineData("uploads")]
    [InlineData("./uploads")]
    [InlineData("../uploads")]
    [InlineData("C:uploads")]
    public void Relative_root_is_rejected_without_creating_directories(string root)
    {
        using var directory = new TemporaryMediaStorage();
        var before = Directory.GetFileSystemEntries(directory.DirectoryPath, "*", SearchOption.AllDirectories);
        Assert.Throws<InvalidOperationException>(() => directory.CreateWithRoot(root));
        Assert.Equal(before, Directory.GetFileSystemEntries(directory.DirectoryPath, "*", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t")]
    public void Missing_root_uses_a_default_outside_the_application_directory(string? root)
    {
        using var directory = new TemporaryMediaStorage();
        var storage = directory.CreateWithRoot(root);
        Assert.True(Path.IsPathFullyQualified(storage.Root));
        Assert.NotEqual(directory.ApplicationRoot, storage.Root, StringComparer.OrdinalIgnoreCase);
        Assert.False(storage.Root.StartsWith(directory.ApplicationRoot, StringComparison.OrdinalIgnoreCase));
        Assert.True(Directory.Exists(storage.Images));
        Assert.True(Directory.Exists(storage.Staging));
        Assert.True(Directory.Exists(storage.Keys));
    }

    [Theory]
    [InlineData("root")]
    [InlineData("trailing-separator")]
    [InlineData("child")]
    [InlineData("normalized-child")]
    [InlineData("normalized-root")]
    public void Application_root_and_its_descendants_are_rejected_after_normalization(string scenario)
    {
        using var directory = new TemporaryMediaStorage();
        var root = scenario switch
        {
            "root" => directory.ApplicationRoot,
            "trailing-separator" => directory.ApplicationRoot + Path.DirectorySeparatorChar,
            "child" => Path.Combine(directory.ApplicationRoot, "uploads"),
            "normalized-child" => Path.Combine(directory.DirectoryPath, "outside", "..", "application", "uploads"),
            "normalized-root" => Path.Combine(directory.ApplicationRoot, "uploads", ".."),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        var before = Directory.GetFileSystemEntries(directory.DirectoryPath, "*", SearchOption.AllDirectories);
        Assert.Throws<InvalidOperationException>(() => directory.CreateWithRoot(root));
        Assert.Equal(before, Directory.GetFileSystemEntries(directory.DirectoryPath, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public void Root_comparison_respects_the_platform_case_sensitivity()
    {
        using var directory = new TemporaryMediaStorage();
        var root = Path.Combine(directory.DirectoryPath, "APPLICATION", "uploads");
        if (OperatingSystem.IsWindows())
        {
            Assert.Throws<InvalidOperationException>(() => directory.CreateWithRoot(root));
            Assert.Empty(Directory.GetFileSystemEntries(directory.ApplicationRoot));
        }
        else
        {
            Assert.Equal(Path.GetFullPath(root), directory.CreateWithRoot(root).Root);
        }
    }

    [Fact]
    public void External_absolute_root_creates_separate_directories_and_preserves_files_when_recreated()
    {
        using var directory = new TemporaryMediaStorage();
        var storage = directory.CreateWithRoot(Path.Combine(directory.Root, "unused", ".."));
        Assert.Equal(Path.GetFullPath(directory.Root), storage.Root);
        Assert.Equal(Path.Combine(storage.Root, "images"), storage.Images);
        Assert.Equal(Path.Combine(storage.Root, "staging"), storage.Staging);
        Assert.Equal(Path.Combine(storage.Root, "keys"), storage.Keys);
        Assert.Equal(3, Directory.GetDirectories(storage.Root).Length);
        var paths = new[] { storage.Images, storage.Staging, storage.Keys };
        foreach (var path in paths)
        {
            Assert.True(Directory.Exists(path));
            File.WriteAllBytes(Path.Combine(path, "existing.bin"), [1, 2, 3]);
        }

        var recreated = directory.Create();
        Assert.Equal(storage.Root, recreated.Root);
        Assert.Equal(storage.Images, recreated.Images);
        Assert.Equal(storage.Staging, recreated.Staging);
        Assert.Equal(storage.Keys, recreated.Keys);
        Assert.Equal(3, Directory.GetFiles(storage.Root, "*", SearchOption.AllDirectories).Length);
        foreach (var path in paths)
            Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(Path.Combine(path, "existing.bin")));
        Assert.Empty(Directory.GetFileSystemEntries(directory.ApplicationRoot));
    }

    [Fact]
    public void Sibling_with_application_directory_prefix_is_not_mistaken_for_a_descendant()
    {
        using var directory = new TemporaryMediaStorage();
        var root = directory.ApplicationRoot + "-storage";
        var storage = directory.CreateWithRoot(root);
        Assert.Equal(root, storage.Root);
        Assert.True(Directory.Exists(storage.Images));
        Assert.Empty(Directory.GetFileSystemEntries(directory.ApplicationRoot));
    }
}

// Both test classes own their entire temporary tree; no application configuration is read.
internal sealed class TemporaryMediaStorage : IDisposable
{
    public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "Origami.MediaTests-" + Guid.NewGuid().ToString("N"));
    public string ApplicationRoot => Path.Combine(DirectoryPath, "application");
    public string Root => Path.Combine(DirectoryPath, "persistent");

    public TemporaryMediaStorage() => Directory.CreateDirectory(ApplicationRoot);

    public StoragePaths Create() => CreateWithRoot(Root);

    public StoragePaths CreateWithRoot(string? root)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:RootPath"] = root })
            .Build();
        return new StoragePaths(configuration, new TestHostEnvironment { ContentRootPath = ApplicationRoot });
    }

    public void Dispose() => Directory.Delete(DirectoryPath, recursive: true);

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Origami.Tests";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
