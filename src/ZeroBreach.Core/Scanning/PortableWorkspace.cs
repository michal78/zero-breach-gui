namespace ZeroBreach.Core.Scanning;
public static class PortableWorkspace
{
    public static IDisposable Acquire(string root)
    {
        if (Path.GetFullPath(root).StartsWith(@"\\")) throw new IOException("Move ZeroBreach.exe to a writable local folder before starting.");
        Directory.CreateDirectory(root); RejectReparse(root);
        try { return new FileStream(Path.Combine(root,"application.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None); }
        catch (IOException ex) { throw new IOException("Another Zero Breach instance may be using this folder. Close it and retry, or move the executable to another writable local folder.",ex); }
        catch (UnauthorizedAccessException ex) { throw new IOException("Zero Breach needs a writable local folder beside the executable. Move ZeroBreach.exe to a folder you can write to.",ex); }
    }
    public static void RejectReparse(string path)
    {
        for (var current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidDataException("Runtime and scanner storage must not use redirected folders or links: " + current);
    }
}
