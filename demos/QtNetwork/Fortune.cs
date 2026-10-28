using System;
using System.Text;

// The fortune server/client pair (examples/network/fortuneserver and fortuneclient) over loopback,
// so the official QtNetwork example runs without a network. The server accepts one connection,
// sends a fortune through QTcpSocket, and the client reads it back with QIODevice::readAll.
[Demo("qtnetwork-fortune")]
public static class Fortune
{
    static readonly string[] Fortunes =
    {
        "QtSharp speaks Qt over the C ABI.",
        "A widget a day keeps the event loop awake.",
        "Signals travel faster than exceptions.",
    };

    public static int Run(DemoContext demo)
    {

        using var server = new QTcpServer();
        using var address = new QHostAddress("127.0.0.1");
        bool listening = server.Listen(address, 0);
        #if QTSHARP_DEMO_SMOKE
        demo.Check(listening, "the fortune server listens on loopback");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(server.IsListening() && server.ServerPort() != 0,
            $"the server got a port ({server.ServerPort()})");
        #else
        Console.WriteLine($"[fortune] server listening on 127.0.0.1:{server.ServerPort()}");
        #endif

        string expected = Fortunes[1];
        using var client = new QTcpSocket();
        client.ConnectToHost("127.0.0.1", server.ServerPort());
        bool connected = client.WaitForConnected(3000);
        #if QTSHARP_DEMO_SMOKE
        demo.Check(connected, "the fortune client connects");
        #else
        Console.WriteLine(connected ? "[fortune] client connected" : "[fortune] client did not connect");
        #endif

#if QTSHARP_DEMO_SMOKE

        demo.Check(server.WaitForNewConnection(3000), "the server accepts the connection");
#else
        server.WaitForNewConnection(3000);
#endif
        using var peer = server.NextPendingConnection();
        #if QTSHARP_DEMO_SMOKE
        demo.Check(peer != null && peer.IsOpen(), "the server has a socket for the client");
        #endif

        using (var bytes = new QByteArray(expected))
        {
            long written = peer.Write(bytes);
            peer.Flush();
            #if QTSHARP_DEMO_SMOKE
            demo.Check(written == Encoding.UTF8.GetByteCount(expected),
                $"the server writes the fortune ({written} bytes)");
            #else
            Console.WriteLine($"[fortune] server wrote {written} bytes for the client");
            #endif
        }

#if QTSHARP_DEMO_SMOKE

        demo.Check(client.WaitForReadyRead(3000), "the client receives data");
#else
        client.WaitForReadyRead(3000);
#endif
        using var receivedBytes = client.ReadAll();
        string received = Encoding.UTF8.GetString(receivedBytes.ToArray());
        #if QTSHARP_DEMO_SMOKE
        demo.Check(received == expected, $"the client reads the fortune ('{received}')");
        #else
        Console.WriteLine($"[fortune] client received: {received}");
        #endif

        client.DisconnectFromHost();
        client.WaitForDisconnected(3000);
        #if QTSHARP_DEMO_SMOKE
        demo.Check(!client.IsOpen() || client.State() == QAbstractSocket.SocketState.UnconnectedState,
            $"the client disconnects (state {client.State()})");
        #endif

        #if QTSHARP_DEMO_SMOKE
        return demo.ExitCode;
        #else
        return 0;
        #endif
    }
}
