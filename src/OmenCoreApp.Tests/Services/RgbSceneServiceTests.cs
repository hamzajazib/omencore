using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using FluentAssertions;
using OmenCore.Models;
using OmenCore.Services;
using OmenCore.Services.Rgb;

namespace OmenCoreApp.Tests.Services;

public class RgbSceneServiceTests
{
    [Fact]
    public async Task ApplySceneAsync_WhenRgbManagerHasNoSupportedProviders_DoesNotReportSuccess()
    {
        using var logging = new LoggingService();
        var manager = new RgbManager(logging);
        using var service = new RgbSceneService(logging, manager);
        var sceneChanged = false;
        service.SceneChanged += (_, _) => sceneChanged = true;

        var result = await service.ApplySceneAsync(new RgbScene
        {
            Name = "Static red",
            Effect = RgbSceneEffect.Static,
            PrimaryColor = "#FF0000",
            ApplyToOmenKeyboard = false,
            ApplyToCorsair = true,
            ApplyToLogitech = false,
            ApplyToRazer = false
        });

        result.Success.Should().BeFalse("a scene should not become the confirmed RGB state when no selected RGB provider accepted the write");
        result.ProvidersApplied.Should().Be(0);
        result.ProvidersFailed.Should().Be(1);
        sceneChanged.Should().BeFalse("failed scene applies should not be published as confirmed current RGB state");
        service.CurrentScene.Should().BeNull();
    }

    [Fact]
    public async Task SceneChanged_WhenSubscriberThrows_StillNotifiesRemainingSubscribers()
    {
        using var logging = new LoggingService();
        var manager = new RgbManager(logging);
        manager.RegisterProvider(new RecordingRgbProvider(RgbEffectType.Static));
        using var service = new RgbSceneService(logging, manager);
        RgbSceneChangedEventArgs? delivered = null;

        service.SceneChanged += (_, _) => throw new InvalidOperationException("subscriber failed");
        service.SceneChanged += (_, args) => delivered = args;

        var result = await service.ApplySceneAsync(new RgbScene
        {
            Name = "Static red",
            Effect = RgbSceneEffect.Static,
            PrimaryColor = "#FF0000",
            ApplyToOmenKeyboard = false,
            ApplyToCorsair = true,
            ApplyToLogitech = false,
            ApplyToRazer = false
        });

        result.Success.Should().BeTrue();
        delivered.Should().NotBeNull();
        delivered!.CurrentScene.Name.Should().Be("Static red");
    }

    [Fact]
    public void BuiltInScenes_IncludeP2EffectPresets()
    {
        Environment.SetEnvironmentVariable("OMENCORE_DISABLE_FILE_LOG", "1");
        using var service = CreateService(new RecordingRgbProvider());

        service.Scenes.Should().Contain(scene =>
            scene.Id == "heat-wave" &&
            scene.Effect == RgbSceneEffect.Wave &&
            scene.Icon == "W");

        service.Scenes.Should().Contain(scene =>
            scene.Id == "calm-pulse" &&
            scene.Effect == RgbSceneEffect.Breathing &&
            scene.TriggerOnPerformanceMode == "Quiet");
    }

    // Regression guard for a real field bug (Discord, "snowfall hateall", board 8D87/88F7,
    // OMEN MAX 16-ak003nr): Night Mode and Work shipped with a baked-in ScheduledTime, and
    // CheckScheduledScenes() fires any scene with one unconditionally - there is no UI anywhere
    // to see, enable/disable, or edit scene scheduling (IsSchedulingEnabled has no setter call
    // site outside this class and defaults true). That meant every user's keyboard lighting
    // silently changed at 10pm/9am regardless of what they'd manually configured, including
    // having turned lighting off entirely. Built-in scenes must never carry a schedule unless a
    // real opt-in UI exists to surface and control it.
    [Fact]
    public void BuiltInScenes_NeverShipWithASilentDefaultSchedule()
    {
        Environment.SetEnvironmentVariable("OMENCORE_DISABLE_FILE_LOG", "1");
        using var service = CreateService(new RecordingRgbProvider());

        service.Scenes.Should().OnlyContain(scene => string.IsNullOrEmpty(scene.ScheduledTime),
            "no built-in scene may auto-apply on a schedule with zero UI to see or disable it");
    }

    [Fact]
    public async Task ApplySceneAsync_RoutesWaveScene_AsWaveEffect()
    {
        Environment.SetEnvironmentVariable("OMENCORE_DISABLE_FILE_LOG", "1");
        var provider = new RecordingRgbProvider(RgbEffectType.Wave);
        using var service = CreateService(provider);

        var result = await service.ApplySceneAsync(new RgbScene
        {
            Id = "test-wave",
            Name = "Test Wave",
            Effect = RgbSceneEffect.Wave,
            ApplyToOmenKeyboard = false,
            ApplyToCorsair = true,
            ApplyToLogitech = true,
            ApplyToRazer = true
        });

        result.Success.Should().BeTrue();
        provider.LastEffect.Should().Be("effect:wave");
    }

    private static RgbSceneService CreateService(RecordingRgbProvider provider)
    {
        var logging = new LoggingService { Level = LogLevel.Info };
        var manager = new RgbManager(logging);
        manager.RegisterProvider(provider);
        return new RgbSceneService(logging, manager);
    }

    private sealed class RecordingRgbProvider : IRgbProvider
    {
        private readonly IReadOnlyList<RgbEffectType> _supportedEffects;

        public RecordingRgbProvider(params RgbEffectType[] supportedEffects)
        {
            _supportedEffects = supportedEffects.Length == 0
                ? new[] { RgbEffectType.Static, RgbEffectType.Breathing, RgbEffectType.Spectrum, RgbEffectType.Wave, RgbEffectType.Off }
                : supportedEffects;
        }

        public string ProviderName => "Recording";
        public string ProviderId => "recording";
        public bool IsAvailable { get; private set; }
        public bool IsConnected => IsAvailable;
        public int DeviceCount => IsAvailable ? 1 : 0;
        public RgbProviderConnectionStatus ConnectionStatus => IsAvailable
            ? RgbProviderConnectionStatus.Connected
            : RgbProviderConnectionStatus.Disabled;
        public string StatusDetail => IsAvailable ? "1 device" : "Not initialized";
        public IReadOnlyList<RgbEffectType> SupportedEffects => _supportedEffects;
        public string? LastEffect { get; private set; }

        public Task InitializeAsync()
        {
            IsAvailable = true;
            return Task.CompletedTask;
        }

        public Task ApplyEffectAsync(string effectId)
        {
            LastEffect = effectId;
            return Task.CompletedTask;
        }

        public Task SetStaticColorAsync(Color color)
        {
            LastEffect = $"color:#{color.R:X2}{color.G:X2}{color.B:X2}";
            return Task.CompletedTask;
        }

        public Task SetBreathingEffectAsync(Color color)
        {
            LastEffect = $"breathing:#{color.R:X2}{color.G:X2}{color.B:X2}";
            return Task.CompletedTask;
        }

        public Task SetSpectrumEffectAsync()
        {
            LastEffect = "effect:spectrum";
            return Task.CompletedTask;
        }

        public Task TurnOffAsync()
        {
            LastEffect = "off";
            return Task.CompletedTask;
        }
    }
}
