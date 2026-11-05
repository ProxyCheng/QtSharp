using System;
using QtSharp.QtMultimedia;

// std::optional<T> is T? for a value type. QScreenCapture::maximumFrameRate is the deterministic
// probe: it stores the requested rate without needing a capture session.
[Test("test-optional")]
public static class OptionalApi
{
    public static int Run(TestContext context)
    {
        using var capture = new QScreenCapture();
        capture.SetMaximumFrameRate(30.0);
        var engaged = capture.MaximumFrameRate;
        context.Check(engaged == 30.0,
            $"an engaged std::optional<qreal> becomes double? (got {engaged?.ToString() ?? "null"})");

        capture.SetMaximumFrameRate(null);
        context.Check(capture.MaximumFrameRate == null,
            "a disengaged std::optional<qreal> becomes null");

        return context.ExitCode;
    }
}
