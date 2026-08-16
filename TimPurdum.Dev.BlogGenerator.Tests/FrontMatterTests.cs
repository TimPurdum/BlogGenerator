using TimPurdum.Dev.BlogGenerator.Compiler;

namespace TimPurdum.Dev.BlogGenerator.Tests;

[TestClass]
public sealed class FrontMatterTests
{
    [TestMethod]
    public void GetBool_ReturnsDefault_WhenKeyAbsent()
    {
        FrontMatter front = FrontMatter.Parse("title: Hello");
        Assert.IsFalse(front.GetBool("draft"));
    }

    [TestMethod]
    [DataRow("draft: true")]
    [DataRow("draft: True")]
    [DataRow("draft: yes")]
    [DataRow("draft: y")]
    [DataRow("draft: on")]
    [DataRow("draft: 1")]
    public void GetBool_IsTrue_ForYamlTruthyScalars(string yaml)
    {
        FrontMatter front = FrontMatter.Parse(yaml);
        Assert.IsTrue(front.GetBool("draft"), $"Expected '{yaml}' to read as true.");
    }

    [TestMethod]
    [DataRow("draft: false")]
    [DataRow("draft: no")]
    [DataRow("draft: 0")]
    [DataRow("draft:")]
    public void GetBool_IsFalse_ForYamlFalsyScalars(string yaml)
    {
        FrontMatter front = FrontMatter.Parse(yaml);
        Assert.IsFalse(front.GetBool("draft"), $"Expected '{yaml}' to read as false.");
    }

    [TestMethod]
    public void GetBool_ReturnsDefault_ForUnrecognizedValue()
    {
        FrontMatter front = FrontMatter.Parse("draft: maybe");
        Assert.IsFalse(front.GetBool("draft"));
    }
}
