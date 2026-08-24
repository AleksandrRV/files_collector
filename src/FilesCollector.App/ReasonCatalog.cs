namespace FilesCollector.App;

public static class ReasonCatalog
{
    public static string Describe(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return string.Empty;
        }

        return reason switch
        {
            "access_denied" => "Access to the file is denied.",
            "file_unavailable" => "The file is unavailable.",
            "read_failed" => "The file could not be read.",
            "decode_failed" => "The file could not be decoded as text.",
            "binary_file" => "Binary content was detected while reading.",
            "binary_extension" => "Known binary extension; the binary file mode applies.",
            "size_limit" => "The file exceeds the maximum size limit.",
            "extension_disabled" => "The extension is disabled by formats settings.",
            "excluded_pattern" => "Matches an exclude pattern.",
            "not_included_pattern" => "Does not match any include pattern.",
            "hidden_file" => "Hidden files are excluded.",
            "system_file" => "System files are excluded.",
            "collection_mode_excluded" => "Excluded by a collection mode rule.",
            "signature_extractor_unavailable" => "No signatures extractor is available for this format.",
            "signature_extraction_failed" => "The signatures extractor failed to read the structure.",
            _ => reason
        };
    }

    public static string ShortName(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return "no reason";
        }

        return reason switch
        {
            "access_denied" => "access denied",
            "file_unavailable" => "file unavailable",
            "read_failed" => "read failed",
            "decode_failed" => "decode failed",
            "binary_file" => "binary content",
            "binary_extension" => "binary extension",
            "size_limit" => "size limit",
            "extension_disabled" => "extension disabled",
            "excluded_pattern" => "exclude pattern",
            "not_included_pattern" => "not in include patterns",
            "hidden_file" => "hidden file",
            "system_file" => "system file",
            "collection_mode_excluded" => "collection rule",
            "signature_extractor_unavailable" => "no extractor",
            "signature_extraction_failed" => "extractor failed",
            _ => reason
        };
    }
}
