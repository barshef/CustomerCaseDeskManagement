namespace CaseDesk.Seeder;

/// <summary>Small console helper so every step reports in the same format.</summary>
public static class Log
{
    public static int Created { get; private set; }
    public static int Updated { get; private set; }
    public static int Unchanged { get; private set; }
    public static int Planned { get; private set; }
    public static int Warnings { get; private set; }

    public static void Section(string title)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"== {title}");
        Console.ResetColor();
    }

    public static void Create(string what) { Created++; Write(ConsoleColor.Green, "+ created  ", what); }
    public static void Update(string what) { Updated++; Write(ConsoleColor.Yellow, "~ updated  ", what); }
    public static void Same(string what) { Unchanged++; Write(ConsoleColor.DarkGray, "= unchanged", what); }
    public static void WouldDo(string what) { Planned++; Write(ConsoleColor.Magenta, "? dry-run  ", what); }
    public static void Warn(string what) { Warnings++; Write(ConsoleColor.DarkYellow, "! warning  ", what); }
    public static void Info(string what) => Console.WriteLine($"  {what}");

    public static void Error(string what)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine($"ERROR: {what}");
        Console.ResetColor();
    }

    public static void Summary(bool dryRun)
    {
        Section("Summary");
        Info($"created {Created}, updated {Updated}, unchanged {Unchanged}, warnings {Warnings}"
             + (dryRun ? $", would change {Planned} (dry run - nothing was written)" : ""));
    }

    private static void Write(ConsoleColor color, string tag, string what)
    {
        Console.ForegroundColor = color;
        Console.Write($"  {tag} ");
        Console.ResetColor();
        Console.WriteLine(what);
    }
}
