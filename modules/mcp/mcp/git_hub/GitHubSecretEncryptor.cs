// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
#pragma warning disable JCC11003
namespace McpToolDispatch;

/// <summary>
/// GitHub Secret 加密器 — 实现 libsodium crypto_box_seal（X25519 + blake2b + XSalsa20-Poly1305）
/// <para>用于 gh secret set：获取仓库公钥 → 加密 secret 值 → PUT API</para>
/// </summary>
internal static class GitHubSecretEncryptor {
    private static readonly BigInteger P = BigInteger.Parse("57896044618658097711785492504343953926634992332820282019728792003956564819949");

    /// <summary>
    /// 加密 secret 值（libsodium crypto_box_seal）
    /// </summary>
    /// <param name="recipientPublicKey">接收方公钥（32 bytes，从 GitHub API 获取的 base64 解码后）</param>
    /// <param name="plaintext">明文（secret 值的 UTF-8 bytes）</param>
    /// <returns>加密后的 sealed box（epk || ciphertext，base64 编码后传给 API）</returns>
    public static byte[] Seal(byte[] recipientPublicKey, byte[] plaintext) {
        var (ephemeralPublic, ephemeralPrivate) = GenerateX25519KeyPair();
        var nonce = Blake2b256(ephemeralPublic, recipientPublicKey, 24);
        var shared = X25519(ephemeralPrivate, recipientPublicKey);
        var ciphertext = XSalsa20Poly1305Encrypt(shared, nonce, plaintext);
        var sealedBox = new byte[ephemeralPublic.Length + ciphertext.Length];
        Buffer.BlockCopy(ephemeralPublic, 0, sealedBox, 0, ephemeralPublic.Length);
        Buffer.BlockCopy(ciphertext, 0, sealedBox, ephemeralPublic.Length, ciphertext.Length);
        return sealedBox;
    }

    /// <summary>
    /// 生成 X25519 密钥对
    /// </summary>
    private static (byte[] PublicKey, byte[] PrivateKey) GenerateX25519KeyPair() {
        using var ecdh = ECDiffieHellman.Create(ECCurve.CreateFromFriendlyName("curve25519"));
        var privateKey = ecdh.ExportParameters(true);
        var publicKey = ecdh.ExportParameters(false);
        return (publicKey.Q.X!, privateKey.D!);
    }

    /// <summary>
    /// X25519 标量乘法（ECDH 共享密钥计算）
    /// </summary>
    private static byte[] X25519(byte[] privateKey, byte[] publicKey) {
        using var ecdh1 = ECDiffieHellman.Create(ECCurve.CreateFromFriendlyName("curve25519"));
        var ecParams = new ECParameters {
            Curve = ECCurve.CreateFromFriendlyName("curve25519"),
            D = privateKey,
            Q = new ECPoint { X = publicKey, Y = ComputeYCoordinate(publicKey) }
        };
        ecdh1.ImportParameters(ecParams);
        using var ecdh2 = ECDiffieHellman.Create(ECCurve.CreateFromFriendlyName("curve25519"));
        var pubParams = new ECParameters {
            Curve = ECCurve.CreateFromFriendlyName("curve25519"),
            Q = new ECPoint { X = publicKey, Y = ComputeYCoordinate(publicKey) }
        };
        ecdh2.ImportParameters(pubParams);
        var shared = ecdh1.DeriveKeyMaterial(ecdh2.PublicKey);
        return shared;
    }

    /// <summary>
    /// 计算 Curve25519 公钥的 Y 坐标（从 X 坐标恢复）
    /// <para>Curve25519: y^2 = x^3 + 486662*x^2 + x (mod p)</para>
    /// </summary>
    private static byte[] ComputeYCoordinate(byte[] xBytes) {
        var x = BytesToBigInteger(xBytes);
        var x2 = Mod(x * x, P);
        var x3 = Mod(x2 * x, P);
        var y2 = Mod(x3 + Mod(486662 * x2, P) + x, P);
        var y = ModSqrt(y2, P);
        return BigIntegerToBytes(y);
    }

    /// <summary>
    /// XSalsa20-Poly1305 加密（NaCl crypto_box 的核心）
    /// <para>1. 用 XSalsa20 加密 32 bytes 零填充 + plaintext</para>
    /// <para>2. 密文前 32 bytes 作为 Poly1305 key</para>
    /// <para>3. Poly1305 计算剩余密文的 MAC（16 bytes）</para>
    /// </summary>
    private static byte[] XSalsa20Poly1305Encrypt(byte[] sharedKey, byte[] nonce, byte[] plaintext) {
        var combinedKey = new byte[32];
        Buffer.BlockCopy(sharedKey, 0, combinedKey, 0, Math.Min(32, sharedKey.Length));
        using var xsalsa20 = new XSalsa20(combinedKey);
        var paddedPlaintext = new byte[32 + plaintext.Length];
        Buffer.BlockCopy(plaintext, 0, paddedPlaintext, 32, plaintext.Length);
        var ciphertext = new byte[paddedPlaintext.Length];
        xsalsa20.Encrypt(paddedPlaintext, nonce, ciphertext);
        var macKey = new byte[32];
        Buffer.BlockCopy(ciphertext, 0, macKey, 0, 32);
        var mac = new byte[16];
        Poly1305.ComputeMac(macKey, ciphertext.AsSpan(32), mac);
        var result = new byte[16 + plaintext.Length];
        Buffer.BlockCopy(mac, 0, result, 0, 16);
        Buffer.BlockCopy(ciphertext, 32, result, 16, plaintext.Length);
        return result;
    }

    /// <summary>
    /// BLAKE2b-256 哈希（可变输出长度，用于生成 nonce）
    /// </summary>
    private static byte[] Blake2b256(byte[] input1, byte[] input2, int outputLength) {
        var combined = new byte[input1.Length + input2.Length];
        Buffer.BlockCopy(input1, 0, combined, 0, input1.Length);
        Buffer.BlockCopy(input2, 0, combined, input1.Length, input2.Length);
        return Blake2bCore(combined, outputLength);
    }

    /// <summary>
    /// BLAKE2b 核心实现（RFC 7693）
    /// </summary>
    private static byte[] Blake2bCore(byte[] input, int outputLength) {
        var h = new ulong[] {
            0x6a09e667f3bcc908, 0xbb67ae8584caa73b, 0x3c6ef372fe94f82b, 0xa54ff53a5f1d36f1,
            0x510e527fade682d1, 0x9b05688c2b3e6c1f, 0x1f83d9abfb41bd6b, 0x5be0cd19137e2179
        };
        h[0] ^= 0x01010000UL ^ (ulong)outputLength;
        ulong t0 = 0, t1 = 0;
        var blockSize = 128;
        var offset = 0;
        while (input.Length - offset > blockSize) {
            t0 += (ulong)blockSize;
            if (t0 < (ulong)blockSize) t1++;
            Compress(h, input, offset, t0, t1, false);
            offset += blockSize;
        }
        var lastBlock = new byte[blockSize];
        Buffer.BlockCopy(input, offset, lastBlock, 0, input.Length - offset);
        t0 += (ulong)(input.Length - offset);
        if (t0 < (ulong)(input.Length - offset)) t1++;
        Compress(h, lastBlock, 0, t0, t1, true);
        var result = new byte[outputLength];
        Buffer.BlockCopy(BitConverter.GetBytes(h[0]), 0, result, 0, Math.Min(8, outputLength));
        if (outputLength > 8) Buffer.BlockCopy(BitConverter.GetBytes(h[1]), 0, result, 8, Math.Min(8, outputLength - 8));
        if (outputLength > 16) Buffer.BlockCopy(BitConverter.GetBytes(h[2]), 0, result, 16, Math.Min(8, outputLength - 16));
        return result;
    }

    /// <summary>
    /// BLAKE2b 压缩函数
    /// </summary>
    private static void Compress(ulong[] h, byte[] block, int offset, ulong t0, ulong t1, bool last) {
        var v = new ulong[16];
        Array.Copy(h, 0, v, 0, 8);
        v[8] = 0x6a09e667f3bcc908; v[9] = 0xbb67ae8584caa73b;
        v[10] = 0x3c6ef372fe94f82b; v[11] = 0xa54ff53a5f1d36f1;
        v[12] = 0x510e527fade682d1 ^ t0; v[13] = 0x9b05688c2b3e6c1f ^ t1;
        v[14] = 0x1f83d9abfb41bd6b; v[15] = 0x5be0cd19137e2179;
        if (last) v[14] = ~v[14];
        var m = new ulong[16];
        for (var i = 0; i < 16; i++) m[i] = BitConverter.ToUInt64(block, offset + i * 8);
        G(v, 0, 4, 8, 12, m[0], m[1]); G(v, 1, 5, 9, 13, m[2], m[3]);
        G(v, 2, 6, 10, 14, m[4], m[5]); G(v, 3, 7, 11, 15, m[6], m[7]);
        G(v, 0, 5, 10, 15, m[8], m[9]); G(v, 1, 6, 11, 12, m[10], m[11]);
        G(v, 2, 7, 8, 13, m[12], m[13]); G(v, 3, 4, 9, 14, m[14], m[15]);
        G(v, 0, 4, 8, 12, m[14], m[10]); G(v, 1, 5, 9, 13, m[4], m[8]);
        G(v, 2, 6, 10, 14, m[9], m[15]); G(v, 3, 7, 11, 15, m[13], m[6]);
        G(v, 0, 5, 10, 15, m[1], m[12]); G(v, 1, 6, 11, 12, m[0], m[2]);
        G(v, 2, 7, 8, 13, m[11], m[7]); G(v, 3, 4, 9, 14, m[5], m[3]);
        G(v, 0, 4, 8, 12, m[11], m[8]); G(v, 1, 5, 9, 13, m[12], m[0]);
        G(v, 2, 6, 10, 14, m[5], m[2]); G(v, 3, 7, 11, 15, m[15], m[13]);
        G(v, 0, 5, 10, 15, m[10], m[14]); G(v, 1, 6, 11, 12, m[3], m[6]);
        G(v, 2, 7, 8, 13, m[7], m[1]); G(v, 3, 4, 9, 14, m[9], m[4]);
        G(v, 0, 4, 8, 12, m[7], m[9]); G(v, 1, 5, 9, 13, m[5], m[2]);
        G(v, 2, 6, 10, 14, m[3], m[10]); G(v, 3, 7, 11, 15, m[15], m[12]);
        G(v, 0, 5, 10, 15, m[1], m[13]); G(v, 1, 6, 11, 12, m[11], m[14]);
        G(v, 2, 7, 8, 13, m[8], m[6]); G(v, 3, 4, 9, 14, m[0], m[4]);
        G(v, 0, 4, 8, 12, m[6], m[11]); G(v, 1, 5, 9, 13, m[15], m[0]);
        G(v, 2, 6, 10, 14, m[13], m[5]); G(v, 3, 7, 11, 15, m[10], m[14]);
        G(v, 0, 5, 10, 15, m[3], m[12]); G(v, 1, 6, 11, 12, m[9], m[4]);
        G(v, 2, 7, 8, 13, m[2], m[7]); G(v, 3, 4, 9, 14, m[8], m[1]);
        for (var i = 0; i < 8; i++) h[i] ^= v[i] ^ v[i + 8];
    }

    private static void G(ulong[] v, int a, int b, int c, int d, ulong x, ulong y) {
        v[a] = v[a] + v[b] + x; v[d] = RotR64(v[d] ^ v[a], 32);
        v[c] = v[c] + v[d]; v[b] = RotR64(v[b] ^ v[c], 24);
        v[a] = v[a] + v[b] + y; v[d] = RotR64(v[d] ^ v[a], 16);
        v[c] = v[c] + v[d]; v[b] = RotR64(v[b] ^ v[c], 63);
    }

    private static ulong RotR64(ulong x, int n) => (x >> n) | (x << (64 - n));

    private static BigInteger Mod(BigInteger a, BigInteger m) => ((a % m) + m) % m;

    private static BigInteger ModSqrt(BigInteger a, BigInteger p) {
        var exp = (p + 1) / 4;
        return BigInteger.ModPow(a, exp, p);
    }

    private static BigInteger BytesToBigInteger(byte[] bytes) {
        var reversed = new byte[bytes.Length];
        Buffer.BlockCopy(bytes, 0, reversed, 0, bytes.Length);
        return new BigInteger(reversed);
    }

    private static byte[] BigIntegerToBytes(BigInteger n) {
        var bytes = n.ToByteArray();
        if (bytes.Length < 32) Array.Resize(ref bytes, 32);
        return bytes;
    }
}
