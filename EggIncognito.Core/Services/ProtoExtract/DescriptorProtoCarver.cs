using System.Text;
using Google.Protobuf.Reflection;

namespace EggIncognito.Core.Services.ProtoExtract;

public static class DescriptorProtoCarver {
    private static readonly string[] DescriptorFiles = ["ei.proto", "common.proto", "abb.proto"];

    public static IReadOnlyList<CarvedDescriptor> CarveAll(byte[] binary) {
        var found = new List<CarvedDescriptor>();
        if (binary is null || binary.Length < 16) return found;
        foreach (string name in DescriptorFiles) {
            int at = FindAnchor(binary, name);
            if (at < 0) continue;
            int len = WireWalkLength(binary, at);
            if (len <= 0) continue;
            byte[] bytes = new byte[len];
            Array.Copy(binary, at, bytes, 0, len);
            found.Add(new CarvedDescriptor(name, at, bytes));
        }

        return found;
    }

    public static string? EmitProto(byte[] fileDescriptorProtoBytes) {
        var fdp = TryParse(fileDescriptorProtoBytes);
        if (fdp is null) return null;
        var sb = new StringBuilder();
        EmitFile(fdp, sb);
        return sb.ToString();
    }

    public static string EmitProto(FileDescriptorProto fdp) {
        var sb = new StringBuilder();
        EmitFile(fdp, sb);
        return sb.ToString();
    }

    public static ExtractResult FromCarvedBase64(string? eiB64, string? commonB64, int? clientVersion) {
        byte[] ei;
        try {
            ei = Convert.FromBase64String(eiB64 ?? "");
        } catch {
            return new ExtractResult(false, null, "carved ei.proto descriptor is not valid base64", null, []);
        }

        byte[]? common = null;
        if (!string.IsNullOrEmpty(commonB64)) {
            try {
                common = Convert.FromBase64String(commonB64);
            } catch {
                common = null;
            }
        }

        return FromCarved(ei, common, clientVersion);
    }

    public static ExtractResult FromCarved(byte[] eiBytes, byte[]? commonBytes, int? clientVersion) {
        if (eiBytes is null || eiBytes.Length == 0)
            return new ExtractResult(false, null, "carved manifest missing ei.proto descriptor", null, []);

        string? eiText = EmitProto(eiBytes);
        if (eiText is null)
            return new ExtractResult(false, null, "carved ei.proto descriptor failed to parse", null, []);

        string? commonText = commonBytes is { Length: > 0 } ? EmitProto(commonBytes) : null;
        string proto = commonText is not null ? ProtoCleanup.Clean(eiText, commonText) : eiText;
        var norm = ProtoCanonicalForm.Normalize(proto);
        if (norm.Ok) proto = norm.Text;
        string sha = norm.Ok ? norm.Sha : EggIncognito.Core.ProtoHash.OfDescriptor(eiBytes);
        var messages = ProtoTextIndex.Names(proto);

        var eiFdp = TryParse(eiBytes) ?? new FileDescriptorProto();
        string diag =
            $"ei.proto (client-carved): {eiFdp.MessageType.Count} top-level messages, {eiFdp.EnumType.Count} enums"
            + (commonText is not null ? "; merged common.proto (aux)" : "; common.proto absent");
        return new ExtractResult(true, proto, diag, sha, messages, ClientVersion: clientVersion);
    }

    public static ExtractResult Extract(byte[] binary) {
        var carved = CarveAll(binary);
        var ei = carved.FirstOrDefault(c => c.Name == "ei.proto");
        if (ei is null) {
            return new ExtractResult(false, null,
                "no ei.proto descriptor found (not a descriptor-bearing Egg Inc binary?)", null, []);
        }

        var common = carved.FirstOrDefault(c => c.Name == "common.proto");
        string? eiText = EmitProto(ei.Bytes);
        if (eiText is null) {
            return new ExtractResult(false, null,
                $"ei.proto descriptor found at 0x{ei.FileOffset:X} but failed to parse", null, []);
        }

        string? commonText = common is not null ? EmitProto(common.Bytes) : null;

        string proto = commonText is not null ? ProtoCleanup.Clean(eiText, commonText) : eiText;
        var norm = ProtoCanonicalForm.Normalize(proto);
        if (norm.Ok) proto = norm.Text;
        string sha = norm.Ok ? norm.Sha : EggIncognito.Core.ProtoHash.OfDescriptor(ei.Bytes);
        var messages = ProtoTextIndex.Names(proto);

        var eiFdp = TryParse(ei.Bytes) ?? new FileDescriptorProto();
        string diag =
            $"ei.proto @0x{ei.FileOffset:X}: {eiFdp.MessageType.Count} top-level messages, {eiFdp.EnumType.Count} enums"
            + (common is not null ? "; merged common.proto (aux)" : "; common.proto absent");
        return new ExtractResult(true, proto, diag, sha, messages, ClientVersion: LibegincClientVersion.ReadFromBinary(binary));
    }

    private static FileDescriptorProto? TryParse(byte[] bytes) {
        try {
            return FileDescriptorProto.Parser.ParseFrom(bytes);
        } catch {
            return null;
        }
    }

    private static int FindAnchor(byte[] b, string protoName) {
        byte[] nb = Encoding.ASCII.GetBytes(protoName);
        byte[] pat = new byte[nb.Length + 2];
        pat[0] = 0x0A;
        pat[1] = (byte)nb.Length;
        Array.Copy(nb, 0, pat, 2, nb.Length);
        return IndexOf(b, pat);
    }

    internal static int WireWalkLength(byte[] b, int start) {
        int pos = start, lastGood = start;
        try {
            while (pos < b.Length) {
                int p = pos;
                ulong tag = ReadVarint(b, ref p);
                int fieldNum = (int)(tag >> 3);
                int wire = (int)(tag & 7);
                if (fieldNum is < 1 or > 12 || wire != 2) break;
                pos = p;
                ulong len = ReadVarint(b, ref pos);
                if (pos + (long)len > b.Length) break;
                pos += (int)len;
                lastGood = pos;
            }
        } catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentOutOfRangeException
                                         or InvalidDataException or OverflowException) {
            return lastGood - start;
        }

        return lastGood - start;
    }

    private static int IndexOf(byte[] hay, byte[] needle) {
        for (int i = 0; i <= hay.Length - needle.Length; i++) {
            bool match = true;
            for (int j = 0; j < needle.Length; j++) {
                if (hay[i + j] != needle[j]) {
                    match = false;
                    break;
                }
            }

            if (match) return i;
        }

        return -1;
    }

    private static ulong ReadVarint(byte[] b, ref int pos) {
        int shift = 0;
        ulong result = 0;
        while (true) {
            byte by = b[pos++];
            result |= (ulong)(by & 0x7F) << shift;
            if ((by & 0x80) == 0) break;
            shift += 7;
            if (shift > 63) throw new InvalidDataException("varint too long");
        }

        return result;
    }

    private static void EmitFile(FileDescriptorProto f, StringBuilder sb) {
        sb.Append("syntax = \"").Append(string.IsNullOrEmpty(f.Syntax) ? "proto2" : f.Syntax).Append("\";\n");
        if (!string.IsNullOrEmpty(f.Package)) sb.Append("\npackage ").Append(f.Package).Append(";\n");
        if (f.Dependency.Count > 0) {
            sb.Append('\n');
            foreach (string? dep in f.Dependency) sb.Append("import \"").Append(dep).Append("\";\n");
        }

        var symbols = CollectSymbols(f);
        string root = string.IsNullOrEmpty(f.Package) ? "" : "." + f.Package;
        foreach (var m in f.MessageType) {
            sb.Append('\n');
            EmitMessage(m, sb, 0, root, symbols);
        }

        foreach (var en in f.EnumType) {
            sb.Append('\n');
            EmitEnum(en, sb, 0);
        }
    }

    private static void EmitMessage(DescriptorProto m, StringBuilder sb, int indent, string parentScope, HashSet<string> symbols) {
        string pad = new(' ', indent * 4);
        string self = parentScope + "." + m.Name;
        sb.Append(pad).Append("message ").Append(m.Name).Append(" {\n");
        bool any = false;
        foreach (var en in m.EnumType) {
            if (any) sb.Append('\n');
            EmitEnum(en, sb, indent + 1);
            any = true;
        }

        foreach (var nested in m.NestedType) {
            if (any) sb.Append('\n');
            EmitMessage(nested, sb, indent + 1, self, symbols);
            any = true;
        }

        if (m.Field.Count > 0) {
            if (any) sb.Append('\n');
            string p2 = new(' ', (indent + 1) * 4);
            foreach (var fld in m.Field) {
                string label = fld.Label switch {
                    FieldDescriptorProto.Types.Label.Required => "required",
                    FieldDescriptorProto.Types.Label.Repeated => "repeated",
                    _ => "optional"
                };
                string def = fld.HasDefaultValue ? $" [default = {fld.DefaultValue}]" : "";
                sb.Append(p2).Append(label).Append(' ').Append(TypeName(fld, self, symbols)).Append(' ')
                    .Append(fld.Name).Append(" = ").Append(fld.Number).Append(def).Append(";\n");
            }
        }

        sb.Append(pad).Append("}\n");
    }

    private static void EmitEnum(EnumDescriptorProto e, StringBuilder sb, int indent) {
        string pad = new(' ', indent * 4);
        sb.Append(pad).Append("enum ").Append(e.Name).Append(" {\n");
        string p2 = new(' ', (indent + 1) * 4);
        foreach (var v in e.Value)
            sb.Append(p2).Append(v.Name).Append(" = ").Append(v.Number).Append(";\n");
        sb.Append(pad).Append("}\n");
    }

    private static HashSet<string> CollectSymbols(FileDescriptorProto f) {
        var symbols = new HashSet<string>(StringComparer.Ordinal);
        var rootSb = new StringBuilder();
        if (!string.IsNullOrEmpty(f.Package)) {
            foreach (string part in f.Package.Split('.')) {
                rootSb.Append('.').Append(part);
                symbols.Add(rootSb.ToString());
            }
        }

        string root = rootSb.ToString();
        foreach (var m in f.MessageType) AddMessageSymbols(m, root, symbols);
        foreach (var en in f.EnumType) symbols.Add(root + "." + en.Name);
        return symbols;
    }

    private static void AddMessageSymbols(DescriptorProto m, string scope, HashSet<string> symbols) {
        string self = scope + "." + m.Name;
        symbols.Add(self);
        foreach (var en in m.EnumType) symbols.Add(self + "." + en.Name);
        foreach (var nested in m.NestedType) AddMessageSymbols(nested, self, symbols);
    }

    private static string? Resolve(string scope, string type, HashSet<string> symbols) {
        string[] parts = type.Split('.');
        string current = scope;
        while (true) {
            string candidate = current + "." + parts[0];
            if (symbols.Contains(candidate)) {
                var fullSb = new StringBuilder(candidate);
                for (int i = 1; i < parts.Length; i++) {
                    fullSb.Append('.').Append(parts[i]);
                    if (!symbols.Contains(fullSb.ToString())) return null;
                }

                return fullSb.ToString();
            }

            if (current.Length == 0) return null;
            int cut = current.LastIndexOf('.');
            current = cut <= 0 ? "" : current[..cut];
        }
    }

    private static string TypeName(FieldDescriptorProto f, string scope, HashSet<string> symbols) {
        if (f.Type is not (FieldDescriptorProto.Types.Type.Message or FieldDescriptorProto.Types.Type.Enum)) {
            return ScalarName(f.Type);
        }

        string target = f.TypeName;
        if (string.IsNullOrEmpty(target) || target[0] != '.') return target.TrimStart('.');
        string[] segs = target[1..].Split('.');
        for (int i = segs.Length - 1; i >= 0; i--) {
            string candidate = string.Join('.', segs[i..]);
            if (Resolve(scope, candidate, symbols) == target) return candidate;
        }

        return target.TrimStart('.');
    }

    private static string ScalarName(FieldDescriptorProto.Types.Type t) {
        return t switch {
            FieldDescriptorProto.Types.Type.Double => "double",
            FieldDescriptorProto.Types.Type.Float => "float",
            FieldDescriptorProto.Types.Type.Int64 => "int64",
            FieldDescriptorProto.Types.Type.Uint64 => "uint64",
            FieldDescriptorProto.Types.Type.Int32 => "int32",
            FieldDescriptorProto.Types.Type.Fixed64 => "fixed64",
            FieldDescriptorProto.Types.Type.Fixed32 => "fixed32",
            FieldDescriptorProto.Types.Type.Bool => "bool",
            FieldDescriptorProto.Types.Type.String => "string",
            FieldDescriptorProto.Types.Type.Bytes => "bytes",
            FieldDescriptorProto.Types.Type.Uint32 => "uint32",
            FieldDescriptorProto.Types.Type.Sfixed32 => "sfixed32",
            FieldDescriptorProto.Types.Type.Sfixed64 => "sfixed64",
            FieldDescriptorProto.Types.Type.Sint32 => "sint32",
            FieldDescriptorProto.Types.Type.Sint64 => "sint64",
            _ => "bytes"
        };
    }

    public sealed record CarvedDescriptor(string Name, int FileOffset, byte[] Bytes);

    public sealed record ExtractResult(
        bool Ok,
        string? Proto,
        string Diagnostics,
        string? ProtoSha,
        IReadOnlyList<string> Messages,
        string? AppVersion = null,
        string? Build = null,
        int? ClientVersion = null);
}
