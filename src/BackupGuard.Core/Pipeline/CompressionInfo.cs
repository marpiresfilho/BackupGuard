using System.Runtime.InteropServices;

namespace BackupGuard.Core.Pipeline;

/// <summary>Como o arquivo foi compactado (vai na mensagem do Telegram para revelar gargalos).</summary>
public sealed record CompressionInfo(int Level, int Threads)
{
    public static string LevelName(int level) => level switch
    {
        >= 9 => "Máxima",
        >= 7 => "Alta",
        >= 5 => "Normal",
        >= 1 => "Rápida",
        _ => "Sem compactação",
    };

    public string Description => $"{LevelName(Level)} · {Threads} {(Threads == 1 ? "núcleo" : "núcleos")}";
}

internal static class SystemMemory
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    /// <summary>RAM física livre agora (ou 4 GB se não for possível consultar).</summary>
    public static ulong AvailableBytes()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        return GlobalMemoryStatusEx(ref status) ? status.AvailPhys : 4UL << 30;
    }
}
