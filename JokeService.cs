namespace App.WindowsService;
using System.Net.Sockets;
using System.Net;
using System;
using System.Threading;

public sealed class UDPingClient
{
    private const int PORT = 65432;
    private const int CHECK = 255;
   
    public void Listen()
    {
        UdpClient listener = new(PORT+1);
        UdpClient sender = new(0);
        Console.Write("run");

        while (true) {
            IPEndPoint sourceIP = new IPEndPoint(IPAddress.Any, 0);
            // receive buffer containing ID, stores source IP in ref
            byte[] buffer = listener.Receive(ref sourceIP);
            buffer[2] = CHECK;
            sourceIP.Port = PORT;
            // send back to source
            sender.SendAsync(buffer, buffer.Length, sourceIP);
        }
    }
}

