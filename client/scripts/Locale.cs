using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using Godot;

namespace IndifferentStars.Client;

public static class Locale
{
    public static string Language { get; private set; } = "ru";
    private static Dictionary<string, string> _strings = new();

    public static void Load(string language)
    {
        Language = language == "en" ? "en" : "ru";
        var path = language == "en" ? "res://locales/en.json" : "res://locales/ru.json";
        using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
        _strings = file is null ? new() : JsonSerializer.Deserialize<Dictionary<string, string>>(file.GetAsText()) ?? new();
    }

    public static string Text(string key) => _strings.GetValueOrDefault(key, key);
    public static string Format(string key, params object[] args) => string.Format(CultureInfo.InvariantCulture, Text(key), args);
}
