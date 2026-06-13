using Microsoft.OpenApi;

namespace MMLib.OpenApiForYarp.Tests;

internal static class FixtureLoader
{
    private static string FixturesDirectory => Path.Combine(AppContext.BaseDirectory, "TestFixtures");

    public static string ReadText(string fileName) => File.ReadAllText(Path.Combine(FixturesDirectory, fileName));

    public static OpenApiDocument Load(string fileName) => OpenApiDocument.Parse(ReadText(fileName), "json").Document!;
}
