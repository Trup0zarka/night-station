using System.Linq;
using Content.Client.Gameplay;
using RulesSystem = Content.Client.RandomRules.RulesSystem;
using Content.Shared.Audio;
using Content.Shared.CCVar;
using Content.Shared.Random;
using Content.Shared.Random.Rules;
using Robust.Client.GameObjects;
using Robust.Client.Player;
using Robust.Client.ResourceManagement;
using Robust.Client.State;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Utility;

namespace Content.Client.Audio;

public sealed class IngameMusicSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _configManager = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IStateManager _state = default!;
    [Dependency] private readonly RulesSystem _rules = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly ContentAudioSystem _contentAudio = default!;

    private const float MusicFadeTime = 5f;
    private static float _volumeSlider;

    private EntityUid? _currentMusicStream;
    private IngameMusicPrototype? _currentMusic;
    private bool _interruptable;

    private readonly Dictionary<string, List<ResPath>> _musicTracks = new();
    private readonly HashSet<string> _loggedEmptyPrototypes = new();
    private ISawmill _sawmill = default!;
    private double _lastNoMusicWarning = 0;

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_configManager, CCVars.IngameMusicVolume, MusicVolumeChanged, true);
        _sawmill = IoCManager.Resolve<ILogManager>().GetSawmill("audio.ingame_music");

        SetupMusicTracks();
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnProtoReload);
        _state.OnStateChanged += OnStateChange;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _state.OnStateChanged -= OnStateChange;
        StopCurrentMusic();
    }

    private void MusicVolumeChanged(float volume)
    {
        _volumeSlider = SharedAudioSystem.GainToVolume(volume);

        // Защита от бесконечности и NaN
        if (float.IsInfinity(_volumeSlider) || float.IsNaN(_volumeSlider) || _volumeSlider < -100f)
        {
            _volumeSlider = -80f;
        }

        if (_currentMusicStream != null && _currentMusic != null)
        {
            var finalVolume = _currentMusic.Sound.Params.Volume + _volumeSlider;
            finalVolume = Math.Clamp(finalVolume, -80f, 0f);

            _audio.SetVolume(_currentMusicStream, finalVolume);
        }
    }

    private void OnProtoReload(PrototypesReloadedEventArgs args)
    {
        if (args.WasModified<IngameMusicPrototype>())
            SetupMusicTracks();
    }

    private void OnStateChange(StateChangedEventArgs args)
    {
        if (args.NewState is GameplayState)
        {
            UpdateMusic();
        }
        else
        {
            StopCurrentMusic();
        }
    }

    private void SetupMusicTracks()
    {
        _musicTracks.Clear();
        foreach (var musicProto in _proto.EnumeratePrototypes<IngameMusicPrototype>())
        {
            var tracks = _musicTracks.GetOrNew(musicProto.ID);
            RefreshTracks(musicProto.Sound, tracks, null);
            _random.Shuffle(tracks);
        }
    }

    private void RefreshTracks(SoundSpecifier sound, List<ResPath> tracks, ResPath? lastPlayed)
    {
        DebugTools.Assert(tracks.Count == 0);

        switch (sound)
        {
            case SoundCollectionSpecifier collection:
                if (collection.Collection == null)
                    break;

                var soundCollection = _proto.Index<SoundCollectionPrototype>(collection.Collection);
                tracks.AddRange(soundCollection.PickFiles);
                break;
            case SoundPathSpecifier path:
                tracks.Add(path.Path);
                break;
        }

        if (tracks.Count > 1 && tracks[^1] == lastPlayed)
        {
            (tracks[0], tracks[^1]) = (tracks[^1], tracks[0]);
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_state.CurrentState is not GameplayState)
            return;

        UpdateMusic();
    }

    private void UpdateMusic()
    {
        bool? isDone = null;

        if (TryComp(_currentMusicStream, out AudioComponent? audioComp))
        {
            isDone = !audioComp.Playing;
        }

        if (_interruptable)
        {
            var player = _player.LocalSession?.AttachedEntity;

            if (player == null || _currentMusic == null)
            {
                FadeOutCurrentMusic();
                _currentMusic = null;
                isDone = true;
            }
            else if (!string.IsNullOrEmpty(_currentMusic.Rules) &&
                     _proto.TryIndex<RulesPrototype>(_currentMusic.Rules, out var rules) &&
                     !_rules.IsTrue(player.Value, rules))
            {
                FadeOutCurrentMusic();
                _currentMusic = null;
                isDone = true;
            }
        }

        if (isDone == false)
            return;

        var newMusic = GetNextMusic();

        if (newMusic == null)

        PlayMusic(newMusic);
    }

    private void PlayMusic(IngameMusicPrototype musicProto)
    {
        FadeOutCurrentMusic();

        _currentMusic = musicProto;
        _interruptable = musicProto.Interruptable;

        var tracks = _musicTracks[musicProto.ID];
        if (tracks.Count == 0)
        {
            if (_loggedEmptyPrototypes.Add(musicProto.ID))
            {
                _sawmill.Debug($"Music prototype {musicProto.ID} has no tracks, skipping");
            }
            return;
        }

        var track = tracks[^1];
        tracks.RemoveAt(tracks.Count - 1);

        var volume = musicProto.Sound.Params.Volume + _volumeSlider;
        volume = Math.Clamp(volume, -80f, 0f);

        var audioParams = AudioParams.Default
            .WithVolume(volume);
            //.WithLoop(true);

        var strim = _audio.PlayGlobal(
            track.ToString(),
            Filter.Local(),
            false,
            audioParams);

        if (strim == null)
        {
            _sawmill.Error($"Failed to play music track {track}");
            return;
        }

        _currentMusicStream = strim.Value.Entity;

        if (musicProto.FadeIn)
        {
            _contentAudio.FadeIn(_currentMusicStream, strim.Value.Component, MusicFadeTime);
        }

        // Update list if track is end
        if (tracks.Count == 0)
        {
            RefreshTracks(musicProto.Sound, tracks, track);
        }
    }

    private IngameMusicPrototype? GetNextMusic()
    {
        var player = _player.LocalEntity;

        if (player == null)
            return null;

        var ev = new PlayIngameMusicEvent();
        RaiseLocalEvent(ref ev);

        if (ev.Cancelled)
            return null;

        var allMusic = _proto.EnumeratePrototypes<IngameMusicPrototype>().ToList();
        _sawmill.Debug($"Found {allMusic.Count} music prototypes");

        allMusic.Sort((x, y) => y.Priority.CompareTo(x.Priority));

        foreach (var music in allMusic)
        {
            _sawmill.Debug($"Checking music {music.ID} priority {music.Priority} rules {music.Rules}");

            if (string.IsNullOrEmpty(music.Rules))
            {
                _sawmill.Debug($"Music {music.ID} has no rules, selecting");
                return music;
            }

            if (!_proto.TryIndex<RulesPrototype>(music.Rules, out var rules))
            {
                _sawmill.Debug($"Rules {music.Rules} not found");
                continue;
            }

            if (!_rules.IsTrue(player.Value, rules))
            {
                _sawmill.Debug($"Rules {music.Rules} returned false");
                continue;
            }

            _sawmill.Debug($"Music {music.ID} selected");
            return music;
        }

        return null;
    }

    private void FadeOutCurrentMusic()
    {
        if (_currentMusicStream != null)
        {
            _contentAudio.FadeOut(_currentMusicStream, duration: MusicFadeTime);
            _currentMusicStream = null;
        }
    }

    private void StopCurrentMusic()
    {
        _currentMusicStream = _audio.Stop(_currentMusicStream);
        _currentMusic = null;
    }
}

/// <summary>
/// Raised whenever ingame music tries to play.
/// </summary>
[ByRefEvent]
public record struct PlayIngameMusicEvent(bool Cancelled = false);
