using System;
using System.Threading.Tasks;
using Windows.Media.Control;

namespace PodPeek;

/// <summary>
/// Pausa y reanuda lo que suene (Spotify, el navegador, Teams…) con la API de medios de Windows. A diferencia de
/// la tecla play/pausa, sabe si está sonando, así que nunca pone música que estaba parada.
/// </summary>
static class MediaControl
{
    public static async Task<bool> PauseIfPlayingAsync()
    {
        var session = await CurrentAsync();
        if (session?.GetPlaybackInfo().PlaybackStatus != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
            return false;
        bool ok = await session.TryPauseAsync();
        Log.Write($"pausa al quitar un auricular: {ok}");
        return ok;
    }

    public static async Task ResumeAsync()
    {
        var session = await CurrentAsync();
        if (session is null) return;
        bool ok = await session.TryPlayAsync();
        Log.Write($"reanudar al volver a ponerlos: {ok}");
    }

    static async Task<GlobalSystemMediaTransportControlsSession?> CurrentAsync()
    {
        try { return (await GlobalSystemMediaTransportControlsSessionManager.RequestAsync()).GetCurrentSession(); }
        catch (Exception e) { Log.Write($"control de medios no disponible: {e.Message}"); return null; }
    }
}
