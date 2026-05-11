using IfcMcpServer.Services;
using Xbim.IO;

namespace IfcMcpServer.Tests;

public class CacheHelperTests
{
    [Theory]
    [InlineData(".ifc", true)]
    [InlineData(".IFC", true)]
    [InlineData(".ifcxml", true)]
    [InlineData(".ifczip", true)]
    [InlineData(".IFCZIP", true)]
    [InlineData(".xbim", false)]
    [InlineData(".txt", false)]
    [InlineData(".dwg", false)]
    public void IsCacheEligible_ReturnsCorrectly(string extension, bool expected)
    {
        var path = $@"C:\models\test{extension}";
        Assert.Equal(expected, IfcService.IsCacheEligible(path));
    }

    [Theory]
    [InlineData(".ifc", StorageType.Ifc)]
    [InlineData(".ifcxml", StorageType.IfcXml)]
    [InlineData(".ifczip", StorageType.IfcZip)]
    [InlineData(".xbim", StorageType.Xbim)]
    public void GetStorageType_ReturnsCorrectType(string extension, StorageType expected)
    {
        var path = $@"C:\models\test{extension}";
        Assert.Equal(expected, IfcService.GetStorageType(path));
    }

    [Fact]
    public void GetStorageType_ThrowsForUnsupportedExtension()
    {
        Assert.Throws<InvalidOperationException>(() =>
            IfcService.GetStorageType(@"C:\models\test.dwg"));
    }
}
