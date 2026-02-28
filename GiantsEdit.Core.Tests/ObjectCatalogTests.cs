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
            "1\tKabuto\tkb_l0.gbs",
            "11\tSmartie\tsm_L0.gbs",
            ""
        };

        var catalog = ObjectCatalog.LoadFromTsv(lines);

        Assert.AreEqual(2, catalog.Entries.Count);
        Assert.AreEqual(1, catalog.GetById(1).Count);
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
