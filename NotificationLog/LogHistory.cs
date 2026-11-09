using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NotificationLog;

internal sealed class LogEntry
{
    private const string Dim = "#999999";

    internal readonly string When;
    internal readonly string Type;
    internal readonly string Text;

    internal LogEntry(string when, string type, string text)
    {
        When = when;
        Type = type;
        Text = text;
    }

    // Text keeps the game's rich text tags, so it looks as it did on screen.
    internal string Format()
    {
        return Type.Length == 0
            ? $"<color={Dim}><i>{Text}</i></color>"
            : $"<color={Dim}>{When}</color>   <b>{Type}</b>   {Text}";
    }
}

// The game forgets a notification once it fades, so the mod keeps its own history in one
// tab-separated file, outside the saves. It isn't tied to a save slot: loading an older save leaves
// the later entries in place, below a marker.
internal static class LogHistory
{
    private static readonly List<LogEntry> entries = new List<LogEntry>();
    private static string file;

    internal static IReadOnlyList<LogEntry> Entries => entries;

    internal static int Capacity { get; private set; }

    // Counts every entry ever added, including ones since dropped, so a tab can tell what it hasn't shown.
    internal static int Total { get; private set; }

    internal static void Load(string path, int capacity)
    {
        file = path;
        Capacity = capacity;
        try
        {
            if (!File.Exists(path))
            {
                return;
            }
            string[] lines = File.ReadAllLines(path);
            string[] kept = lines.Skip(Math.Max(0, lines.Length - capacity)).ToArray();
            foreach (string line in kept)
            {
                string[] fields = line.Split('\t');
                if (fields.Length == 3)
                {
                    entries.Add(new LogEntry(fields[0], fields[1], fields[2]));
                }
            }
            Total = entries.Count;
            if (kept.Length < lines.Length)
            {
                File.WriteAllLines(path, kept);
            }
        }
        catch (Exception exception)
        {
            Plugin.Log.LogError(exception);
        }
    }

    internal static void Add(string type, string text)
    {
        Add(new LogEntry(GameTime(), Clean(type), Clean(text)));
    }

    internal static void AddMarker(string text)
    {
        Add(new LogEntry("", "", Clean(text)));
    }

    private static void Add(LogEntry entry)
    {
        entries.Add(entry);
        Total++;
        if (entries.Count > Capacity)
        {
            entries.RemoveAt(0);
        }
        File.AppendAllText(file, $"{entry.When}\t{entry.Type}\t{entry.Text}\n");
    }

    private static string GameTime()
    {
        if (TimeOfDayAzure.instance == null)
        {
            return "";
        }
        TimeAndDay time = TimeOfDayAzure.instance.currentTimeAndDay;
        return $"{time.day}.{time.month}.{time.year} {(int)time.hours:00}:{(int)time.minutes:00}";
    }

    private static string Clean(string value)
    {
        return (value ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
    }
}
