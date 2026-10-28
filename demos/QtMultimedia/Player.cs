using System;
using System.IO;
using System.Text;
using System.Threading;

// Qt's Multimedia "player" example (examples/multimedia/player), reduced to the part that works
// without an audio device: a real media file is written, QMediaPlayer loads it through the ffmpeg
// backend, and the duration, media status and audio track are asserted. The device and format
// classes and the QVideoWidget half are checked as well, so the module's API is exercised even on
// a runner with no sound card.
[Demo("qtmultimedia-player")]
public static class Player
{
    public static int Run(DemoContext demo)
    {

        string path = Path.Combine(Path.GetTempPath(),
            "qtsharp-player-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".wav");
        WriteWave(path, seconds: 1);
        try
        {
            using var player = new QMediaPlayer();
            using var audio = new QAudioOutput();
            player.SetAudioOutput(audio);

            player.SetSource(QUrl.FromLocalFile(path));
            bool settled = PumpUntil(() =>
                player.mediaStatus() == QMediaPlayer.MediaStatus.LoadedMedia ||
                player.mediaStatus() == QMediaPlayer.MediaStatus.BufferedMedia ||
                player.mediaStatus() == QMediaPlayer.MediaStatus.InvalidMedia, 8000);
            #if QTSHARP_DEMO_SMOKE
            demo.Check(settled, "QMediaPlayer reaches a settled media status");
            #endif
            #if QTSHARP_DEMO_SMOKE
            demo.Check(player.mediaStatus() == QMediaPlayer.MediaStatus.LoadedMedia,
                $"the WAV file loads (status {player.mediaStatus()})");
            #endif
            #if QTSHARP_DEMO_SMOKE
            demo.Check(player.error() == QMediaPlayer.Error.NoError,
                $"loading reports no error ({player.error()})");
            #endif
            #if QTSHARP_DEMO_SMOKE
            demo.Check(Math.Abs(player.duration() - 1000) < 100,
                $"the duration is the file's one second ({player.duration()} ms)");
            #endif
            #if QTSHARP_DEMO_SMOKE
            demo.Check(player.HasAudio, "the player reports an audio track");
            #endif
            #if QTSHARP_DEMO_SMOKE
            demo.Check(player.playbackState() == QMediaPlayer.PlaybackState.StoppedState,
                "a freshly loaded player is stopped");
            #endif

            // QAudioFormat has no declared constructor in Qt 6.12 (it is implicitly default
            // constructible), so the binding cannot offer `new QAudioFormat()` yet; the supported
            // route is the device's preferred format, which is what applications use anyway.
            var mediaFormat = new QMediaFormat(QMediaFormat.FileFormat.Wave);
            #if QTSHARP_DEMO_SMOKE
            demo.Check(mediaFormat.fileFormat() == QMediaFormat.FileFormat.Wave,
                "QMediaFormat keeps its file format");
            #endif

#if QTSHARP_DEMO_SMOKE

            demo.Check(QMediaDevices.audioOutputs() != null,
                "QMediaDevices enumerates the audio outputs");
#endif
            using var defaultOutput = QMediaDevices.defaultAudioOutput();
            #if QTSHARP_DEMO_SMOKE
            demo.Check(defaultOutput != null,
                $"a default audio output object is available (null device: {defaultOutput.IsNull()})");
            #endif
            if (defaultOutput != null && !defaultOutput.IsNull())
            {
                var preferred = defaultOutput.PreferredFormat();
                #if QTSHARP_DEMO_SMOKE
                demo.Check(preferred.SampleRate() > 0,
                    $"the preferred format reports a sample rate ({preferred.SampleRate()})");
                #endif
            }

            using var video = new QVideoWidget();
            #if QTSHARP_DEMO_SMOKE
            using (var sink = video.VideoSink())
                demo.Check(sink != null, "QVideoWidget exposes its QVideoSink");
            #endif
            video.SetAspectRatioMode(Qt.AspectRatioMode.KeepAspectRatio);
            #if QTSHARP_DEMO_SMOKE
            demo.Check(video.AspectRatioMode == Qt.AspectRatioMode.KeepAspectRatio,
                "QVideoWidget keeps the aspect-ratio mode");
            #else
            // The player is a headless port: the generated file has no video track and there are
            // no controls, so View reports what the backend made of it instead of opening a window.
            Console.WriteLine($"[player] {Path.GetFileName(path)}: {player.mediaStatus()}, "
                + $"{player.duration()} ms, {player.error()}, default audio output present: "
                + (!defaultOutput.IsNull()));
            #endif
        }
        finally
        {
            try { File.Delete(path); } catch { /* temp files are harmless */ }
        }

        #if QTSHARP_DEMO_SMOKE
        return demo.ExitCode;
        #else
        return 0;
        #endif
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

    // A minimal 16-bit mono PCM WAV, so the test does not depend on a media file in the repository.
    static void WriteWave(string path, int seconds)
    {
        const int rate = 8000;
        const int channels = 1;
        const int bits = 16;
        int dataSize = rate * seconds * channels * bits / 8;
        using var writer = new BinaryWriter(File.Create(path));
        void Text(string s) => writer.Write(Encoding.ASCII.GetBytes(s));
        Text("RIFF"); writer.Write(36 + dataSize); Text("WAVE");
        Text("fmt "); writer.Write(16); writer.Write((short)1); writer.Write((short)channels);
        writer.Write(rate); writer.Write(rate * channels * bits / 8);
        writer.Write((short)(channels * bits / 8)); writer.Write((short)bits);
        Text("data"); writer.Write(dataSize);
        for (int i = 0; i < dataSize / 2; i++)
            writer.Write((short)0);
    }
}
