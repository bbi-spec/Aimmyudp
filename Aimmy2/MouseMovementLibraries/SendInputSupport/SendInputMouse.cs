using System;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace MouseMovementLibraries.SendInputSupport
{
    internal class SendInputMouse
    {
        // --- CONFIGURATION ---
        // MUST MATCH YOUR PYTHON SERVER EXACTLY
        private const string SERVER_IP = "192.168.1.100"; // CHANGE THIS
        private const int SERVER_PORT = 5000;
        private const string FERNET_KEY = "6rQ32-s5w9D_g8L1z4M7v0P2k5N8j3X6y9R1c4B2m5A=";

        private static TcpClient _tcpClient;
        private static NetworkStream _stream;
        private static byte[] _signingKey;
        private static byte[] _encryptionKey;

        static SendInputMouse()
        {
            // 1. Decode the Fernet Key
            // Fernet keys are 32 bytes URL-Safe Base64
            // First 16 bytes = Signing Key, Last 16 bytes = Encryption Key
            byte[] fullKey = FromUrlSafeBase64(FERNET_KEY);
            _signingKey = fullKey.Take(16).ToArray();
            _encryptionKey = fullKey.Skip(16).Take(16).ToArray();

            Connect();
        }

        private static void Connect()
        {
            try
            {
                _tcpClient = new TcpClient();
                _tcpClient.NoDelay = true; // Match Python's TCP_NODELAY
                _tcpClient.Connect(SERVER_IP, SERVER_PORT);
                _stream = _tcpClient.GetStream();
            }
            catch { _tcpClient = null; }
        }

        public static void SendMouseCommand(uint MouseCommand, int x = 0, int y = 0)
        {
            try
            {
                // Auto-reconnect if connection dropped
                if (_tcpClient == null || !_tcpClient.Connected) Connect();
                if (_stream == null) return;

                // --- 1. PREPARE DATA ---
                string message = $"{x},{y}";
                if ((MouseCommand & 0x0002) != 0) message = "CLICK";
                else if (x == 0 && y == 0) return;

                // --- 2. ENCRYPT (FERNET SPEC) ---
                string token = EncryptFernet(message);

                // --- 3. SEND WITH DELIMITER ---
                // Your server splits by b'$'
                byte[] dataToSend = Encoding.UTF8.GetBytes(token + "$");
                _stream.Write(dataToSend, 0, dataToSend.Length);
            }
            catch
            {
                // If send fails, force reconnect next time
                if (_tcpClient != null) _tcpClient.Close();
                _tcpClient = null; 
            }
        }

        // --- MANUAL FERNET IMPLEMENTATION ---
        // Fernet Spec: Version(80) | Timestamp(64) | IV(128) | Ciphertext | HMAC(256)
        private static string EncryptFernet(string text)
        {
            byte[] version = { 0x80 };
            
            // Timestamp (Seconds since epoch, Big Endian Int64)
            long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            byte[] timeBytes = BitConverter.GetBytes(timestamp);
            if (BitConverter.IsLittleEndian) Array.Reverse(timeBytes);

            // IV (16 random bytes)
            byte[] iv = new byte[16];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(iv);

            // Encrypt Content (AES-128-CBC)
            byte[] cipherText;
            using (Aes aes = Aes.Create())
            {
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = _encryptionKey;
                aes.IV = iv;

                using (var encryptor = aes.CreateEncryptor())
                {
                    byte[] inputBytes = Encoding.UTF8.GetBytes(text);
                    cipherText = encryptor.TransformFinalBlock(inputBytes, 0, inputBytes.Length);
                }
            }

            // Combine Basic Parts for Signing
            // basic_parts = Version + Timestamp + IV + Ciphertext
            byte[] basicParts = new byte[1 + 8 + 16 + cipherText.Length];
            Buffer.BlockCopy(version, 0, basicParts, 0, 1);
            Buffer.BlockCopy(timeBytes, 0, basicParts, 1, 8);
            Buffer.BlockCopy(iv, 0, basicParts, 9, 16);
            Buffer.BlockCopy(cipherText, 0, basicParts, 25, cipherText.Length);

            // Generate HMAC-SHA256 Signature
            byte[] hmac;
            using (var hmacSha256 = new HMACSHA256(_signingKey))
            {
                hmac = hmacSha256.ComputeHash(basicParts);
            }

            // Final Packet = BasicParts + HMAC
            byte[] finalPacket = new byte[basicParts.Length + hmac.Length];
            Buffer.BlockCopy(basicParts, 0, finalPacket, 0, basicParts.Length);
            Buffer.BlockCopy(hmac, 0, finalPacket, basicParts.Length, hmac.Length);

            // Return URL-Safe Base64
            return ToUrlSafeBase64(finalPacket);
        }

        // Helper: Convert URL-Safe Base64 to Bytes
        private static byte[] FromUrlSafeBase64(string s)
        {
            string padded = s.Replace('-', '+').Replace('_', '/');
            switch (padded.Length % 4)
            {
                case 2: padded += "=="; break;
                case 3: padded += "="; break;
            }
            return Convert.FromBase64String(padded);
        }

        // Helper: Convert Bytes to URL-Safe Base64
        private static string ToUrlSafeBase64(byte[] bytes)
        {
            return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        }
    }
}