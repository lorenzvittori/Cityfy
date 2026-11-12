namespace ServiceDefault.City;

// Parameter string of a "custom" map ("key=value; key=value"), shared by the generator and the
// gateway: the UI builds its form from the schema and never repeats the backend constants.

/// <summary>
/// Keys of the parameter string, in canonical order. Bases: canonical string of every preset
/// ("base=organic|grid" chooses which one the missing keys come from).
/// </summary>
public sealed record RecipeSchema(int MaxLength, IReadOnlyList<RecipeKey> Keys, IReadOnlyDictionary<string, string> Bases);

/// <summary>Type: <see cref="RecipeKeyTypes"/>. Values for "choice", Min/Max/Step for "number" and "integer".</summary>
public sealed record RecipeKey(string Key, string Type, IReadOnlyList<string>? Values, double? Min, double? Max, double? Step, bool Optional);

/// <summary>A problem of a parameter string (CITY_INVALID_RECIPE "problems"); Key empty for the whole string.</summary>
public sealed record RecipeProblem(string Key, string Code);

public static class RecipeKeyTypes
{
    public const string Choice = "choice";
    public const string Number = "number";
    public const string Integer = "integer";
}

public static class RecipeProblemCodes
{
    public const string Missing = "MISSING";
    public const string TooLong = "TOO_LONG";
    public const string Syntax = "SYNTAX";
    public const string UnknownKey = "UNKNOWN_KEY";
    public const string DuplicateKey = "DUPLICATE_KEY";
    public const string InvalidValue = "INVALID_VALUE";
    public const string OutOfRange = "OUT_OF_RANGE";
}
