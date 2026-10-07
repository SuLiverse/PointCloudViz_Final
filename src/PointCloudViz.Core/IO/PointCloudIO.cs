using PointCloudViz.Core.Data;

namespace PointCloudViz.Core.IO;

/// <summary>格式注册表与文件读写入口。</summary>
public static class PointCloudIO
{
    public static IReadOnlyList<IPointCloudReader> Readers { get; } =
    [
        new LasReader(),
        new PlyReader(),
        new XyzReader(),
    ];

    public static IReadOnlyList<IPointCloudWriter> Writers { get; } =
    [
        new LasWriter(),
        new PlyWriter(),
        new XyzWriter(),
    ];

    public static IPointCloudReader? FindReader(string path) => Find(Readers, path, r => r.Extensions);

    public static IPointCloudWriter? FindWriter(string path) => Find(Writers, path, w => w.Extensions);

    public static bool CanRead(string path) => FindReader(path) is not null;

    /// <summary>所有可读取的扩展名，形如 <c>*.las;*.ply</c>。</summary>
    public static string ReadablePatterns => string.Join(";", Readers.SelectMany(r => r.Extensions).Select(e => "*" + e));

    /// <summary>打开文件对话框使用的过滤器字符串。</summary>
    public static string OpenFileFilter =>
        $"所有支持的点云|{ReadablePatterns}|" +
        string.Join("|", Readers.Select(r => $"{r.FormatName}|{string.Join(";", r.Extensions.Select(e => "*" + e))}")) +
        "|所有文件|*.*";

    /// <summary>保存文件对话框使用的过滤器字符串。</summary>
    public static string SaveFileFilter =>
        string.Join("|", Writers.Select(w => $"{w.FormatName}|{string.Join(";", w.Extensions.Select(e => "*" + e))}"));

    public static PointCloud Load(string path, PointCloudReadOptions? options = null,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        var reader = FindReader(path) ?? throw new NotSupportedException($"不支持的文件格式：{Path.GetExtension(path)}");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan);
        var cloud = reader.Read(stream, options, progress, cancellationToken);
        return cloud.WithName(Path.GetFileName(path));
    }

    public static Task<PointCloud> LoadAsync(string path, PointCloudReadOptions? options = null,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default) =>
        Task.Run(() => Load(path, options, progress, cancellationToken), cancellationToken);

    public static void Save(PointCloud cloud, string path,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        var writer = FindWriter(path) ?? throw new NotSupportedException($"不支持写出该格式：{Path.GetExtension(path)}");
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        // 先写临时文件再替换，避免写出中途失败破坏已有文件
        var temp = path + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
            {
                writer.Write(cloud, stream, progress, cancellationToken);
            }
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    public static Task SaveAsync(PointCloud cloud, string path,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default) =>
        Task.Run(() => Save(cloud, path, progress, cancellationToken), cancellationToken);

    private static T? Find<T>(IEnumerable<T> items, string path, Func<T, IReadOnlyList<string>> extensions) where T : class
    {
        var ext = Path.GetExtension(path);
        if (string.IsNullOrEmpty(ext)) return null;
        return items.FirstOrDefault(i => extensions(i).Contains(ext, StringComparer.OrdinalIgnoreCase));
    }
}
