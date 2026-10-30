# QtSharp.Multimedia

> The package id carries the platform: the snippets below use the `win-x64` variant. On
> Linux, use the matching `.linux-x64` package (`QtSharp.Api.linux-x64`, ...).

Qt Multimedia for .NET, generated from the Qt 6.12 headers the same way `QtSharp.Api` is:
`QMediaPlayer` with `QAudioOutput`/`QVideoSink`, the capture chain (`QCamera`,
`QMediaCaptureSession`, `QImageCapture`, `QMediaRecorder`), the audio device/format classes, and
the two widget classes from QtMultimediaWidgets (`QVideoWidget`, `QGraphicsVideoItem`).

```xml
<ItemGroup>
  <PackageReference Include="QtSharp.Api.win-x64" Version="0.23.0" />
  <PackageReference Include="QtSharp.Multimedia.win-x64" Version="0.23.0" />
</ItemGroup>
```

```csharp
using QtSharp.QtCore;
using QtSharp.QtMultimedia;

using var player = new QMediaPlayer();
using var output = new QAudioOutput();
player.SetAudioOutput(output);
player.SetSource(new QUrl("file:///C:/media/track.mp3"));
player.Play();
```

The package depends on `QtSharp.Api` (and through it `QtSharp.Runtime`/`QtSharp.Qt6`) and on
`QtSharp.Multimedia.Qt6`, the companion package that carries `Qt6Multimedia.dll`,
`Qt6MultimediaWidgets.dll` and the `multimedia` backend plugin (`ffmpegmediaplugin.dll`). The
plugin is copied into `<output>\multimedia\`, where Qt looks for it.
