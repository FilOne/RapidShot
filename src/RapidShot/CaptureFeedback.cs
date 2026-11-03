using System.ComponentModel;
using System.Media;

namespace RapidShot;

internal static class CaptureFeedback
{
    private static SoundPlayer? shutter;
    private static Stream? source;

    internal static void Initialize()
    {
        if (shutter != null) return;
        try
        {
            source = typeof(CaptureFeedback).Assembly.GetManifestResourceStream("RapidShot.Assets.shutter.wav");
            if (source == null) return;
            shutter = new SoundPlayer(source);
            shutter.Load(); // Preload once so the capture action never waits for audio loading.
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException or Win32Exception)
        { Shutdown(); }
    }

    internal static void Play()
    {
        // SoundPlayer.Play is asynchronous. The application owns the player so
        // closing the region editor does not interrupt the shutter.
        try { shutter?.Play(); }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException or Win32Exception)
        { /* Audio feedback must not affect a successfully copied screenshot. */ }
    }

    internal static void Shutdown()
    {
        shutter?.Dispose(); shutter = null;
        source?.Dispose(); source = null;
    }
}
