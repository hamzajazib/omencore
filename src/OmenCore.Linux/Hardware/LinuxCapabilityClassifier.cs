namespace OmenCore.Linux.Hardware;

public enum LinuxCapabilityClass
{
    FullControl,
    ProfileOnly,
    TelemetryOnly,
    UnsupportedControl
}

public sealed class LinuxCapabilityAssessment
{
    public LinuxCapabilityClass CapabilityClass { get; init; }
    public bool SupportsManualFanControl { get; init; }
    public bool SupportsProfileControl { get; init; }
    public bool SupportsTelemetry { get; init; }
    public string Reason { get; init; } = string.Empty;

    public string CapabilityKey => CapabilityClass switch
    {
        LinuxCapabilityClass.FullControl => "full-control",
        LinuxCapabilityClass.ProfileOnly => "profile-only",
        LinuxCapabilityClass.TelemetryOnly => "telemetry-only",
        _ => "unsupported-control"
    };
}

public static class LinuxCapabilityClassifier
{
    public static LinuxCapabilityAssessment Assess(
        bool isRoot,
        bool hasEcAccess,
        bool hasHpWmiPath,
        bool hasThermalProfile,
        bool hasPlatformProfile,
        bool hasAcpiPlatformProfile,
        bool hasFan1Output,
        bool hasFan2Output,
        bool hasFan1Target,
        bool hasFan2Target,
        bool hasHwmonFanAccess,
        bool hasTelemetryPaths,
        bool isUnsafeEcModel,
        string? model,
        string? boardId)
    {
        var isWmaaAbortProneBoard = IsWmaaAbortProneBoard(boardId);
        var hasManualFanControl = hasEcAccess || hasFan1Output || hasFan2Output || hasFan1Target || hasFan2Target;
        // hwmon pwm_enable gives coarse policy control (auto/full/manual mode), but not reliable
        // per-fan/manual target writes by itself.
        var hasProfileControl = hasThermalProfile || hasPlatformProfile || hasAcpiPlatformProfile || hasHwmonFanAccess;
        var hasTelemetry = hasTelemetryPaths || hasHpWmiPath || hasManualFanControl || hasProfileControl;

        if (isWmaaAbortProneBoard && hasManualFanControl)
        {
            var reason = "Board 8BCD has field reports of ACPI WMAA/WHCM aborts where WMI-backed fan, RGB, and battery paths can report success without hardware effect. Treat visible manual/profile fan paths as degraded until an effective write/readback check proves control.";

            if (!isRoot)
            {
                reason += " Run with sudo for write/readback validation.";
            }

            return new LinuxCapabilityAssessment
            {
                CapabilityClass = hasProfileControl ? LinuxCapabilityClass.ProfileOnly : LinuxCapabilityClass.TelemetryOnly,
                SupportsManualFanControl = false,
                SupportsProfileControl = hasProfileControl,
                SupportsTelemetry = true,
                Reason = reason
            };
        }

        if (hasManualFanControl)
        {
            // Priority here must match the actual terms of hasManualFanControl's OR-chain above -
            // hasHwmonFanAccess is deliberately NOT one of them (see the hasProfileControl comment:
            // hwmon pwm_enable alone is coarse policy control, not manual control), so it must never
            // be checked here. It previously was checked first, which meant a board with both
            // hasEcAccess and hasHwmonFanAccess true (an independent, unrelated signal) got told
            // "hp-wmi hwmon pwm/fan targets" instead of the real reason, legacy EC access - a
            // confirmed mismatch surfaced by GitHub #127's own diagnose output, which showed the
            // hwmon-worded reason while its EC diagnostics were also positive.
            var reason = hasEcAccess
                ? "Manual fan control is available through legacy EC access."
                : hasFan1Target || hasFan2Target
                    ? "Manual fan control is available through hp-wmi hwmon fan target files."
                    : "Manual fan control is available through hp-wmi fan output files.";

            if (!isRoot)
            {
                reason += " Run with sudo to use write-capable controls.";
            }

            return new LinuxCapabilityAssessment
            {
                CapabilityClass = LinuxCapabilityClass.FullControl,
                SupportsManualFanControl = true,
                SupportsProfileControl = hasProfileControl,
                SupportsTelemetry = true,
                Reason = reason
            };
        }

        if (hasProfileControl)
        {
            var reason = hasHwmonFanAccess
                ? "Firmware exposes hwmon pwm_enable policy control, but no writable fan target/output interface was detected for manual per-fan speed control."
                : isUnsafeEcModel
                ? $"Board '{boardId ?? "unknown"}' on model '{model ?? "unknown"}' is classified profile-only because direct EC writes are blocked for safety and only thermal/platform profile control is exposed."
                : "Thermal/platform profile control is available, but firmware does not expose manual fan target/output interfaces on this board.";

            if (!isRoot)
            {
                reason += " Run with sudo to apply profile changes.";
            }

            if (isWmaaAbortProneBoard)
            {
                reason += " Board 8BCD is currently treated as degraded profile control because field diagnostics show ACPI WMAA/WHCM aborts can make WMI-backed fan, RGB, and battery calls report success without hardware effect.";
            }

            return new LinuxCapabilityAssessment
            {
                CapabilityClass = LinuxCapabilityClass.ProfileOnly,
                SupportsManualFanControl = false,
                SupportsProfileControl = true,
                SupportsTelemetry = true,
                Reason = reason
            };
        }

        if (hasTelemetry)
        {
            return new LinuxCapabilityAssessment
            {
                CapabilityClass = LinuxCapabilityClass.TelemetryOnly,
                SupportsManualFanControl = false,
                SupportsProfileControl = false,
                SupportsTelemetry = true,
                Reason = "Telemetry paths are present, but no writable EC, hp-wmi, hwmon target, or platform profile control interface is exposed for this board/kernel combination."
            };
        }

        return new LinuxCapabilityAssessment
        {
            CapabilityClass = LinuxCapabilityClass.UnsupportedControl,
            SupportsManualFanControl = false,
            SupportsProfileControl = false,
            SupportsTelemetry = false,
            Reason = "No supported Linux control interface was detected. This is usually a kernel exposure gap, unsupported firmware path, or missing hp-wmi/ec_sys support for the current board."
        };
    }

    private static bool IsWmaaAbortProneBoard(string? boardId) =>
        string.Equals(boardId?.Trim(), "8BCD", StringComparison.OrdinalIgnoreCase);
}
