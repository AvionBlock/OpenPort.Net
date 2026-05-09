using OpenPort.Net.Models;

namespace OpenPort.Net.Internal;

internal static class UpnpErrorMapper
{
    public static OpenPortStatus MapSoapError(int? errorCode) =>
        errorCode switch
        {
            402 => OpenPortStatus.InvalidRequest,
            501 => OpenPortStatus.Failed,
            606 => OpenPortStatus.Unauthorized,
            713 or 714 or 715 or 716 or 724 or 727 or 732 => OpenPortStatus.InvalidRequest,
            718 or 729 => OpenPortStatus.Conflict,
            725 => OpenPortStatus.NotSupported,
            728 => OpenPortStatus.NoResources,
            _ => OpenPortStatus.Failed
        };

    public static string GetErrorName(int? errorCode) =>
        errorCode switch
        {
            402 => "InvalidArgs",
            501 => "ActionFailed",
            606 => "Unauthorized",
            713 => "SpecifiedArrayIndexInvalid",
            714 => "NoSuchEntryInArray",
            715 => "WildCardNotPermittedInSrcIP",
            716 => "WildCardNotPermittedInExtPort",
            718 => "ConflictInMappingEntry",
            724 => "SamePortValuesRequired",
            725 => "OnlyPermanentLeasesSupported",
            727 => "ExternalPortOnlySupportsWildcard",
            728 => "NoPortMapsAvailable",
            729 => "ConflictWithOtherMechanisms",
            732 => "WildCardNotPermittedInIntPort",
            _ => "Unknown"
        };
}
