namespace EggIncognito.Data.Models;

public interface IBlobRow {
    long Id { get; }

    string Sha256 { get; }

    byte[]? Bytes { get; set; }

    long ByteSize { get; }
}
