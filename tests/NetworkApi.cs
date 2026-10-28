using System;
using System.Diagnostics;
using System.Text;

// The QtNetwork module, which is what an app that is more than a window needs: the single-instance
// handoff (a second launch talks to the running one over QLocalServer/QLocalSocket - QLockFile only
// says "you lost the race"), the TCP transport, and host addressing.
//
// These classes live in Qt6Network.dll, a module of their own: the binding links Qt6Network and the
// runtime folder carries the matching DLL (see README.md).
[Test("test-network")]
public static class ApiNetworkDemo
{
    public static int Run(TestContext context)
    {
        // Addressing: a QHostAddress round-trips through its string form, and the machine has at
        // least the loopback interface.
        var loopback = new QHostAddress("127.0.0.1");
        context.Check(loopback.ToString() == "127.0.0.1", $"QHostAddress round-trip ('{loopback}')");
        context.Check(QNetworkInterface.AllInterfaces().Length >= 1,
            $"QNetworkInterface::allInterfaces ({QNetworkInterface.AllInterfaces().Length} interface(s))");

        LocalSocketHandoff(context.Check);
        TcpLoopback(context.Check);

        return context.ExitCode;
    }

    // The single-instance handoff: the running instance listens, the second one connects and hands
    // over a message.
    static void LocalSocketHandoff(Action<bool, string> check)
    {
        // A name unique to this run: a server left behind by an earlier (or crashed) run would
        // otherwise be the one that answers.
        string name = "qtsharp-" + Environment.ProcessId + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        QLocalServer.RemoveServer(name);

        var server = new QLocalServer();
        check(server.Listen(name), $"QLocalServer::listen ('{name}')");
        check(server.IsListening() && server.ServerName() == name, "the server reports it is listening");

        var client = new QLocalSocket();
        client.ConnectToServer(name);
        check(PumpUntil(() => server.HasPendingConnections(), 3000),
            "the server saw the second instance connect");

        var peer = server.NextPendingConnection();
        check(peer != null && peer.IsValid(), "nextPendingConnection returned a valid socket");

        byte[] payload = Encoding.ASCII.GetBytes("wake:open/dashboard");
        long written = client.Write(new QByteArray(payload));
        client.Flush();
        check(written == payload.Length, $"the second instance wrote {written} byte(s)");

        check(PumpUntil(() => peer.BytesAvailable() >= payload.Length, 3000),
            $"the server received the message ({peer.BytesAvailable()} byte(s) available)");
        var got = peer.ReadAll();
        check(got.Size() == payload.Length && got.ConstData() == "wake:open/dashboard",
            $"the message round-tripped ('{got.ConstData()}')");

        client.DisconnectFromServer();
        check(PumpUntil(() => client.State() == QLocalSocket.LocalSocketState.UnconnectedState, 3000),
            "the second instance reports it disconnected");

        peer.Dispose();
        client.Dispose();
        server.Close();
        server.Dispose();
        QLocalServer.RemoveServer(name);
    }

    // The TCP transport: a server on an ephemeral localhost port, a client, and a message each way.
    static void TcpLoopback(Action<bool, string> check)
    {
        var server = new QTcpServer();
        check(server.Listen(), "QTcpServer::listen on an ephemeral port");
        ushort port = server.ServerPort();
        check(server.IsListening() && port != 0, $"the TCP server is listening on port {port}");

        // The address/port overload is what a fixed-port service needs - it only exists because an
        // optional *boxed* parameter now keeps its parameter (the managed default is null and the
        // native glue substitutes QHostAddress(QHostAddress::Any)).
        var pinned = new QTcpServer();
        check(pinned.Listen(new QHostAddress(QHostAddress.SpecialAddress.LocalHost), 0),
            "QTcpServer::listen(address, port) accepts an explicit local address");
        pinned.Close();
        pinned.Dispose();

        var client = new QTcpSocket();
        client.ConnectToHost(new QHostAddress(QHostAddress.SpecialAddress.LocalHost), port);
        check(PumpUntil(() => server.HasPendingConnections(), 3000), "the TCP server saw the connection");
        check(PumpUntil(() => client.State() == QAbstractSocket.SocketState.ConnectedState, 3000),
            $"the TCP client connected (state {client.State()})");
        check(client.PeerPort() == port, $"the client sees the server's port ({client.PeerPort()})");

        var accepted = server.NextPendingConnection();
        check(accepted != null && accepted.IsValid(), "QTcpServer::nextPendingConnection returned a socket");

        byte[] payload = Encoding.ASCII.GetBytes("pong");
        check(accepted.Write(new QByteArray(payload)) == payload.Length, "the server wrote the reply back");
        accepted.Flush();
        check(PumpUntil(() => client.BytesAvailable() >= payload.Length, 3000), "the client received the reply");
        var reply = client.ReadAll();
        check(reply.Size() == payload.Length && reply.ConstData() == "pong",
            $"the TCP round-trip carried '{reply.ConstData()}'");

        client.DisconnectFromHost();
        accepted.Dispose();
        client.Dispose();
        server.Close();
        server.Dispose();
    }

    // The two ends live on this one thread, so a blocking waitFor*() on one end would stall the
    // other; pump the event loop until the condition holds instead.
    static bool PumpUntil(Func<bool> ready, int timeoutMs)
    {
        var clock = Stopwatch.StartNew();
        while (!ready() && clock.ElapsedMilliseconds < timeoutMs)
            QCoreApplication.ProcessEvents(QEventLoop.ProcessEventsFlags.AllEvents);
        return ready();
    }
}
