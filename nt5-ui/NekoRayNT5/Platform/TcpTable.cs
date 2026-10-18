using System;
using System.Runtime.InteropServices;

namespace NekoRayNT5.Platform
{
    // IPv4 TCP connection table of the system (iphlpapi GetTcpTable, Windows 2000 and later).
    internal static class TcpTable
    {
        private const int StateListen = 2;
        private const int ErrorInsufficientBuffer = 122;

        // True when some program listens on this TCP port on any IPv4 address.
        // False also when the table cannot be read; the caller then relies on its own bind attempt.
        public static bool IsListening(int port)
        {
            try
            {
                int size = 0;
                int result = GetTcpTable(IntPtr.Zero, ref size, false);
                if (result != ErrorInsufficientBuffer && result != 0)
                {
                    return false;
                }
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    size += 64 * 20;
                    IntPtr buffer = Marshal.AllocHGlobal(size);
                    try
                    {
                        int used = size;
                        result = GetTcpTable(buffer, ref used, false);
                        if (result == ErrorInsufficientBuffer)
                        {
                            size = used;
                            continue;
                        }
                        if (result != 0)
                        {
                            return false;
                        }
                        int count = Marshal.ReadInt32(buffer);
                        for (int i = 0; i < count; i++)
                        {
                            // MIB_TCPROW: state, local addr, local port, remote addr, remote port (5 DWORDs)
                            IntPtr row = new IntPtr(buffer.ToInt64() + 4 + (long)i * 20);
                            int state = Marshal.ReadInt32(row, 0);
                            int localPort = NetworkPort(Marshal.ReadInt32(row, 8));
                            if (state == StateListen && localPort == port)
                            {
                                return true;
                            }
                        }
                        return false;
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(buffer);
                    }
                }
            }
            catch (DllNotFoundException)
            {
            }
            catch (EntryPointNotFoundException)
            {
            }
            return false;
        }

        // The port is stored in network byte order in the low 16 bits.
        private static int NetworkPort(int value)
        {
            return ((value & 0xFF) << 8) | ((value >> 8) & 0xFF);
        }

        [DllImport("iphlpapi.dll")]
        private static extern int GetTcpTable(IntPtr table, ref int size, [MarshalAs(UnmanagedType.Bool)] bool order);
    }
}
