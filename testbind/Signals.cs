using System;

public sealed class QSignal<TDelegate> where TDelegate : Delegate
{
    readonly Action<TDelegate> _connect;
    internal QSignal(Action<TDelegate> connect) => _connect = connect;
    public void Connect(TDelegate handler) => _connect(handler);
}
