using System;
using System.IO;
using System.Reflection;
using FluentAssertions;
using OmenCore.Models;
using OmenCore.Services;
using Xunit;

namespace OmenCoreApp.Tests.Services
{
    /// <summary>
    /// The OMEN key action was read once at construction, so choosing a different action in Settings did nothing
    /// until OmenCore was restarted. The action is now re-read on every key press.
    /// </summary>
    [Collection("Config Isolation")]
    public class OmenKeyActionReloadTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "omencore-omenkey-" + Guid.NewGuid().ToString("N"));
        private readonly string? _previousDir = Environment.GetEnvironmentVariable("OMENCORE_CONFIG_DIR");

        public OmenKeyActionReloadTests()
        {
            Directory.CreateDirectory(_dir);
            Environment.SetEnvironmentVariable("OMENCORE_CONFIG_DIR", _dir);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("OMENCORE_CONFIG_DIR", _previousDir);
            try { Directory.Delete(_dir, true); } catch { }
        }

        private static void Press(OmenKeyService svc) =>
            typeof(OmenKeyService).GetMethod("ExecuteAction", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(svc, null);

        [Fact]
        public void ChangingTheActionInConfig_AppliesToTheNextKeyPress()
        {
            var logging = new LoggingService();
            logging.Initialize();
            var config = new ConfigurationService();
            config.Config.Features ??= new FeaturePreferences();
            config.Config.Features.OmenKeyAction = "DoNothing";
            var svc = new OmenKeyService(logging, config);

            var window = 0;
            svc.ToggleOmenCoreRequested += (_, _) => window++;

            Press(svc);
            window.Should().Be(0, "DoNothing was configured");

            config.Config.Features.OmenKeyAction = "ShowWindow";
            Press(svc);
            window.Should().Be(1, "the new action must apply without restarting");
        }
    }
}
