namespace FilesCollector.Core.Signatures;

public interface ISignatureExtractor
{
    bool CanHandle(string extension);

    SignatureExtractionResult Extract(string path, string source);
}
