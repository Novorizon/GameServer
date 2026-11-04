using System.Text;

namespace GameServer.SchemaGenerator;

public static class GenerationWriter
{
    public static void Write(string outputDirectory, IReadOnlyList<GeneratedFile> files, bool force = false)
    {
        string root = Path.GetFullPath(outputDirectory);
        var pending = new List<(string Path, string Content)>();
        // Validate every destination before writing anything. Custom partial files are always preserved.
        foreach (var file in files)
        {
            if (Path.GetFileName(file.Name) != file.Name || file.Name is "." or "..")
                throw new SchemaGenerationException("Generated file name must not contain a path.");
            string path = Path.Combine(root, file.Name);
            if (File.Exists(path))
            {
                if (new FileInfo(path).LinkTarget != null) throw new SchemaGenerationException("Refusing to replace a symbolic link.");
                if (file.Handwritten) continue;
                string previous = File.ReadAllText(path);
                if (previous == file.Content) continue;
                if (!force || !previous.Replace("\r\n", "\n", StringComparison.Ordinal).StartsWith(CodeGenerator.Header + "\n", StringComparison.Ordinal))
                    throw new SchemaGenerationException($"'{file.Name}' already exists. --force replaces only files owned by this generator.");
            }
            else if (Directory.Exists(path)) throw new SchemaGenerationException($"'{file.Name}' is a directory.");
            pending.Add((path, file.Content));
        }
        Directory.CreateDirectory(root);
        foreach (var (path, content) in pending)
        {
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, content, new UTF8Encoding(false));
                File.Move(temporary, path, overwrite: true);
            }
            finally { File.Delete(temporary); }
        }
    }
}
