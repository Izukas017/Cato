using System.Net.Http;
using System.Text.Json;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace Cato;

// Música y modo letra. Las letras salen de lrclib.net: se envían título y artista de la canción.
public partial class MainWindow
{
    static readonly HttpClient http = CreateHttp();
    GlobalSystemMediaTransportControlsSessionManager? mgr;
    GlobalSystemMediaTransportControlsSession? session;
    List<LyricLine> lyrics = new();
    string lyricsKey = "";
    int lyricIndex = -2;

    static HttpClient CreateHttp()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("Cato/0.2");
        return h;
    }

    async Task StartMedia()
    {
        try
        {
            mgr = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            mgr.CurrentSessionChanged += (_, _) => Dispatcher.Invoke(HookSession);
            HookSession();
        }
        catch (Exception ex)
        {
            MediaTitle.Text = "Sin acceso a medios";
            Debug.WriteLine(ex);
        }
    }

    void HookSession()
    {
        if (session != null)
        {
            session.MediaPropertiesChanged -= OnMediaProps;
            session.PlaybackInfoChanged -= OnPlayback;
        }
        session = mgr?.GetCurrentSession();
        if (session != null)
        {
            session.MediaPropertiesChanged += OnMediaProps;
            session.PlaybackInfoChanged += OnPlayback;
        }
        _ = RefreshMedia();
    }

    void OnMediaProps(GlobalSystemMediaTransportControlsSession s, MediaPropertiesChangedEventArgs e) => Dispatcher.InvokeAsync(RefreshMedia);
    void OnPlayback(GlobalSystemMediaTransportControlsSession s, PlaybackInfoChangedEventArgs e) => Dispatcher.InvokeAsync(UpdatePlayState);

    async Task RefreshMedia()
    {
        string title = "Nada sonando", artist = "";
        ImageSource? art = null;
        if (session == null) { lyricsKey = ""; lyrics = new(); }
        else
        {
            try
            {
                var p = await session.TryGetMediaPropertiesAsync();
                title = p.Title;
                artist = p.Artist;
                var key = title + "|" + artist;
                if (key != lyricsKey)
                {
                    lyricsKey = key;
                    lyrics = new();
                    lyricIndex = -2;
                    if (data.Lyrics) _ = LoadLyrics(key, title, artist);
                }
                if (p.Thumbnail != null) art = await LoadThumb(p.Thumbnail);
            }
            catch (Exception ex) { Debug.WriteLine(ex); } // session can vanish mid-call when the player closes
        }
        MediaTitle.Text = LyrTitle.Text = title;
        MediaArtist.Text = LyrArtist.Text = artist;
        SetArt(ArtBox, ArtGlyph, art);
        SetArt(LyrArtBox, LyrArtGlyph, art);
        UpdatePlayState();
    }

    static void SetArt(Border box, TextBlock glyph, ImageSource? img)
    {
        box.Background = img == null ? Hex("#26FFFFFF") : new ImageBrush(img) { Stretch = Stretch.UniformToFill };
        glyph.Visibility = img == null ? Visibility.Visible : Visibility.Collapsed;
    }

    static async Task<BitmapImage> LoadThumb(IRandomAccessStreamReference r)
    {
        using var winStream = await r.OpenReadAsync();
        var ms = new MemoryStream();
        await winStream.AsStreamForRead().CopyToAsync(ms);
        ms.Position = 0;
        var b = new BitmapImage();
        b.BeginInit(); b.CacheOption = BitmapCacheOption.OnLoad; b.StreamSource = ms; b.EndInit(); b.Freeze();
        return b;
    }

    bool IsPlaying() =>
        session?.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

    void UpdatePlayState()
    {
        var playing = IsPlaying();
        PlayBtn.Content = PlayBtn2.Content = playing ? "" : "";
        SetWave(playing);
    }

    async void Prev_Click(object s, RoutedEventArgs e) { if (session != null) await session.TrySkipPreviousAsync(); }
    async void Play_Click(object s, RoutedEventArgs e) { if (session != null) await session.TryTogglePlayPauseAsync(); }
    async void Next_Click(object s, RoutedEventArgs e) { if (session != null) await session.TrySkipNextAsync(); }

    void Lyrics_Click(object s, RoutedEventArgs e)
    {
        lyricsMode = !lyricsMode;
        GoTo(lyricsMode ? Mode.Lyrics : Mode.Expanded);
    }

    (TimeSpan Pos, TimeSpan Len) Timeline()
    {
        if (session == null) return (TimeSpan.Zero, TimeSpan.Zero);
        try
        {
            var t = session.GetTimelineProperties();
            var pos = t.Position;
            if (IsPlaying()) pos += DateTimeOffset.Now - t.LastUpdatedTime; // players only report position now and then
            var len = t.EndTime - t.StartTime;
            return (len > TimeSpan.Zero && pos > len ? len : pos, len);
        }
        catch (COMException) { return (TimeSpan.Zero, TimeSpan.Zero); }
    }

    static string Fmt(TimeSpan t) => $"{(int)t.TotalMinutes}:{t.Seconds:00}";

    void UpdateMediaProgress()
    {
        var (pos, len) = Timeline();
        MediaProgress.Value = LyrProgress.Value = len > TimeSpan.Zero ? pos / len : 0;
        Elapsed.Text = LyrElapsed.Text = Fmt(pos);
        Remaining.Text = LyrRemaining.Text = "-" + Fmt(len > pos ? len - pos : TimeSpan.Zero);
        if (mode == Mode.Lyrics) UpdateLyric(pos);
    }

    void UpdateLyric(TimeSpan pos)
    {
        if (lyrics.Count == 0)
        {
            if (lyricIndex == -1) return;
            lyricIndex = -1;
            LyrPrev.Text = LyrNext.Text = "";
            LyrCur.Text = !data.Lyrics ? "Letras desactivadas en Configuración"
                : session == null ? "Nada sonando" : "No hay letra sincronizada para esta canción";
            return;
        }
        var i = lyrics.FindLastIndex(l => l.At <= pos);
        if (i == lyricIndex) return;
        lyricIndex = i;
        LyrPrev.Text = i > 0 ? lyrics[i - 1].Text : "";
        LyrCur.Text = i >= 0 ? lyrics[i].Text : "♪";
        LyrNext.Text = i + 1 < lyrics.Count ? lyrics[i + 1].Text : "";
        // the sung line lights up: slides up and fades in
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        ((TranslateTransform)LyrCur.RenderTransform).BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, Dur(280)) { EasingFunction = ease });
        LyrCur.BeginAnimation(OpacityProperty, new DoubleAnimation(0.25, 1, Dur(280)));
    }

    async Task LoadLyrics(string key, string title, string artist)
    {
        if (string.IsNullOrWhiteSpace(title)) return;
        try
        {
            var url = $"https://lrclib.net/api/search?track_name={Uri.EscapeDataString(title)}&artist_name={Uri.EscapeDataString(artist)}";
            using var doc = JsonDocument.Parse(await http.GetStringAsync(url));
            var synced = doc.RootElement.EnumerateArray()
                .Select(e => e.TryGetProperty("syncedLyrics", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null)
                .FirstOrDefault(v => !string.IsNullOrEmpty(v));
            if (key == lyricsKey && synced != null) { lyrics = Lrc.Parse(synced); lyricIndex = -2; }
        }
        catch (Exception ex) { Debug.WriteLine(ex); } // offline or no match: just no lyrics
    }
}
