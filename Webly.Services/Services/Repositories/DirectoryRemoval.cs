namespace Webly.Services.Services.Repositories;

/// <summary>
/// Deletes a directory that may hold a git repository, on any platform.
///
/// git writes its object and pack files read-only, and on Windows <see cref="Directory.Delete(string, bool)"/> refuses
/// a read-only file with <see cref="UnauthorizedAccessException"/> — so deleting a site there threw before its
/// repository was gone, and every test that made one failed in its teardown. Linux only asks whether the directory is
/// writable, which is why nothing had ever noticed. The attribute is cleared first, and only where there is one to
/// clear.
/// </summary>
public static class DirectoryRemoval
{
    public static void Delete(string path)
    {
        if (!Directory.Exists(path)) return;

        if (OperatingSystem.IsWindows())
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                var attributes = File.GetAttributes(file);

                if (attributes.HasFlag(FileAttributes.ReadOnly))
                    File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);
            }
        }

        Directory.Delete(path, recursive: true);
    }
}
