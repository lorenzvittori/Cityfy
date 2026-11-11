using System.Reflection;
using System.Text.RegularExpressions;
using ServiceDefault.Errors;

namespace ServiceDefault.Tests;

public class ErrorCodesTests
{
    private static readonly string[] AllCodes = typeof(ErrorCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.IsLiteral && f.FieldType == typeof(string))
        .Select(f => (string)f.GetRawConstantValue()!)
        .ToArray();

    [Fact]
    public void Codes_AreUnique_AndUpperSnakeCase()
    {
        Assert.Equal(AllCodes.Length, AllCodes.Distinct().Count());
        Assert.All(AllCodes, c => Assert.Matches("^[A-Z]+(_[A-Z]+)*$", c));
    }

    /// <summary>The UI catalog (ERRORI_UI.md, repo root) must document every code the backend can return.</summary>
    [Fact]
    public void EveryCode_IsDocumentedForTheUi()
    {
        var doc = File.ReadAllText(FindUp("ERRORI_UI.md"));

        var missing = AllCodes.Where(c => !Regex.IsMatch(doc, $"`{c}`")).ToList();

        Assert.True(missing.Count == 0, "Codes missing from ERRORI_UI.md: " + string.Join(", ", missing));
    }

    /// <summary>The frontend must have a message for every code (cityfy-web/src/app/core/errors/error-messages.ts).</summary>
    [Fact]
    public void EveryCode_HasAFrontendMessage()
    {
        var messages = File.ReadAllText(FindUp(Path.Combine("cityfy-web", "src", "app", "core", "errors", "error-messages.ts")));

        var missing = AllCodes.Where(c => !Regex.IsMatch(messages, $@"\b{c}\s*:")).ToList();

        Assert.True(missing.Count == 0, "Codes missing from error-messages.ts: " + string.Join(", ", missing));
    }

    private static string FindUp(string fileName)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, fileName);
            if (File.Exists(path)) return path;
        }
        throw new FileNotFoundException(fileName);
    }
}
