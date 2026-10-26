# QtSharp.WebSockets

Qt WebSockets for .NET, generated from the Qt 6.12 headers the same way `QtSharp.Api` is:
`QWebSocket`, `QWebSocketServer`, the CORS authenticator and the handshake options, with signals
as C# events and virtuals as overridable methods.

```xml
<ItemGroup>
  <PackageReference Include="QtSharp.Api" Version="0.12.0" />
  <PackageReference Include="QtSharp.WebSockets" Version="0.12.0" />
</ItemGroup>
```

```csharp
using QtSharp.QtCore;
using QtSharp.QtWebSockets;

using var server = new QWebSocketServer("echo", QWebSocketServer.SslMode.NonSecureMode);
server.Listen(new QHostAddress(QHostAddress.SpecialAddress.LocalHost), 0);
server.NewConnection += () =>
{
    using var socket = server.NextPendingConnection();
    socket.TextMessageReceived += text => socket.SendTextMessage(text);
};

using var client = new QWebSocket();
client.Connected += () => client.SendTextMessage("hello");
client.ConnectToUrl(new QUrl($"ws://127.0.0.1:{server.ServerPort()}"));
```

The package depends on `QtSharp.Api` (and through it `QtSharp.Runtime`/`QtSharp.Qt6`) and on
`QtSharp.WebSockets.Qt6`, the companion package that carries `Qt6WebSockets.dll`.
