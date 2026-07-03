using System.Runtime.InteropServices;

namespace IcfEditor;

internal static class NativeCodec
{
    [DllImport("IcfCodec.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern uint icf_crc32(byte[] data, int length);

    [DllImport("IcfCodec.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int icf_crypt(byte[] input, int length, byte[] output, int encrypt);

    internal static uint Crc32(ReadOnlySpan<byte> data)
    {
        byte[] buffer = data.ToArray();
        return icf_crc32(buffer, buffer.Length);
    }

    internal static byte[] Crypt(byte[] input, bool encrypt)
    {
        byte[] output = new byte[input.Length];
        int result = icf_crypt(input, input.Length, output, encrypt ? 1 : 0);
        if (result != 0) throw new InvalidOperationException($"原生编解码器执行失败：0x{result:X8}");
        return output;
    }
}
