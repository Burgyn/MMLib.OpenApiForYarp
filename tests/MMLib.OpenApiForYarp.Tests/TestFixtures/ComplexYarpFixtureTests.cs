using System.Text.Json;

namespace MMLib.OpenApiForYarp.Tests.TestFixtures;

public class ComplexYarpFixtureTests
{
    [Fact]
    public void ComplexYarp_Fixture_IsWellFormed()
    {
        using JsonDocument doc = JsonDocument.Parse(FixtureLoader.ReadText("complex-yarp.json"));
        JsonElement reverseProxy = doc.RootElement.GetProperty("ReverseProxy");

        reverseProxy.GetProperty("Routes").EnumerateObject().Count().ShouldBe(3);
        reverseProxy.GetProperty("Clusters").EnumerateObject().Count().ShouldBe(3);
    }
}
