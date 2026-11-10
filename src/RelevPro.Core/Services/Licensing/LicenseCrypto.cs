using System.Security.Cryptography;
using System.Text;

namespace RelevPro.Core.Services.Licensing;

internal static class Yx4
{
    private static readonly byte[] A = { 0x4A,0x1F,0x6D,0x92,0x3B,0xC7,0x58,0xE1,0x0D,0x77,0xA4,0x2E,0x9C,0x63,0xF0,0x15,0x8B,0x2A,0x5D,0xE9,0x41,0xC6,0x0A,0x73,0xB8,0x3F,0x16,0xD2,0x6E,0x94,0x5C,0xA1 };
    private static readonly byte[] B = { 0x7C,0x1E,0x4B,0x9A,0x33,0xD6,0x58,0x02,0xE7,0x4F,0x8A,0x21,0x6D,0xB3,0x90,0x5E };
    private const byte C = 0x5A;

    public static byte[] E(byte[] p) { using var a = Aes.Create(); a.Key = A; a.IV = B; using var x = a.CreateEncryptor(); return x.TransformFinalBlock(p, 0, p.Length); }
    public static byte[] F(byte[] c) { using var a = Aes.Create(); a.Key = A; a.IV = B; using var x = a.CreateDecryptor(); return x.TransformFinalBlock(c, 0, c.Length); }

    public static string Z(string b64)
    {
        var d = Convert.FromBase64String(b64);
        for (var i = 0; i < d.Length; i++) d[i] ^= C;
        return Encoding.UTF8.GetString(d);
    }
}
