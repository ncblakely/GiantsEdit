using GiantsEdit.Core.DataModel;

namespace GiantsEdit.Core.Tests;

[TestClass]
public class ObjectCatalogTests
{
    [TestMethod]
    public void LoadFromTsv_ParsesEntries()
    {
        var lines = new[]
        {
            "# comment line",
            "1\txx_kabuto_flick\tkb (0)",
            "1\txx_kabutolev1\tkb (0..7)",
            "11\txx_smartie\tsm (0..7)",
            ""
        };

        var catalog = ObjectCatalog.LoadFromTsv(lines);

        Assert.AreEqual(3, catalog.Entries.Count);
        Assert.AreEqual(2, catalog.GetById(1).Count);
        Assert.AreEqual(1, catalog.GetById(11).Count);
        Assert.AreEqual(0, catalog.GetById(999).Count);
    }

    [TestMethod]
    public void GetById_ReturnsCorrectEntries()
    {
        var catalog = new ObjectCatalog();
        catalog.Add(new ObjectCatalogEntry(42, "TestObj", "model.gbs"));
        catalog.Add(new ObjectCatalogEntry(42, "TestObj2", "model2.gbs"));

        var entries = catalog.GetById(42);
        Assert.AreEqual(2, entries.Count);
        Assert.AreEqual("TestObj", entries[0].Name);
        Assert.AreEqual("TestObj2", entries[1].Name);
    }

    [TestMethod]
    public void LoadFromTsv_SkipsComments_And_EmptyLines()
    {
        var lines = new[]
        {
            "# header comment",
            "",
            "   ",
            "5\tobj\tmodel"
        };

        var catalog = ObjectCatalog.LoadFromTsv(lines);
        Assert.AreEqual(1, catalog.Entries.Count);
    }
}
