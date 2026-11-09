using Groovefin.Core.Playback;

namespace Groovefin.Core.Tests.Playback;

public sealed class StreamFailuresTests
{
    [Theory]
    [InlineData("NetworkError", null)]
    [InlineData("Unknown", unchecked((int)0x80072EFE))] // ERROR_INTERNET_CONNECTION_ABORTED
    [InlineData("Unknown", unchecked((int)0x80072EE2))] // ERROR_INTERNET_TIMEOUT
    [InlineData("Unknown", unchecked((int)0x80072EE7))] // ERROR_INTERNET_NAME_NOT_RESOLVED
    [InlineData("Unknown", unchecked((int)0x800704CF))] // ERROR_NETWORK_UNREACHABLE
    [InlineData("Unknown", unchecked((int)0x801901F7))] // HTTP 503
    [InlineData("NetworkError", unchecked((int)0x80190198))] // HTTP 408
    [InlineData("NetworkError", unchecked((int)0x801901AD))] // HTTP 429
    public void Network_trouble_is_worth_another_try(string error, int? result) =>
        StreamFailures.IsNetwork(error, result).Should().BeTrue();

    [Theory]
    [InlineData("SourceNotSupported", null)]
    [InlineData("MediaSourceNotSupported", unchecked((int)0xC00D36C4))] // MF_E_UNSUPPORTED_BYTESTREAM_TYPE
    [InlineData("DecodingError", null)]
    [InlineData("NetworkError", unchecked((int)0x80190194))] // HTTP 404
    [InlineData("NetworkError", unchecked((int)0x80190191))] // HTTP 401
    [InlineData("Unknown", null)]
    [InlineData(null, null)]
    public void A_file_or_a_refusal_is_not(string? error, int? result) =>
        StreamFailures.IsNetwork(error, result).Should().BeFalse();
}
