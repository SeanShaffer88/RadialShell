namespace RadialShell.App;

internal static class RadialActionSmokeTests
{
    public static void Run()
    {
        if (RadialActionCatalog.Labels.Count != 6 || RadialActionCatalog.Names.Count != 6)
            throw new InvalidOperationException("The radial wheel and action catalog must contain six aligned actions.");
        if (RadialActionCatalog.Names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != 6)
            throw new InvalidOperationException("Radial action names must be unique.");
        if (RadialActionExecutor.ToPosixPath(@"C:\Users\Ada\notes.md") != "/c/Users/Ada/notes.md")
            throw new InvalidOperationException("Drive paths must convert to /<drive>/ POSIX form.");
        if (RadialActionExecutor.ToPosixPath(@"\\server\share\notes.md") != "//server/share/notes.md")
            throw new InvalidOperationException("UNC paths must retain their network-root form.");
        if (RadialActionExecutor.GetName(@"C:\Users\Ada\") != "Ada")
            throw new InvalidOperationException("Names should be extracted from paths with trailing separators.");
        Console.WriteLine("PASS: six radial actions are registered and aligned with wheel slices.");
        Console.WriteLine("PASS: action path-formatting helpers cover drive, UNC, and folder paths.");
    }
}
