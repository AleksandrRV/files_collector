namespace FilesCollector.App;

public static class SizeFormatter
{
    public static string Format(long sizeBytes)
    {
        if (sizeBytes < 1024)
        {
            return $"{sizeBytes} B";
        }

        if (sizeBytes < 1024 * 1024)
        {
            return $"{sizeBytes / 1024d:F1} KB";
        }

        if (sizeBytes < 1024L * 1024 * 1024)
        {
            return $"{sizeBytes / 1024d / 1024d:F1} MB";
        }

        return $"{sizeBytes / 1024d / 1024d / 1024d:F1} GB";
    }

    public static string FormatCompact(long sizeBytes)
    {
        if (sizeBytes < 1024)
        {
            return $"{sizeBytes} B";
        }

        if (sizeBytes < 1024 * 1024)
        {
            return $"{sizeBytes / 1024d:0.#} KB";
        }

        if (sizeBytes < 1024L * 1024 * 1024)
        {
            return $"{sizeBytes / 1024d / 1024d:0.#} MB";
        }

        return $"{sizeBytes / 1024d / 1024d / 1024d:0.#} GB";
    }

    public static string FormatCount(int count)
    {
        return count.ToString("N0");
    }
}
