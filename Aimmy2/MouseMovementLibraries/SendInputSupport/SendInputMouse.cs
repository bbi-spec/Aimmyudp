using System;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace MouseMovementLibraries.SendInputSupport
{
    internal class SendInputMouse
    {
        // --- CONFIGURATION ---
        // [IMPORTANT] CHANGE THIS IP to your Gaming PC's IP Address
        private const string SERVER_IP = "192.168.3.59"; 
        private const int SERVER_PORT = 5000;

        private static UdpClient _udpClient;
        private static IPEndPoint _endPoint;

        // Static constructor initializes the connection once when the app starts
        static SendInputMouse()
        {
            try
            {
                _udpClient = new UdpClient();
                // Setup the destination (Gaming PC)
                _endPoint = new IPEndPoint(IPAddress.Parse(SERVER_IP), SERVER_PORT);
            }
            catch
            {
                // Silently ignore setup errors to prevent crashing
            }
        }

        // Aimmy calls this function expecting to move the local mouse.
        // We intercept it and send a packet instead.
        public static void SendMouseCommand(uint MouseCommand, int x = 0, int y = 0)
        {
            if (_udpClient == null) return;

            try
            {
                // --- 1. HANDLE MOVEMENT ---
                // If Aimmy is trying to move the mouse (x or y is not 0)
                if (x != 0 || y != 0)
                {
                    // Create a 4-byte packet [Short X] [Short Y]
                    byte[] packet = new byte[4];
                    BitConverter.GetBytes((short)x).CopyTo(packet, 0);
                    BitConverter.GetBytes((short)y).CopyTo(packet, 2);

                    // Send immediately to PC 2
                    _udpClient.Send(packet, packet.Length, _endPoint);
                }

                // --- 2. HANDLE CLICKS ---
                // MOUSEEVENTF_LEFTDOWN is usually 0x0002.
                // We check if the command asks for a Left Click Down.
                if ((MouseCommand & 0x0002) != 0)
                {
                    byte[] clickPacket = Encoding.ASCII.GetBytes("CLICK");
                    _udpClient.Send(clickPacket, clickPacket.Length, _endPoint);
                }
            }
            catch
            {
                // Ignore network errors so the aimbot doesn't freeze/lag
            }
        }
    }
}
