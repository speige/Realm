using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

public enum NatType
{
    Open,
    FullCone,
    RestrictedCone,
    PortRestrictedCone,
    Symmetric
}

public class NatTypeTester
{
    public class StunResult
    {
        public bool Success { get; set; }
        public IPEndPoint LocalEndPoint { get; set; }
        public IPEndPoint MappedEndPoint { get; set; }
        public IPEndPoint ChangedEndPoint { get; set; }
    }

    public static async Task<NatType> DetermineNatTypeAsync(int testLocalPort = 8999)
    {
        try
        {
            // 1. Resolve stun.l.google.com IPs
            var stunAddresses = await Dns.GetHostAddressesAsync("stun.l.google.com");
            if (stunAddresses.Length == 0)
            {
                // Fallback to open if DNS is down/unavailable
                return NatType.Open;
            }

            // We need 2 distinct IP addresses of the STUN servers for full tests.
            // If DNS returns only one, we can use a hardcoded fallback STUN server as well.
            var serverIp1 = stunAddresses[0];
            var serverIp2 = stunAddresses.Length > 1 ? stunAddresses[1] : IPAddress.Parse("74.125.200.127"); // google stun alternate

            // Test I: Query STUN server 1
            var test1 = await QueryStunAsync(serverIp1, 19302, testLocalPort);
            if (!test1.Success)
            {
                // If STUN fails, assume Symmetric/Restricted (safe fallback) or Open for local testing
                return NatType.RestrictedCone;
            }

            // If mapped endpoint matches local endpoint, we are directly connected (Open)
            if (test1.MappedEndPoint.Address.Equals(test1.LocalEndPoint.Address) && 
                test1.MappedEndPoint.Port == test1.LocalEndPoint.Port)
            {
                // Perform Test II to verify if firewall blocks incoming
                var test2Open = await QueryStunWithChangeRequestAsync(serverIp1, 19302, testLocalPort, changeIP: true, changePort: true);
                if (test2Open.Success)
                {
                    return NatType.Open;
                }
                else
                {
                    return NatType.Symmetric; // Symmetric UDP Firewall
                }
            }

            // Test II: Change IP and Port
            var test2 = await QueryStunWithChangeRequestAsync(serverIp1, 19302, testLocalPort, changeIP: true, changePort: true);
            if (test2.Success)
            {
                return NatType.FullCone;
            }

            // If Test II fails, we are behind some NAT. Check if Symmetric or Cone.
            // We do this by sending Test I to Server 2 (different IP) from the SAME local port.
            var test1Alt = await QueryStunAsync(serverIp2, 19302, testLocalPort);
            if (test1Alt.Success)
            {
                // If mapped port or IP changed when talking to a different STUN IP, it's Symmetric NAT.
                if (!test1.MappedEndPoint.Address.Equals(test1Alt.MappedEndPoint.Address) || 
                    test1.MappedEndPoint.Port != test1Alt.MappedEndPoint.Port)
                {
                    return NatType.Symmetric;
                }
            }

            // Test III: Change Port only
            var test3 = await QueryStunWithChangeRequestAsync(serverIp1, 19302, testLocalPort, changeIP: false, changePort: true);
            if (test3.Success)
            {
                return NatType.RestrictedCone;
            }
            
            return NatType.PortRestrictedCone;
        }
        catch (Exception ex)
        {
            Godot.GD.PrintErr($"[NatTypeTester] Error checking NAT: {ex.Message}");
            return NatType.PortRestrictedCone; // Safe default
        }
    }

    private static async Task<StunResult> QueryStunAsync(IPAddress serverIp, int serverPort, int localPort)
    {
        var result = new StunResult();
        using var udp = new UdpClient();
        udp.ExclusiveAddressUse = false;
        udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        
        try
        {
            udp.Client.Bind(new IPEndPoint(IPAddress.Any, localPort));
            result.LocalEndPoint = (IPEndPoint)udp.Client.LocalEndPoint!;
            
            byte[] request = CreateStunBindingRequest();
            var serverEp = new IPEndPoint(serverIp, serverPort);
            
            await udp.SendAsync(request, request.Length, serverEp);
            
            var receiveTask = udp.ReceiveAsync();
            var timeoutTask = Task.Delay(1000);
            
            if (await Task.WhenAny(receiveTask, timeoutTask) == receiveTask)
            {
                var response = await receiveTask;
                return ParseStunResponse(response.Buffer, result);
            }
        }
        catch { /* Ignore errors and return success=false */ }
        
        result.Success = false;
        return result;
    }

    private static async Task<StunResult> QueryStunWithChangeRequestAsync(IPAddress serverIp, int serverPort, int localPort, bool changeIP, bool changePort)
    {
        var result = new StunResult();
        using var udp = new UdpClient();
        udp.ExclusiveAddressUse = false;
        udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        
        try
        {
            udp.Client.Bind(new IPEndPoint(IPAddress.Any, localPort));
            result.LocalEndPoint = (IPEndPoint)udp.Client.LocalEndPoint!;
            
            byte[] request = CreateStunChangeRequest(changeIP, changePort);
            var serverEp = new IPEndPoint(serverIp, serverPort);
            
            await udp.SendAsync(request, request.Length, serverEp);
            
            var receiveTask = udp.ReceiveAsync();
            var timeoutTask = Task.Delay(1000); // 1s timeout
            
            if (await Task.WhenAny(receiveTask, timeoutTask) == receiveTask)
            {
                var response = await receiveTask;
                return ParseStunResponse(response.Buffer, result);
            }
        }
        catch { /* Ignore */ }
        
        result.Success = false;
        return result;
    }

    private static byte[] CreateStunBindingRequest()
    {
        byte[] packet = new byte[20];
        // Message Type: 0x0001 (Binding Request)
        packet[0] = 0x00; packet[1] = 0x01;
        // Message Length: 0x0000
        packet[2] = 0x00; packet[3] = 0x00;
        
        // Transaction ID (16 bytes random)
        var rand = new Random();
        rand.NextBytes(new Span<byte>(packet, 4, 16));
        
        return packet;
    }

    private static byte[] CreateStunChangeRequest(bool changeIP, bool changePort)
    {
        byte[] packet = new byte[28];
        // Message Type: 0x0001 (Binding Request)
        packet[0] = 0x00; packet[1] = 0x01;
        // Message Length: 8 bytes of attributes
        packet[2] = 0x00; packet[3] = 0x08;
        
        // Transaction ID
        var rand = new Random();
        rand.NextBytes(new Span<byte>(packet, 4, 16));
        
        // Attribute 1: CHANGE-REQUEST (Type: 0x0003, Length: 4)
        packet[20] = 0x00; packet[21] = 0x03;
        packet[22] = 0x00; packet[23] = 0x04;
        
        // Value: 4 bytes flags. 0x02 = Change IP, 0x04 = Change Port.
        int flags = 0;
        if (changeIP) flags |= 0x02;
        if (changePort) flags |= 0x04;
        
        packet[27] = (byte)flags;
        
        return packet;
    }

    public static StunResult ParseStunResponse(byte[] response, StunResult result)
    {
        if (response.Length < 20)
        {
            result.Success = false;
            return result;
        }

        // Check if message type is 0x0101 (Binding Response)
        int type = (response[0] << 8) | response[1];
        if (type != 0x0101 && type != 0x0111) // standard response or XOR response
        {
            result.Success = false;
            return result;
        }

        int length = (response[2] << 8) | response[3];
        int offset = 20;

        while (offset + 4 <= response.Length && offset < 20 + length)
        {
            int attrType = (response[offset] << 8) | response[offset + 1];
            int attrLength = (response[offset + 2] << 8) | response[offset + 3];
            offset += 4;

            if (offset + attrLength > response.Length)
                break;

            if (attrType == 0x0001 || attrType == 0x0020) // MAPPED-ADDRESS or XOR-MAPPED-ADDRESS
            {
                result.MappedEndPoint = ParseAddress(response, offset, attrLength, attrType == 0x0020);
            }
            else if (attrType == 0x0005) // CHANGED-ADDRESS
            {
                result.ChangedEndPoint = ParseAddress(response, offset, attrLength, false);
            }

            // Attribute values are aligned to 4-byte boundaries
            offset += (attrLength + 3) & ~3;
        }

        result.Success = result.MappedEndPoint != null;
        return result;
    }

    private static IPEndPoint ParseAddress(byte[] response, int offset, int length, bool isXor)
    {
        if (length < 8) return null;
        
        byte family = response[offset + 1];
        if (family != 0x01) return null; // IPv4 only for now

        int port = (response[offset + 2] << 8) | response[offset + 3];
        if (isXor)
        {
            // XOR Port with most significant 2 bytes of magic cookie 0x2112A442
            port ^= 0x2112;
        }

        byte[] ipBytes = new byte[4];
        Array.Copy(response, offset + 4, ipBytes, 0, 4);
        
        if (isXor)
        {
            // XOR IP with magic cookie 0x2112A442
            ipBytes[0] ^= 0x21;
            ipBytes[1] ^= 0x12;
            ipBytes[2] ^= 0xA4;
            ipBytes[3] ^= 0x42;
        }

        var ip = new IPAddress(ipBytes);
        return new IPEndPoint(ip, port);
    }
}
