namespace OsuManiaMapAnalyser.Core;

internal static class AnalyzerResources
{
    private const string resource_prefix = "OsuManiaMapAnalyser.Core.Assets.";

    public static byte[] ReadBytes(string fileName)
    {
        using Stream stream = OpenStream(fileName);
        using var memoryStream = new MemoryStream();
        stream.CopyTo(memoryStream);
        return memoryStream.ToArray();
    }

    public static Stream OpenStream(string fileName)
    {
        string resourceName = resource_prefix + fileName;
        Stream? stream = typeof(AnalyzerResources).Assembly.GetManifestResourceStream(resourceName);

        if (stream != null)
            return stream;

        string availableResources = string.Join(", ", typeof(AnalyzerResources).Assembly.GetManifestResourceNames());
        throw new FileNotFoundException($"Embedded analyzer resource was not found: {resourceName}. Available: {availableResources}", fileName);
    }

    public static StreamReader OpenTextReader(string fileName)
        => new(OpenStream(fileName));
}
