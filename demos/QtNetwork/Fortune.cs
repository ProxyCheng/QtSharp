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

    public static int Run(QApplication app)
    {
        int failures = 0;
        void Check(bool ok, string what)
        {
            Console.WriteLine($"{(ok ? "[PASS]" : "[FAIL]")} {what}");
            if (!ok)
                failures++;
        }

        using var server = new QTcpServer();
        using var address = new QHostAddress("127.0.0.1");
        Check(server.Listen(address, 0), "the fortune server listens on loopback");
        Check(server.IsListening() && server.ServerPort() != 0,
            $"the server got a port ({server.ServerPort()})");

        string expected = Fortunes[1];
        using var client = new QTcpSocket();
        client.ConnectToHost("127.0.0.1", server.ServerPort());
        Check(client.WaitForConnected(3000), "the fortune client connects");

        Check(server.WaitForNewConnection(3000), "the server accepts the connection");
        using var peer = server.NextPendingConnection();
        Check(peer != null && peer.IsOpen(), "the server has a socket for the client");

        using (var bytes = new QByteArray(expected))
        {
            long written = peer.Write(bytes);
            peer.Flush();
            Check(written == Encoding.UTF8.GetByteCount(expected),
                $"the server writes the fortune ({written} bytes)");
        }

        Check(client.WaitForReadyRead(3000), "the client receives data");
        using var receivedBytes = client.ReadAll();
        string received = Encoding.UTF8.GetString(receivedBytes.ToArray());
        Check(received == expected, $"the client reads the fortune ('{received}')");

        client.DisconnectFromHost();
        client.WaitForDisconnected(3000);
        Check(!client.IsOpen() || client.State() == QAbstractSocket.SocketState.UnconnectedState,
            $"the client disconnects (state {client.State()})");

        Console.WriteLine(failures == 0 ? "[SELFTEST OK]" : $"[FAIL] {failures} check(s) failed");
        return failures == 0 ? 0 : 1;
    }
}
