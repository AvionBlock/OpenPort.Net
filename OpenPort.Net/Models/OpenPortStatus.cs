namespace OpenPort.Net.Models;

/// <summary>
/// Result status returned by port mapping operations.
/// </summary>
public enum OpenPortStatus
{
    /// <summary>
    /// The operation completed successfully.
    /// </summary>
    Success,

    /// <summary>
    /// The gateway or selected provider does not support the requested operation.
    /// </summary>
    NotSupported,

    /// <summary>
    /// No usable gateway was discovered.
    /// </summary>
    GatewayNotFound,

    /// <summary>
    /// The operation timed out or was cancelled by the configured timeout.
    /// </summary>
    Timeout,

    /// <summary>
    /// The requested mapping conflicts with an existing mapping or gateway policy.
    /// </summary>
    Conflict,

    /// <summary>
    /// The gateway rejected the operation due to authorization or policy.
    /// </summary>
    Unauthorized,

    /// <summary>
    /// The request was malformed or invalid for the gateway.
    /// </summary>
    InvalidRequest,

    /// <summary>
    /// The gateway has no resources available for the requested mapping.
    /// </summary>
    NoResources,

    /// <summary>
    /// The gateway accepted the mapping but assigned a different external port.
    /// </summary>
    ExternalPortChanged,

    /// <summary>
    /// The operation failed for a reason that does not map to a more specific status.
    /// </summary>
    Failed
}
