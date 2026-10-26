using System;
using System.Threading;
using QtSharp.QtNetwork;

// Qt's WebSockets "echoserver" example (examples/websockets/echoserver) driven by its own client:
// the server echoes every text message back, so one process proves both halves of the binding over
// a real loopback connection.
[Demo("qtwebsockets-echo")]
public static class EchoServer
{
    public static int Run(QApplication app)
    {
        int failures = 0;
        void Check(bool ok, string what)
        {
            Console.WriteLine($"{(ok ? "[PASS]" : "[FAIL]")} {what}");
            if (!ok)
                failures++;
        }

        string received = null;
        string echoed = null;
        bool connected = false;
        QWebSocket peer = null;

        using var server = new QWebSocketServer("QtSharp echo", QWebSocketServer.SslMode.NonSecureMode);
        Check(server.Listen(new QHostAddress(QHostAddress.SpecialAddress.LocalHost), 0),
            "QWebSocketServer listens on an ephemeral port");
        int port = server.ServerPort();
        Check(port > 0, $"the server reports the port it bound ({port})");

        server.NewConnection += () =>
        {
            peer = server.NextPendingConnection();
            if (peer == null)
                return;
            peer.TextMessageReceived += text =>
            {
                received = text;
                peer.SendTextMessage("echo:" + text);
            };
        };

        using var client = new QWebSocket();
        client.Connected += () => connected = true;
        client.TextMessageReceived += text => echoed = text;
        client.Open(new QUrl($"ws://127.0.0.1:{port}"));
        Check(PumpUntil(() => connected, 5000), "the client connects to the server");
        Check(peer != null, "the server accepts the connection");

        Check(client.SendTextMessage("hello") > 0, "the client sends a text message");
        Check(PumpUntil(() => echoed != null, 5000), "the echo comes back");
        Check(received == "hello", "the server received the message");
        Check(echoed == "echo:hello", "the server's echo reached the client");

        // Binary frames go through the same socket.
        string binary = null;
        if (peer != null)
            peer.BinaryMessageReceived += data => binary = data.ToArray().Length.ToString();
        client.SendBinaryMessage(new QByteArray(new byte[] { 1, 2, 3, 4 }));
        Check(PumpUntil(() => binary != null, 5000) && binary == "4", "a binary message crosses the socket");

        client.Close(CloseCode.CloseCodeNormal, "done");
        Check(PumpUntil(() => !client.IsValid() || client.State() == QtSharp.QtNetwork.QAbstractSocket.SocketState.UnconnectedState, 3000),
            "closing the client returns the socket to UnconnectedState");
        peer?.Close(CloseCode.CloseCodeNormal, "done");

        Console.WriteLine(failures == 0 ? "[SELFTEST OK]" : $"[FAIL] {failures} check(s) failed");
        return failures == 0 ? 0 : 1;
    }

    static bool PumpUntil(Func<bool> condition, int timeoutMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return true;
            QCoreApplication.ProcessEvents();
            Thread.Sleep(5);
        }
        return condition();
    }
}
