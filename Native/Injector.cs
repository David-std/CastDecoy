using System;
using System.Runtime.InteropServices;

namespace CastDecoy;

internal static class Injector
{
    const uint PROCESS_CREATE_THREAD = 0x0002;
    const uint PROCESS_VM_OPERATION = 0x0008;
    const uint PROCESS_VM_WRITE = 0x0020;
    const uint PROCESS_VM_READ = 0x0010;
    const uint PROCESS_QUERY_INFORMATION = 0x0400;

    const uint MEM_COMMIT = 0x1000;
    const uint MEM_RESERVE = 0x2000;
    const uint MEM_RELEASE = 0x8000;

    const uint PAGE_EXECUTE_READWRITE = 0x40;

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr VirtualAllocEx(IntPtr hProc, IntPtr addr, uint size, uint type, uint protect);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool WriteProcessMemory(IntPtr hProc, IntPtr addr, byte[] buf, uint size, out uint written);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr CreateRemoteThread(IntPtr hProc, IntPtr attr, uint stack, IntPtr start, IntPtr param, uint flags, out uint tid);

    [DllImport("kernel32.dll")]
    static extern uint WaitForSingleObject(IntPtr handle, uint ms);

    [DllImport("kernel32.dll")]
    static extern bool VirtualFreeEx(IntPtr hProc, IntPtr addr, uint size, uint type);

    [DllImport("kernel32.dll")]
    static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
    static extern IntPtr GetModuleHandleA(string name);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
    static extern IntPtr GetProcAddress(IntPtr hModule, string name);

    public static bool SetAffinityRemote(uint processId, IntPtr hwnd, uint affinity)
    {
        IntPtr user32 = GetModuleHandleA("user32.dll");
        if (user32 == IntPtr.Zero) return false;

        IntPtr funcAddr = GetProcAddress(user32, "SetWindowDisplayAffinity");
        if (funcAddr == IntPtr.Zero) return false;

        uint access = PROCESS_CREATE_THREAD | PROCESS_VM_OPERATION
                    | PROCESS_VM_WRITE | PROCESS_VM_READ | PROCESS_QUERY_INFORMATION;

        IntPtr hProcess = OpenProcess(access, false, processId);
        if (hProcess == IntPtr.Zero) return false;

        try
        {
            return ExecuteRemote(hProcess, funcAddr, hwnd, affinity);
        }
        finally
        {
            CloseHandle(hProcess);
        }
    }

    private static bool ExecuteRemote(IntPtr hProcess, IntPtr funcAddr, IntPtr hwnd, uint affinity)
    {
        byte[] args = new byte[16];
        BitConverter.GetBytes(hwnd.ToInt64()).CopyTo(args, 0);
        BitConverter.GetBytes((long)affinity).CopyTo(args, 8);

        byte[] shellcode = new byte[]
        {
            0x48, 0x83, 0xEC, 0x28,
            0x48, 0x8B, 0x51, 0x08,
            0x48, 0x8B, 0x09,
            0x48, 0xB8, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0xFF, 0xD0,
            0x48, 0x83, 0xC4, 0x28,
            0xC3
        };

        BitConverter.GetBytes(funcAddr.ToInt64()).CopyTo(shellcode, 13);

        uint totalSize = (uint)(args.Length + shellcode.Length);
        IntPtr remoteMem = VirtualAllocEx(hProcess, IntPtr.Zero, totalSize,
                                          MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE);
        if (remoteMem == IntPtr.Zero) return false;

        try
        {
            if (!WriteProcessMemory(hProcess, remoteMem, args, (uint)args.Length, out _))
                return false;

            IntPtr codeAddr = IntPtr.Add(remoteMem, args.Length);
            if (!WriteProcessMemory(hProcess, codeAddr, shellcode, (uint)shellcode.Length, out _))
                return false;

            IntPtr hThread = CreateRemoteThread(hProcess, IntPtr.Zero, 0,
                                                codeAddr, remoteMem, 0, out _);
            if (hThread == IntPtr.Zero) return false;

            WaitForSingleObject(hThread, 3000);
            CloseHandle(hThread);
            return true;
        }
        finally
        {
            VirtualFreeEx(hProcess, remoteMem, 0, MEM_RELEASE);
        }
    }
}
