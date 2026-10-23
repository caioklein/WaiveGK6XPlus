sealed class CheckDirectory : IDisposable
{
    readonly string artifactRoot;
    public string Path { get; }
    public CheckDirectory(string root)
    {
        artifactRoot=System.IO.Path.GetFullPath(System.IO.Path.Combine(root,"native","artifacts"));
        Path=System.IO.Path.Combine(artifactRoot,"checks-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }
    public void Dispose()
    {
        if(System.IO.Path.GetDirectoryName(Path)!=artifactRoot||!System.IO.Path.GetFileName(Path).StartsWith("checks-",StringComparison.Ordinal))throw new InvalidOperationException("Refusing to clean outside the generated check directory.");
        Directory.Delete(Path,true);
    }
}
