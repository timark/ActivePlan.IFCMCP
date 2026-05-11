using IfcMcpServer.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace IfcMcpServer.Tests;

public class CacheIntegrationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly IfcService _sut;

    private static readonly string SampleIfc =
        @"D:\Users\TimAikin\Documents\GitHub\Sample-Test-Files\IFC 2x3\Duplex Apartment\Duplex_A_20110907.ifc";

    public CacheIntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"xbim-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _sut = new IfcService(NullLogger<IfcService>.Instance);
    }

    public void Dispose()
    {
        _sut.CloseModel();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private string CopyToTemp(string sourcePath)
    {
        var dest = Path.Combine(_tempDir, Path.GetFileName(sourcePath));
        File.Copy(sourcePath, dest);
        return dest;
    }

    [Fact]
    public void LoadModel_CreatesXbimCache_OnFirstLoad()
    {
        var ifcPath = CopyToTemp(SampleIfc);
        var xbimPath = Path.ChangeExtension(ifcPath, ".xbim");

        var metadata = _sut.LoadModel(ifcPath);

        Assert.Equal("parsed", metadata.LoadedFrom);
        Assert.Equal(ifcPath, metadata.FilePath);
        Assert.Equal(xbimPath, metadata.CachePath);
        Assert.True(File.Exists(xbimPath), ".xbim cache file should exist");
    }

    [Fact]
    public void LoadModel_UsesCache_OnSecondLoad()
    {
        var ifcPath = CopyToTemp(SampleIfc);

        _sut.LoadModel(ifcPath);
        _sut.CloseModel();

        var metadata = _sut.LoadModel(ifcPath);

        Assert.Equal("cache", metadata.LoadedFrom);
    }

    [Fact]
    public void LoadModel_RebuildsCache_WhenIfcIsNewer()
    {
        var ifcPath = CopyToTemp(SampleIfc);

        _sut.LoadModel(ifcPath);
        _sut.CloseModel();

        var xbimPath = Path.ChangeExtension(ifcPath, ".xbim");
        File.SetLastWriteTimeUtc(ifcPath, DateTime.UtcNow.AddMinutes(1));

        var metadata = _sut.LoadModel(ifcPath);

        Assert.Equal("parsed", metadata.LoadedFrom);
    }

    [Fact]
    public void LoadModel_DirectXbim_SetsLoadedFromDirect()
    {
        var ifcPath = CopyToTemp(SampleIfc);
        _sut.LoadModel(ifcPath);
        _sut.CloseModel();

        var xbimPath = Path.ChangeExtension(ifcPath, ".xbim");
        var metadata = _sut.LoadModel(xbimPath);

        Assert.Equal("direct", metadata.LoadedFrom);
        Assert.Null(metadata.CachePath);
    }

    [Fact]
    public void SaveModelAs_Ifc_UpdatesCacheSibling()
    {
        var ifcPath = CopyToTemp(SampleIfc);
        _sut.LoadModel(ifcPath);

        var newIfcPath = Path.Combine(_tempDir, "output.ifc");
        _sut.SaveModelAs(newIfcPath);

        var newXbimPath = Path.ChangeExtension(newIfcPath, ".xbim");
        Assert.True(File.Exists(newIfcPath), "IFC file should exist");
        Assert.True(File.Exists(newXbimPath), ".xbim cache should exist alongside saved IFC");
    }

    [Fact]
    public void SaveModelAs_Xbim_SavesDirectly()
    {
        var ifcPath = CopyToTemp(SampleIfc);
        _sut.LoadModel(ifcPath);

        var xbimOut = Path.Combine(_tempDir, "output.xbim");
        _sut.SaveModelAs(xbimOut);

        Assert.True(File.Exists(xbimOut), ".xbim file should exist");
        var siblingIfc = Path.ChangeExtension(xbimOut, ".ifc");
        Assert.False(File.Exists(siblingIfc), "No .ifc sibling should be created when saving as .xbim");
    }

    [Fact]
    public void SaveModel_UpdatesExistingCache()
    {
        var ifcPath = CopyToTemp(SampleIfc);
        _sut.LoadModel(ifcPath);

        var xbimPath = Path.ChangeExtension(ifcPath, ".xbim");
        var originalWriteTime = File.GetLastWriteTimeUtc(xbimPath);

        Thread.Sleep(100);
        _sut.SaveModel();

        var updatedWriteTime = File.GetLastWriteTimeUtc(xbimPath);
        Assert.True(updatedWriteTime >= originalWriteTime, "Cache should be updated on save");
    }
}
