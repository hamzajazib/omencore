using System.Reflection;
using FluentAssertions;
using OmenCore.Hardware;
using OmenCore.Services;
using Xunit;

namespace OmenCoreApp.Tests.Hardware
{
    public class CapabilityDetectionServiceTests
    {
        private static CapabilityDetectionService CreateService()
        {
            var logging = new LoggingService();
            logging.Initialize();
            return new CapabilityDetectionService(logging);
        }

        private static void SetWmiBios(CapabilityDetectionService service, HpWmiBios wmiBios)
        {
            var field = typeof(CapabilityDetectionService).GetField("_wmiBios", BindingFlags.Instance | BindingFlags.NonPublic);
            field.Should().NotBeNull();
            field!.SetValue(service, wmiBios);
        }

        private static void SetSystemDesign(HpWmiBios wmiBios, HpWmiBios.SystemDesignData? design)
        {
            var property = typeof(HpWmiBios).GetProperty(nameof(HpWmiBios.SystemDesign));
            property.Should().NotBeNull();
            property!.SetValue(wmiBios, design);
        }

        private static void InvokeRefineCapabilitiesFromModel(CapabilityDetectionService service)
        {
            var method = typeof(CapabilityDetectionService).GetMethod("RefineCapabilitiesFromModel", BindingFlags.Instance | BindingFlags.NonPublic);
            method.Should().NotBeNull();
            method!.Invoke(service, null);
        }

        private static ModelCapabilities UnverifiedTemplateModel() => new()
        {
            ProductId = "TESTFAMILYFALLBACK",
            ModelName = "Test Family Fallback Board",
            Family = OmenModelFamily.Victus,
            SupportsFanControlWmi = true,
            SupportsFanControlEc = false,
            FanZoneCount = 2,
            UserVerified = false
        };

        [Fact]
        public void RefineCapabilitiesFromModel_DisablesFanControl_WhenFirmwareDeniesSwFanControl_OnUnverifiedBoard()
        {
            // Ohman cross-project review (docs/ROADMAP_v4.3.1.md): HP's own firmware-authored
            // SystemDesignData block can say "no software fan control" outright. For a board that
            // has never been hand-verified, that firmware statement should override a same-family
            // template's optimistic guess.
            var service = CreateService();
            service.Capabilities.ModelConfig = UnverifiedTemplateModel();
            service.Capabilities.CanSetFanSpeed = true;
            service.Capabilities.FanControl = FanControlMethod.WmiBios;

            using var wmiBios = new HpWmiBios(null);
            SetSystemDesign(wmiBios, new HpWmiBios.SystemDesignData { IsSwFanControlSupport = false });
            SetWmiBios(service, wmiBios);

            InvokeRefineCapabilitiesFromModel(service);

            service.Capabilities.CanSetFanSpeed.Should().BeFalse();
            service.Capabilities.FanControl.Should().Be(FanControlMethod.MonitoringOnly);
        }

        [Fact]
        public void RefineCapabilitiesFromModel_LeavesFanControlAlone_WhenBoardIsUserVerified()
        {
            // A hand-verified board's flags come from a real person's hardware and must stay
            // authoritative over a generic firmware-byte heuristic, even if that heuristic
            // disagrees.
            var service = CreateService();
            var verifiedModel = UnverifiedTemplateModel();
            verifiedModel.UserVerified = true;
            service.Capabilities.ModelConfig = verifiedModel;
            service.Capabilities.CanSetFanSpeed = true;
            service.Capabilities.FanControl = FanControlMethod.WmiBios;

            using var wmiBios = new HpWmiBios(null);
            SetSystemDesign(wmiBios, new HpWmiBios.SystemDesignData { IsSwFanControlSupport = false });
            SetWmiBios(service, wmiBios);

            InvokeRefineCapabilitiesFromModel(service);

            service.Capabilities.CanSetFanSpeed.Should().BeTrue(
                "a UserVerified board's own confirmed flags must not be second-guessed by SystemDesignData");
            service.Capabilities.FanControl.Should().Be(FanControlMethod.WmiBios);
        }

        [Fact]
        public void RefineCapabilitiesFromModel_LeavesFanControlAlone_WhenFirmwareConfirmsSwFanControl()
        {
            var service = CreateService();
            service.Capabilities.ModelConfig = UnverifiedTemplateModel();
            service.Capabilities.CanSetFanSpeed = true;
            service.Capabilities.FanControl = FanControlMethod.WmiBios;

            using var wmiBios = new HpWmiBios(null);
            SetSystemDesign(wmiBios, new HpWmiBios.SystemDesignData { IsSwFanControlSupport = true });
            SetWmiBios(service, wmiBios);

            InvokeRefineCapabilitiesFromModel(service);

            service.Capabilities.CanSetFanSpeed.Should().BeTrue();
            service.Capabilities.FanControl.Should().Be(FanControlMethod.WmiBios);
        }

        [Fact]
        public void RefineCapabilitiesFromModel_LeavesFanControlAlone_WhenSystemDesignDataUnavailable()
        {
            // No WMI BIOS / no SystemDesignData reading (e.g. WMI unavailable on this machine) must
            // fall back to the existing model-database-only behavior, not silently disable fan
            // control for lack of evidence.
            var service = CreateService();
            service.Capabilities.ModelConfig = UnverifiedTemplateModel();
            service.Capabilities.CanSetFanSpeed = true;
            service.Capabilities.FanControl = FanControlMethod.WmiBios;

            InvokeRefineCapabilitiesFromModel(service);

            service.Capabilities.CanSetFanSpeed.Should().BeTrue();
            service.Capabilities.FanControl.Should().Be(FanControlMethod.WmiBios);
        }
    }
}
