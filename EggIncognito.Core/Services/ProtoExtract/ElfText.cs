namespace EggIncognito.Core.Services.ProtoExtract;

public static class ElfText {
    public static bool TryFindText(byte[] bin, out int fileOff, out int size, out ulong vmAddr) {
        fileOff = 0;
        size = 0;
        vmAddr = 0;
        var s = MachoSections.Find(ElfSections.Read(bin), "", ".text");
        if (s is null) return false;
        if (s.Value.FileOff < 0 || s.Value.VmSize == 0 || s.Value.FileOff + (long)s.Value.VmSize > bin.Length) return false;
        fileOff = s.Value.FileOff;
        size = (int)s.Value.VmSize;
        vmAddr = s.Value.VmAddr;
        return true;
    }
}
