using System;
using System.IO;
using QtSharp.Api;

static class Program
{
    static int Main()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "assets", "sample.rcc");
        if (!QResource.RegisterResource(path)) throw new Exception("Resource registration failed.");
        try
        {
            using var file = new QFile(":/qtsharp/message.txt");
            if (!file.Open(QIODeviceBase.OpenMode.ReadOnly)) throw new Exception("Resource open failed.");
            using var bytes = file.ReadAll();
            string text = bytes.ConstData().Trim();
            if (text != "Hello from a QtSharp RCC resource.") throw new Exception(text);
            Console.WriteLine("[PASS] Packaged rcc -> MSBuild -> QResource -> QFile");
        }
        finally { QResource.UnregisterResource(path); }
        return 0;
    }
}
