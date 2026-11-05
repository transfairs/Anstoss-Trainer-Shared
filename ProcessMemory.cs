using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace AnstossTrainer.Shared
{
    /// <summary>Holds the currently attached game process, its main module, and the handle/base address used to read/write its memory.</summary>
    public class MemProcess
    {
        /// <summary>The attached game process, or null if not attached.</summary>
        public Process Process { get; internal set; }
        /// <summary>The attached process's main executable module.</summary>
        public ProcessModule MainModule { get; internal set; }
        /// <summary>Handle used for reading and writing the process's memory.</summary>
        internal IntPtr Handle { get; set; }
        /// <summary>Base address of the main module.</summary>
        internal IntPtr ModuleBaseAddress { get; set; }
        /// <summary>Size in bytes of the main module's image.</summary>
        internal uint ModuleSize { get; set; }
    }

    /// <summary>
    /// Replaces the external libs\Memory.dll. Only implements the exact address-string dialect
    /// ("ModuleName+hexA,hexB") this project actually generates: hexA is the offset - from the
    /// module base - of a static pointer field, hexB is the offset within the struct it points to.
    /// Verified against the live game: dereferencing base+hexA and reading at (that pointer + hexB)
    /// reproduces the exact field values the original Memory.dll produced.
    /// Shared by all trainers; a trainer adds its game-specific state in its own partial part.
    /// </summary>
    public partial class ProcessMemory
    {
        private const uint ProcessAllAccess = 2035711u;

        /// <summary>Whether the trainer runs on Mono (e.g. Wine Mono under Wine/Proton) rather than .NET Framework.</summary>
        internal static readonly bool IsMono = Type.GetType("Mono.Runtime") != null;
        private const uint MinValidAddress = 0x10000;

        /// <summary>The currently attached game process and its module/handle info.</summary>
        public MemProcess mProc { get; } = new MemProcess();

        [DllImport("kernel32.dll", EntryPoint = "OpenProcess")]
        private static extern IntPtr OpenProcessNative(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll")]
        private static extern bool ReadProcessMemory(IntPtr hProcess, UIntPtr lpBaseAddress, [Out] byte[] lpBuffer, UIntPtr nSize, IntPtr lpNumberOfBytesRead);

        [DllImport("kernel32.dll")]
        private static extern bool WriteProcessMemory(IntPtr hProcess, UIntPtr lpBaseAddress, byte[] lpBuffer, UIntPtr nSize, IntPtr lpNumberOfBytesWritten);

        [DllImport("kernel32.dll")]
        private static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, UIntPtr dwSize, uint flAllocationType, uint flProtect);

        [DllImport("kernel32.dll")]
        private static extern bool VirtualProtectEx(IntPtr hProcess, UIntPtr lpAddress, UIntPtr dwSize, uint flNewProtect, out uint lpflOldProtect);

        [DllImport("kernel32.dll")]
        private static extern bool FlushInstructionCache(IntPtr hProcess, UIntPtr lpBaseAddress, UIntPtr dwSize);

        private const uint MemCommitReserve = 0x3000;
        private const uint PageExecuteReadWrite = 0x40;

        /// <summary>
        /// Allocates <paramref name="size"/> bytes of executable, writable memory inside the attached
        /// process and returns its address, or null if that fails (or the address doesn't fit the
        /// 32-bit game). Used only for the small code stub JobOfferInjector places there; the memory
        /// lives as long as the game process and is never freed.
        /// </summary>
        public uint? AllocateExecutable(int size)
        {
            IntPtr address = VirtualAllocEx(this.mProc.Handle, IntPtr.Zero, (UIntPtr)size, MemCommitReserve, PageExecuteReadWrite);
            long value = address.ToInt64();
            return value == 0 || value > uint.MaxValue ? (uint?)null : (uint)value;
        }

        /// <summary>
        /// Writes <paramref name="data"/> over existing code of the attached process: lifts the
        /// page protection for the write, restores it and flushes the instruction cache. Returns
        /// whether the bytes were written.
        /// </summary>
        public bool WriteCode(uint address, byte[] data)
        {
            if (address < MinValidAddress)
                return false;

            if (!VirtualProtectEx(this.mProc.Handle, (UIntPtr)address, (UIntPtr)data.Length, PageExecuteReadWrite, out uint oldProtect))
                return false;
            bool written = WriteProcessMemory(this.mProc.Handle, (UIntPtr)address, data, (UIntPtr)data.Length, IntPtr.Zero);
            VirtualProtectEx(this.mProc.Handle, (UIntPtr)address, (UIntPtr)data.Length, oldProtect, out _);
            FlushInstructionCache(this.mProc.Handle, (UIntPtr)address, (UIntPtr)data.Length);
            return written;
        }

        // Module base as a plain uint, for callers that need to build an address at runtime (e.g.
        // NamePoolResolver) instead of through the "Module+hexA,hexB" dialect ResolveAddress below
        // implements.
        /// <summary>The attached process's main module base address.</summary>
        public uint ModuleBase => (uint)this.mProc.ModuleBaseAddress.ToInt64();

        /// <summary>Size in bytes of the attached process's main module image.</summary>
        public uint ModuleSize => this.mProc.ModuleSize;

        /// <summary>Attaches to the process with the given PID, caching its handle, main module, and base address. Safe to call repeatedly for the same PID.</summary>
        public bool OpenProcess(int pid)
        {
            if (pid <= 0)
                return false;

            // Already attached to this exact process - cheap no-op, matches the original library's
            // behavior (callers rely on this being safe to call every tick).
            if (this.mProc.Process != null && this.mProc.Process.Id == pid)
                return true;

            Process process;
            try
            {
                process = Process.GetProcessById(pid);
            }
            catch (ArgumentException)
            {
                return false;
            }

            // Mono (Wine Mono, when the trainer runs under Wine/Proton) always reports "not
            // responding", even for a live game with a main window, so the check is .NET Framework only.
            if (!IsMono && !process.Responding)
                return false;

            IntPtr handle = OpenProcessNative(ProcessAllAccess, true, pid);
            if (handle == IntPtr.Zero)
                return false;

            this.mProc.Process = process;
            this.mProc.Handle = handle;
            this.mProc.MainModule = process.MainModule;
            this.mProc.ModuleBaseAddress = process.MainModule.BaseAddress;
            this.mProc.ModuleSize = (uint)process.MainModule.ModuleMemorySize;
            return true;
        }

        /// <summary>Reads a single byte at the given "Module+hexA,hexB" address, or 0 if it can't be read.</summary>
        public int ReadByte(string address)
        {
            byte[] bytes = ReadBytes(address, 1);
            return bytes != null ? bytes[0] : 0;
        }

        // Same null-safe fallback as ReadByte above, for the two other primitive sizes callers
        // read via BitConverter. Without this, an unreadable address (e.g. no savegame loaded,
        // so a club's memory region isn't backed yet) throws ArgumentNullException out of
        // BitConverter.ToInt32/ToUInt16 instead of just yielding a harmless default - which used
        // to crash the UI thread on manual reload and spam Warn logs during background refresh.
        /// <summary>Reads a 32-bit integer at the given "Module+hexA,hexB" address, or 0 if it can't be read.</summary>
        public int ReadInt32(string address)
        {
            byte[] bytes = ReadBytes(address, 4);
            return bytes != null ? BitConverter.ToInt32(bytes, 0) : 0;
        }

        /// <summary>Reads an unsigned 16-bit integer at the given "Module+hexA,hexB" address, or 0 if it can't be read.</summary>
        public ushort ReadUInt16(string address)
        {
            byte[] bytes = ReadBytes(address, 2);
            return bytes != null ? BitConverter.ToUInt16(bytes, 0) : (ushort)0;
        }

        /// <summary>Reads <paramref name="length"/> bytes at the given "Module+hexA,hexB" address, or null if it can't be read.</summary>
        public byte[] ReadBytes(string address, int length)
        {
            uint finalAddress = ResolveAddress(address);
            if (finalAddress < MinValidAddress)
                return null;

            byte[] buffer = new byte[length];
            if (!ReadProcessMemory(this.mProc.Handle, (UIntPtr)finalAddress, buffer, (UIntPtr)length, IntPtr.Zero))
                return null;

            return buffer;
        }

        /// <summary>Reads a null-terminated string of up to <paramref name="length"/> bytes at the given "Module+hexA,hexB" address, or "" if it can't be read.</summary>
        public string ReadString(string address, int length, Encoding stringEncoding)
        {
            byte[] bytes = ReadBytes(address, length);
            if (bytes == null)
                return "";

            return stringEncoding.GetString(bytes).Split('\0')[0];
        }

        /// <summary>Writes bytes to the given "Module+hexA,hexB" address; does nothing if the address can't be resolved.</summary>
        public void WriteBytes(string address, byte[] data)
        {
            uint finalAddress = ResolveAddress(address);
            if (finalAddress < MinValidAddress)
                return;

            WriteProcessMemory(this.mProc.Handle, (UIntPtr)finalAddress, data, (UIntPtr)data.Length, IntPtr.Zero);
        }

        // Reads/writes directly at an already-computed absolute address, bypassing ResolveAddress's
        // pointer chase. ResolveAddress always does *(moduleBase+hexA)+hexB - one dereference baked
        // into every AddressPresets entry - which doesn't fit addresses built at runtime from values
        // just read out of memory (e.g. the name pool: pool-base-pointer, dereference it, then walk
        // forward by a byte count computed on the fly). NamePoolResolver is the only current caller.
        /// <summary>Reads <paramref name="length"/> bytes directly at an already-computed absolute address (no module-relative pointer chase), or null if it can't be read.</summary>
        public byte[] ReadBytesAtAddress(uint address, int length)
        {
            if (address < MinValidAddress)
                return null;

            byte[] buffer = new byte[length];
            if (!ReadProcessMemory(this.mProc.Handle, (UIntPtr)address, buffer, (UIntPtr)length, IntPtr.Zero))
                return null;

            return buffer;
        }

        /// <summary>Writes bytes directly at an already-computed absolute address (no module-relative pointer chase); does nothing if the address is invalid.</summary>
        public void WriteBytesAtAddress(uint address, byte[] data)
        {
            if (address < MinValidAddress)
                return;

            WriteProcessMemory(this.mProc.Handle, (UIntPtr)address, data, (UIntPtr)data.Length, IntPtr.Zero);
        }

        /// <summary>Reads a null-terminated string of up to <paramref name="length"/> bytes directly at an already-computed absolute address, or "" if it can't be read.</summary>
        public string ReadStringAtAddress(uint address, int length, Encoding stringEncoding)
        {
            byte[] bytes = ReadBytesAtAddress(address, length);
            if (bytes == null)
                return "";

            return stringEncoding.GetString(bytes).Split('\0')[0];
        }

        /// <summary>Writes a string value; "string" is currently the only supported <paramref name="type"/>.</summary>
        public void WriteMemory(string address, string type, string value, Encoding stringEncoding)
        {
            if (type != "string")
                throw new ArgumentException($"ProcessMemory.WriteMemory only supports the \"string\" type, got \"{type}\".", nameof(type));

            WriteBytes(address, stringEncoding.GetBytes(value));
        }

        // "ModuleName+hexA,hexB" -> *(uint32*)(mainModuleBase + hexA) + hexB.
        // The module name itself is never resolved by lookup: every address this project generates
        // refers to its own already-attached main module, whose base address is cached in OpenProcess.
        private uint ResolveAddress(string address)
        {
            int plusIndex = address.IndexOf('+');
            int commaIndex = address.IndexOf(',');
            if (plusIndex < 0 || commaIndex < 0 || commaIndex < plusIndex)
                throw new FormatException($"Unsupported address format: \"{address}\". Expected \"Module+hexA,hexB\".");

            uint offsetA = ParseHex(address.Substring(plusIndex + 1, commaIndex - plusIndex - 1));
            uint offsetB = ParseHex(address.Substring(commaIndex + 1));

            uint moduleBase = (uint)this.mProc.ModuleBaseAddress.ToInt64();
            uint pointerFieldAddress = moduleBase + offsetA;

            byte[] pointerBytes = new byte[4];
            if (!ReadProcessMemory(this.mProc.Handle, (UIntPtr)pointerFieldAddress, pointerBytes, (UIntPtr)4, IntPtr.Zero))
                return 0;

            uint pointerValue = BitConverter.ToUInt32(pointerBytes, 0);
            // A null pointer means this chain is genuinely unresolved (e.g. no savegame loaded
            // yet) - report that as 0 rather than silently returning offsetB alone. offsetB is
            // typically well above MinValidAddress by itself (real struct offsets are commonly in
            // the 0x30000+ range), so without this check ReadBytes/WriteBytes would treat a raw,
            // unrelated low address as valid and attempt to read/write it instead of skipping.
            if (pointerValue == 0)
                return 0;

            return pointerValue + offsetB;
        }

        private static uint ParseHex(string token)
        {
            token = token.Trim();
            return token.Length == 0 ? 0u : Convert.ToUInt32(token, 16);
        }
    }
}
