using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace IndifferentStars.Simulation;

internal static class ServerText
{
    private static readonly Dictionary<string, string> English = Load("en");
    private static readonly Dictionary<string, string> Russian = Load("ru");

    private static Dictionary<string, string> Load(string language)
    {
        var name = $"IndifferentStars.Simulation.Texts.{language}.json";
        using var stream = typeof(ServerText).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Missing text resource {name}.");
        using var reader = new StreamReader(stream);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(reader.ReadToEnd())
            ?? throw new InvalidOperationException($"Invalid text resource {name}.");
    }

    public static string Get(string language, string key, params object[] args)
    {
        var strings = language == "ru" ? Russian : English;
        return string.Format(CultureInfo.InvariantCulture, strings[key], args);
    }
}
